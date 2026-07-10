namespace AgentPlatform.Core.Memory;

/// <summary>
/// Long-term memory: kullanıcı tercihleri, profil, kalıcı ayarlar.
/// Bilinçli olarak stub bırakıldı — proje yol haritasında "Memory" ayrı bir konu (#6).
/// Implementasyon adayları: Postgres (yapısal veriler) + embedding tabanlı
/// bir "semantic memory" (vector store) tercih/özet bilgileri için.
///
/// ÖNEMLİ TASARIM NOTU: Long-term memory'yi her turn'de tam context'e basmak
/// yerine, RAG'daki gibi retrieval ile sadece ilgili kısmı taşımak token/maliyet
/// açısından çok daha verimli (bkz. proje bağlamındaki RAG akışı).
/// </summary>
public interface IUserProfileStore
{
    Task<UserProfile?> GetProfileAsync(string userId, CancellationToken ct = default);
    Task SaveProfileAsync(UserProfile profile, CancellationToken ct = default);
}

public sealed record UserProfile(string UserId, IReadOnlyDictionary<string, string> Preferences);
