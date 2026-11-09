//
//  ChatModel.cs
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

using System.Diagnostics;
using static Citadel.Loc;

namespace Citadel;

/// <summary>
/// State of the main window: conversations, composer, model and Ollama status. Everything runs on the UI
/// thread; awaits resume there, so no locking is needed.
/// </summary>
internal sealed class ChatModel
{
    public List<Conversation> Conversations = [];
    public Guid? SelectedId;
    public string Search = "";

    public string Composer = "";
    public List<Attachment> Attachments = [];

    public List<string> Models = [];
    public bool? Reachable;
    public List<string> Loaded = [];
    public bool IsGenerating, IsDraftingMemory, ReceivedFirstToken, ReceivingThinking;
    public double Elapsed;
    public AnswerStats LastStats;
    public MemoryDraft PendingDraft;

    /// <summary>False in self-test and screenshot modes: nothing is written to the conversations folder.</summary>
    public bool Persist = true;

    /// <summary>Raised when the list, the selection, the status or the composer changes.</summary>
    public event Action Changed;
    /// <summary>Raised ten times per second while the model works, for the elapsed time.</summary>
    public event Action StatusChanged;
    /// <summary>Raised many times per second while an answer streams in.</summary>
    public event Action<ChatMessage> MessageChanged;
    /// <summary>Raised when /save has a draft ready for review.</summary>
    public event Action<MemoryDraft> DraftReady;
    /// <summary>Raised for short messages that do not belong in the conversation.</summary>
    public event Action<string> Toast;

    private CancellationTokenSource streamCts, draftCts;
    private readonly Stopwatch watch = new();
    private readonly System.Windows.Threading.DispatcherTimer ticker = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly System.Windows.Threading.DispatcherTimer poller = new() { Interval = TimeSpan.FromSeconds(4) };

    public const string DefaultModel = "qwen3-8b-32k:latest";

    public string SelectedModel
    {
        get => Settings.Current.Model ?? DefaultModel;
        set { Settings.Current.Model = value; Settings.Current.Save(); Changed?.Invoke(); }
    }

    /// <summary>Whether each installed model can reason, from its Ollama capabilities.</summary>
    public Dictionary<string, bool> ReasoningModels { get; } = [];

    /// <summary>Reason applies only to models that can reason; unknown models are given the benefit of the doubt.</summary>
    public bool CanReason => !ReasoningModels.TryGetValue(SelectedModel, out bool can) || can;

    public bool Reason
    {
        get => Settings.Current.Reason;
        set { Settings.Current.Reason = value; Settings.Current.Save(); Changed?.Invoke(); }
    }

    public ChatModel()
    {
        ticker.Tick += (s, e) => { Elapsed = watch.Elapsed.TotalSeconds; StatusChanged?.Invoke(); };
        poller.Tick += async (s, e) => await RefreshStatus();
    }

    public void Start(bool loadSaved)
    {
        try { Rules.EnsureExists(); } catch (Exception ex) { Program.Log("Could not create rules.md: " + ex.Message); }
        if (loadSaved) Conversations = ConversationStore.LoadAll();
        SelectedId = Conversations.FirstOrDefault()?.Id;
        if (SelectedId == null) NewConversation();
        poller.Start();
        _ = RefreshStatus();
    }

    public async Task RefreshStatus()
    {
        try
        {
            var list = await Ollama.ListModels();
            Models = list;
            Reachable = true;
            if (list.Count > 0 && !list.Contains(SelectedModel))
                Settings.Current.Model = list.Contains(DefaultModel) ? DefaultModel : list[0];
            foreach (string name in list.Where(n => !ReasoningModels.ContainsKey(n)))
                try { ReasoningModels[name] = (await Ollama.Capabilities(name)).Contains("thinking"); } catch { }
            try { Loaded = await Ollama.LoadedModels(); } catch { Loaded = []; }
        }
        catch
        {
            Reachable = false;
            Loaded = [];
        }
        Changed?.Invoke();
    }

    public bool ModelIsLoaded => Loaded.Contains(SelectedModel);

    // Conversations

    public Conversation Current => Conversations.FirstOrDefault(c => c.Id == SelectedId);

    public IEnumerable<Conversation> Filtered
    {
        get
        {
            string q = Text.Fold(Search.Trim());
            var visible = Conversations.Where(c => c.Messages.Count > 0 || c.Id == SelectedId);
            return q.Length == 0 ? visible : visible.Where(c => Text.Fold(c.DisplayTitle).Contains(q));
        }
    }

    public void NewConversation()
    {
        if (Current is { Messages.Count: 0 }) return;
        var empty = Conversations.FirstOrDefault(c => c.Messages.Count == 0);
        if (empty == null)
        {
            empty = new Conversation();
            Conversations.Insert(0, empty);
        }
        SelectedId = empty.Id;
        Changed?.Invoke();
    }

    public void Select(Guid id)
    {
        if (id == SelectedId) return;
        SelectedId = id;
        // Drop an untouched empty conversation when leaving it.
        Conversations.RemoveAll(c => c.Messages.Count == 0 && c.Id != id);
        Changed?.Invoke();
    }

