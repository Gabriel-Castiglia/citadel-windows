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
/// Nothing is written to the conversations folder; memories go only to the given folder.
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

    public static async Task<bool> Run(ChatModel m)
    {
        failures = 0;
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
}
