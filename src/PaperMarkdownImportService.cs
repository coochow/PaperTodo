using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PaperTodo;

internal sealed class PaperImportSource
{
    public string Type { get; set; } = PaperTypes.Note;
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public List<PaperTransferTodoItem> TodoItems { get; } = new();
    public string MarkdownDirectory { get; set; } = "";
    public string SourceLabel { get; set; } = "";
}

internal sealed class PaperImportBatch : IDisposable
{
    private readonly string _tempRoot;
    private readonly HashSet<string> _seenMarkdown = new(StringComparer.OrdinalIgnoreCase);
    private int _zipIndex;

    private PaperImportBatch(string tempRoot)
    {
        _tempRoot = tempRoot;
    }

    public List<PaperImportSource> Sources { get; } = new();
    public List<string> Failures { get; } = new();

    public static PaperImportBatch Read(IEnumerable<string> paths)
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "PaperTodo",
            "import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var batch = new PaperImportBatch(tempRoot);
        try
        {
            foreach (var path in paths)
            {
                try
                {
                    batch.ReadPath(path);
                }
                catch (Exception ex)
                {
                    batch.Failures.Add(Path.GetFileName(path) + ": " + ex.Message);
                }
            }
        }
        catch
        {
            batch.Dispose();
            throw;
        }

        return batch;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, recursive: true);
            }
        }
        catch
        {
            // Extraction data is temporary and safe for the OS to clean up.
        }
    }

    private void ReadPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            Failures.Add(path ?? "");
            return;
        }

        var extension = Path.GetExtension(path);
        if (string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".markdown", StringComparison.OrdinalIgnoreCase))
        {
            ReadMarkdownFile(path, manifestType: null, manifestTitle: null);
            return;
        }

        if (!string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            Failures.Add(Path.GetFileName(path));
            return;
        }

        var extractedRoot = ExtractZip(path);
        CollectZipMarkdown(extractedRoot);
    }

    private string ExtractZip(string zipPath)
    {
        _zipIndex++;
        var target = Path.Combine(_tempRoot, "zip-" + _zipIndex.ToString());
        Directory.CreateDirectory(target);
        var rootPrefix = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        long totalBytes = 0;
        var entryCount = 0;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.Length == 0 || name.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            if (name.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("/._", StringComparison.Ordinal))
            {
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(target, name));
            if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(name);
            }

            entryCount++;
            totalBytes += entry.Length;
            if (entryCount > 4000 || totalBytes > 512L * 1024 * 1024)
            {
                throw new InvalidDataException(name);
            }

            var parent = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            entry.ExtractToFile(fullPath, overwrite: true);
        }

        return target;
    }

    private void CollectZipMarkdown(string extractedRoot)
    {
        PaperTransferManifest? manifest = null;
        var manifestPath = Path.Combine(extractedRoot, "manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                manifest = PaperTransferFormat.ParseManifest(File.ReadAllText(manifestPath));
            }
            catch (IOException)
            {
                manifest = null;
            }
        }

        var allMarkdown = Directory
            .EnumerateFiles(extractedRoot, "*.md", SearchOption.AllDirectories)
            .Where(path => path.IndexOf("__MACOSX", StringComparison.OrdinalIgnoreCase) < 0)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var byRelative = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in allMarkdown)
        {
            byRelative[Path.GetRelativePath(extractedRoot, file).Replace('\\', '/')] = file;
        }

        var ordered = new List<(string File, string? Type, string? Title)>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (manifest != null)
        {
            foreach (var entry in manifest.Papers)
            {
                var relative = (entry.File ?? "").Replace('\\', '/').TrimStart('/');
                if (byRelative.TryGetValue(relative, out var file) && added.Add(file))
                {
                    ordered.Add((file, entry.Type, entry.Title));
                }
            }
        }

        foreach (var file in allMarkdown)
        {
            if (added.Add(file))
            {
                ordered.Add((file, null, null));
            }
        }

        foreach (var item in ordered)
        {
            ReadMarkdownFile(item.File, item.Type, item.Title);
        }
    }

    private void ReadMarkdownFile(string path, string? manifestType, string? manifestTitle)
    {
        var fullPath = Path.GetFullPath(path);
        if (!_seenMarkdown.Add(fullPath))
        {
            return;
        }

        try
        {
            var length = new FileInfo(fullPath).Length;
            if (length > 20L * 1024 * 1024)
            {
                Failures.Add(Strings.Format("DataImportFileTooLarge", Path.GetFileName(fullPath)));
                return;
            }

            var text = File.ReadAllText(fullPath);
            var kind = PaperTransferFormat.ParsePaperFile(text, out var metadata, out var body);
            if (kind == PaperTransferFileKind.Malformed)
            {
                Failures.Add(Strings.Format("DataImportMalformedFile", Path.GetFileName(fullPath)));
                return;
            }

            var type = metadata?.Type ?? NormalizeType(manifestType);
            var title = metadata?.Title;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = manifestTitle;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = Path.GetFileNameWithoutExtension(fullPath);
            }

            var source = new PaperImportSource
            {
                Type = type,
                Title = title ?? "",
                Content = body,
                MarkdownDirectory = Path.GetDirectoryName(fullPath) ?? "",
                SourceLabel = Path.GetFileName(fullPath)
            };
            if (type == PaperTypes.Todo)
            {
                source.TodoItems.AddRange(PaperTransferFormat.ParseTodoBody(body));
            }

            Sources.Add(source);
        }
        catch (Exception ex)
        {
            Failures.Add(Path.GetFileName(fullPath) + ": " + ex.Message);
        }
    }

    private static string NormalizeType(string? value)
    {
        return string.Equals(value, PaperTypes.Todo, StringComparison.OrdinalIgnoreCase)
            ? PaperTypes.Todo
            : PaperTypes.Note;
    }
}