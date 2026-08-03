using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Zalo.Console;

/// <summary>
/// Lightweight Unicode table renderer for professional CLI interfaces.
/// </summary>
internal sealed class ConsoleTable
{
    private readonly string[] _headers;
    private readonly List<string[]> _rows = [];

    public ConsoleTable(params string[] headers) => _headers = headers ?? Array.Empty<string>();

    public void AddRow(params string[] values) => _rows.Add(values ?? Array.Empty<string>());

    public void Print(ConsoleColor borderColor = ConsoleColor.DarkGray, ConsoleColor headerColor = ConsoleColor.Cyan)
    {
        int colCount = _headers.Length;
        if (colCount == 0)
        {
            return;
        }

        int[] widths = new int[colCount];
        for (int i = 0; i < colCount; i++)
        {
            int hLen = GetDisplayWidth(_headers[i]);
            int maxRowLen = _rows.Count > 0 ? _rows.Max(r => i < r.Length ? GetDisplayWidth(r[i]) : 0) : 0;
            widths[i] = Math.Max(hLen, maxRowLen) + 2;
        }

        PrintLine('┌', '┬', '┐', widths, borderColor);

        System.Console.ForegroundColor = borderColor;
        System.Console.Write("│");
        for (int i = 0; i < colCount; i++)
        {
            System.Console.ForegroundColor = headerColor;
            string text = _headers[i];
            int padding = widths[i] - GetDisplayWidth(text);
            System.Console.Write($" {text}{new string(' ', Math.Max(0, padding - 1))}");
            System.Console.ForegroundColor = borderColor;
            System.Console.Write("│");
        }
        System.Console.WriteLine();
        System.Console.ResetColor();

        PrintLine('├', '┼', '┤', widths, borderColor);

        foreach (string[] row in _rows)
        {
            System.Console.ForegroundColor = borderColor;
            System.Console.Write("│");
            for (int i = 0; i < colCount; i++)
            {
                System.Console.ForegroundColor = ConsoleColor.White;
                string text = i < row.Length ? row[i] ?? "" : "";
                int padding = widths[i] - GetDisplayWidth(text);
                System.Console.Write($" {text}{new string(' ', Math.Max(0, padding - 1))}");
                System.Console.ForegroundColor = borderColor;
                System.Console.Write("│");
            }
            System.Console.WriteLine();
            System.Console.ResetColor();
        }

        PrintLine('└', '┴', '┘', widths, borderColor);
    }

    private static void PrintLine(char left, char sep, char right, int[] widths, ConsoleColor color)
    {
        System.Console.ForegroundColor = color;
        StringBuilder sb = new();
        _ = sb.Append(left);
        for (int i = 0; i < widths.Length; i++)
        {
            _ = sb.Append(new string('─', widths[i]));
            _ = sb.Append(i == widths.Length - 1 ? right : sep);
        }
        System.Console.WriteLine(sb.ToString());
        System.Console.ResetColor();
    }

    private static int GetDisplayWidth(string str)
    {
        if (string.IsNullOrEmpty(str))
        {
            return 0;
        }

        int width = 0;
        foreach (char c in str)
        {
            if ((c >= 0x1100 && c <= 0x115F) ||
                (c >= 0x2E80 && c <= 0xA4CF) ||
                (c >= 0xAC00 && c <= 0xD7A3) ||
                (c >= 0xF900 && c <= 0xFAFF) ||
                (c >= 0xFE10 && c <= 0xFE19) ||
                (c >= 0xFE30 && c <= 0xFE6F) ||
                (c >= 0xFF01 && c <= 0xFF60) ||
                (c >= 0xFFE0 && c <= 0xFFE6))
            {
                width += 2;
            }
            else
            {
                width += 1;
            }
        }
        return width;
    }
}
