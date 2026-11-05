using System;
using System.Collections.Generic;
using System.Text;

namespace PaperTodo;

// === Custom patch (dev branch): explicit pipe-table to aligned code-block view ===
// The upstream markdown renderer deliberately does not support GFM tables (lightweight
// design). Pasting stays untouched (raw pipes, fully reversible data); this transform
// is invoked only from the note context menu "table view toggle": it locates the GFM
// table block around the caret/right-click point and renders it as a fenced code
// block with monospace column alignment (CJK = 2 cells). Ctrl+Z restores instantly.
internal static class MarkdownTablePaste
{
    /// <summary>
    /// Finds the contiguous GFM table block containing <paramref name="offset"/> and
    /// returns its replacement as an aligned code block. Null when no table there.
    /// </summary>
    public static (int Start, int Length, string Replacement)? TryBuildTableBlockView(
        string text,
        int offset)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('|') < 0)
        {
            return null;
        }

        var lines = SplitLinesWithOffsets(text, out var lineStarts);
        var containingLine = Array.BinarySearch(lineStarts, offset);
        if (containingLine < 0)
        {
            containingLine = ~containingLine - 1;
        }

        if (containingLine < 0 || containingLine >= lines.Length)
        {
            return null;
        }

        // Walk outward from the caret line: tables are contiguous pipe lines around it.
        var first = containingLine;
        while (first > 0 && IsTableLine(lines[first - 1]))
        {
            first--;
        }

        var last = containingLine;
        while (last < lines.Length - 1 && IsTableLine(lines[last + 1]))
        {
            last++;
        }

        if (!IsTableLine(lines[first]) || !IsTableLine(lines[last]))
        {
            return null;
        }

        var tableLines = new List<string>();
        for (var i = first; i <= last; i++)
        {
            tableLines.Add(lines[i]);
        }

        if (tableLines.Count < 3 || !IsSeparatorRow(tableLines[1]))
        {
            return null;
        }

        var header = SplitCells(tableLines[0]);
        var rows = new List<List<string>>();
        for (var k = 2; k < tableLines.Count; k++)
        {
            rows.Add(SplitCells(tableLines[k]));
        }

        var columnCount = Math.Max(header.Count,
            rows.Count > 0 ? rows.Max(r => r.Count) : header.Count);
        var widths = new int[columnCount];
        MeasureWidth(header, widths);
        foreach (var row in rows)
        {
            MeasureWidth(row, widths);
        }

        var builder = new StringBuilder();
        builder.AppendLine("```");
        builder.AppendLine(BuildAlignedRow(header, widths));
        builder.AppendLine(BuildRuler(widths));
        for (var k = 2; k < tableLines.Count; k++)
        {
            builder.AppendLine(BuildAlignedRow(SplitCells(tableLines[k]), widths));
        }

        builder.Append("```");
        var startOffset = lineStarts[first];
        var endOffset = last < lines.Length - 1
            ? lineStarts[last + 1]
            : text.Length;
        return (startOffset, endOffset - startOffset, builder.ToString());
    }

    private static string[] SplitLinesWithOffsets(string text, out int[] lineStarts)
    {
        var lines = new List<string>();
        var starts = new List<int>();
        var current = new StringBuilder();
        var lineStart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\r' || ch == '\n')
            {
                lines.Add(current.ToString());
                starts.Add(lineStart);
                current.Clear();
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                lineStart = i + 1;
            }
            else
            {
                current.Append(ch);
            }
        }

        lines.Add(current.ToString());
        starts.Add(lineStart);
        lineStarts = starts.ToArray();
        return lines.ToArray();
    }

    // Visual width: CJK chars count as 2 monospace cells, ASCII as 1. Keeps columns
    // aligned when mixed-script cells render in any monospace font.
    private static int VisualWidth(string cell)
    {
        var width = 0;
        foreach (var ch in cell)
        {
            width += ch >= 0x1100 && IsWideCodePoint(ch) ? 2 : 1;
        }

        return width;
    }

    private static bool IsWideCodePoint(char ch)
    {
        return (ch >= '一' && ch <= '鿿') ||
            (ch >= '㐀' && ch <= '䶿') ||
            (ch >= '豈' && ch <= '﫿') ||
            (ch >= '぀' && ch <= 'ヿ') ||
            (ch >= '가' && ch <= '힯') ||
            (ch >= '！' && ch <= '｠') ||
            (ch >= '　' && ch <= '〿');
    }

    private static void MeasureWidth(IReadOnlyList<string> row, int[] widths)
    {
        for (var i = 0; i < row.Count && i < widths.Length; i++)
        {
            var w = VisualWidth(row[i]);
            if (w > widths[i])
            {
                widths[i] = w;
            }
        }
    }

    private static string BuildAlignedRow(IReadOnlyList<string> row, int[] widths)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < widths.Length; i++)
        {
            var cell = i < row.Count ? row[i] : "";
            builder.Append(cell);
            if (i < widths.Length - 1)
            {
                builder.Append(' ', Math.Max(1, widths[i] - VisualWidth(cell) + 2));
            }
        }

        return builder.ToString().TrimEnd();
    }

    private static string BuildRuler(int[] widths)
    {
        var parts = new string[widths.Length];
        for (var i = 0; i < widths.Length; i++)
        {
            parts[i] = new string('-', widths[i]);
        }

        return string.Join("  ", parts);
    }

    private static bool IsTableLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 1 &&
            trimmed.StartsWith('|') &&
            trimmed.EndsWith('|');
    }

    private static bool IsSeparatorRow(string line)
    {
        var trimmed = line.Trim().Trim('|');
        foreach (var cell in trimmed.Split('|'))
        {
            var c = cell.Trim();
            if (c.Length == 0)
            {
                continue;
            }

            if (c.Replace(":", string.Empty).Trim('-').Length != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> SplitCells(string line)
    {
        var trimmed = line.Trim().Trim('|');
        var cells = trimmed.Split('|');
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = cells[i].Trim();
        }

        return new List<string>(cells);
    }

}
