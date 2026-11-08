//
//  Memory.cs
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

using System.Text.Json.Nodes;
using static Citadel.Loc;

namespace Citadel;

/// <summary>One line of the memory index: `file.md` — topic.</summary>
internal sealed record MemoryEntry(string File, string Topic);

/// <summary>A memory proposed by the model, waiting for the user's confirmation.</summary>
internal sealed class MemoryDraft
{
    public string File = "";
    public string IndexLine = "";
    public string Content = "";
}

/// <summary>Reads and writes the memory folder. The app writes memories, never the model.</summary>
internal static class MemoryStore
{
    public const string IndexName = "_index.md";

    /// <summary>The folder in use: --memory-folder, then Settings, then the default.</summary>
    public static string Folder =>
        Paths.MemoryOverride ??
        (string.IsNullOrEmpty(Settings.Current.MemoryFolder) ? Paths.DefaultMemoryFolder
                                                             : Environment.ExpandEnvironmentVariables(Settings.Current.MemoryFolder));

    public static string IndexPath => Path.Combine(Folder, IndexName);

    public static string IndexText()
    {
        try { return File.Exists(IndexPath) ? File.ReadAllText(IndexPath) : ""; } catch { return ""; }
    }

    public static (string file, string topic)? ParseIndexLine(string line)
    {
        int open = line.IndexOf('`');
        if (open < 0) return null;
        int close = line.IndexOf('`', open + 1);
        if (close < 0) return null;
        string file = line[(open + 1)..close].Trim();
        if (!file.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || file.Contains('/') || file.Contains('\\')) return null;
        string topic = line[(close + 1)..].Trim();
        foreach (string sep in new[] { "—", "–", "-", ":" })
            if (topic.StartsWith(sep)) { topic = topic[sep.Length..].Trim(); break; }
        return (file, topic);
    }

    /// <summary>Every index line that names a file between backticks.</summary>
    public static List<MemoryEntry> Entries()
    {
        var list = new List<MemoryEntry>();
        foreach (string line in IndexText().Replace("\r\n", "\n").Split('\n'))
            if (ParseIndexLine(line) is var (file, topic) && !list.Any(e => e.File == file))
                list.Add(new MemoryEntry(file, topic));
        return list;
    }

    /// <summary>The index entries plus any .md file in the folder that the index misses.</summary>
    public static List<MemoryEntry> All()
    {
        var list = Entries();
        try
        {
            if (Directory.Exists(Folder))
                foreach (string f in Directory.GetFiles(Folder, "*.md").Select(Path.GetFileName).Order())
                    if (f != IndexName && !list.Any(e => e.File == f)) list.Add(new MemoryEntry(f, ""));
        }
        catch { }
        return list;
    }

    private static readonly HashSet<string> StopWords =
    [
        "the", "and", "for", "with", "about", "from", "that", "this", "what", "las", "los", "del",
        "que", "con", "por", "para", "una", "uno", "sobre", "como", "mem", "md",
    ];

    /// <summary>Best index entry for a topic by simple word overlap, accents ignored.</summary>
    public static MemoryEntry BestMatch(string topic)
    {
        var query = Text.Words(topic).Where(w => w.Length >= 2 && !StopWords.Contains(w)).ToHashSet();
        if (query.Count == 0) return null;
        MemoryEntry best = null;
        int bestScore = 0;
        foreach (var entry in Entries())
        {
            var fileWords = Text.Words(Path.GetFileNameWithoutExtension(entry.File)).ToHashSet();
            var topicWords = Text.Words(entry.Topic).ToHashSet();
            int score = query.Count(topicWords.Contains) * 2 + query.Count(fileWords.Contains) * 2;
            // Partial credit for prefixes (plural and singular, short forms).
            var all = topicWords.Union(fileWords).ToList();
            foreach (string q in query.Where(q => q.Length >= 4))
                if (all.Any(w => w != q && w.Length >= 4 && (w.StartsWith(q) || q.StartsWith(w)))) score++;
            if (score > bestScore) { best = entry; bestScore = score; }
        }
        return best;
    }

    public static string Read(string file)
    {
        try { return File.ReadAllText(Path.Combine(Folder, file)); } catch { return null; }
    }

    public static bool Exists(string file) => File.Exists(Path.Combine(Folder, file));

