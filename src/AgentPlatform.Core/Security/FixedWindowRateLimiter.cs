using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace AgentPlatform.Core.Security;

/// <summary>
/// Basit sabit-pencere (fixed window) rate limiter. Sliding window veya
/// token-bucket kadar hassas değil ama bu ölçekte (küçük kullanıcı sayısı,
/// test amaçlı kullanım) yeterli ve bağımlılıksız (Redis gerektirmiyor).
/// ÜRETİME UYGUN DEĞİL NOTU: In-memory olduğu için birden fazla API instance'ı
/// çalıştırırsan (yatay ölçekleme) her instance kendi sayacını tutar — limit
/// instance sayısıyla çarpılmış olur. Tek instance'ta doğru çalışır.
/// </summary>
public sealed class FixedWindowRateLimiter : IRateLimiter
{
    private sealed class Bucket
    {
        public int Count;
        public DateTime WindowStart;
    }

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new();
    private readonly RateLimitOptions _options;

    public FixedWindowRateLimiter(IOptions<RateLimitOptions> options)
    {
        _options = options.Value;
    }

    public bool TryAcquire(string clientId)
    {
        var now = DateTime.UtcNow;
        var bucket = _buckets.GetOrAdd(clientId, _ => new Bucket { Count = 0, WindowStart = now });

        lock (bucket)
        {
            if ((now - bucket.WindowStart).TotalSeconds > _options.WindowSeconds)
            {
                bucket.WindowStart = now;
                bucket.Count = 0;
            }

            if (bucket.Count >= _options.MaxRequestsPerWindow)
                return false;

            bucket.Count++;
            return true;
        }
    }
}