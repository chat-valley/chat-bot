using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AgentPlatform.Core.Memory;

/// <summary>
/// IUserProfileStore'un Postgres implementasyonu. Tercihler basit bir JSONB
/// kolonunda tutuluyor — ayrı bir "preferences" tablosu yerine tek satırda
/// esnek key-value saklamak, bu aşamada şema göçü (migration) derdi olmadan
/// yeterli. İhtiyaç büyürse normalize edilmiş bir tabloya geçilebilir.
/// </summary>
public sealed class PostgresUserProfileStore : IUserProfileStore
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<PostgresUserProfileStore> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private bool _initialized;

    public PostgresUserProfileStore(NpgsqlDataSource dataSource, ILogger<PostgresUserProfileStore> logger)
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
                CREATE TABLE IF NOT EXISTS user_profiles (
                    user_id TEXT PRIMARY KEY,
                    preferences JSONB NOT NULL DEFAULT '{}'::jsonb,
                    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
                );
                """;
            await cmd.ExecuteNonQueryAsync(ct);

            _initialized = true;
            _logger.LogInformation("Postgres user_profiles tablosu hazır.");
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<UserProfile?> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT preferences FROM user_profiles WHERE user_id = @userId";
        cmd.Parameters.AddWithValue("userId", userId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var json = reader.GetString(0);
        var prefs = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        return new UserProfile(userId, prefs);
    }

    public async Task SaveProfileAsync(UserProfile profile, CancellationToken ct = default)
    {
        await EnsureTableAsync(ct);

        var json = JsonSerializer.Serialize(profile.Preferences);

        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO user_profiles (user_id, preferences, updated_at)
            VALUES (@userId, @preferences::jsonb, now())
            ON CONFLICT (user_id) DO UPDATE
            SET preferences = @preferences::jsonb, updated_at = now();
            """;
        cmd.Parameters.AddWithValue("userId", profile.UserId);
        cmd.Parameters.AddWithValue("preferences", json);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}