    public void Rename(Guid id, string title)
    {
        var c = Conversations.FirstOrDefault(x => x.Id == id);
        if (c == null) return;
        c.Title = title.Trim();
        Save(c);
        Changed?.Invoke();
    }

    public void Delete(Guid id)
    {
        if (id == SelectedId) Stop();
        Conversations.RemoveAll(c => c.Id == id);
        ConversationStore.Delete(id);
        if (SelectedId == id)
        {
            SelectedId = Conversations.FirstOrDefault()?.Id;
            if (SelectedId == null) NewConversation();
        }
        Changed?.Invoke();
    }

    private void Save(Conversation c)
    {
        if (Persist && c != null && c.Messages.Count > 0) ConversationStore.Save(c);
    }

    private void Append(Conversation c, ChatMessage m)
    {
        c.Messages.Add(m);
        c.Updated = DateTime.UtcNow;
    }

    private void Notice(string text)
    {
        var c = Current;
        if (c == null) return;
        Append(c, new ChatMessage { Role = Role.Notice, Content = text });
        Save(c);
        Changed?.Invoke();
    }

    // Composer

    public bool CanSend => !IsGenerating && !IsDraftingMemory && (Composer.Trim().Length > 0 || Attachments.Count > 0);

    public static bool IsTextFile(string path) =>
        new[] { ".txt", ".md", ".markdown", ".text" }.Contains(Path.GetExtension(path).ToLowerInvariant());

    public void AttachFile(string path)
    {
        if (!IsTextFile(path))
        {
            Toast?.Invoke(tr("Solo se pueden adjuntar archivos .txt o .md.", "Only .txt and .md files can be attached."));
            return;
        }
        try { Attachments.Add(new Attachment { Name = Path.GetFileName(path), Text = File.ReadAllText(path) }); }
        catch { Toast?.Invoke(tr("No se pudo leer el archivo.", "Could not read the file.")); }
        Changed?.Invoke();
    }

    public void Send()
    {
        string text = Composer.Trim();
        var c = Current;
        if (!CanSend || c == null) return;

        if (text == "/save" || text.StartsWith("/save "))
        {
            Composer = "";
            StartMemoryDraft();
            return;
        }
        if (text == "/mem" || text.StartsWith("/mem "))
        {
            Composer = "";
            LoadMemory(text[4..].Trim());
            return;
        }

        var message = new ChatMessage { Role = Role.User, Content = text, Attachments = [.. Attachments] };
        Composer = "";
        Attachments.Clear();
        if (c.Title.Length == 0) c.Title = Conversation.TitleFrom(text.Length == 0 ? message.Attachments.FirstOrDefault()?.Name ?? "" : text);
        Append(c, message);
        Save(c);
        _ = RunAnswer(c);
    }

    // Answers

    /// <summary>The system message: rules file, memory index and any memory loaded with /mem.</summary>
    public static string SystemPrompt(Conversation c)
    {
        var parts = new List<string> { Rules.Load().Trim() };
        string index = MemoryStore.IndexText().Trim();
        if (index.Length > 0)
            parts.Add("## Memory index\nThese memories exist in the user's memory folder. You cannot open them yourself: " +
                      "the app loads one when the user types /mem <topic>.\n\n" + index);
        var loaded = c.Messages.Where(m => m.Role == Role.Memory).ToList();
        if (loaded.Count > 0)
            parts.Add("## Loaded memories" + string.Concat(loaded.Select(m => $"\n\n### {m.MemoryFile ?? "memory.md"}\n{m.Content.Trim()}")));
        return string.Join("\n\n", parts);
    }

    public static List<(string, string)> WireMessages(Conversation c)
    {
        var list = new List<(string, string)> { ("system", SystemPrompt(c)) };
        foreach (var m in c.Messages)
        {
            if (m.Role == Role.User)
            {
                string content = m.Content;
                foreach (var a in m.Attachments) content += $"\n\n--- {a.Name} ---\n{a.Text}\n--- end of {a.Name} ---";
                list.Add(("user", content.Trim()));
            }
            else if (m.Role == Role.Assistant && m.Content.Length > 0) list.Add(("assistant", m.Content));
        }
        return list;
    }

