using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VetPos.AI;

/// <summary>Why the AI could not answer, in words for the shop owner.</summary>
public sealed class AiUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record ToolCall(string Id, string Name, string ArgumentsJson);

public sealed record ChatReply(string? Text, IReadOnlyList<ToolCall> ToolCalls)
{
    internal JsonObject? GeminiContent { get; init; }
    internal JsonNode? ReasoningDetails { get; init; }
    internal string? RoutedModel { get; init; }

    public JsonObject ToAssistantMessage()
    {
        var message = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = Text,
            ["tool_calls"] = new JsonArray(ToolCalls.Select(call => (JsonNode)new JsonObject
            {
                ["id"] = call.Id,
                ["type"] = "function",
                ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.ArgumentsJson },
            }).ToArray()),
        };
        if (GeminiContent is not null) message["gemini_content"] = GeminiContent.DeepClone();
        if (ReasoningDetails is not null) message["reasoning_details"] = ReasoningDetails.DeepClone();
        if (RoutedModel is not null) message["openrouter_model"] = RoutedModel;
        return message;
    }
}

/// <summary>Chat client for Gemini's native API and OpenAI-compatible providers.</summary>
public sealed class AiClient(HttpClient http)
{
    public static JsonObject ToolResult(ToolCall call, string result) => new()
    {
        ["role"] = "tool", ["tool_call_id"] = call.Id, ["name"] = call.Name, ["content"] = result,
    };

