using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace VetPos.AI;

/// <summary>Why the AI could not answer, in words for the shop owner.</summary>
public sealed class AiUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed record ToolCall(string Id, string Name, string ArgumentsJson);

public sealed record ChatReply(string? Text, IReadOnlyList<ToolCall> ToolCalls);

/// <summary>Minimal OpenAI-compatible chat client with native tool calling.</summary>
public sealed class AiClient(HttpClient http)
{
    public async Task<ChatReply> ChatAsync(string baseUrl, string? apiKey, string model, JsonArray messages, JsonArray? tools, CancellationToken ct)
    {
        // A model sometimes writes one broken tool call at random (Groq: "tool_use_failed"); asking
        // again almost always works, so try once more before telling the shop.
        try
        {
            return await SendAsync(baseUrl, apiKey, model, messages, tools, ct);
        }
        catch (AiRetryableException)
        {
            try
            {
                return await SendAsync(baseUrl, apiKey, model, messages, tools, ct);
            }
            catch (AiRetryableException ex)
            {
                throw new AiUnavailableException("The AI made a mistake while looking up your data. Ask again, or choose another model in Settings → AI.", ex);
            }
        }
    }

    private sealed class AiRetryableException(string message) : Exception(message);

    private async Task<ChatReply> SendAsync(string baseUrl, string? apiKey, string model, JsonArray messages, JsonArray? tools, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(baseUrl)) throw new AiUnavailableException("The AI service address is not set (Settings → AI).");
        if (string.IsNullOrWhiteSpace(model)) throw new AiUnavailableException("No AI model is set (Settings → AI).");
        var body = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages.DeepClone(),
            ["temperature"] = 0.2,
        };
        if (tools is { Count: > 0 })
        {
            body["tools"] = tools.DeepClone();
            body["tool_choice"] = "auto";
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, baseUrl.Trim().TrimEnd('/') + "/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(apiKey)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
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
                var msg = JsonNode.Parse(text)?["choices"]?[0]?["message"] ?? throw new AiUnavailableException("The AI gave an empty answer. Try again.");
                var calls = (msg["tool_calls"] as JsonArray ?? [])
                    .Select(c => new ToolCall(
                        StringOf(c?["id"]) is { Length: > 0 } id ? id : "call_" + Guid.NewGuid().ToString("N")[..12],
                        StringOf(c?["function"]?["name"]) ?? "",
                        ArgumentsOf(c?["function"]?["arguments"])))
                    .Where(c => c.Name.Length > 0).ToList();
                return new ChatReply(ContentOf(msg["content"]), calls);
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

    private static string? StringOf(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

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
