//
//  Markdown.cs
//  Citadel: local AI chat for Windows.
//
//  Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
//  Date: November 9, 2026
//  SPDX-License-Identifier: GPL-2.0-only
//
//  This program is free software: you can redistribute it and/or modify it under the terms of
//  the GNU General Public License, version 2, as published by the Free Software Foundation.
//  It is distributed WITHOUT ANY WARRANTY. See the LICENSE file for details.
//

using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using static Citadel.Loc;

namespace Citadel;

/// <summary>
/// Renders the Markdown of chat answers (headings, lists, quotes, tables, code blocks and inline styles)
/// into a FlowDocument, shown in a read-only RichTextBox so the whole answer can be selected.
/// </summary>
internal static partial class Markdown
{
    private abstract record Block;
    private sealed record Heading(int Level, string Text) : Block;
    private sealed record Paragraph(string Text) : Block;
    private sealed record ListBlock(bool Numbered, List<(int indent, string marker, string text)> Items) : Block;
    private sealed record Quote(string Text) : Block;
    private sealed record CodeBlock(string Language, string Code) : Block;
    private sealed record RuleBlock : Block;
    private sealed record TableBlock(List<string> Rows) : Block;

    [GeneratedRegex(@"^(\d+)([.)])\s")]
    private static partial Regex NumberedItem();

    private static List<Block> Parse(string text)
    {
        var blocks = new List<Block>();
        var paragraph = new List<string>();
        var items = new List<(int, string, string)>();
        bool numbered = false;
        var quote = new List<string>();
        var table = new List<string>();
        List<string> code = null;
        string codeLanguage = "";

        void Flush()
        {
            if (paragraph.Count > 0) { blocks.Add(new Paragraph(string.Join("\n", paragraph))); paragraph.Clear(); }
            if (items.Count > 0) { blocks.Add(new ListBlock(numbered, [.. items])); items.Clear(); }
            if (quote.Count > 0) { blocks.Add(new Quote(string.Join("\n", quote))); quote.Clear(); }
            if (table.Count > 0) { blocks.Add(new TableBlock([.. table])); table.Clear(); }
        }

        foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
        {
            string t = line.Trim();
            if (code != null)
            {
                if (t.StartsWith("```")) { blocks.Add(new CodeBlock(codeLanguage, string.Join("\n", code))); code = null; }
                else code.Add(line);
                continue;
            }
            if (t.StartsWith("```")) { Flush(); codeLanguage = t[3..].Trim(); code = []; continue; }
            if (t.Length == 0) { Flush(); continue; }
            if (t.StartsWith('#'))
            {
                int level = t.TakeWhile(c => c == '#').Count();
                if (level <= 6 && t.Length > level && t[level] == ' ') { Flush(); blocks.Add(new Heading(level, t[level..].Trim())); continue; }
            }
            if (t is "---" or "***" or "___") { Flush(); blocks.Add(new RuleBlock()); continue; }
            if (t.StartsWith('|') && (table.Count > 0 || paragraph.Count == 0)) { if (table.Count == 0) Flush(); table.Add(t); continue; }
            if (t.StartsWith('>')) { if (quote.Count == 0) Flush(); quote.Add(t[1..].Trim()); continue; }
            int indent = line.TakeWhile(c => c is ' ' or '\t').Count() / 2;
            if (t.StartsWith("- ") || t.StartsWith("* ") || t.StartsWith("+ "))
            {
                if (items.Count == 0 || numbered) { Flush(); numbered = false; }
                items.Add((indent, "•", t[2..]));
                continue;
            }
            var m = NumberedItem().Match(t);
            if (m.Success)
            {
                if (items.Count == 0 || !numbered) { Flush(); numbered = true; }
                items.Add((indent, m.Groups[1].Value + m.Groups[2].Value, t[m.Length..].Trim()));
                continue;
            }
            // A continuation line of a list item stays with it.
            if (items.Count > 0 && indent > 0) { var last = items[^1]; items[^1] = (last.Item1, last.Item2, last.Item3 + " " + t); continue; }
            if (items.Count > 0 || quote.Count > 0 || table.Count > 0) Flush();
            paragraph.Add(t);
        }
        if (code != null) blocks.Add(new CodeBlock(codeLanguage, string.Join("\n", code)));
        Flush();
        return blocks;
    }

