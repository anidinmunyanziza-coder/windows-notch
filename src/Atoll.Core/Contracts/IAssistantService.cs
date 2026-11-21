using System;
using System.Collections.Generic;
using System.Threading;

namespace Atoll.Core.Contracts;

/// <summary>One message in an assistant conversation ("user" or "assistant").</summary>
public sealed record ChatTurn(string Role, string Text);

/// <summary>The notch's AI assistant: a streaming chat backed by the user's own
/// API key, which is kept in the Windows credential store, never in settings.</summary>
public interface IAssistantService
{
    bool HasApiKey { get; }

    /// <summary>Saves the key (null or empty removes it).</summary>
    void SetApiKey(string? key);

    /// <summary>Streams the reply to the conversation, chunk by chunk.</summary>
    IAsyncEnumerable<string> StreamReplyAsync(IReadOnlyList<ChatTurn> history, CancellationToken ct = default);
}

/// <summary>A failed assistant request. <see cref="IsAuthError"/> means the key
/// was rejected and should be re-entered.</summary>
public sealed class AssistantException : Exception
{
    public bool IsAuthError { get; }
    public AssistantException(string message, bool isAuthError = false) : base(message) => IsAuthError = isAuthError;
}