    /// <summary>Lowercase kebab-case file name ending in .md.</summary>
    public static string SanitizeFileName(string raw)
    {
        string name = (raw ?? "").Trim();
        if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) name = name[..^3];
        name = Path.GetFileName(name.Replace('/', '\\'));
        string slug = string.Join("-", Text.Words(name));
        if (slug.Length > 60) slug = slug[..60].Trim('-');
        return (slug.Length == 0 ? "memory" : slug) + ".md";
    }

    /// <summary>Makes sure line 2 is `_Updated: YYYY-MM-DD | Model: model_` after a "# " title.</summary>
    public static string NormalizeContent(string raw, string title, string model)
    {
        string stamp = $"_Updated: {DateTime.Now:yyyy-MM-dd} | Model: {model}_";
        var lines = (raw ?? "").Replace("\r\n", "\n").Split('\n').Where(l => !l.Trim().StartsWith("_Updated:")).ToList();
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        string titleLine = "# " + title;
        if (lines.Count > 0 && lines[0].StartsWith("# ")) { titleLine = lines[0].Trim(); lines.RemoveAt(0); }
        while (lines.Count > 0 && lines[0].Trim().Length == 0) lines.RemoveAt(0);
        var output = new List<string> { titleLine, stamp, "" };
        output.AddRange(lines);
        while (output.Count > 0 && output[^1].Trim().Length == 0) output.RemoveAt(output.Count - 1);
        return string.Join("\n", output) + "\n";
    }

    /// <summary>Writes the memory file and its index line, then verifies both on disk.</summary>
    public static string Save(MemoryDraft draft)
    {
        string file = SanitizeFileName(draft.File);
        string path = Path.Combine(Folder, file);
        Paths.WriteText(path, draft.Content);

        string topic = draft.IndexLine.Replace("\r", " ").Replace("\n", " ").Replace($"`{file}`", "").Trim();
        string newLine = $"`{file}` — {topic}";
        var lines = IndexText().Replace("\r\n", "\n").Split('\n').ToList();
        if (lines.Count > 0 && lines[^1] == "") lines.RemoveAt(lines.Count - 1);
        int i = lines.FindIndex(l => ParseIndexLine(l)?.file == file);
        if (i >= 0) lines[i] = newLine; else lines.Add(newLine);
        Paths.WriteText(IndexPath, string.Join("\n", lines) + "\n");

        // Confirm only what is really on disk.
        if (!File.Exists(path) || File.ReadAllText(path) != draft.Content || !Entries().Any(e => e.File == file))
            throw new IOException(tr("No se pudo verificar la memoria guardada en el disco.", "Could not verify the saved memory on disk."));
        return path;
    }

    /// <summary>Deletes a memory file and its index line.</summary>
    public static void Delete(string file)
    {
        string path = Path.Combine(Folder, file);
        if (File.Exists(path)) File.Delete(path);
        var lines = IndexText().Split('\n').ToList();
        int before = lines.Count;
        lines.RemoveAll(l => ParseIndexLine(l)?.file == file);
        if (lines.Count != before) Paths.WriteText(IndexPath, string.Join("\n", lines));
    }

    /// <summary>Asks the model (without reasoning) to write a memory from the conversation, as JSON.</summary>
    public static async Task<MemoryDraft> Draft(string transcript, string model, CancellationToken token)
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        string existing = string.Join("\n", Entries().Select(e => $"- `{e.File}` — {e.Topic}"));
        string system =
            "You write one memory file for a local memory folder, from the conversation the user gives you.\n" +
            "Write it in the same language the user writes in. Keep only facts worth remembering in future conversations: decisions, preferences, data, results and open items. Never invent anything that is not in the conversation.\n" +
            $"The memory is Markdown: line 1 is a \"# \" title; line 2 is exactly `_Updated: {today} | Model: {model}_`; then the content, at most about 50 lines.\n" +
            "Reply with ONLY a JSON object with three string keys:\n" +
            "\"file\": a short kebab-case file name ending in .md,\n" +
            "\"index_line\": one line that describes the topic in the user's words (without the file name),\n" +
            "\"content\": the full Markdown of the memory.\n" +
            (existing.Length == 0 ? "" : "If one of these existing memories covers the same topic, reuse its file name:\n" + existing);
        string text = await Ollama.Complete(model, [("system", system), ("user", "Conversation:\n\n" + transcript)], false, 3000, true, token);
        JsonObject obj = ParseObject(text) ?? throw new OllamaException(tr("El modelo no devolvió un JSON válido.", "The model did not return valid JSON."));
        string file = SanitizeFileName((string)obj["file"] ?? "memory");
        string indexLine = ((string)obj["index_line"] ?? "").Trim();
        if (indexLine.Length == 0) indexLine = Path.GetFileNameWithoutExtension(file).Replace('-', ' ');
        return new MemoryDraft { File = file, IndexLine = indexLine, Content = NormalizeContent((string)obj["content"] ?? "", indexLine, model) };
    }

    private static JsonObject ParseObject(string text)
    {
        try { if (JsonNode.Parse(text) is JsonObject o) return o; } catch { }
        int a = text.IndexOf('{'), b = text.LastIndexOf('}');
        if (a < 0 || b <= a) return null;
        try { return JsonNode.Parse(text[a..(b + 1)]) as JsonObject; } catch { return null; }
    }
}
