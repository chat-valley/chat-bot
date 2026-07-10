namespace AgentPlatform.Core.Security;

/// <summary>"RateLimit" config bölümüne bind edilir.</summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimit";

    public int MaxRequestsPerWindow { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
}