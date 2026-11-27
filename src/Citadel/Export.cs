//
//  Export.cs
//  Citadel: local AI chat for Windows.
//
//  Copyright (C) 2026 Gabriel Castiglia. Licensed under the GNU GPL, version 2 only.
//  Date: November 20, 2026
//  SPDX-License-Identifier: GPL-2.0-only
//
//  This program is free software: you can redistribute it and/or modify it under the terms of
//  the GNU General Public License, version 2, as published by the Free Software Foundation.
//  It is distributed WITHOUT ANY WARRANTY. See the LICENSE file for details.
//

using System.Globalization;

namespace Citadel;

/// <summary>
/// A conversation as a Markdown file: the title, the date, the model and every turn under its own heading.
/// Answers are already Markdown and are copied as they are, so their code blocks stay intact; an answer
/// stopped in the middle of a code block gets its closing fence, so the next heading is not swallowed.
/// Attachments and reasoning go in folded &lt;details&gt; blocks. Notes from the app are left out.
/// </summary>
internal static class MarkdownExport
{
    /// <summary>The Markdown text, with "\n" line endings and a final line break.</summary>
    public static string Render(Conversation c, UiLanguage language, TimeZoneInfo zone, string version)
    {
        string T(string s, string e, string j) => language switch { UiLanguage.Es => s, UiLanguage.Ja => j, _ => e };
        string Date(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone)
                                                 .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var md = new System.Text.StringBuilder();
        void Line(string s = "") => md.Append(s).Append('\n');

        string title = c.Title.Length == 0 ? T("Conversación nueva", "New conversation", "新しい会話") : c.Title;
        Line("# " + title.Replace('\n', ' ').Trim());
        Line();
        var models = c.Messages.Where(m => m.Role == Role.Assistant && !string.IsNullOrEmpty(m.Stats?.Model))
                               .Select(m => m.Stats.Model).Distinct().ToList();
        Line($"- {T("Fecha", "Date", "日付")}: {Date(c.Created)}");
        if (c.Updated - c.Created >= TimeSpan.FromMinutes(1)) Line($"- {T("Última actividad", "Last activity", "最終更新")}: {Date(c.Updated)}");
        Line($"- {(models.Count > 1 ? T("Modelos", "Models", "モデル") : T("Modelo", "Model", "モデル"))}: {(models.Count == 0 ? "-" : string.Join(", ", models))}");
        Line($"- {T("Exportada con", "Exported with", "エクスポート元:")} Citadel {version}");

        foreach (var m in c.Messages)
        {
            switch (m.Role)
            {
                case Role.User:
                    Line();
                    Line("## " + T("Usuario", "User", "ユーザー"));
                    Line();
                    if (m.Content.Length > 0) Line(Normalize(m.Content));
                    foreach (var a in m.Attachments)
                    {
                        if (m.Content.Length > 0 || a != m.Attachments[0]) Line();
                        Details(md, T("Adjunto: ", "Attachment: ", "添付ファイル: ") + a.Name, a.Text);
                    }
                    break;
                case Role.Assistant:
                    Line();
                    Line("## " + T("Asistente", "Assistant", "アシスタント"));
                    Line();
                    if (m.Thinking.Trim().Length > 0)
                    {
                        Details(md, T("Razonamiento", "Reasoning", "推論の過程"), m.Thinking);
                        Line();
                    }
                    if (m.Content.Length > 0) Line(CloseFences(Normalize(m.Content)));
                    var notes = new List<string>();
                    if (m.Stats is { } s)
                    {
                        if (s.Model.Length > 0) notes.Add(s.Model);
                        if (s.TokensPerSecond > 0) notes.Add(s.TokensPerSecond.ToString("0.0", CultureInfo.InvariantCulture) + " tok/s");
                        if (s.Seconds > 0) notes.Add(s.Seconds.ToString("0.0", CultureInfo.InvariantCulture) + " s");
                    }
                    if (m.Stopped) notes.Add(T("detenida", "stopped", "停止"));
                    if (notes.Count > 0)
                    {
                        if (m.Content.Length > 0) Line();
                        Line("_" + string.Join(" · ", notes) + "_");
                    }
                    break;
                case Role.Memory:
                    Line();
                    Line($"> {T("Memoria cargada", "Memory loaded", "読み込んだメモリ")}: `{m.MemoryFile ?? "memory.md"}`");
                    break;
            }
        }
        return md.ToString();
    }

    /// <summary>"\n" line endings and no line breaks at the start or the end.</summary>
    private static string Normalize(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n').Trim('\n').TrimEnd();

    /// <summary>Text shown folded: a &lt;details&gt; block with the text in a fence longer than any inside it.</summary>
    private static void Details(System.Text.StringBuilder md, string summary, string text)
    {
        int longest = 0, run = 0;
        foreach (char ch in text) { run = ch == '`' ? run + 1 : 0; longest = Math.Max(longest, run); }
        string fence = new('`', Math.Max(3, longest + 1));
        md.Append("<details>\n<summary>").Append(System.Net.WebUtility.HtmlEncode(summary)).Append("</summary>\n\n")
          .Append(fence).Append("text\n").Append(Normalize(text)).Append('\n').Append(fence).Append("\n\n</details>\n");
    }

    /// <summary>Adds the closing fence of a code block left open (an answer stopped while writing code).</summary>
    public static string CloseFences(string text)
    {
        string open = null;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimStart();
            int n = 0;
            while (n < line.Length && (line[n] == '`' || line[n] == '~') && line[n] == line[0]) n++;
            if (n < 3) continue;
            string marker = line[..n];
            if (open == null) open = marker;
            else if (marker[0] == open[0] && marker.Length >= open.Length && line[n..].Trim().Length == 0) open = null;
        }
        return open == null ? text : text + "\n" + open;
    }

    /// <summary>A file name made from the title: no characters Windows forbids, at most 80 characters.</summary>
    public static string FileName(Conversation c)
    {
        var invalid = Path.GetInvalidFileNameChars();
        string name = new(c.Title.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray());
        name = string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Trim('.', ' ', '-');
        if (name.Length > 80) name = Text.Cut(name, 80).TrimEnd('.', ' ');
        // Names Windows reserves for devices (CON, NUL, COM1...) cannot be files, with or without an extension.
        if (ReservedNames.Contains(name.Split('.')[0].TrimEnd())) name = "Citadel - " + name;
        return (name.Length == 0 ? "Citadel" : name) + ".md";
    }

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };
}
