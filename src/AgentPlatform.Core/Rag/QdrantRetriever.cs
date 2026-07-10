using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Embeddings;
using Qdrant.Client;

namespace AgentPlatform.Core.Rag;

/// <summary>
/// Similarity search adımı: kullanıcı sorgusunu embed eder, Qdrant'ta en yakın
/// chunk'ları bulur. Koleksiyon boşsa (henüz ingestion yapılmadıysa) sessizce
/// boş liste döner — LLM akışını kırmaz.
/// </summary>
public sealed class QdrantRetriever : IRetriever
{
    private readonly QdrantClient _client;
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly RagOptions _options;
    private readonly ILogger<QdrantRetriever> _logger;

    public QdrantRetriever(
        QdrantClient client,
        ITextEmbeddingGenerationService embeddingService,
        IOptions<RagOptions> options,
        ILogger<QdrantRetriever> logger)
    {
        _client = client;
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int topK = 5, CancellationToken ct = default)
    {
        try
        {
            var exists = await _client.CollectionExistsAsync(_options.Qdrant.CollectionName, cancellationToken: ct);
            if (!exists)
            {
                _logger.LogWarning("Qdrant koleksiyonu '{Collection}' henüz yok. Önce /api/rag/ingest çağırılmalı.",
                    _options.Qdrant.CollectionName);
                return Array.Empty<RetrievedChunk>();
            }

            var embedding = await _embeddingService.GenerateEmbeddingAsync(query, cancellationToken: ct);

            var results = await _client.SearchAsync(
                collectionName: _options.Qdrant.CollectionName,
                vector: embedding.ToArray(),
                limit: (ulong)topK,
                cancellationToken: ct);

            return results.Select(point => new RetrievedChunk(
                Content: point.Payload.TryGetValue("content", out var content) ? content.StringValue : string.Empty,
                SourceId: point.Payload.TryGetValue("source", out var source) ? source.StringValue : "unknown",
                Score: point.Score
            )).ToList();
        }
        catch (Exception ex)
        {
            // RAG bir "nice-to-have" context katmanı; Qdrant erişilemez olsa bile
            // sohbet akışı kesilmemeli. Hata loglanır, boş context ile devam edilir.
            _logger.LogError(ex, "Qdrant similarity search başarısız — context olmadan devam ediliyor.");
            return Array.Empty<RetrievedChunk>();
        }
    }
}
