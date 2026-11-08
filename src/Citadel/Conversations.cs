//
//  Conversations.cs
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
using static Citadel.Loc;

namespace Citadel;

/// <summary>A text file attached to a user message.</summary>
internal sealed class Attachment
{
    public string Name { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>Speed and time of a finished answer.</summary>
internal sealed class AnswerStats
{
    public string Model { get; set; } = "";
    public int Tokens { get; set; }
    public double TokensPerSecond { get; set; }
    public double Seconds { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<Role>))]
internal enum Role
{
    User,
    Assistant,
    /// <summary>A memory file loaded with /mem; sent to the model as context.</summary>
    Memory,
    /// <summary>A note from the app (errors, "nothing matched"); never sent to the model.</summary>
    Notice,
}

internal sealed class ChatMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Role Role { get; set; }
    public string Content { get; set; } = "";
    public string Thinking { get; set; } = "";
    public List<Attachment> Attachments { get; set; } = [];
    public string MemoryFile { get; set; }
    public AnswerStats Stats { get; set; }
    public bool Reasoned { get; set; }
    public bool Stopped { get; set; }
    public DateTime Date { get; set; } = DateTime.UtcNow;
}

internal sealed class Conversation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime Updated { get; set; } = DateTime.UtcNow;
    public List<ChatMessage> Messages { get; set; } = [];

    [JsonIgnore] public string DisplayTitle => Title.Length == 0 ? tr("Conversación nueva", "New conversation") : Title;

    /// <summary>Title = the first line of the first user message, trimmed to 60 characters.</summary>
    public static string TitleFrom(string text)
    {
        string line = text.Trim().Split('\n')[0].Trim();
        line = string.Join(' ', line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
        return line.Length > 60 ? line[..60].Trim() + "…" : line;
    }
}

/// <summary>Stores each conversation as one JSON file. Missing fields take their defaults when read.</summary>
internal static class ConversationStore
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static string FileFor(Guid id) => Path.Combine(Paths.Conversations, id.ToString().ToUpperInvariant() + ".json");

    public static List<Conversation> LoadAll()
    {
        var list = new List<Conversation>();
        try
        {
            Directory.CreateDirectory(Paths.Conversations);
            foreach (string f in Directory.GetFiles(Paths.Conversations, "*.json"))
            {
                try
                {
                    var c = JsonSerializer.Deserialize<Conversation>(File.ReadAllText(f), Json);
                    if (c != null) list.Add(c);
                }
                catch (Exception ex) { Program.Log($"Skipped {Path.GetFileName(f)}: {ex.Message}"); }
            }
        }
        catch (Exception ex) { Program.Log("Could not read the conversations: " + ex.Message); }
        return list.OrderByDescending(c => c.Updated).ToList();
    }

    public static void Save(Conversation c)
    {
        try { Paths.WriteText(FileFor(c.Id), JsonSerializer.Serialize(c, Json)); }
        catch (Exception ex) { Program.Log("Could not save a conversation: " + ex.Message); }
    }

    public static void Delete(Guid id)
    {
        try { File.Delete(FileFor(id)); } catch { }
    }
}