    private async Task RunAnswer(Conversation c)
    {
        bool think = Reason && CanReason;
        string model = SelectedModel;
        var wire = WireMessages(c);
        var answer = new ChatMessage { Role = Role.Assistant, Reasoned = think };
        Append(c, answer);

        IsGenerating = true;
        ReceivedFirstToken = ReceivingThinking = false;
        Elapsed = 0;
        watch.Restart();
        ticker.Start();
        streamCts = new CancellationTokenSource();
        Changed?.Invoke();

        bool inThinkTag = false;
        ChatChunk final = null;
        try
        {
            await foreach (var chunk in Ollama.StreamChat(model, wire, think, think ? 8000 : 3000, streamCts.Token))
            {
                string thinking = chunk.Thinking, content = chunk.Content;
                // Fallback for models that write their reasoning between <think> tags.
                if (content.Contains("<think>")) { inThinkTag = true; content = content.Replace("<think>", ""); }
                if (inThinkTag)
                {
                    int end = content.IndexOf("</think>", StringComparison.Ordinal);
                    if (end >= 0) { thinking += content[..end]; content = content[(end + 8)..]; inThinkTag = false; }
                    else { thinking += content; content = ""; }
                }
                if (thinking.Length > 0 || content.Length > 0)
                {
                    ReceivedFirstToken = true;
                    ReceivingThinking = thinking.Length > 0 && content.Length == 0;
                    answer.Thinking += thinking;
                    answer.Content += answer.Content.Length == 0 ? content.TrimStart('\n') : content;
                    MessageChanged?.Invoke(answer);
                }
                if (chunk.Done) final = chunk;
            }
        }
        catch (OperationCanceledException) { answer.Stopped = true; }
        catch (Exception ex)
        {
            if (streamCts.IsCancellationRequested) answer.Stopped = true;
            else
            {
                Append(c, new ChatMessage { Role = Role.Notice, Content = ex.Message });
                if (ex is OllamaException { Unreachable: true }) Reachable = false;
            }
        }

        ticker.Stop();
        double seconds = watch.Elapsed.TotalSeconds;
        if (final is { EvalCount: > 0 })
        {
            double evalSeconds = final.EvalDuration / 1e9;
            answer.Stats = new AnswerStats
            {
                Model = model, Tokens = final.EvalCount, Seconds = seconds,
                TokensPerSecond = evalSeconds > 0 ? final.EvalCount / evalSeconds : 0,
            };
            LastStats = answer.Stats;
        }
        else LastStats = new AnswerStats { Model = model, Seconds = seconds };
        // Remove an answer that produced nothing at all.
        if (answer.Content.Length == 0 && answer.Thinking.Length == 0 && !answer.Stopped) c.Messages.Remove(answer);
        Elapsed = seconds;
        IsGenerating = ReceivingThinking = false;
        streamCts = null;
        Save(c);
        Changed?.Invoke();
        await RefreshStatus();
    }

    /// <summary>Stops the answer (or the memory draft) in progress by closing the connection.</summary>
    public void Stop()
    {
        streamCts?.Cancel();
        draftCts?.Cancel();
    }

    // Memories

    public void LoadMemory(string topic)
    {
        var c = Current;
        if (c == null) return;
        if (topic.Length == 0) { Notice(tr("Uso: /mem <tema>", "Usage: /mem <topic>")); return; }
        var entry = MemoryStore.BestMatch(topic);
        if (entry == null) { Notice(tr($"Ninguna memoria coincide con «{topic}».", $"No memory matches “{topic}”.")); return; }
        string text = MemoryStore.Read(entry.File);
        if (text == null)
        {
            Notice(tr($"El índice nombra {entry.File}, pero el archivo no existe.", $"The index lists {entry.File}, but the file does not exist."));
            return;
        }
        if (c.Title.Length == 0) c.Title = "/mem " + topic;
        Append(c, new ChatMessage { Role = Role.Memory, Content = text, MemoryFile = entry.File });
        Save(c);
        Changed?.Invoke();
    }

    public static string Transcript(Conversation c)
    {
        var lines = new List<string>();
        foreach (var m in c.Messages)
        {
            switch (m.Role)
            {
                case Role.User:
                    lines.Add("User: " + m.Content + string.Concat(m.Attachments.Select(a => $"\n[attached {a.Name}]\n{a.Text}")));
                    break;
                case Role.Assistant when m.Content.Length > 0:
                    lines.Add("Assistant: " + m.Content);
                    break;
                case Role.Memory:
                    lines.Add($"[Memory {m.MemoryFile} loaded]\n{m.Content}");
                    break;
            }
        }
        string text = string.Join("\n\n", lines);
        return text.Length > 24000 ? "…" + text[^24000..] : text;
    }

    public async void StartMemoryDraft()
    {
        var c = Current;
        if (c == null || !c.Messages.Any(m => m.Role is Role.User or Role.Assistant))
        {
            Notice(tr("No hay nada que guardar todavía.", "There is nothing to save yet."));
            return;
        }
        IsDraftingMemory = true;
        watch.Restart();
        ticker.Start();
        draftCts = new CancellationTokenSource();
        Changed?.Invoke();
        try
        {
            var draft = await MemoryStore.Draft(Transcript(c), SelectedModel, draftCts.Token);
            PendingDraft = draft;
            DraftReady?.Invoke(draft);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Notice(tr("No se pudo preparar la memoria: ", "Could not prepare the memory: ") + ex.Message); }
        ticker.Stop();
        IsDraftingMemory = false;
        draftCts = null;
        Changed?.Invoke();
    }

    /// <summary>Writes the reviewed memory; confirms only after checking it on disk.</summary>
    public bool ConfirmMemory(MemoryDraft draft, out string error)
    {
        error = null;
        try
        {
            draft.File = MemoryStore.SanitizeFileName(draft.File);
            MemoryStore.Save(draft);
            PendingDraft = null;
            Notice(tr($"Memoria guardada y verificada en el disco: {draft.File}", $"Memory saved and verified on disk: {draft.File}"));
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
