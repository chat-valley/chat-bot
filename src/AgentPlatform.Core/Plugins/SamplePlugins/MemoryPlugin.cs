using System.ComponentModel;
using AgentPlatform.Core.Memory;
using Microsoft.SemanticKernel;

namespace AgentPlatform.Core.Plugins;

/// <summary>
/// Long-term memory'yi function calling üzerinden LLM'e açar. LLM, kullanıcı
/// kalıcı bir bilgi paylaştığında veya açıkça istediğinde bu fonksiyonları çağırır.
/// Yetkilendirme burada da LLM'e bırakılmıyor: userId, AgentOrchestrator'ın
/// session başında sisteme enjekte ettiği değerden geliyor, LLM'in uydurduğu
/// bir değer değil.
/// </summary>
public sealed class MemoryPlugin
{
    private readonly IUserProfileStore _profileStore;

    public MemoryPlugin(IUserProfileStore profileStore)
    {
        _profileStore = profileStore;
    }

    [KernelFunction("remember_user_preference")]
    [Description("Kullanıcı kendisiyle ilgili kalıcı olarak hatırlanmasını istediği bir bilgi " +
                  "paylaştığında (isim, meslek, ilgi alanı vb.) veya açıkça 'bunu hatırla' dediğinde " +
                  "çağrılır. Bilgi, gelecekteki sohbetlerde de hatırlanacak şekilde kalıcı kaydedilir.")]
    public async Task<string> RememberUserPreferenceAsync(
        [Description("Sistem mesajından alınan kullanıcı kimliği")] string userId,
        [Description("Tercih anahtarı, örn. 'isim', 'meslek', 'ilgi_alani'")] string key,
        [Description("Tercih değeri")] string value)
    {
        // Yetkilendirme/iş kuralı LLM'e bırakılmaz: parametre doğrulaması burada,
        // deterministik kodda yapılır. LLM'in gönderdiği değerlere körü körüne güvenilmez.
        const int maxKeyLength = 100;
        const int maxValueLength = 500;

        if (string.IsNullOrWhiteSpace(userId))
            return "Hata: Geçersiz kullanıcı kimliği. Bu bilgi kaydedilemedi.";

        key = key.Trim();
        value = value.Trim();

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return "Hata: Anahtar veya değer boş olamaz.";

        if (key.Length > maxKeyLength)
            return $"Hata: Anahtar çok uzun (maksimum {maxKeyLength} karakter).";

        if (value.Length > maxValueLength)
            return $"Hata: Değer çok uzun (maksimum {maxValueLength} karakter). " +
                   "Daha kısa ve öz bir şekilde ifade et.";

        var existing = await _profileStore.GetProfileAsync(userId)
            ?? new UserProfile(userId, new Dictionary<string, string>());

        // Bir kullanıcının kaydedebileceği tercih sayısını sınırla (kötüye kullanım/
        // depolama şişmesi koruması) — bir istismar senaryosunda biri binlerce
        // sahte "tercih" kaydettirip veritabanını şişiremesin.
        const int maxPreferencesPerUser = 50;
        if (!existing.Preferences.ContainsKey(key) && existing.Preferences.Count >= maxPreferencesPerUser)
            return $"Hata: Kullanıcı başına maksimum {maxPreferencesPerUser} tercih kaydedilebilir. " +
                   "Önce mevcut bir tercihi güncellemeyi dene.";

        var updatedPrefs = new Dictionary<string, string>(existing.Preferences) { [key] = value };
        await _profileStore.SaveProfileAsync(new UserProfile(userId, updatedPrefs));

        return $"Kaydedildi: {key} = {value}";
    }

    [KernelFunction("recall_user_preferences")]
    [Description("Kullanıcı hakkında daha önce kaydedilmiş tüm kalıcı bilgileri/tercihleri getirir. " +
                  "Kullanıcı 'beni hatırlıyor musun' gibi sorduğunda veya kişiselleştirilmiş bir yanıt " +
                  "vermeden önce çağrılır.")]
    public async Task<string> RecallUserPreferencesAsync(
        [Description("Sistem mesajından alınan kullanıcı kimliği")] string userId)
    {
        var profile = await _profileStore.GetProfileAsync(userId);
        if (profile is null || profile.Preferences.Count == 0)
            return "Bu kullanıcı için kayıtlı bir tercih/bilgi yok.";

        return string.Join(", ", profile.Preferences.Select(kv => $"{kv.Key}: {kv.Value}"));
    }
}