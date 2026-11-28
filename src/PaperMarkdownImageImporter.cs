using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PaperTodo;

internal static class PaperMarkdownImageImporter
{
    public static string ImportForNote(
        NoteImageStore imageStore,
        string noteId,
        string markdown,
        string baseDirectory,
        List<string> warnings)
    {
        if (string.IsNullOrEmpty(markdown))
        {
            return markdown;
        }

        var cache = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder(markdown.Length);
        var cursor = 0;
        foreach (var (start, length) in EnumeratePhysicalLines(markdown))
        {
            var line = markdown.Substring(start, length);
            var trimmed = line.Trim();
            if (trimmed.StartsWith("![", StringComparison.Ordinal) &&
                MarkdownImageReferences.TrySplitMarkdownImage(
                    trimmed,
                    out var label,
                    out var url,
                    out var attributes))
            {
                var localPath = TryResolveLocalImagePath(url, baseDirectory);
                if (localPath != null)
                {
                    if (!cache.TryGetValue(localPath, out var replacement))
                    {
                        try
                        {
                            var asset = imageStore.ImportImageFile(noteId, localPath);
                            replacement = label.Length == 0
                                ? MarkdownImageReferences.CreateReference(asset.Id)
                                : "![" + label + "](" +
                                    MarkdownImageReferences.UriPrefix + asset.Id + ")" + attributes;
                            cache[localPath] = replacement;
                        }
                        catch (Exception)
                        {
                            cache[localPath] = null;
                            warnings.Add(Strings.Format(
                                "DataImportImageWarning",
                                Path.GetFileName(localPath)));
                        }
                    }

                    if (replacement != null)
                    {
                        var leadingLength = line.Length - line.TrimStart().Length;
                        builder.Append(markdown, cursor, start - cursor);
                        builder.Append(line[..leadingLength]).Append(replacement);
                        cursor = start + length;
                        continue;
                    }
                }
            }
        }

        builder.Append(markdown, cursor, markdown.Length - cursor);
        return builder.ToString();
    }

    private static string? TryResolveLocalImagePath(string url, string baseDirectory)
    {
        var value = (url ?? "").Trim();
        if (value.Length == 0)
        {
            return null;
        }

        if (value.StartsWith(MarkdownImageReferences.UriPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        if (value.Length > 2 && value[0] == '<' && value[^1] == '>')
        {
            value = value[1..^1].Trim();
        }

        // Markdown image titles ("path \"title\"") are not part of the file path.
        var titleIndex = value.IndexOf(" \"", StringComparison.Ordinal);
        if (titleIndex >= 0)
        {
            value = value[..titleIndex].Trim();
        }

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("#", StringComparison.Ordinal))
        {
            return null;
        }

        string localPath;
        if (Path.IsPathRooted(value))
        {
            localPath = value;
        }
        else if (value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var fileUri) || !fileUri.IsFile)
            {
                return null;
            }

            localPath = fileUri.LocalPath;
        }
        else if (Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            return null;
        }
        else
        {
            localPath = value;
        }

        try
        {
            var decoded = Uri.UnescapeDataString(localPath);
            var fullPath = Path.IsPathRooted(decoded)
                ? Path.GetFullPath(decoded)
                : Path.GetFullPath(Path.Combine(baseDirectory, decoded));
            return File.Exists(fullPath) ? fullPath : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerable<(int Start, int Length)> EnumeratePhysicalLines(string text)
    {
        var start = 0;
        while (start <= text.Length)
        {
            var end = start;
            while (end < text.Length && text[end] is not '\r' and not '\n')
            {
                end++;
            }

            yield return (start, end - start);
            if (end >= text.Length)
            {
                yield break;
            }

            start = text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n'
                ? end + 2
                : end + 1;
        }
    }
}