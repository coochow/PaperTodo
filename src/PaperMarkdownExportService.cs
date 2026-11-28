using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace PaperTodo;

internal sealed record PaperExportSummary(
    int Notes,
    int Todos,
    IReadOnlyList<string> Warnings);

internal static class PaperMarkdownExportService
{
    public static PaperExportSummary ExportPaperToFile(
        PaperData paper,
        NoteImageStore imageStore,
        string markdownPath)
    {
        var warnings = new List<string>();
        var directory = Path.GetDirectoryName(markdownPath);
        if (string.IsNullOrEmpty(directory))
        {
            directory = Directory.GetCurrentDirectory();
        }

        var stem = Path.GetFileNameWithoutExtension(markdownPath);
        var body = BuildBody(paper, imageStore, directory, stem, warnings);
        File.WriteAllText(
            markdownPath,
            PaperTransferFormat.ComposePaperFile(paper, body),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        return new PaperExportSummary(
            paper.Type == PaperTypes.Note ? 1 : 0,
            paper.Type == PaperTypes.Todo ? 1 : 0,
            warnings);
    }

    public static PaperExportSummary ExportAllToZip(
        IReadOnlyList<PaperData> papers,
        NoteImageStore imageStore,
        string zipPath,
        string appVersion)
    {
        var warnings = new List<string>();
        var staging = Path.Combine(
            Path.GetTempPath(),
            "PaperTodo",
            "export-" + Guid.NewGuid().ToString("N"));
        var tempZip = zipPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(staging);
            var notesDirectory = Path.Combine(staging, "notes");
            var todosDirectory = Path.Combine(staging, "todos");
            Directory.CreateDirectory(notesDirectory);
            Directory.CreateDirectory(todosDirectory);

            var manifest = new PaperTransferManifest
            {
                Version = appVersion,
                ExportedAt = DateTimeOffset.Now.ToString("O")
            };
            var usedStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var noteCount = 0;
            var todoCount = 0;

            foreach (var paper in papers)
            {
                var isTodo = paper.Type == PaperTypes.Todo;
                var folderName = isTodo ? "todos" : "notes";
                var folderPath = isTodo ? todosDirectory : notesDirectory;
                var title = DisplayTitle(paper);
                var stem = PaperTransferFormat.SanitizeFileName(title, isTodo ? "todo" : "note");
                stem = MakeUniqueStem(stem, folderName, usedStems);

                var body = BuildBody(paper, imageStore, folderPath, stem, warnings);
                var markdownPath = Path.Combine(folderPath, stem + ".md");
                File.WriteAllText(
                    markdownPath,
                    PaperTransferFormat.ComposePaperFile(paper, body),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                manifest.Papers.Add(new PaperTransferManifestEntry
                {
                    File = folderName + "/" + stem + ".md",
                    Type = isTodo ? PaperTypes.Todo : PaperTypes.Note,
                    Title = title,
                    Id = paper.Id
                });

                if (isTodo)
                {
                    todoCount++;
                }
                else
                {
                    noteCount++;
                }
            }

            File.WriteAllText(
                Path.Combine(staging, "manifest.json"),
                PaperTransferFormat.SerializeManifest(manifest),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }

            ZipFile.CreateFromDirectory(
                staging,
                tempZip,
                CompressionLevel.Optimal,
                includeBaseDirectory: false);
            File.Move(tempZip, zipPath, overwrite: true);

            return new PaperExportSummary(noteCount, todoCount, warnings);
        }
        finally
        {
            TryDeleteFile(tempZip);
            TryDeleteDirectory(staging);
        }
    }

    private static string BuildBody(
        PaperData paper,
        NoteImageStore imageStore,
        string directory,
        string stem,
        List<string> warnings)
    {
        if (paper.Type == PaperTypes.Todo)
        {
            return PaperTransferFormat.FormatTodoBody(paper);
        }

        var content = paper.Content ?? "";
        if (!MarkdownImageReferences.Enumerate(content).Any())
        {
            return content;
        }

        var assetsName = PaperTransferFormat.SafeLinkComponent(stem, "paper") + ".assets";
        var assetsDirectory = GetUniqueDirectoryPath(directory, assetsName);
        try
        {
            return imageStore.ConvertMarkdownForExternalEditor(
                paper.Id,
                content,
                assetsDirectory,
                directory);
        }
        catch (Exception)
        {
            TryDeleteDirectory(assetsDirectory);
            warnings.Add(Strings.Format("DataExportImageWarning", DisplayTitle(paper)));
            return content;
        }
    }

    private static string DisplayTitle(PaperData paper)
    {
        return string.IsNullOrWhiteSpace(paper.Title)
            ? PaperTitles.EffectiveTitle(paper, 1)
            : paper.Title;
    }

    private static string MakeUniqueStem(string stem, string folder, HashSet<string> used)
    {
        var candidate = stem;
        var index = 2;
        while (!used.Add(folder + "/" + candidate.ToLowerInvariant()))
        {
            candidate = stem + "-" + index.ToString(CultureInfo.InvariantCulture);
            index++;
        }

        return candidate;
    }

    private static string GetUniqueDirectoryPath(string parent, string name)
    {
        var candidate = Path.Combine(parent, name);
        var index = 2;
        while (Directory.Exists(candidate))
        {
            candidate = Path.Combine(parent, name + "-" + index.ToString(CultureInfo.InvariantCulture));
            index++;
        }

        return candidate;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup; the export result is already on disk.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; the export result is already on disk.
        }
    }
}