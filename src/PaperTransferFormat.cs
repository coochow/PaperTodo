using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace PaperTodo;

// Markdown transfer format shared by export and import. A file written by PaperTodo
// starts with one invisible HTML comment carrying the paper type and title, so the
// document stays readable in any Markdown viewer while PaperTodo can round-trip it:
//
//   <!-- papertodo: {"app":"PaperTodo","format":1,"type":"todo","title":"..."} -->
//   - [ ] item
//   - [x] done item
//
// Files without that marker are imported as ordinary Markdown notes.
internal enum PaperTransferFileKind
{
    None = 0,
    PaperTodo = 1,
    Malformed = 2
}

internal sealed class PaperTransferMetadata
{
    [JsonPropertyName("app")]
    public string App { get; set; } = "";

    [JsonPropertyName("format")]
    public int Format { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = PaperTypes.Note;

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

internal sealed class PaperTransferManifest
{
    [JsonPropertyName("app")]
    public string App { get; set; } = "PaperTodo";

    [JsonPropertyName("format")]
    public int Format { get; set; } = PaperTransferFormat.FormatVersion;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("exportedAt")]
    public string ExportedAt { get; set; } = "";

    [JsonPropertyName("papers")]
    public List<PaperTransferManifestEntry> Papers { get; set; } = new();
}

internal sealed class PaperTransferManifestEntry
{
    [JsonPropertyName("file")]
    public string File { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = PaperTypes.Note;

    [JsonPropertyName("title")]
    public string Title { get; set; } = "";

    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
}

internal readonly record struct PaperTransferTodoItem(string Text, bool Done);

internal static class PaperTransferFormat
{
    public const int FormatVersion = 1;
    private const string MetadataPrefix = "<!-- papertodo:";
    private const string MetadataSuffix = "-->";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
    private static readonly JsonSerializerOptions ManifestWriteOptions = new()
    {
        WriteIndented = true
    };
    private static readonly Regex TodoLinePattern = new(
        @"^[ \t]{0,3}[-*+][ \t]+\[([ xX])\][ \t]?(.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> ReservedWindowsNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static string BuildMetadataLine(string type, string title, string? id)
    {
        var metadata = new PaperTransferMetadata
        {
            App = "PaperTodo",
            Format = FormatVersion,
            Type = type == PaperTypes.Todo ? PaperTypes.Todo : PaperTypes.Note,
            Title = title ?? "",
            Id = string.IsNullOrWhiteSpace(id) ? null : id
        };
        return MetadataPrefix + " " + JsonSerializer.Serialize(metadata, JsonOptions) + " " + MetadataSuffix;
    }

    public static string ComposePaperFile(PaperData paper, string body)
    {
        var title = string.IsNullOrWhiteSpace(paper.Title)
            ? PaperTitles.EffectiveTitle(paper, 1)
            : paper.Title;
        return BuildMetadataLine(paper.Type, title, paper.Id) + "\r\n" + (body ?? "");
    }

    public static PaperTransferFileKind ParsePaperFile(
        string? text,
        out PaperTransferMetadata? metadata,
        out string body)
    {
        metadata = null;
        body = text ?? "";
        if (body.Length > 0 && body[0] == '\uFEFF')
        {
            body = body[1..];
        }

        if (!body.StartsWith(MetadataPrefix, StringComparison.Ordinal))
        {
            return PaperTransferFileKind.None;
        }

        var lineEnd = body.IndexOfAny(new[] { '\r', '\n' });
        var firstLine = lineEnd < 0 ? body : body[..lineEnd];
        var rest = lineEnd < 0 ? "" : SkipLineBreak(body, lineEnd);
        var trimmed = firstLine.Trim();
        if (!trimmed.EndsWith(MetadataSuffix, StringComparison.Ordinal))
        {
            return PaperTransferFileKind.Malformed;
        }

        var json = trimmed[MetadataPrefix.Length..^MetadataSuffix.Length].Trim();
        try
        {
            metadata = JsonSerializer.Deserialize<PaperTransferMetadata>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return PaperTransferFileKind.Malformed;
        }

        if (metadata == null || metadata.Format <= 0)
        {
            return PaperTransferFileKind.Malformed;
        }

        if (metadata.Type != PaperTypes.Todo)
        {
            metadata.Type = PaperTypes.Note;
        }

        body = rest;
        return PaperTransferFileKind.PaperTodo;
    }

    public static string FormatTodoBody(PaperData paper)
    {
        var builder = new StringBuilder();
        var ordered = paper.Items.OrderBy(item => item.Order).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            if (i > 0)
            {
                builder.Append("\r\n");
            }
            var item = ordered[i];
            var text = (item.Text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            builder.Append(item.Done ? "- [x] " : "- [ ] ");
            builder.Append(text);
        }

        return builder.ToString();
    }

    public static IReadOnlyList<PaperTransferTodoItem> ParseTodoBody(string? body)
    {
        var items = new List<PaperTransferTodoItem>();
        if (string.IsNullOrEmpty(body))
        {
            return items;
        }

        foreach (var line in EnumerateLines(body))
        {
            var match = TodoLinePattern.Match(line);
            if (match.Success)
            {
                items.Add(new PaperTransferTodoItem(
                    match.Groups[2].Value.Trim(),
                    match.Groups[1].Value is "x" or "X"));
                continue;
            }

            if (items.Count > 0 &&
                line.Length > 0 &&
                char.IsWhiteSpace(line[0]) &&
                line.Trim().Length > 0)
            {
                var previous = items[^1];
                items[^1] = previous with { Text = (previous.Text + " " + line.Trim()).Trim() };
            }
        }

        return items;
    }

    public static string SanitizeFileName(string? title, string fallback)
    {
        var value = (title ?? "").Trim();
        if (value.Length == 0)
        {
            value = fallback;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(char.IsControl(ch) || Array.IndexOf(invalidChars, ch) >= 0 ? ' ' : ch);
        }

        var name = Regex.Replace(builder.ToString(), @"\s+", " ").Trim(' ', '.');
        if (name.Length == 0)
        {
            name = fallback;
        }

        if (name.Length > 60)
        {
            var cut = 60;
            if (char.IsHighSurrogate(name[cut - 1]))
            {
                cut--;
            }
            name = name[..cut].TrimEnd();
        }

        if (name.Length == 0)
        {
            name = fallback;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        if (ReservedWindowsNames.Contains(stem))
        {
            name = "_" + name;
        }

        return name;
    }

    public static string SafeLinkComponent(string? value, string fallback)
    {
        var source = (value ?? "").Trim();
        var builder = new StringBuilder(source.Length);
        foreach (var ch in source)
        {
            if (char.IsLetterOrDigit(ch) || ch >= 0x80 || ch is '-' or '_' or '.')
            {
                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var result = builder.ToString().Trim('-', '.');
        return result.Length == 0 ? fallback : result;
    }

    public static PaperTransferManifest? ParseManifest(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PaperTransferManifest>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string SerializeManifest(PaperTransferManifest manifest)
        => JsonSerializer.Serialize(manifest, ManifestWriteOptions);

    private static string SkipLineBreak(string text, int lineEnd)
    {
        if (text[lineEnd] == '\r' && lineEnd + 1 < text.Length && text[lineEnd + 1] == '\n')
        {
            return text[(lineEnd + 2)..];
        }

        return text[(lineEnd + 1)..];
    }

    private static IEnumerable<string> EnumerateLines(string text)
    {
        var start = 0;
        while (start <= text.Length)
        {
            var end = start;
            while (end < text.Length && text[end] is not '\r' and not '\n')
            {
                end++;
            }

            yield return text[start..end];
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