    [GeneratedRegex(@"`([^`]+)`|\*\*(.+?)\*\*|__(.+?)__|\*([^*\s][^*]*?)\*|(?<![\w])_([^_\s][^_]*?)_(?![\w])|\[([^\]]+)\]\((https?://[^)\s]+)\)")]
    private static partial Regex InlineToken();

    /// <summary>Inline Markdown: inline code, bold, italics and links.</summary>
    private static IEnumerable<Inline> Inlines(string s)
    {
        int pos = 0;
        foreach (Match m in InlineToken().Matches(s))
        {
            if (m.Index > pos) yield return new Run(s[pos..m.Index]);
            if (m.Groups[1].Success)
                yield return new Run(m.Groups[1].Value)
                {
                    FontFamily = Theme.Mono, FontSize = 13, Background = Theme.Card, Foreground = Theme.Text,
                };
            else if (m.Groups[2].Success || m.Groups[3].Success)
            {
                var bold = new Bold();
                bold.Inlines.AddRange(Inlines(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value));
                yield return bold;
            }
            else if (m.Groups[4].Success || m.Groups[5].Success)
            {
                var italic = new Italic();
                italic.Inlines.AddRange(Inlines(m.Groups[4].Success ? m.Groups[4].Value : m.Groups[5].Value));
                yield return italic;
            }
            else
            {
                var link = new Hyperlink(new Run(m.Groups[6].Value)) { NavigateUri = new Uri(m.Groups[7].Value), Foreground = Theme.Accent };
                link.RequestNavigate += (o, e) => Open(e.Uri.AbsoluteUri);
                yield return link;
            }
            pos = m.Index + m.Length;
        }
        if (pos < s.Length) yield return new Run(s[pos..]);
    }

