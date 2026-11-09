//
//  Ollama.cs
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

using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Citadel.Loc;

namespace Citadel;

/// <summary>One line of a streamed /api/chat answer (or the whole answer when not streamed).</summary>
internal sealed class ChatChunk
{
    public string Content = "";
    public string Thinking = "";
    public bool Done;
    public int EvalCount;
    public long EvalDuration;
    public string Error;

    public static ChatChunk Parse(string line)
    {
        var node = JsonNode.Parse(line);
        var chunk = new ChatChunk
        {
            Content = (string)node?["message"]?["content"] ?? "",
            Thinking = (string)node?["message"]?["thinking"] ?? "",
            Done = (bool?)node?["done"] ?? false,
            EvalCount = (int?)node?["eval_count"] ?? 0,
            EvalDuration = (long?)node?["eval_duration"] ?? 0,
            Error = (string)node?["error"],
        };
        return chunk;
    }
}

internal sealed class OllamaException(string message) : Exception(message)
{
    public bool Unreachable { get; init; }

    public static OllamaException NotReachable() => new(Connection.RemoteHost is string host
        ? Connection.LastError ?? tr($"Ollama no responde en {host} (por SSH).", $"Ollama is not reachable on {host} (through SSH).")
        : tr("No se puede conectar con Ollama. Ábrelo desde el menú Inicio o ejecuta: ollama serve",
             "Cannot reach Ollama. Open it from the Start menu or run: ollama serve")) { Unreachable = true };
}

/// <summary>Minimal client for the local Ollama HTTP API.</summary>
internal static class Ollama
{
    // One client for the whole app; requests that stream set no overall timeout.
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>Full address of an API path on this computer or through the SSH tunnel.</summary>
    private static async Task<Uri> Url(string path) => new(await Connection.Base(), path);

    private static async Task<JsonNode> GetJson(string path)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            string text = await Http.GetStringAsync(await Url(path), cts.Token);
            return JsonNode.Parse(text);
        }
        catch (Exception) { throw OllamaException.NotReachable(); }
    }

    /// <summary>Installed models (/api/tags), sorted by name.</summary>
    public static async Task<List<string>> ListModels()
    {
        var json = await GetJson("api/tags");
        return (json?["models"]?.AsArray() ?? [])
            .Select(m => (string)m?["name"]).Where(n => !string.IsNullOrEmpty(n))
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>What a model can do (/api/show), such as "completion" and "thinking".</summary>
    public static async Task<List<string>> Capabilities(string model)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        var body = new JsonObject { ["model"] = model };
        try
        {
            using var response = await Http.PostAsync(await Url("api/show"), new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"), cts.Token);
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cts.Token));
            return (json?["capabilities"]?.AsArray() ?? []).Select(c => (string)c).Where(c => c != null).ToList();
        }
        catch (Exception) { throw OllamaException.NotReachable(); }
    }

    /// <summary>Models currently loaded in memory (/api/ps).</summary>
    public static async Task<List<string>> LoadedModels()
    {
        var json = await GetJson("api/ps");
        return (json?["models"]?.AsArray() ?? []).Select(m => (string)m?["name"]).Where(n => n != null).ToList();
    }

    private static HttpRequestMessage ChatRequest(Uri url, string model, IEnumerable<(string role, string content)> messages,
                                                  bool think, bool stream, int numPredict, bool json)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray(messages.Select(m => (JsonNode)new JsonObject { ["role"] = m.role, ["content"] = m.content }).ToArray()),
            ["stream"] = stream,
            ["think"] = think,
            ["options"] = new JsonObject { ["num_predict"] = numPredict },
        };
        if (json) body["format"] = "json";
        return new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    /// <summary>
    /// Streams /api/chat, one JSON object per line. Cancelling the token closes the connection, and Ollama
    /// then stops generating.
    /// </summary>
    public static async IAsyncEnumerable<ChatChunk> StreamChat(string model, IEnumerable<(string, string)> messages, bool think,
                                                               int numPredict, [EnumeratorCancellation] CancellationToken token)
    {
        using var request = ChatRequest(await Url("api/chat"), model, messages, think, true, numPredict, false);
        HttpResponseMessage response;
        try { response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw OllamaException.NotReachable(); }
        using (response)
        {
            using var stream = await response.Content.ReadAsStreamAsync(token);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (true)
            {
                string line = await reader.ReadLineAsync(token);
                if (line == null) break;
                if (line.Length == 0) continue;
                ChatChunk chunk;
                try { chunk = ChatChunk.Parse(line); } catch (JsonException) { continue; }
                if (chunk.Error != null) throw new OllamaException("Ollama: " + chunk.Error);
                yield return chunk;
                if (chunk.Done) yield break;
            }
            if (!response.IsSuccessStatusCode) throw new OllamaException("Ollama: HTTP " + (int)response.StatusCode);
        }
    }

    /// <summary>One /api/chat call without streaming; returns the answer text.</summary>
    public static async Task<string> Complete(string model, IEnumerable<(string, string)> messages, bool think, int numPredict,
                                              bool json, CancellationToken token)
    {
        using var request = ChatRequest(await Url("api/chat"), model, messages, think, false, numPredict, json);
        string text;
        try
        {
            using var response = await Http.SendAsync(request, token);
            text = await response.Content.ReadAsStringAsync(token);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw OllamaException.NotReachable(); }
        var chunk = ChatChunk.Parse(text);
        if (chunk.Error != null) throw new OllamaException("Ollama: " + chunk.Error);
        return chunk.Content;
    }
}
