//
//  AppData.cs
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

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Citadel;

/// <summary>Folders and files used by Citadel.</summary>
internal static class Paths
{
    /// <summary>Set by "--data-folder" (tests and screenshots), so real conversations are never touched.</summary>
    public static string DataOverride;

    /// <summary>Set by "--memory-folder" (tests); wins over the setting.</summary>
    public static string MemoryOverride;

    /// <summary>%APPDATA%\Citadel, or the folder given with --data-folder.</summary>
    public static string Data => DataOverride ??
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Citadel");

    public static string Conversations => Path.Combine(Data, "conversations");
    public static string Rules => Path.Combine(Data, "rules.md");
    public static string SettingsFile => Path.Combine(Data, "settings.json");
    public static string Log => Path.Combine(Data, "citadel.log");
    public static string DefaultMemoryFolder => Path.Combine(Data, "memories");

    /// <summary>The path with %APPDATA% or %USERPROFILE% in place of the user's own folders.</summary>
    public static string Short(string path)
    {
        foreach (var (folder, name) in new[] { (Environment.SpecialFolder.ApplicationData, "%APPDATA%"), (Environment.SpecialFolder.UserProfile, "%USERPROFILE%") })
        {
            string root = Environment.GetFolderPath(folder);
            if (root.Length > 0 && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return name + path[root.Length..];
        }
        return path;
    }

    /// <summary>Writes UTF-8 text without a byte order mark, replacing the file in one step.</summary>
    public static void WriteText(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        string temp = path + ".tmp";
        File.WriteAllText(temp, text, new System.Text.UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}

/// <summary>User settings, stored in settings.json.</summary>
internal sealed class Settings
{
    public string Language { get; set; }
    public string Model { get; set; }
    public bool Reason { get; set; }
    public string MemoryFolder { get; set; }
    public double[] Window { get; set; }

    [JsonIgnore] public bool Persist = true;

    public static Settings Current { get; private set; } = new();

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Reads settings.json. On a first start, the language comes from the setup program, or English.</summary>
    public static void Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
                Current = JsonSerializer.Deserialize<Settings>(File.ReadAllText(Paths.SettingsFile)) ?? new Settings();
        }
        catch (Exception ex) { Program.Log("Could not read settings.json: " + ex.Message); }
        if (string.IsNullOrEmpty(Current.Language))
        {
            try { Current.Language = Registry.LocalMachine.OpenSubKey(@"Software\Citadel")?.GetValue("Language") as string; }
            catch { }
        }
        Loc.Current = Loc.FromCode(Current.Language);
    }

    public void Save()
    {
        if (!Persist) return;
        Language = Loc.Code(Loc.Current);
        try { Paths.WriteText(Paths.SettingsFile, JsonSerializer.Serialize(this, Json)); }
        catch (Exception ex) { Program.Log("Could not write settings.json: " + ex.Message); }
    }
}

/// <summary>The rules file, sent at the start of every request.</summary>
internal static class Rules
{
    public const string DefaultText =
        "## How to answer (every reply)\n" +
        "1. Answer in the user's language. Answer exactly what was asked. If the user asks for only the result, give only the result: no preamble, no explanation, no closing offer.\n" +
        "2. Do not invent. If you do not know, if it happened after your training data, if it is private data you cannot access, or if a book, person or event may not exist, say that you do not know (\"No lo sé\" in Spanish) and why, in one sentence. Never make up details.\n" +
        "3. Check the premise. If the question assumes something false, say it is false first.\n" +
        "4. Exact formats: when asked for only JSON, only a list, or exactly N items, output exactly that and nothing else.\n" +
        "5. Code: put it in one fenced Markdown code block (```python ... ```) with the complete function; no input() and no example calls unless asked.\n" +
        "6. Translation: copy text between backticks, paths, commands, numbers and names unchanged; reply only with the translation.\n" +
        "7. You run locally on this computer through Ollama; nothing the user writes leaves the computer.\n";

    /// <summary>Creates the rules file with the default rules on first run.</summary>
    public static void EnsureExists()
    {
        if (!File.Exists(Paths.Rules)) Paths.WriteText(Paths.Rules, DefaultText);
    }

    public static string Load()
    {
        try
        {
            EnsureExists();
            return File.ReadAllText(Paths.Rules);
        }
        catch { return DefaultText; }
    }

    public static void Save(string text) => Paths.WriteText(Paths.Rules, text);
}

/// <summary>Small text helpers shared by the memory search and the conversation search.</summary>
internal static class Text
{
    /// <summary>Lowercase, without accents.</summary>
    public static string Fold(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in s.Normalize(System.Text.NormalizationForm.FormD))
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString().Normalize(System.Text.NormalizationForm.FormC);
    }

    /// <summary>Folded words made of letters and digits.</summary>
    public static List<string> Words(string s)
    {
        var words = new List<string>();
        var sb = new System.Text.StringBuilder();
        foreach (char c in Fold(s) + " ")
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (sb.Length > 0) { words.Add(sb.ToString()); sb.Clear(); }
        }
        return words;
    }
}
