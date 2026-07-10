using AgentPlatform.Core.Security;

namespace AgentPlatform.Api.Middleware;

/// <summary>
/// HTTP kanalı için rate limiting. Client kimliği olarak X-Api-Key (yoksa IP)
/// kullanılır. ApiKeyAuthMiddleware'den SONRA çalışmalı — geçersiz key zaten
/// 401 ile erken kesiliyor, rate limit bütçesini boşa harcamıyor.
/// </summary>
public sealed class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IRateLimiter _rateLimiter;
    private readonly ILogger<RateLimitingMiddleware> _logger;

    public RateLimitingMiddleware(RequestDelegate next, IRateLimiter rateLimiter, ILogger<RateLimitingMiddleware> logger)
    {
        _next = next;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/health"))
        {
            await _next(context);
            return;
        }

        var clientId = context.Request.Headers.TryGetValue("X-Api-Key", out var apiKey) && !string.IsNullOrEmpty(apiKey)
            ? $"apikey-{apiKey}"
            : $"ip-{context.Connection.RemoteIpAddress}";

        if (!_rateLimiter.TryAcquire(clientId))
        {
            _logger.LogWarning("Rate limit aşıldı: {ClientId}, Path: {Path}", clientId, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsJsonAsync(new { error = "Çok fazla istek gönderdiniz, lütfen biraz bekleyin." });
            return;
        }

        await _next(context);
    }
}