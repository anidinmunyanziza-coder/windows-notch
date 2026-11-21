using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Atoll.Core.Contracts;
using Windows.Security.Credentials;

namespace Atoll.Services.Assistant;

/// <summary>
/// Chat for the notch assistant through OpenRouter (openrouter.ai), which
/// speaks the OpenAI Chat Completions format with streaming (server-sent
/// events). The model comes from the <c>assistantModel</c> setting (default
/// nvidia/nemotron-3-ultra-550b-a55b:free). The API key lives in Windows
/// Credential Manager (PasswordVault), encrypted to the signed-in user.
/// </summary>
public sealed class OpenRouterAssistantService : IAssistantService, IDisposable
{
    private const string Endpoint = "https://openrouter.ai/api/v1/chat/completions";
    private const string VaultResource = "Atoll.OpenRouter";
    private const string VaultUser = "api-key";
    public const string DefaultModel = "nvidia/nemotron-3-ultra-550b-a55b:free";
    private const string SystemPrompt =
        "You are the assistant built into a small notch at the top of a Windows desktop. " +
        "Answer directly and briefly: a few sentences or a short list unless the user asks for detail. " +
        "Use light Markdown only (short paragraphs, '-' bullets, **bold**, `code`).";

    private readonly ISettingsService _settings;
    private readonly HttpClient _http;
    private string? _key;
    private bool _keyLoaded;

    public OpenRouterAssistantService(ISettingsService settings)
    {
        _settings = settings;
        _http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>For tests: a fixed key (never stored) and a fake HTTP handler.</summary>
    public OpenRouterAssistantService(ISettingsService settings, HttpMessageHandler handler, string? key)
    {
        _settings = settings;
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _key = key;
        _keyLoaded = true;
    }

    public bool HasApiKey => !string.IsNullOrEmpty(LoadKey());

    public void SetApiKey(string? key)
    {
        var vault = new PasswordVault();
        try
        {
            foreach (var c in vault.FindAllByResource(VaultResource)) vault.Remove(c);
        }
        catch { /* none stored */ }

        key = key?.Trim();
        if (!string.IsNullOrEmpty(key))
            vault.Add(new PasswordCredential(VaultResource, VaultUser, key));
        _key = string.IsNullOrEmpty(key) ? null : key;
        _keyLoaded = true;
    }

    private string? LoadKey()
    {
        if (_keyLoaded) return _key;
        try
        {
            var c = new PasswordVault().Retrieve(VaultResource, VaultUser);
            c.RetrievePassword();
            _key = c.Password;
        }
        catch
        {
            _key = null; // nothing stored yet
        }
        _keyLoaded = true;
        return _key;
    }

    public async IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatTurn> history,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var key = LoadKey() ?? throw new AssistantException("Add your OpenRouter API key first.", isAuthError: true);
        var model = _settings.Get("assistantModel", DefaultModel);
        if (string.IsNullOrWhiteSpace(model)) model = DefaultModel;

        var messages = new List<object> { new { role = "system", content = SystemPrompt } };
        messages.AddRange(history.TakeLast(20).Select(t => (object)new { role = t.Role, content = t.Text }));
        var body = JsonSerializer.Serialize(new { model, messages, stream = true });

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        // OpenRouter's optional app attribution: just the app's name, nothing about the user.
        request.Headers.TryAddWithoutValidation("X-Title", "Atoll");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new AssistantException("Couldn't reach OpenRouter. Check your connection. (" + ex.Message + ")");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadErrorAsync(response, ct);
                var auth = response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
                throw new AssistantException(auth
                    ? "OpenRouter rejected the API key. Paste a valid key."
                    : response.StatusCode == HttpStatusCode.TooManyRequests
                        ? "Rate limit reached (free models are limited). Try again in a moment. " + detail
                        : response.StatusCode == HttpStatusCode.PaymentRequired
                            ? "OpenRouter says this needs credits. " + detail
                            : $"OpenRouter error ({(int)response.StatusCode}). {detail}", auth);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                var data = line[5..].Trim();
                if (data == "[DONE]") yield break;

                string? delta = null;
                string? streamError = null;
                try
                {
                    using var doc = JsonDocument.Parse(data);
                    if (doc.RootElement.TryGetProperty("error", out var err))
                        streamError = err.TryGetProperty("message", out var em) ? em.GetString() : err.ToString();
                    var choices = doc.RootElement.TryGetProperty("choices", out var ch) ? ch : default;
                    if (choices.ValueKind != JsonValueKind.Array) choices = JsonDocument.Parse("[]").RootElement;
                    if (choices.GetArrayLength() > 0
                        && choices[0].TryGetProperty("delta", out var d)
                        && d.TryGetProperty("content", out var content)
                        && content.ValueKind == JsonValueKind.String)
                        delta = content.GetString();
                }
                catch (JsonException) { }
                catch (KeyNotFoundException) { }

                if (streamError is not null) throw new AssistantException("OpenRouter: " + streamError);
                // Only the answer text is shown; reasoning models' "reasoning" deltas are skipped.
                if (!string.IsNullOrEmpty(delta)) yield return delta;
            }
        }
    }

    private static async System.Threading.Tasks.Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m))
                return m.GetString() ?? "";
        }
        catch { }
        return "";
    }

    public void Dispose() => _http.Dispose();
}
