using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using Npgsql;

namespace AgentPlatform.Core.Memory;

/// <summary>
/// IConversationStore'un Postgres implementasyonu. Mesajlar JSONB kolonunda
/// tutuluyor — PostgresUserProfileStore ile aynı desen, aynı veritabanı,
/// ayrı tablo. Çoklu müşteri senaryosunda restart/deploy'larda sohbet
/// geçmişinin kaybolmaması için gerekli.
/// </summary>
public sealed class PostgresConversationStore : IConversationStore
{
    private sealed record StoredMessage(string Role, string Content);

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresConversationStore> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public PostgresConversationStore(NpgsqlDataSource dataSource, ILogger<PostgresConversationStore> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    private async Task EnsureTableAsync(CancellationToken ct)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            await using var conn = await _dataSource.OpenConnectionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE IF NOT EXISTS conversation_history (
                    session_id TEXT PRIMARY KEY,
                    messages JSONB NOT NULL DEFAULT '[]'::jsonb,
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );
                """;
            await cmd.ExecuteNonQueryAsync(ct);

            _initialized = true;
            _logger.LogInformation("Postgres conversation_history tablosu hazır.");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<ChatHistory> GetOrCreateAsync(string sessionId, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT messages FROM conversation_history WHERE session_id = @sessionId";
        cmd.Parameters.AddWithValue("sessionId", sessionId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return new ChatHistory();

        var json = reader.GetString(0);
        var stored = JsonSerializer.Deserialize<List<StoredMessage>>(json) ?? new();

        var history = new ChatHistory();
        foreach (var msg in stored)
        {
            // SAVUNMA AMAÇLI FİLTRE: AppendAsync zaten sadece user/assistant
            // kaydediyor, ama eski/bozuk satırlar (örn. bu düzeltmeden önce
            // yazılmış tool-call mesajları içeren kayıtlar) hâlâ tabloda
            // kalmış olabilir. Burada da filtrelemek, böyle bir veri
            // sızıntısında uygulamanın kendini otomatik korumasını sağlar —
            // manuel DB temizliğine bağımlı kalınmaz.
            var role = msg.Role.ToLowerInvariant();
            if (role != "user" && role != "assistant")
                continue;

            history.AddMessage(new AuthorRole(msg.Role), msg.Content);
        }

        return history;

    }

    public async Task AppendAsync(string sessionId, ChatHistory history, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);

        var stored = history
            .Where(m => (m.Role == AuthorRole.User || m.Role == AuthorRole.Assistant)
                        && !string.IsNullOrWhiteSpace(m.Content))
            .Select(m => new StoredMessage(m.Role.ToString(), m.Content!))
            .ToList();
        var json = JsonSerializer.Serialize(stored);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO conversation_history (session_id, messages, updated_at)
            VALUES (@sessionId, @messages::jsonb, now())
            ON CONFLICT (session_id) DO UPDATE
            SET messages = @messages::jsonb, updated_at = now();
            """;
        cmd.Parameters.AddWithValue("sessionId", sessionId);
        cmd.Parameters.AddWithValue("messages", json);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}