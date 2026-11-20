//
//  SelfTest.cs
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

using System.Windows.Media;

namespace Citadel;

/// <summary>
/// End-to-end check driven through the same ChatModel the window uses:
///   Citadel.exe --self-test --memory-folder &lt;empty folder&gt; [--data-folder &lt;folder&gt;]
///   Citadel.exe --self-test --offline      (only the checks that need no Ollama)
/// Nothing is written to the conversations folder; memories go only to the given folder. The search,
/// export and update checks run first, need no Ollama, and use made-up conversations in a temporary folder.
/// </summary>
internal static class SelfTest
{
    private static int failures;

    private static void Say(string s)
    {
        Console.WriteLine(s);
        Console.Out.Flush();
    }

    private static void Check(bool ok, string what)
    {
        Say((ok ? "PASS  " : "FAIL  ") + what);
        if (!ok) failures++;
    }

    private static async Task<bool> WaitWhile(double seconds, Func<bool> busy)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (busy())
        {
            if (DateTime.UtcNow > until) return false;
            await Task.Delay(200);
        }
        return true;
    }

    public static async Task<bool> Run(ChatModel m, bool offline = false)
    {
        failures = 0;
        await OfflineChecks();
        if (offline)
        {
            Say(failures == 0 ? "ALL PASSED (offline)" : $"{failures} FAILED (offline)");
            return failures == 0;
        }
        if (Paths.MemoryOverride == null)
        {
            Say("FAIL  --self-test needs --memory-folder <temporary folder>");
            return false;
        }
        // Never write test memories into a folder the user really uses.
        string folder = Path.GetFullPath(MemoryStore.Folder).TrimEnd('\\');
        var inUse = new List<string> { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Citadel", "memories") };
        if (!string.IsNullOrEmpty(Settings.Current.MemoryFolder)) inUse.Add(Environment.ExpandEnvironmentVariables(Settings.Current.MemoryFolder));
        if (inUse.Any(f => string.Equals(Path.GetFullPath(f).TrimEnd('\\'), folder, StringComparison.OrdinalIgnoreCase)))
        {
            Say("FAIL  refusing to test against a memory folder in use: " + folder);
            return false;
        }
        Say("memory folder: " + folder);
        bool savedReason = Settings.Current.Reason;

        // Theme: twelve colours with their icons; light and dark give different colours; a change is announced.
        var (savedLook, savedAccent) = (Settings.Current.Appearance, Settings.Current.Accent);
        int loaded = 0;
        foreach (var acc in Theme.Accents) { try { if (Theme.AccentIcon(acc.Key).Width > 0) loaded++; } catch { } }
        Check(Theme.Accents.Length == 12 && loaded == 12, $"twelve theme colours, each with its icon ({loaded})");
        int changes = 0;
        void count() => changes++;
        Theme.Changed += count;
        Theme.SetAppearance("light");
        var lightWindow = ((SolidColorBrush)Theme.Window).Color;
        Theme.SetAppearance("dark");
        var darkWindow = ((SolidColorBrush)Theme.Window).Color;
        Theme.SetAccent("purple");
        Check(lightWindow.R > 200 && darkWindow.R < 60 && changes == 3, $"light and dark palettes, changes announced ({lightWindow} {darkWindow} {changes})");
        Check(((SolidColorBrush)Theme.Accent).Color == Color.FromRgb(0xB3, 0x92, 0xE0), "the accent takes the chosen colour");
        Theme.Changed -= count;
        Settings.Current.Appearance = savedLook; Settings.Current.Accent = savedAccent;

        await m.RefreshStatus();
        Check(m.Reachable == true, "Ollama reachable");
        Say("      models: " + string.Join(", ", m.Models));
        Check(m.Models.Contains(ChatModel.DefaultModel), $"default model {ChatModel.DefaultModel} installed");
        if (m.Models.Contains(ChatModel.DefaultModel)) Settings.Current.Model = ChatModel.DefaultModel;
        Check(ChatModel.SystemPrompt(new Conversation()).StartsWith("## How to answer (every reply)"), "system message starts with the rules file");

        // 1. Reason off
        Settings.Current.Reason = false;
        m.Composer = "What is 17 times 23? Reply with only the number.";
        m.Send();
        Check(m.IsGenerating, "stream started (Reason off)");
        await WaitWhile(300, () => m.IsGenerating);
        var a = m.Current?.Messages.LastOrDefault(x => x.Role == Role.Assistant);
        if (a != null)
        {
            Say("      answer: " + a.Content.Trim());
            Say($"      {a.Stats?.TokensPerSecond:0.0} tok/s, {a.Stats?.Seconds:0.0} s, thinking chars: {a.Thinking.Length}");
            Check(a.Content.Length > 0 && a.Thinking.Length == 0, "answer streamed with no thinking (Reason off)");
            Check(a.Content.Contains("391"), "answer is correct (391)");
        }
        else Check(false, "answer streamed (Reason off)");

        // 2. Reason on
        Settings.Current.Reason = true;
        m.Composer = "How many times does the letter r appear in the word strawberry? Reply with only the number.";
        m.Send();
        await WaitWhile(400, () => m.IsGenerating);
        a = m.Current?.Messages.LastOrDefault(x => x.Role == Role.Assistant);
        if (a != null)
        {
            Say("      answer: " + a.Content.Trim());
            Say($"      {a.Stats?.TokensPerSecond:0.0} tok/s, {a.Stats?.Seconds:0.0} s, thinking chars: {a.Thinking.Length}");
            Check(a.Thinking.Length > 0, "thinking streamed (Reason on)");
            Check(a.Content.Length > 0, "answer streamed after thinking (Reason on)");
        }
        else Check(false, "answer streamed (Reason on)");

        // 3. Stop closes the stream
        Settings.Current.Reason = false;
        m.Composer = "Write a long story of about 800 words about a lighthouse keeper.";
        m.Send();
        await Task.Delay(4000);
        m.Stop();
        bool stopped = await WaitWhile(15, () => m.IsGenerating);
        var last = m.Current?.Messages.LastOrDefault(x => x.Role == Role.Assistant);
        Check(stopped && last?.Stopped == true, $"Stop cancels the stream ({last?.Content.Length ?? 0} chars kept)");

        // 4. /save writes a memory and its index line
        MemoryDraft draft = null;
        void OnDraft(MemoryDraft d) => draft = d;
        m.DraftReady += OnDraft;
        m.Composer = "/save";
        m.Send();
        await WaitWhile(300, () => m.IsDraftingMemory);
        m.DraftReady -= OnDraft;
        if (draft != null)
        {
            Say("      draft file: " + draft.File);
            Say("      draft index line: " + draft.IndexLine);
            var lines = draft.Content.Split('\n');
            Check(lines.Length >= 2 && lines[0].StartsWith("# ") && lines[1].StartsWith("_Updated: ") && lines[1].Contains($"| Model: {m.SelectedModel}_"),
                  "memory starts with title and _Updated line");
            bool saved = m.ConfirmMemory(draft, out string error);
            string path = Path.Combine(MemoryStore.Folder, draft.File);
            byte[] raw = File.Exists(path) ? File.ReadAllBytes(path) : [];
            bool noBom = !(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF);
            Check(saved && raw.Length > 0 && noBom, $"memory file written (UTF-8, no BOM): {path} {error}");
            Check(MemoryStore.IndexText().Contains($"`{draft.File}` — "), "index line written in _index.md");
            Say("      _index.md: " + MemoryStore.IndexText().Trim());

            // 5. /mem loads it back
            m.NewConversation();
            m.Composer = "/mem " + draft.IndexLine;
            m.Send();
            var chip = m.Current?.Messages.LastOrDefault();
            Check(chip?.Role == Role.Memory && chip.MemoryFile == draft.File, $"/mem loads {draft.File}");
            Check(ChatModel.SystemPrompt(m.Current).Contains("### " + draft.File), "loaded memory is in the system message");
        }
        else Check(false, "/save produced a draft");
        m.Composer = "/mem zzzz qqqq";
        m.Send();
        Check(m.Current?.Messages.LastOrDefault()?.Role == Role.Notice, "/mem with no match says so");

        Settings.Current.Reason = savedReason;
        Say(failures == 0 ? "ALL PASSED" : $"{failures} FAILED");
        return failures == 0;
    }

    private static ChatMessage Msg(Role role, string content, DateTime date) => new() { Role = role, Content = content, Date = date };

    /// <summary>Search, Markdown export and the pure parts of the updates. No Ollama, no network, no real data.</summary>
    private static async Task OfflineChecks()
    {
        string temp = Path.Combine(Path.GetTempPath(), "citadel-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        Say("temporary folder: " + temp);
        try
        {
            // Search: made-up conversations, saved and read back from a temporary data folder, never the real one.
            var day = new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc);
            var older = new Conversation { Title = "Trip", Created = day.AddDays(-2), Updated = day.AddDays(-2) };
            older.Messages.Add(Msg(Role.User, "When does the truck arrive?", day.AddDays(-2)));
            older.Messages.Add(Msg(Role.Assistant, "El Camión llegó a las nueve. Son dos\n  palabras juntas.", day.AddDays(-2)));
            var newer = new Conversation { Title = "Code", Created = day, Updated = day };
            newer.Messages.Add(Msg(Role.User, "Show PYTHON code, and the camion word once more.", day));
            newer.Messages.Add(Msg(Role.Assistant, "```python\nprint('hi')\n```", day));
            newer.Messages.Add(new ChatMessage { Role = Role.Notice, Content = "camion in a notice is not searched", Date = day });
            var echo = new Conversation { Title = "Echo", Created = day.AddDays(-5), Updated = day.AddDays(-5) };
            for (int i = 0; i < 8; i++) echo.Messages.Add(Msg(Role.User, $"eco {i}", day.AddDays(-5)));
            var longText = new Conversation { Title = "Long", Created = day.AddDays(-6), Updated = day.AddDays(-6) };
            longText.Messages.Add(Msg(Role.Assistant, string.Concat(Enumerable.Repeat("word ", 30)) + "needle " + string.Concat(Enumerable.Repeat("tail ", 40)), day.AddDays(-6)));

            string savedOverride = Paths.DataOverride;
            List<Conversation> loaded;
            try
            {
                Paths.DataOverride = Path.Combine(temp, "data");
                foreach (var c in new[] { older, newer, echo, longText }) ConversationStore.Save(c);
                loaded = ConversationStore.LoadAll();
            }
            finally { Paths.DataOverride = savedOverride; }
            Check(loaded.Count == 4 && loaded[0].Id == newer.Id, $"search: four test conversations saved and read back ({loaded.Count})");

            var hits = ConversationSearch.Find(loaded, "camion");
            Check(hits.Count == 2 && hits[0].Conversation.Id == newer.Id && hits[1].Match == "Camión",
                  $"search: no case or accents, newest first ({string.Join(" | ", hits.Select(h => h.Match))})");
            hits = ConversationSearch.Find(loaded, "  DOS palabras ");
            Check(hits.Count == 1 && hits[0].Match == "dos palabras" && hits[0].Before.EndsWith("Son ") && hits[0].After == " juntas.",
                  $"search: across a line break, with the text around it (\"{hits.FirstOrDefault()?.Before}[{hits.FirstOrDefault()?.Match}]{hits.FirstOrDefault()?.After}\")");
            hits = ConversationSearch.Find(loaded, "print('hi')");
            Check(hits.Count == 1 && hits[0].Message.Role == Role.Assistant, "search: inside an answer's code block");
            Check(ConversationSearch.Find(loaded, "eco").Count == 5, "search: at most five results per conversation");
            Check(ConversationSearch.Find(loaded, "zzqq").Count == 0 && ConversationSearch.Find(loaded, "a").Count == 0 && ConversationSearch.Find(loaded, "notice").Count == 0,
                  "search: nothing for no match, a single letter or a note from the app");
            hits = ConversationSearch.Find(loaded, "needle");
            Check(hits.Count == 1 && hits[0].Before.StartsWith("…word") && hits[0].After.EndsWith("tail…") && hits[0].Before.Length < 50,
                  $"search: a long message is cut around the match ({hits.FirstOrDefault()?.Before.Length} + {hits.FirstOrDefault()?.After.Length} chars)");

            // Export: one conversation with every kind of turn, compared with the expected Markdown.
            var conv = new Conversation { Title = "Sorting a list", Created = day, Updated = day.AddMinutes(5) };
            var question = Msg(Role.User, "How do I sort a list in Python?", day);
            question.Attachments.Add(new Attachment { Name = "notes.md", Text = "Use ```sorted```.\n" });
            conv.Messages.Add(question);
            conv.Messages.Add(new ChatMessage
            {
                Role = Role.Assistant, Thinking = "The user wants sorting.", Content = "Use `sorted()`:\n\n```python\nprint(sorted([3, 1, 2]))\n```",
                Stats = new AnswerStats { Model = "qwen3:8b", Tokens = 20, TokensPerSecond = 25.04, Seconds = 3.21 },
            });
            conv.Messages.Add(new ChatMessage { Role = Role.Memory, MemoryFile = "python.md", Content = "# Python\n" });
            conv.Messages.Add(new ChatMessage { Role = Role.Notice, Content = "Ollama is not reachable" });
            conv.Messages.Add(Msg(Role.User, "And in reverse?", day));
            conv.Messages.Add(new ChatMessage { Role = Role.Assistant, Content = "```python\nprint(sorted(x, reverse=True))", Stopped = true });
            const string expected =
                "# Sorting a list\n\n" +
                "- Date: 2026-11-20 09:00\n" +
                "- Last activity: 2026-11-20 09:05\n" +
                "- Model: qwen3:8b\n" +
                "- Exported with Citadel 1.3.0\n\n" +
                "## User\n\n" +
                "How do I sort a list in Python?\n\n" +
                "<details>\n<summary>Attachment: notes.md</summary>\n\n````text\nUse ```sorted```.\n````\n\n</details>\n\n" +
                "## Assistant\n\n" +
                "<details>\n<summary>Reasoning</summary>\n\n```text\nThe user wants sorting.\n```\n\n</details>\n\n" +
                "Use `sorted()`:\n\n```python\nprint(sorted([3, 1, 2]))\n```\n\n" +
                "_qwen3:8b · 25.0 tok/s · 3.2 s_\n\n" +
                "> Memory loaded: `python.md`\n\n" +
                "## User\n\n" +
                "And in reverse?\n\n" +
                "## Assistant\n\n" +
                "```python\nprint(sorted(x, reverse=True))\n```\n\n" +
                "_stopped_\n";
            string file = Path.Combine(temp, MarkdownExport.FileName(conv));
            Paths.WriteText(file, MarkdownExport.Render(conv, UiLanguage.En, TimeZoneInfo.Utc, "1.3.0"));
            byte[] raw = File.ReadAllBytes(file);
            string written = new System.Text.UTF8Encoding(false).GetString(raw);
            bool same = written == expected;
            if (!same)
            {
                int at = Enumerable.Range(0, Math.Min(written.Length, expected.Length)).FirstOrDefault(i => written[i] != expected[i], Math.Min(written.Length, expected.Length));
                Say("      first difference at " + at + ": " + written[Math.Max(0, at - 20)..Math.Min(written.Length, at + 40)].Replace("\n", "\\n"));
            }
            Check(same && raw[0] != 0xEF, $"export: the .md file is exactly the expected Markdown, UTF-8 without BOM ({Path.GetFileName(file)}, {raw.Length} bytes)");
            string es = MarkdownExport.Render(conv, UiLanguage.Es, TimeZoneInfo.Utc, "1.3.0");
            Check(es.Contains("\n## Usuario\n") && es.Contains("\n## Asistente\n") && es.Contains("- Fecha: 2026-11-20 09:00\n") && es.EndsWith("_detenida_\n"),
                  "export: Spanish headings and labels");
            Check(MarkdownExport.FileName(new Conversation { Title = "a/b: c?" }) == "a-b- c.md" && MarkdownExport.FileName(new Conversation()) == "Citadel.md",
                  "export: file name without forbidden characters");
            Check(MarkdownExport.CloseFences("text\n```cs\nx\n```") == "text\n```cs\nx\n```" && MarkdownExport.CloseFences("```\nx") == "```\nx\n```",
                  "export: a code block left open is closed, a closed one is left alone");

            // Updates: version comparison, SHA256SUMS.txt, the release answer and the check of the download.
            Check(MainWindow.IsNewer("v1.3.0", "1.2.0") && MainWindow.IsNewer("1.10.0", "1.9.0") && !MainWindow.IsNewer("v1.2.0", "1.2.0")
                  && !MainWindow.IsNewer("1.1.9", "1.2.0") && !MainWindow.IsNewer("latest", "1.2.0") && !MainWindow.IsNewer("", "1.2.0"),
                  "update: IsNewer compares versions, not text");
            string h1 = new('a', 64), h2 = new('B', 64), h3 = new('c', 64);
            string sums = $"{h1}  citadel-setup.exe\n{h2} *Citadel.exe\r\n{h3}  citadel.msi\nnot a line\n{new string('d', 63)}  short.exe\n";
            Check(MainWindow.HashFor(sums, "citadel-setup.exe") == h1 && MainWindow.HashFor(sums, "Citadel.exe") == h2.ToLowerInvariant()
                  && MainWindow.HashFor(sums, "CITADEL.MSI") == h3 && MainWindow.HashFor(sums, "short.exe") == null && MainWindow.HashFor(sums, "other.exe") == null,
                  "update: HashFor reads both SHA256SUMS.txt formats and ignores bad lines");
            var release = MainWindow.ParseRelease("""
                { "tag_name": "v1.3.0", "html_url": "https://github.com/Gabriel-Castiglia/citadel-windows/releases/tag/v1.3.0",
                  "assets": [
                    { "name": "citadel-setup.exe", "browser_download_url": "https://example.invalid/citadel-setup.exe" },
                    { "name": "citadel.msi", "browser_download_url": "https://example.invalid/citadel.msi" },
                    { "name": "Citadel.exe", "browser_download_url": "https://example.invalid/Citadel.exe" },
                    { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.invalid/SHA256SUMS.txt" } ] }
                """);
            Check(release.Tag == "1.3.0" && release.SetupUrl.EndsWith("/citadel-setup.exe") && release.SumsUrl.EndsWith("/SHA256SUMS.txt") && release.Page.EndsWith("/v1.3.0"),
                  "update: the release gives its version, page, setup and hashes");
            string download = Path.Combine(temp, "citadel-setup.exe");
            File.WriteAllText(download, "not really a setup");
            string good = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(download))).ToLowerInvariant();
            bool kept = await MainWindow.VerifyDownload(download, good.ToUpperInvariant()) && File.Exists(download);
            bool thrown = !await MainWindow.VerifyDownload(download, h1) && !File.Exists(download);
            Check(kept && thrown, "update: a download that matches its hash is kept; one that does not is deleted");
        }
        catch (Exception ex) { Check(false, "offline checks: " + ex); }
        finally
        {
            try { Directory.Delete(temp, true); } catch { }
        }
    }
}
