namespace AgentPlatform.Core.Telegram;

/// <summary>
/// "Telegram" config bölümüne bind edilir.
///
/// TASARIM NOTU: Telegram, mevcut AgentOrchestrator'ı çağıran AYRI BİR KANALDIR
/// (tıpkı /api/chat gibi). Kod tekrarı yok — RAG, memory, function calling
/// otomatik olarak Telegram'da da çalışır. Session, "telegram-{chatId}" olarak
/// IConversationStore'a eşlenir.
/// </summary>
public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public bool Enabled { get; set; } = false;

    /// <summary>BotFather'dan alınan token. appsettings.json'a asla yazılmaz.</summary>
    public string BotToken { get; set; } = string.Empty;

    /// <summary>
    /// Virgülle ayrılmış Telegram user ID listesi (örn. "123456789,987654321").
    /// BOŞ BIRAKILIRSA BOT HERKESE AÇIK OLUR — her mesaj OpenAI'a ücretli istek
    /// gönderir. "Yetkilendirme LLM'ye bırakılmaz" ilkesi gereği bu kontrol
    /// deterministik kodda (TelegramBotHostedService) yapılır, LLM'e değil.
    /// </summary>
    public string AllowedUserIds { get; set; } = string.Empty;

    public IReadOnlyList<long> ParseAllowedUserIds()
    {
        if (string.IsNullOrWhiteSpace(AllowedUserIds))
            return Array.Empty<long>();

        return AllowedUserIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(long.Parse)
            .ToList();
    }
}