    public async Task<TimeSpan> TestConnectionAsync(string url, string? key, string model)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        JsonArray messages = [new JsonObject
        {
            ["role"] = "user",
            ["content"] = "Call check_connection. Then reply with the word returned by the tool.",
        }];
        JsonArray tools = [new JsonObject
        {
            ["type"] = "function",
            ["function"] = new JsonObject
            {
                ["name"] = "check_connection",
                ["description"] = "Returns a word to verify the connection.",
                ["parameters"] = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject() },
            },
        }];

        try
        {
            var first = await ChatAsync(url, key, model, messages, tools, timeout.Token);
            if (first.ToolCalls.Count == 0 || first.ToolCalls.Any(call => call.Name != "check_connection"))
                throw new AiUnavailableException("The service answered, but this model could not look up data. Try the recommended model.");

            messages.Add(first.ToAssistantMessage());
            var word = "PINEAPPLE";
            foreach (var call in first.ToolCalls)
                messages.Add(ToolResult(call, word));
            var final = await ChatAsync(url, key, model, messages, tools, allowTools: false, timeout.Token);
            if (final.Text?.Contains(word, StringComparison.OrdinalIgnoreCase) != true)
                throw new AiUnavailableException("The model could call a tool, but it could not read its result. Try the recommended model.");
            return watch.Elapsed;
        }
        catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
        {
            throw new AiUnavailableException("The connection test took too long. Try again in a moment.", ex);
        }
    }

    public Task<ChatReply> ChatAsync(string baseUrl, string? apiKey, string model, JsonArray messages, JsonArray? tools, CancellationToken ct) =>
        ChatAsync(baseUrl, apiKey, model, messages, tools, allowTools: true, ct);

    internal async Task<ChatReply> ChatAsync(string baseUrl, string? apiKey, string model, JsonArray messages, JsonArray? tools, bool allowTools, CancellationToken ct)
    {
        // A model sometimes writes one broken tool call at random (Groq: "tool_use_failed"); asking
        // again almost always works, so try once more before telling the shop.
        try
        {
            return await SendAsync(baseUrl, apiKey, model, messages, tools, allowTools, ct);
        }
        catch (AiRetryableException)
        {
            try
            {
                return await SendAsync(baseUrl, apiKey, model, messages, tools, allowTools, ct);
            }
            catch (AiRetryableException ex)
            {
                throw new AiUnavailableException("The AI made a mistake while looking up your data. Ask again, or choose another model in Settings → AI.", ex);
            }
        }
    }

    private sealed class AiRetryableException(string message) : Exception(message);

    private async Task<ChatReply> SendAsync(string baseUrl, string? apiKey, string model, JsonArray messages, JsonArray? tools, bool allowTools, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new AiUnavailableException("The AI service address is not set (Settings → AI).");
        if (string.IsNullOrWhiteSpace(model)) throw new AiUnavailableException("No AI model is set (Settings → AI).");
        var endpoint = new Uri(baseUrl.Trim().TrimEnd('/') + "/", UriKind.Absolute);
        var gemini = endpoint.Host.Equals("generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase);
        var openRouter = endpoint.Host.Equals("openrouter.ai", StringComparison.OrdinalIgnoreCase);
        var body = gemini ? GeminiChat.CreateRequest(model, messages, tools, allowTools) : new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages.DeepClone(),
            ["temperature"] = 0.2,
        };
        if (!gemini && body["messages"] is JsonArray wireMessages)
        {
            var mistral = endpoint.Host.Equals("api.mistral.ai", StringComparison.OrdinalIgnoreCase);
            foreach (var message in wireMessages.OfType<JsonObject>())
            {
                if (openRouter && model == "openrouter/free"
                    && StringOf(message["openrouter_model"]) is { } routed && routed.EndsWith(":free", StringComparison.Ordinal))
                    body["model"] = routed;
                message.Remove("openrouter_model");
                if (!mistral && StringOf(message["role"]) == "tool") message.Remove("name");
            }
        }
        if (!gemini && (allowTools || openRouter) && tools is { Count: > 0 })
        {
            body["tools"] = tools.DeepClone();
            body["tool_choice"] = allowTools ? "auto" : "none";
            if (openRouter) body["provider"] = new JsonObject { ["require_parameters"] = true };
        }
        var address = gemini ? GeminiChat.Address(model) : new Uri(endpoint, "chat/completions");
        using var request = new HttpRequestMessage(HttpMethod.Post, address)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            if (gemini) request.Headers.Add("x-goog-api-key", apiKey.Trim());
            else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(gemini ? 30 : 90));
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, deadline.Token);
        }
        catch (HttpRequestException ex)
        {
            throw new AiUnavailableException("AI is offline: the AI service could not be reached. Check the internet connection.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new AiUnavailableException("The AI took too long to answer. Try again.", ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = ErrorDetail(text);
                if (response.StatusCode == HttpStatusCode.BadRequest
                    && (text.Contains("tool_use_failed", StringComparison.OrdinalIgnoreCase) || text.Contains("Failed to call a function", StringComparison.OrdinalIgnoreCase)
                        || text.Contains("failed_generation", StringComparison.OrdinalIgnoreCase)))
                    throw new AiRetryableException(detail ?? "tool call failed");
                var badKey = text.Contains("API key", StringComparison.OrdinalIgnoreCase) || text.Contains("API_KEY", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("invalid_api_key", StringComparison.OrdinalIgnoreCase);
                var why = response.StatusCode switch
                {
                    HttpStatusCode.BadRequest when badKey => "The AI key was not accepted. Copy the key again from the provider's website and save it in Settings → AI.",
                    HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The AI key was not accepted. Copy the key again from the provider's website and save it in Settings → AI.",
                    HttpStatusCode.PaymentRequired => "The AI account has no credit left. Add credit or choose a free provider in Settings → AI.",
                    HttpStatusCode.TooManyRequests => "The AI service's free limit is reached for now. Wait a minute and try again.",
                    HttpStatusCode.NotFound => "The AI model or address was not found. In Settings → AI, clear the Model box to use the recommended one.",
                    HttpStatusCode.BadRequest when text.Contains("model", StringComparison.OrdinalIgnoreCase)
                        && (text.Contains("decommission", StringComparison.OrdinalIgnoreCase) || text.Contains("not exist", StringComparison.OrdinalIgnoreCase) || text.Contains("not found", StringComparison.OrdinalIgnoreCase) || text.Contains("not supported", StringComparison.OrdinalIgnoreCase))
                        => "That AI model is not available any more. In Settings → AI, clear the Model box to use the recommended one.",
                    HttpStatusCode.BadRequest when text.Contains("tool", StringComparison.OrdinalIgnoreCase) => "This AI model cannot look up your data (no tool support). Choose another model in Settings → AI.",
                    >= HttpStatusCode.InternalServerError => "The AI service has a problem right now. Try again in a few minutes.",
                    _ => $"The AI service answered with an error ({(int)response.StatusCode}).",
                };
                throw new AiUnavailableException(detail is null ? why : $"{why}\nService says: {detail}");
            }
            try
            {
                if (gemini) return GeminiChat.ReadReply(text);
                var payload = JsonNode.Parse(text);
                var msg = payload?["choices"]?[0]?["message"] ?? throw new AiUnavailableException("The AI gave an empty answer. Try again.");
                var calls = (msg["tool_calls"] as JsonArray ?? [])
                    .Select(c => new ToolCall(
                        StringOf(c?["id"]) is { Length: > 0 } id ? id : "call_" + Guid.NewGuid().ToString("N")[..12],
                        StringOf(c?["function"]?["name"]) ?? "",
                        ArgumentsOf(c?["function"]?["arguments"])))
                    .Where(c => c.Name.Length > 0).ToList();
                return new ChatReply(ContentOf(msg["content"]), calls)
                {
                    ReasoningDetails = openRouter ? msg["reasoning_details"] : null,
                    RoutedModel = openRouter ? StringOf(payload?["model"]) : null,
                };
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
            {
                throw new AiUnavailableException("The AI's answer could not be read. Try again, or choose another model in Settings → AI.", ex);
            }
        }
    }

    /// <summary>The provider's own error text ({"error":{"message":…}} or {"error":"…"}), shortened.</summary>
    private static string? ErrorDetail(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            if (node is JsonArray list) node = list.FirstOrDefault(); // Gemini: [{"error":{…}}]
            var err = node is JsonObject obj ? obj["error"] : null;
            var text = err is JsonObject o ? StringOf(o["message"]) : StringOf(err) ?? (node is JsonObject n ? StringOf(n["message"]) : null);
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            return text.Length > 220 ? text[..220] + "…" : text;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static string? StringOf(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>Arguments come as a JSON string (OpenAI) or as an object (Ollama, some Gemini answers).</summary>
    private static string ArgumentsOf(JsonNode? node) => node switch
    {
        null => "{}",
        JsonValue v when v.TryGetValue<string>(out var s) => string.IsNullOrWhiteSpace(s) ? "{}" : s,
        _ => node.ToJsonString(),
    };

    /// <summary>Content is a string, null (tool calls only) or a list of parts [{type:"text",text:…}].</summary>
    private static string? ContentOf(JsonNode? node) => node switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        JsonArray parts => string.Concat(parts.Select(p => StringOf(p?["text"]) ?? "")),
        _ => null,
    };
}
