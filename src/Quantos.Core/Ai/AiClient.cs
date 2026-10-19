using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace Quantos.Core.Ai;

public sealed record AiMessage(string Role, string Text);

/// <summary>A provider problem, worded for the person reading it (wrong key, server not running, unknown model…).</summary>
public sealed class AiException(string message) : QuantosException(message, 502);

/// <summary>
/// Talks to AI providers: Claude through the official Anthropic SDK, everything else through the
/// OpenAI-style HTTP API that most providers and local servers (Ollama, LM Studio) offer.
/// </summary>
public sealed class AiClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    // Claude declines a few requests for policy reasons; the server-side fallback lets another model answer.
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    /// <summary>Streams the reply text as it's written.</summary>
    public async IAsyncEnumerable<string> ChatAsync(AiEndpoint endpoint, string? key, string system, IReadOnlyList<AiMessage> messages,
        int maxTokens = 16000, [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Model)) throw new AiException("Choose a model for writing and answering in Settings › AI.");
        var stream = endpoint.Api == AiApi.Anthropic
            ? ClaudeChat(endpoint, key, system, messages, maxTokens, ct)
            : OpenAiChat(endpoint, key, system, messages, maxTokens, ct);
        await foreach (var text in stream) yield return text;
    }

    private static async IAsyncEnumerable<string> ClaudeChat(AiEndpoint endpoint, string? key, string system, IReadOnlyList<AiMessage> messages,
        int maxTokens, [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new AiException("Add your Anthropic API key in Settings › AI.");
        var client = new AnthropicClient { ApiKey = key };
        var parameters = ClaudeRequest(endpoint.Model!, system, messages, maxTokens);
        var refused = false;
        var events = client.Beta.Messages.CreateStreaming(parameters, ct).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                BetaRawMessageStreamEvent ev;
                try
                {
                    if (!await events.MoveNextAsync()) break;
                    ev = events.Current;
                }
                catch (Exception ex) when (ex is not OperationCanceledException and not AiException) { throw Explain(ex, endpoint); }
                if (ev.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text)) yield return text.Text;
                else if (ev.TryPickDelta(out var messageDelta) && messageDelta.Delta.StopReason is { } reason && reason.ToString().Contains("refusal", StringComparison.OrdinalIgnoreCase)) refused = true;
            }
        }
        finally { await events.DisposeAsync(); }
        if (refused) yield return "\n\n_Claude declined this request._";
    }

    /// <summary>A Claude request: streamed, with server-side fallback for the rare policy decline.</summary>
    public static MessageCreateParams ClaudeRequest(string model, string system, IReadOnlyList<AiMessage> messages, int maxTokens) => new()
    {
        Model = model,
        MaxTokens = maxTokens,
        System = new List<BetaTextBlockParam> { new() { Text = system } },
        Messages = messages.Select(m => new BetaMessageParam { Role = m.Role == "assistant" ? Role.Assistant : Role.User, Content = m.Text }).ToList(),
        Betas = [FallbackBeta],
        Fallbacks = new Default(),
    };

    private static async IAsyncEnumerable<string> OpenAiChat(AiEndpoint endpoint, string? key, string system, IReadOnlyList<AiMessage> messages,
        int maxTokens, [EnumeratorCancellation] CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = endpoint.Model,
            ["stream"] = true,
            ["max_tokens"] = maxTokens,
            ["messages"] = new JsonArray([new JsonObject { ["role"] = "system", ["content"] = system },
                .. messages.Select(m => (JsonNode)new JsonObject { ["role"] = m.Role == "assistant" ? "assistant" : "user", ["content"] = m.Text })])
        };
        using var request = Request(endpoint, key, "chat/completions", body);
        using var response = await Send(request, endpoint, HttpCompletionOption.ResponseHeadersRead, ct);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:")) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") yield break;
            string? text = null;
            try { text = JsonNode.Parse(data)?["choices"]?[0]?["delta"]?["content"]?.GetValue<string>(); }
            catch (JsonException) { }
            if (!string.IsNullOrEmpty(text)) yield return text;
        }
    }

    /// <summary>One vector per input, in order.</summary>
    public async Task<float[][]> EmbedAsync(AiEndpoint endpoint, string? key, IReadOnlyList<string> inputs, CancellationToken ct = default)
    {
        if (endpoint.Api == AiApi.Anthropic) throw new AiException("Claude has no embedding model. Choose another provider for meaning search.");
        if (string.IsNullOrWhiteSpace(endpoint.Model)) throw new AiException("Choose an embedding model for meaning search in Settings › AI.");
        var body = new JsonObject { ["model"] = endpoint.Model, ["input"] = new JsonArray([.. inputs.Select(i => (JsonNode)JsonValue.Create(i)!)]) };
        using var request = Request(endpoint, key, "embeddings", body);
        using var response = await Send(request, endpoint, HttpCompletionOption.ResponseContentRead, ct);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
        var data = json?["data"]?.AsArray() ?? throw new AiException("The provider's embeddings answer wasn't understood.");
        var result = new float[inputs.Count][];
        var position = 0;
        foreach (var item in data)
        {
            var index = item?["index"]?.GetValue<int>() ?? position;
            result[index] = item!["embedding"]!.AsArray().Select(v => v!.GetValue<float>()).ToArray();
            position++;
        }
        if (result.Any(r => r is null)) throw new AiException("The provider returned fewer embeddings than asked for.");
        return result;
    }

    /// <summary>The models the provider offers, for the model picker.</summary>
    public async Task<IReadOnlyList<string>> ListModelsAsync(AiEndpoint endpoint, string? key, CancellationToken ct = default)
    {
        try
        {
            if (endpoint.Api == AiApi.Anthropic)
            {
                if (string.IsNullOrWhiteSpace(key)) throw new AiException("Add your Anthropic API key first.");
                var client = new AnthropicClient { ApiKey = key };
                var page = await client.Models.List(cancellationToken: ct);
                return page.Items.Select(m => m.ID).ToList();
            }
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.Url + "/models");
            Authorize(request, endpoint, key);
            using var response = await Send(request, endpoint, HttpCompletionOption.ResponseContentRead, ct);
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct));
            // OpenAI-style {data:[{id}]}; Ollama's native style {models:[{name}]} as a fallback.
            var ids = json?["data"]?.AsArray().Select(m => m?["id"]?.GetValue<string>())
                      ?? json?["models"]?.AsArray().Select(m => m?["name"]?.GetValue<string>() ?? m?["id"]?.GetValue<string>());
            return ids?.OfType<string>().OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        }
        catch (Exception ex) when (ex is not AiException and not OperationCanceledException) { throw Explain(ex, endpoint); }
    }

    private static HttpRequestMessage Request(AiEndpoint endpoint, string? key, string path, JsonObject body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint.Url + "/" + path)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
        };
        Authorize(request, endpoint, key);
        return request;
    }

    private static void Authorize(HttpRequestMessage request, AiEndpoint endpoint, string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        if (endpoint.Api == AiApi.AzureOpenAi) request.Headers.Add("api-key", key);
        else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
    }

    private static async Task<HttpResponseMessage> Send(HttpRequestMessage request, AiEndpoint endpoint, HttpCompletionOption option, CancellationToken ct)
    {
        HttpResponseMessage response;
        try { response = await Http.SendAsync(request, option, ct); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested) { throw Explain(ex, endpoint); }
        if (response.IsSuccessStatusCode) return response;
        var detail = "";
        try { detail = await response.Content.ReadAsStringAsync(ct); } catch (HttpRequestException) { }
        response.Dispose();
        throw new AiException(Status(response.StatusCode, endpoint) + Detail(detail));
    }

    private static string Status(HttpStatusCode code, AiEndpoint endpoint) => code switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => $"{Name(endpoint)} didn't accept the API key.",
        HttpStatusCode.NotFound => $"{Name(endpoint)} doesn't know the model “{endpoint.Model}” (or the address is wrong).",
        HttpStatusCode.TooManyRequests => $"{Name(endpoint)} is rate-limiting requests. Try again in a moment.",
        _ when (int)code >= 500 => $"{Name(endpoint)} had a problem ({(int)code}). Try again shortly.",
        _ => $"{Name(endpoint)} refused the request ({(int)code})."
    };

    private static string Detail(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        try
        {
            var node = JsonNode.Parse(body);
            var message = node?["error"]?["message"]?.GetValue<string>() ?? node?["error"]?.ToString() ?? node?["message"]?.GetValue<string>();
            return string.IsNullOrWhiteSpace(message) ? "" : " " + message.Trim();
        }
        catch (JsonException) { return body.Length < 200 ? " " + body.Trim() : ""; }
    }

    private static AiException Explain(Exception ex, AiEndpoint endpoint)
    {
        var text = ex.Message;
        if (endpoint.IsLocal && (ex is HttpRequestException || text.Contains("refused", StringComparison.OrdinalIgnoreCase)))
            return new AiException($"Can't reach {Name(endpoint)} at {endpoint.Url}. Is it running?");
        if (ex is TaskCanceledException) return new AiException($"{Name(endpoint)} took too long to answer.");
        if (ex is HttpRequestException) return new AiException($"Can't reach {Name(endpoint)} at {endpoint.Url}. Check the address and your connection.");
        // SDK errors carry the status in the message (401 invalid key, 404 unknown model…).
        if (text.Contains("401") || text.Contains("authentication", StringComparison.OrdinalIgnoreCase)) return new AiException($"{Name(endpoint)} didn't accept the API key.");
        if (text.Contains("404") || text.Contains("not_found", StringComparison.OrdinalIgnoreCase)) return new AiException($"{Name(endpoint)} doesn't know the model “{endpoint.Model}”.");
        if (text.Contains("429")) return new AiException($"{Name(endpoint)} is rate-limiting requests. Try again in a moment.");
        return new AiException($"{Name(endpoint)}: {text}");
    }

    private static string Name(AiEndpoint endpoint) => endpoint.Preset?.Name.Replace(" (on this computer)", "") ?? "The AI provider";
}
