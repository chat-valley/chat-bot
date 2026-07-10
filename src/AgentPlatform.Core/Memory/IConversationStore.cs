using System.Collections.Concurrent;
using Microsoft.SemanticKernel.ChatCompletion;

namespace AgentPlatform.Core.Memory;

/// <summary>
/// Short-term memory (conversation history) soyutlaması.
/// </summary>
public interface IConversationStore
{
    Task<ChatHistory> GetOrCreateAsync(string sessionId, CancellationToken ct = default);
    Task AppendAsync(string sessionId, ChatHistory history, CancellationToken ct = default);
}

/// <summary>
/// Geliştirme/test için process-memory implementasyon. Restart'ta veri kaybolur.
/// Artık varsayılan olarak PostgresConversationStore kullanılıyor; bu sınıf
/// Postgres'siz hızlı yerel test için hâlâ mevcut (Program.cs'de değiştirerek kullanılabilir).
/// </summary>
public sealed class InMemoryConversationStore : IConversationStore
{
    private readonly ConcurrentDictionary<string, ChatHistory> _sessions = new();

    public Task<ChatHistory> GetOrCreateAsync(string sessionId, CancellationToken ct = default)
        => Task.FromResult(_sessions.GetOrAdd(sessionId, _ => new ChatHistory()));

    public Task AppendAsync(string sessionId, ChatHistory history, CancellationToken ct = default)
    {
        _sessions[sessionId] = history;
        return Task.CompletedTask;
    }
}