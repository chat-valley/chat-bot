using AgentPlatform.Core.Orchestration;
using AgentPlatform.Core.Telegram;
using Microsoft.Extensions.Options;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using AgentPlatform.Core.Security;

namespace AgentPlatform.Api.Telegram;

/// <summary>
/// Telegram'ı ayrı bir kanal olarak AgentOrchestrator'a bağlar.
/// Kullanıcı → Telegram → [BU SERVİS] → AgentOrchestrator → (RAG + Memory + Function Calling) → LLM → Yanıt → Telegram
///
/// LONG POLLING kullanılıyor (webhook değil): yerel/Docker geliştirme ortamında
/// public HTTPS endpoint gerektirmediği için en basit başlangıç noktası.
/// Üretimde ölçeklenirken webhook'a geçmek daha verimli olur (bkz. README).
/// </summary>
public sealed class TelegramBotHostedService : BackgroundService
{
    private readonly TelegramOptions _options;
    private readonly IAgentOrchestrator _orchestrator;
    private readonly ILogger<TelegramBotHostedService> _logger;
    private readonly IReadOnlyList<long> _allowedUserIds;
    private readonly IRateLimiter _rateLimiter;

    public TelegramBotHostedService(
        IOptions<TelegramOptions> options,
        IAgentOrchestrator orchestrator,
        ILogger<TelegramBotHostedService> logger)
    {
        _options = options.Value;
        _orchestrator = orchestrator;
        _logger = logger;
        _allowedUserIds = _options.ParseAllowedUserIds();
    }

    public TelegramBotHostedService(
        IOptions<TelegramOptions> options,
        IAgentOrchestrator orchestrator,
        IRateLimiter rateLimiter,
        ILogger<TelegramBotHostedService> logger)
    {
        _options = options.Value;
        _orchestrator = orchestrator;
        _rateLimiter = rateLimiter;
        _logger = logger;
        _allowedUserIds = _options.ParseAllowedUserIds();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Telegram entegrasyonu kapalı (Telegram:Enabled=false).");
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.BotToken))
        {
            _logger.LogWarning("Telegram:BotToken boş — bot başlatılamadı.");
            return;
        }

        if (_allowedUserIds.Count == 0)
        {
            _logger.LogWarning(
                "Telegram:AllowedUserIds boş — bot HERKESE AÇIK. Botu bulan herkes mesaj atıp " +
                "OpenAI'a ücretli istek gönderebilir. Üretimde mutlaka bir allowlist tanımla.");
        }

        var botClient = new TelegramBotClient(_options.BotToken);

        var me = await botClient.GetMeAsync(cancellationToken: stoppingToken);
        _logger.LogInformation("Telegram botu başlatıldı: @{Username}", me.Username);

        var receiverOptions = new ReceiverOptions
        {
            AllowedUpdates = new[] { UpdateType.Message }
        };

        botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: stoppingToken);

        // StartReceiving arka planda kendi polling task'ını yönetir;
        // burada servisi ayakta tutmak için bekliyoruz.
        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken ct)
    {
        if (update.Message is not { Text: { } messageText } message)
            return;

        var chatId = message.Chat.Id;
        var userId = message.From?.Id ?? 0;

        // Yetkilendirme LLM'ye bırakılmaz ilkesi: kontrol burada, deterministik kodda yapılır.
        if (_allowedUserIds.Count > 0 && !_allowedUserIds.Contains(userId))
        {
            _logger.LogWarning("Yetkisiz Telegram erişim denemesi: UserId={UserId}, ChatId={ChatId}", userId, chatId);
            await botClient.SendTextMessageAsync(chatId, "Bu bota erişim yetkiniz yok.", cancellationToken: ct);
            return;
        }

        if (!_rateLimiter.TryAcquire($"telegram-{userId}"))
        {
            _logger.LogWarning("Telegram rate limit aşıldı: UserId={UserId}", userId);
            await botClient.SendTextMessageAsync(
                chatId, "Çok hızlı mesaj gönderiyorsun, biraz bekleyip tekrar dener misin?",
                cancellationToken: ct);
            return;
        }

        try
        {
            await botClient.SendChatActionAsync(chatId, ChatAction.Typing, cancellationToken: ct);

            // Telegram chat'i, mevcut IConversationStore'daki session mekanizmasına eşleniyor.
            // Aynı kullanıcı hem API'den hem Telegram'dan yazsa bile session'lar ayrı kalır.
            var sessionId = $"telegram-{chatId}";
            var reply = await _orchestrator.SendMessageAsync(sessionId, messageText, ct);

            await botClient.SendTextMessageAsync(chatId, reply, cancellationToken: ct);

            _logger.LogInformation("Telegram: ChatId={ChatId}, UserId={UserId} mesajı işlendi.", chatId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram: ChatId={ChatId} mesajı işlenirken hata.", chatId);
            await botClient.SendTextMessageAsync(
                chatId,
                "Üzgünüm, isteğini işlerken bir hata oluştu. Lütfen tekrar dener misin?",
                cancellationToken: ct);
        }
    }

    private Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken ct)
    {
        _logger.LogError(exception, "Telegram polling hatası.");
        return Task.CompletedTask;
    }
}
