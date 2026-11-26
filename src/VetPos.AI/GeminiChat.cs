using System.Text.Json.Nodes;
using static VetPos.AI.AiClient;

namespace VetPos.AI;

internal static class GeminiChat
{
    internal const string DefaultModel = "gemini-3.5-flash-lite";

    private static string ModelId(string model) =>
        model.StartsWith("models/", StringComparison.Ordinal) ? model["models/".Length..] : model;

    internal static Uri Address(string model) =>
        new($"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(ModelId(model))}:generateContent");

    internal static JsonObject CreateRequest(string model, JsonArray messages, JsonArray? tools, bool allowTools)
    {
        var contents = new JsonArray();
        var instructions = new JsonArray();
        JsonArray? toolResults = null;
        foreach (var message in messages.OfType<JsonObject>())
        {
            var role = StringOf(message["role"]);
            if (role == "tool")
            {
                if (toolResults is null)
                {
                    toolResults = [];
                    contents.Add(Content("user", toolResults));
                }

                var id = StringOf(message["tool_call_id"]);
                var response = new JsonObject
                {
                    ["name"] = StringOf(message["name"]),
                    ["response"] = new JsonObject { ["output"] = StringOf(message["content"]) },
                };
                if (!string.IsNullOrEmpty(id) && !id.StartsWith("local_gemini_", StringComparison.Ordinal))
                    response["id"] = id;
                toolResults.Add(new JsonObject { ["functionResponse"] = response });
                continue;
            }

            toolResults = null;
            if (message["gemini_content"] is JsonObject modelTurn)
            {
                contents.Add(modelTurn.DeepClone());
                continue;
            }
            if (StringOf(message["content"]) is not { Length: > 0 } text) continue;
            var part = new JsonObject { ["text"] = text };
            if (role == "system") instructions.Add(part);
            else contents.Add(Content(role == "assistant" ? "model" : "user", new JsonArray(part)));
        }

        var body = new JsonObject { ["contents"] = contents };
        if (instructions.Count > 0) body["systemInstruction"] = new JsonObject { ["parts"] = instructions };
        if (tools is { Count: > 0 })
        {
            var declarations = new JsonArray();
            foreach (var tool in tools.OfType<JsonObject>())
            {
                if (tool["function"] is not JsonObject function) continue;
                var declaration = new JsonObject
                {
                    ["name"] = function["name"]?.DeepClone(),
                    ["description"] = function["description"]?.DeepClone(),
                };
                if (function["parameters"] is JsonObject parameters && parameters["properties"] is JsonObject { Count: > 0 })
                    declaration["parametersJsonSchema"] = parameters.DeepClone();
                declarations.Add(declaration);
            }
            body["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = declarations });
            if (!allowTools)
            {
                body["toolConfig"] = new JsonObject
                {
                    ["functionCallingConfig"] = new JsonObject { ["mode"] = "NONE" },
                };
            }
        }

        if (ModelId(model).Equals(DefaultModel, StringComparison.Ordinal))
            body["generationConfig"] = new JsonObject
            {
                ["thinkingConfig"] = new JsonObject { ["thinkingLevel"] = "MINIMAL" },
            };
        return body;
    }

    internal static ChatReply ReadReply(string json)
    {
        var response = JsonNode.Parse(json);
        var candidate = (response?["candidates"] as JsonArray)?.FirstOrDefault();
        if (candidate?["content"] is not JsonObject content || content["parts"] is not JsonArray parts)
            throw new AiUnavailableException("Gemini returned no answer. Try asking a shorter question.");

        var calls = new List<ToolCall>();
        var answer = new List<string>();
        foreach (var part in parts.OfType<JsonObject>())
        {
            if (part["functionCall"] is JsonObject call && StringOf(call["name"]) is { Length: > 0 } name)
                calls.Add(new ToolCall(StringOf(call["id"]) ?? $"local_gemini_{calls.Count}", name, call["args"]?.ToJsonString() ?? "{}"));
            if (part["thought"]?.GetValue<bool>() != true && StringOf(part["text"]) is { } text)
                answer.Add(text);
        }
        return new ChatReply(answer.Count == 0 ? null : string.Join("\n", answer), calls) { GeminiContent = content };
    }

    private static JsonObject Content(string role, JsonArray parts) => new() { ["role"] = role, ["parts"] = parts };
}
