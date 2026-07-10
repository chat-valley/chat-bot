namespace AgentPlatform.Core.Security;

/// <summary>
/// Kanal-agnostik rate limiter. Hem HTTP middleware (API key bazlı) hem
/// Telegram servisi (Telegram userId bazlı) AYNI implementasyonu kullanır —
/// tek yerde tanımlı kural, iki kanalda da geçerli.
/// </summary>
public interface IRateLimiter
{
    /// <summary>true dönerse istek kabul edilir, false dönerse limit aşılmıştır.</summary>
    bool TryAcquire(string clientId);
}