//
//  Search.cs
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

namespace Citadel;

/// <summary>One place where the searched text appears: a message of a conversation, with the text around it.</summary>
internal sealed class SearchHit
{
    public Conversation Conversation { get; init; }
    public ChatMessage Message { get; init; }
    /// <summary>The fragment shown in the results: Before + Match + After, on one line.</summary>
    public string Before { get; init; } = "";
    public string Match { get; init; } = "";
    public string After { get; init; } = "";
}

/// <summary>
/// Search in the text of every saved conversation (what the user wrote and the answers). Upper and lower case,
/// accents and runs of spaces or line breaks do not matter: "camion" finds "Camión", "dos  palabras" finds
/// "dos\npalabras". The fragment keeps the original text, so the match is shown as it was written.
/// </summary>
internal static class ConversationSearch
{
    /// <summary>Shortest query searched in the messages; shorter ones would match almost everything.</summary>
    public const int MinLength = 2;

    /// <summary>Folded letters of the characters that are not plain ASCII, computed once each.</summary>
    private static readonly Dictionary<char, string> FoldCache = [];

    /// <summary>
    /// The text lowercased, without accents and with every run of white space as one space, and for each
    /// character of the result the position of the original character it came from.
    /// </summary>
    public static (string Folded, int[] Map) FoldWithMap(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        bool space = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                if (!space) { sb.Append(' '); map.Add(i); space = true; }
                continue;
            }
            space = false;
            string f;
            if (c < 128) f = char.ToLowerInvariant(c).ToString();
            else if (char.IsSurrogate(c)) f = c.ToString();
            else
            {
                lock (FoldCache)
                    if (!FoldCache.TryGetValue(c, out f)) FoldCache[c] = f = Text.Fold(c.ToString());
            }
            foreach (char x in f) { sb.Append(x); map.Add(i); }
        }
        return (sb.ToString(), [.. map]);
    }

    /// <summary>The query as it is compared: folded, trimmed, with single spaces.</summary>
    public static string FoldQuery(string query) => FoldWithMap(query.Trim()).Folded;

    /// <summary>
    /// Every message (of the user or an answer) that contains the query, in the order of the list given
    /// (most recent conversation first) and, inside a conversation, in the order of the messages.
    /// At most <paramref name="perConversation"/> hits per conversation and <paramref name="max"/> in all.
    /// </summary>
    public static List<SearchHit> Find(IEnumerable<Conversation> conversations, string query, int perConversation = 5, int max = 200)
    {
        var hits = new List<SearchHit>();
        string q = FoldQuery(query ?? "");
        if (q.Length < MinLength) return hits;
        foreach (var c in conversations)
        {
            int inThis = 0;
            foreach (var m in c.Messages)
            {
                if (m.Role is not (Role.User or Role.Assistant) || m.Content.Length == 0) continue;
                var (folded, map) = FoldWithMap(m.Content);
                int at = folded.IndexOf(q, StringComparison.Ordinal);
                if (at < 0) continue;
                int start = map[at];
                int end = map[at + q.Length - 1] + 1;
                // A character folded into several letters (or a surrogate pair) ends after its last part.
                if (end < m.Content.Length && char.IsLowSurrogate(m.Content[end])) end++;
                hits.Add(Fragment(c, m, start, end - start));
                if (++inThis >= perConversation) break;
                if (hits.Count >= max) return hits;
            }
        }
        return hits;
    }

    /// <summary>The match with some words before and after it, on one line, cut at spaces.</summary>
    private static SearchHit Fragment(Conversation c, ChatMessage m, int start, int length, int before = 40, int after = 90)
    {
        string text = m.Content;
        int from = Math.Max(0, start - before);
        if (from > 0)
        {
            int space = text.IndexOfAny([' ', '\n', '\t'], from);
            if (space >= 0 && space < start) from = space + 1;
        }
        int to = Math.Min(text.Length, start + length + after);
        if (to < text.Length)
        {
            int space = text.LastIndexOfAny([' ', '\n', '\t'], to - 1);
            if (space > start + length) to = space;
        }
        static string Line(string s) => string.Join(' ', s.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries));
        string pre = Line(text[from..start]), post = Line(text[(start + length)..to]);
        if (pre.Length > 0 && char.IsWhiteSpace(text[start - 1])) pre += " ";
        if (post.Length > 0 && char.IsWhiteSpace(text[start + length])) post = " " + post;
        return new SearchHit
        {
            Conversation = c, Message = m,
            Before = (from > 0 ? "…" : "") + pre,
            Match = Line(text.Substring(start, length)),
            After = post + (to < text.Length ? "…" : ""),
        };
    }
}
