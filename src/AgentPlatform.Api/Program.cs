using AgentPlatform.Api.Endpoints;
using AgentPlatform.Api.Middleware;
using AgentPlatform.Core.Kernel;
using AgentPlatform.Core.Memory;
using AgentPlatform.Core.Orchestration;
using AgentPlatform.Core.Rag;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.SemanticKernel.Embeddings;
using Qdrant.Client;
using Serilog;
using AgentPlatform.Api.Telegram;
using AgentPlatform.Core.Telegram;
using Npgsql;
using AgentPlatform.Core.Security;

var builder = WebApplication.CreateBuilder(args);

// --- Observability: structured logging (Serilog) ---
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

// --- Configuration binding ---
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.Configure<RagOptions>(builder.Configuration.GetSection(RagOptions.SectionName));
builder.Services.Configure<TelegramOptions>(builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services.Configure<PostgresOptions>(builder.Configuration.GetSection(PostgresOptions.SectionName));
builder.Services.Configure<RateLimitOptions>(builder.Configuration.GetSection(RateLimitOptions.SectionName));
// --- DI kayıtları ---
// Kernel factory: sağlayıcı (OpenAI/AzureOpenAI/Anthropic) burada izole edilir.
builder.Services.AddSingleton<IAgentKernelFactory, AgentKernelFactory>();
builder.Services.AddSingleton<IRateLimiter, FixedWindowRateLimiter>();

// Short-term memory: geliştirme için in-memory. Üretimde Redis implementasyonuyla değiştirilecek.
builder.Services.AddSingleton<IConversationStore, PostgresConversationStore>();

builder.Services.AddSingleton(sp =>
{
    var pg = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
    if (string.IsNullOrWhiteSpace(pg.ConnectionString))
        throw new InvalidOperationException(
            "Postgres:ConnectionString boş. .env dosyasında POSTGRES_CONNECTION_STRING tanımlı mı kontrol et.");
    return new NpgsqlDataSourceBuilder(pg.ConnectionString).Build();
});

builder.Services.AddSingleton<IUserProfileStore, PostgresUserProfileStore>();

// --- RAG kayıtları ---
// Embedding üretimi ayrı bir servis olarak kaydedilir (Kernel'den bağımsız),
// çünkü hem ingestion hem retrieval bunu kullanır, ikisi de Kernel'e ihtiyaç duymaz.
builder.Services.AddSingleton<ITextEmbeddingGenerationService>(sp =>
{
    var llm = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
    var rag = sp.GetRequiredService<IOptions<RagOptions>>().Value;

    if (string.IsNullOrWhiteSpace(llm.OpenAI.ApiKey))
        throw new InvalidOperationException(
            "RAG için embedding servisi OpenAI ApiKey gerektirir (Llm:OpenAI:ApiKey). " +
            "Sağlayıcı Anthropic/Azure olsa bile şu an embedding OpenAI üzerinden üretiliyor; " +
            "istersen ayrı bir Embedding:Provider seçeneği ekleyip bunu da soyutlayabiliriz.");

#pragma warning disable SKEXP0010
    return new OpenAITextEmbeddingGenerationService(rag.EmbeddingModel, llm.OpenAI.ApiKey);
#pragma warning restore SKEXP0010
});

builder.Services.AddSingleton(sp =>
{
    var rag = sp.GetRequiredService<IOptions<RagOptions>>().Value;
    return new QdrantClient(rag.Qdrant.Host, rag.Qdrant.Port);
});

builder.Services.AddSingleton<DocumentIngestionService>();

// Rag:Enabled=false ise NullRetriever'a düş — Qdrant'a hiç bağlanmaya çalışmaz.
builder.Services.AddSingleton<IRetriever>(sp =>
{
    var rag = sp.GetRequiredService<IOptions<RagOptions>>().Value;
    return rag.Enabled
        ? sp.GetRequiredService<QdrantRetriever>()
        : new NullRetriever();
});
builder.Services.AddSingleton<QdrantRetriever>();

// Orchestrator + Kernel + plugin kaydı bir kez yapılır (Singleton).
builder.Services.AddSingleton<IAgentOrchestrator, AgentOrchestrator>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHostedService<TelegramBotHostedService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// API'nin kendisini koru (LLM sağlayıcı anahtarından bağımsız bir katman)
app.UseMiddleware<ApiKeyAuthMiddleware>();
app.UseMiddleware<RateLimitingMiddleware>();
app.MapChatEndpoints();
app.MapRagEndpoints();
app.Run();
