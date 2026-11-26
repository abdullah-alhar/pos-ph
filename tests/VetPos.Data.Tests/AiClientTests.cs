using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using VetPos.AI;

namespace VetPos.Data.Tests;

public class AiClientTests
{
    private static HttpResponseMessage Raw(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Task<ChatReply> Ask(FakeAi fake, JsonArray? tools = null) =>
        new AiClient(new HttpClient(fake)).ChatAsync("https://ai.test/v1", "k", "m",
            new JsonArray { new JsonObject { ["role"] = "user", ["content"] = "hi" } }, tools, default);

    [Fact]
    public async Task Reads_tool_arguments_sent_as_an_object()
    {
        var fake = new FakeAi(_ => Raw(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":null,"tool_calls":[{"id":"1","type":"function","function":{"name":"get_low_stock","arguments":{"limit":5}}}]}}]}"""));
        var reply = await Ask(fake);
        Assert.Equal("get_low_stock", Assert.Single(reply.ToolCalls).Name);
        Assert.Equal(5, JsonNode.Parse(reply.ToolCalls[0].ArgumentsJson)!["limit"]!.GetValue<int>());
    }

    [Fact]
    public async Task Reads_content_sent_as_parts()
    {
        var fake = new FakeAi(_ => Raw(HttpStatusCode.OK, """{"choices":[{"message":{"content":[{"type":"text","text":"Hello "},{"type":"text","text":"there"}]}}]}"""));
        Assert.Equal("Hello there", (await Ask(fake)).Text);
    }

    [Fact]
    public async Task A_broken_tool_call_is_asked_again_once()
    {
        var fake = new FakeAi(
            _ => Raw(HttpStatusCode.BadRequest, """{"error":{"message":"Failed to call a function","code":"tool_use_failed"}}"""),
            _ => Raw(HttpStatusCode.OK, """{"choices":[{"message":{"content":"OK"}}]}"""));
        Assert.Equal("OK", (await Ask(fake)).Text);
        Assert.Equal(2, fake.Sent.Count);
    }

    [Fact]
    public async Task A_wrong_key_says_so_with_the_services_own_words()
    {
        var fake = new FakeAi(_ => Raw(HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid API Key","code":"invalid_api_key"}}"""));
        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => Ask(fake));
        Assert.Contains("key was not accepted", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Invalid API Key", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_retired_model_tells_how_to_fix_it()
    {
        var fake = new FakeAi(_ => Raw(HttpStatusCode.BadRequest, """{"error":{"message":"The model `llama3-70b` has been decommissioned"}}"""));
        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => Ask(fake));
        Assert.Contains("clear the Model box", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_gemini_key_is_explained()
    {
        var fake = new FakeAi(_ => Raw(HttpStatusCode.BadRequest, """[{"error":{"code":400,"message":"Please pass a valid API key","status":"INVALID_ARGUMENT"}}]"""));
        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() => Ask(fake));
        Assert.Contains("key was not accepted", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Please pass a valid API key", ex.Message, StringComparison.Ordinal);
    }
    [Fact]
    public async Task Gemini_passes_native_tool_context_through_the_connection_test()
    {
        var fake = new FakeAi(request =>
        {
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash-lite:generateContent", request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-key", Assert.Single(request.Headers.GetValues("x-goog-api-key")));
            return Raw(HttpStatusCode.OK, """
                {"candidates":[{"content":{"role":"model","parts":[
                    {"text":"internal","thought":true},
                    {"functionCall":{"id":"lookup","name":"check_connection","args":{}},"thoughtSignature":"opaque-context"}
                ]}}]}
                """);
        }, _ => Raw(HttpStatusCode.OK, """
            {"candidates":[{"content":{"role":"model","parts":[{"text":"PINEAPPLE"}]}}]}
            """));
        var provider = AiProviders.Get("gemini");
        using var http = new HttpClient(fake);
        await new AiClient(http).TestConnectionAsync(provider.BaseUrl, "test-key", provider.DefaultModel);

        Assert.Equal("MINIMAL", fake.Sent[0]["generationConfig"]!["thinkingConfig"]!["thinkingLevel"]!.GetValue<string>());
        Assert.Equal("check_connection", fake.Sent[0]["tools"]![0]!["functionDeclarations"]![0]!["name"]!.GetValue<string>());
        Assert.Null(fake.Sent[0]["temperature"]);
        var final = fake.Sent[1];
        Assert.Equal("opaque-context", final["contents"]![1]!["parts"]![1]!["thoughtSignature"]!.GetValue<string>());
        var result = final["contents"]![2]!["parts"]![0]!["functionResponse"]!;
        Assert.Equal("lookup", result["id"]!.GetValue<string>());
        Assert.Equal("check_connection", result["name"]!.GetValue<string>());
        Assert.Equal("PINEAPPLE", result["response"]!["output"]!.GetValue<string>());
        Assert.Equal("NONE", final["toolConfig"]!["functionCallingConfig"]!["mode"]!.GetValue<string>());
    }

    [Fact]
    public async Task Connection_test_rejects_a_model_that_cannot_read_a_tool_result()
    {
        var fake = new FakeAi(FakeAi.ToolCall("check_connection", "{}"), FakeAi.Answer("I don't know."));
        using var http = new HttpClient(fake);
        var ex = await Assert.ThrowsAsync<AiUnavailableException>(() =>
            new AiClient(http).TestConnectionAsync("https://ai.test/v1", "key", "model"));
        Assert.Contains("could not read its result", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenRouter_requires_tool_support_when_selecting_a_provider()
    {
        var fake = new FakeAi(FakeAi.ToolCall("check_connection", "{}"), FakeAi.Answer("PINEAPPLE"));
        var provider = AiProviders.Get("openrouter");
        using var http = new HttpClient(fake);
        await new AiClient(http).TestConnectionAsync(provider.BaseUrl, "key", provider.DefaultModel);

        Assert.Equal("openrouter/free", fake.Sent[0]["model"]!.GetValue<string>());
        Assert.True(fake.Sent[0]["provider"]!["require_parameters"]!.GetValue<bool>());
    }
}
