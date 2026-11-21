using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Atoll.Core.Contracts;
using Atoll.Services.Assistant;
using Atoll.Services.Settings;
using Xunit;

namespace Atoll.UnitTests;

public class AssistantServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"atoll_ai_{Guid.NewGuid():N}");
    private readonly JsonSettingsService _settings;

    public AssistantServiceTests()
    {
        Directory.CreateDirectory(_dir);
        _settings = new JsonSettingsService(_dir);
    }

    public void Dispose()
    {
        _settings.Dispose();
        try { Directory.Delete(_dir, true); } catch { }
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body;
        public HttpRequestMessage? Request;
        public string? RequestBody;

        public FakeHandler(HttpStatusCode status, string body) { _status = status; _body = body; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Request = request;
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "text/event-stream") };
        }
    }

    private static string Sse(params string[] deltas)
    {
        var sb = new StringBuilder();
        sb.Append(": keep-alive\n\n");
        foreach (var d in deltas)
            sb.Append("data: ").Append(JsonSerializer.Serialize(new { choices = new[] { new { delta = new { content = d } } } })).Append("\n\n");
        sb.Append("data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n");
        sb.Append("data: [DONE]\n\n");
        return sb.ToString();
    }

    private static async Task<string> Collect(IAsyncEnumerable<string> stream)
    {
        var sb = new StringBuilder();
        await foreach (var s in stream) sb.Append(s);
        return sb.ToString();
    }

    [Fact]
    public async Task Streams_Deltas_And_Sends_Model_History_And_Key()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, Sse("Hel", "lo", " there"));
        var svc = new OpenRouterAssistantService(_settings, handler, "sk-test");
        var history = new[] { new ChatTurn("user", "hi") };

        Assert.Equal("Hello there", await Collect(svc.StreamReplyAsync(history)));

        Assert.Equal("Bearer", handler.Request!.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test", handler.Request.Headers.Authorization.Parameter);
        using var doc = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal(OpenRouterAssistantService.DefaultModel, doc.RootElement.GetProperty("model").GetString());
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", handler.Request.RequestUri!.ToString());
        Assert.True(doc.RootElement.GetProperty("stream").GetBoolean());
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("hi", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task Uses_Model_Setting()
    {
        _settings.Set("assistantModel", "nvidia/nemotron-3.5-lightning:free");
        var handler = new FakeHandler(HttpStatusCode.OK, Sse("x"));
        await Collect(new OpenRouterAssistantService(_settings, handler, "k").StreamReplyAsync(new[] { new ChatTurn("user", "q") }));
        using var doc = JsonDocument.Parse(handler.RequestBody!);
        Assert.Equal("nvidia/nemotron-3.5-lightning:free", doc.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task Unauthorized_IsAuthError()
    {
        var handler = new FakeHandler(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"Incorrect API key\"}}");
        var svc = new OpenRouterAssistantService(_settings, handler, "bad");
        var ex = await Assert.ThrowsAsync<AssistantException>(() => Collect(svc.StreamReplyAsync(new[] { new ChatTurn("user", "q") })));
        Assert.True(ex.IsAuthError);
    }

    [Fact]
    public async Task ServerError_Message_Includes_OpenAI_Detail()
    {
        var handler = new FakeHandler(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"model not found\"}}");
        var svc = new OpenRouterAssistantService(_settings, handler, "k");
        var ex = await Assert.ThrowsAsync<AssistantException>(() => Collect(svc.StreamReplyAsync(new[] { new ChatTurn("user", "q") })));
        Assert.False(ex.IsAuthError);
        Assert.Contains("model not found", ex.Message);
    }

    [Fact]
    public async Task MidStreamError_Throws()
    {
        var body = Sse("partial") .Replace("data: [DONE]", "data: {\"error\":{\"message\":\"Provider overloaded\"}}\n\ndata: [DONE]");
        var svc = new OpenRouterAssistantService(_settings, new FakeHandler(HttpStatusCode.OK, body), "k");
        var ex = await Assert.ThrowsAsync<AssistantException>(() => Collect(svc.StreamReplyAsync(new[] { new ChatTurn("user", "q") })));
        Assert.Contains("Provider overloaded", ex.Message);
    }

    [Fact]
    public async Task ReasoningDeltas_AreNotShown()
    {
        var body = "data: {\"choices\":[{\"delta\":{\"reasoning\":\"thinking...\"}}]}\n\n" + Sse("Answer");
        var svc = new OpenRouterAssistantService(_settings, new FakeHandler(HttpStatusCode.OK, body), "k");
        Assert.Equal("Answer", await Collect(svc.StreamReplyAsync(new[] { new ChatTurn("user", "q") })));
    }

    [Fact]
    public async Task NoKey_IsAuthError_WithoutCallingTheApi()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, Sse("x"));
        var svc = new OpenRouterAssistantService(_settings, handler, null);
        Assert.False(svc.HasApiKey);
        await Assert.ThrowsAsync<AssistantException>(() => Collect(svc.StreamReplyAsync(new[] { new ChatTurn("user", "q") })));
        Assert.Null(handler.Request);
    }
}
