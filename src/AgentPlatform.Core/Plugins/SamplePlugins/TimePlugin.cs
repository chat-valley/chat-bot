using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace AgentPlatform.Core.Plugins.SamplePlugins;

/// <summary>
/// Örnek plugin: LLM'in function calling ile çağırabileceği basit bir yetenek.
/// Gerçek pluginler (örn. sipariş durumu sorgulama, bilet açma) bu deseni takip eder:
///
///   1) [KernelFunction] + [Description] ile LLM'e "ne işe yaradığını" anlat.
///   2) Fonksiyon İÇİNDE yetkilendirme/iş kuralı kontrolü yap — LLM'e güvenme.
///   3) Kritik işlemleri logla (aşağıdaki gibi ILogger enjekte ederek).
/// </summary>
public sealed class TimePlugin
{
    [KernelFunction("get_current_utc_time")]
    [Description("Şu anki UTC tarih ve saatini döner. Kullanıcı 'saat kaç' gibi sorular sorduğunda kullanılır.")]
    public string GetCurrentUtcTime()
    {
        return DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
    }
}
