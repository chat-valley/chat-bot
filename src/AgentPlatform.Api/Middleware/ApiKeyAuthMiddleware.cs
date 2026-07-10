namespace AgentPlatform.Api.Middleware;

/// <summary>
/// "Kritik işlemler loglanır" ve genel güvenlik ilkesi gereği, bu API'nin kendisi
/// çıplak internete açılmamalı. Basit bir paylaşılan-secret kontrolü — üretimde
/// bunun yerine OAuth2/JWT (örn. Azure AD, Auth0) veya API Gateway seviyesinde
/// mTLS/JWT doğrulaması tercih edilmeli. Bu, o katmana giden en basit ilk adım.
/// </summary>
public sealed class ApiKeyAuthMiddleware
{
    private const string HeaderName = "X-Api-Key";
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthMiddleware> _logger;
    private readonly string? _expectedKey;

    public ApiKeyAuthMiddleware(RequestDelegate next, ILogger<ApiKeyAuthMiddleware> logger, IConfiguration config)
    {
        _next = next;
        _logger = logger;
        // Ortam değişkeninden okunur: API_KEY. appsettings.json'a asla yazılmaz.
        _expectedKey = config["Security:ApiKey"];
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Sağlık kontrolü ve Swagger'ı korumadan hariç tut
        if (context.Request.Path.StartsWithSegments("/health") ||
            context.Request.Path.StartsWithSegments("/swagger"))
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrEmpty(_expectedKey))
        {
            _logger.LogWarning("Security:ApiKey konfigüre edilmemiş — API anahtarsız çalışıyor. Üretimde bu kesinlikle ayarlanmalı.");
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var providedKey) ||
            providedKey != _expectedKey)
        {
            _logger.LogWarning("Yetkisiz erişim denemesi: {Path} — IP: {Ip}",
                context.Request.Path, context.Connection.RemoteIpAddress);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { error = "Geçersiz veya eksik X-Api-Key." });
            return;
        }

        await _next(context);
    }
}