    private static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }

    private static System.Windows.Documents.Paragraph Para(string text, double size, FontWeight weight, Thickness margin)
    {
        var p = new System.Windows.Documents.Paragraph { FontSize = size, FontWeight = weight, Margin = margin, LineHeight = size * 1.45 };
        p.Inlines.AddRange(Inlines(text.Replace("\n", " ")));
        return p;
    }

    /// <summary>Builds the document for one answer.</summary>
    public static FlowDocument Render(string text)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(0), FontFamily = Theme.Ui, FontSize = 14.5, Foreground = Theme.Text,
            Background = Brushes.Transparent, TextAlignment = TextAlignment.Left,
        };
        foreach (var block in Parse(text))
        {
            switch (block)
            {
                case Heading h:
                    doc.Blocks.Add(Para(h.Text, h.Level == 1 ? 22 : h.Level == 2 ? 19 : 16.5, FontWeights.SemiBold, new Thickness(0, 6, 0, 8)));
                    break;
                case Paragraph p:
                    doc.Blocks.Add(Para(p.Text, 14.5, FontWeights.Normal, new Thickness(0, 0, 0, 10)));
                    break;
                case ListBlock l:
                    foreach (var (indent, marker, itemText) in l.Items)
                    {
                        var item = new System.Windows.Documents.Paragraph
                        {
                            Margin = new Thickness(6 + indent * 18, 0, 0, 5), TextIndent = -18, LineHeight = 21,
                            Padding = new Thickness(18, 0, 0, 0),
                        };
                        item.Inlines.Add(new Run(marker == "•" ? "•  " : marker + " ") { Foreground = Theme.Secondary });
                        item.Inlines.AddRange(Inlines(itemText));
                        doc.Blocks.Add(item);
                    }
                    if (doc.Blocks.LastBlock is System.Windows.Documents.Paragraph lastItem)
                        lastItem.Margin = new Thickness(lastItem.Margin.Left, 0, 0, 10);
                    break;
                case Quote q:
                    var quote = Para(q.Text, 14.5, FontWeights.Normal, new Thickness(0, 0, 0, 10));
                    quote.BorderBrush = Theme.Tertiary;
                    quote.BorderThickness = new Thickness(3, 0, 0, 0);
                    quote.Padding = new Thickness(12, 2, 0, 2);
                    quote.Foreground = Theme.Secondary;
                    doc.Blocks.Add(quote);
                    break;
                case CodeBlock c:
                    doc.Blocks.Add(new BlockUIContainer(CodeView(c.Language, c.Code)) { Margin = new Thickness(0, 2, 0, 12) });
                    break;
                case RuleBlock:
                    doc.Blocks.Add(new BlockUIContainer(new Border { Height = 1, Background = Theme.Stroke }) { Margin = new Thickness(0, 6, 0, 12) });
                    break;
                case TableBlock t:
                    doc.Blocks.Add(TableView(t.Rows));
                    break;
            }
        }
        return doc;
    }

    private static Table TableView(List<string> rows)
    {
        var cells = rows
            .Where(r => !r.Replace("|", "").All(ch => ch is '-' or ':' or ' '))
            .Select(r => r.Trim().Trim('|').Split('|').Select(x => x.Trim()).ToList()).ToList();
        var table = new Table { CellSpacing = 0, BorderBrush = Theme.Stroke, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 12) };
        int columns = cells.Count == 0 ? 0 : cells.Max(r => r.Count);
        for (int i = 0; i < columns; i++) table.Columns.Add(new TableColumn());
        var group = new TableRowGroup();
        for (int r = 0; r < cells.Count; r++)
        {
            var row = new TableRow { Background = r == 0 ? Theme.Card : Brushes.Transparent };
            foreach (string cell in cells[r])
            {
                var p = Para(cell, 13.5, r == 0 ? FontWeights.SemiBold : FontWeights.Normal, new Thickness(0));
                row.Cells.Add(new TableCell(p) { Padding = new Thickness(8, 4, 8, 4), BorderBrush = Theme.Stroke, BorderThickness = new Thickness(0, 0, 0, 1) });
            }
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        return table;
    }

    /// <summary>A code block: language, Copy button and the code in a monospaced, selectable box.</summary>
    public static UIElement CodeView(string language, string code)
    {
        var copyLabel = new TextBlock { Text = tr("Copiar", "Copy"), FontSize = 12, Foreground = Theme.Secondary, Margin = new Thickness(6, 0, 0, 0) };
        var copyIcon = Theme.Icon(Theme.GlyphCopy, 12);
        var copy = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { copyIcon, copyLabel } },
            Template = Theme.FlatTemplate(4), Background = Brushes.Transparent, Padding = new Thickness(6, 2, 6, 2),
            Cursor = System.Windows.Input.Cursors.Hand, Focusable = false,
        };
        copy.Click += async (s, e) =>
        {
            Clip.Set(code);
            copyIcon.Text = Theme.GlyphCheck;
            copyLabel.Text = tr("Copiado", "Copied");
            await Task.Delay(1500);
            copyIcon.Text = Theme.GlyphCopy;
            copyLabel.Text = tr("Copiar", "Copy");
        };
        var header = new DockPanel { Margin = new Thickness(12, 5, 6, 5) };
        DockPanel.SetDock(copy, Dock.Right);
        header.Children.Add(copy);
        header.Children.Add(new TextBlock
        {
            Text = language.Length == 0 ? "code" : language, FontSize = 12, Foreground = Theme.Secondary,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var body = new TextBox
        {
            Text = code, IsReadOnly = true, FontFamily = Theme.Mono, FontSize = 13, Foreground = Theme.Text,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(12, 10, 12, 12),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            TextWrapping = TextWrapping.NoWrap, MinHeight = 0,
        };
        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(new Border { Height = 1, Background = Theme.Stroke });
        stack.Children.Add(body);
        return new Border
        {
            CornerRadius = new CornerRadius(8), Background = Theme.Code, BorderBrush = Theme.Stroke,
            BorderThickness = new Thickness(1), Child = stack,
        };
    }
}

/// <summary>Clipboard access that survives a busy clipboard.</summary>
internal static class Clip
{
    public static void Set(string text)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(text); return; }
            catch { Thread.Sleep(50); }
        }
    }
}
