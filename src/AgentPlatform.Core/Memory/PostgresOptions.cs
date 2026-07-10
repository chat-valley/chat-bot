namespace AgentPlatform.Core.Memory;

/// <summary>"Postgres" config bölümüne bind edilir.</summary>
public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    public string ConnectionString { get; set; } = string.Empty;
}