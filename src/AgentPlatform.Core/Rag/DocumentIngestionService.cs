using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel.Embeddings;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace AgentPlatform.Core.Rag;

/// <summary>
/// "Doküman → Chunk → Embedding → Vector Store" adımlarını yürütür.
/// Şu an tetikleme /api/rag/ingest endpoint'i üzerinden manuel yapılıyor.
/// İleride: dosya değişikliğinde otomatik tetikleme (FileSystemWatcher) veya
/// zamanlanmış bir job (örn. Hangfire/Quartz) ile değiştirilebilir.
/// </summary>
public sealed class DocumentIngestionService
{
    // NOT: PointStruct / Vectors / Payload API'si Qdrant.Client sürümleri arasında
    // küçük farklar gösterebilir (örn. Vectors.Data vs. doğrudan float[] ataması).
    // `dotnet restore` sonrası derleme hatası alırsan, yüklü Qdrant.Client sürümünün
    // örneklerine (GitHub: qdrant/qdrant-dotnet) bakıp bu iki metodu güncelle;
    // geri kalan mimari (IRetriever, chunking, DI) etkilenmez.
    private readonly QdrantClient _client;
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly RagOptions _options;
    private readonly ILogger<DocumentIngestionService> _logger;

    public DocumentIngestionService(
        QdrantClient client,
        ITextEmbeddingGenerationService embeddingService,
        IOptions<RagOptions> options,
        ILogger<DocumentIngestionService> logger)
    {
        _client = client;
        _embeddingService = embeddingService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<int> IngestFolderAsync(CancellationToken ct = default)
    {
        await EnsureCollectionAsync(ct);

        if (!Directory.Exists(_options.DocumentsFolder))
        {
            _logger.LogWarning("Doküman klasörü bulunamadı: {Folder}", _options.DocumentsFolder);
            return 0;
        }

        var files = Directory.EnumerateFiles(_options.DocumentsFolder, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                        f.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var points = new List<PointStruct>();
        ulong id = 0;

        foreach (var file in files)
        {
            var text = await File.ReadAllTextAsync(file, ct);
            var fileName = Path.GetFileName(file);

            // İDEMPOTENCY: Bu dosyaya ait eski chunk'ları silmeden yeniden ingest
            // edersen duplicate birikir. Yeniden işlemeden önce "source" payload'ı
            // bu dosya adına eşit olan tüm eski noktaları temizliyoruz.
            // NOT: Qdrant.Client'ın Filter/Conditions API'si sürümler arası küçük
            // farklar gösterebilir — derleme hatası alırsan qdrant-dotnet GitHub
            // örneklerine bak.
            var deleteFilter = new Filter
            {
                Must = { Conditions.MatchKeyword("source", fileName) }
            };
            await _client.DeleteAsync(_options.Qdrant.CollectionName, deleteFilter, cancellationToken: ct);

            foreach (var chunk in DocumentChunker.Chunk(text, _options.ChunkSize, _options.ChunkOverlap))
            {
                var embedding = await _embeddingService.GenerateEmbeddingAsync(chunk, cancellationToken: ct);

                var point = new PointStruct { Id = new PointId { Num = id++ } };
                point.Vectors = embedding.ToArray();
                point.Payload.Add("content", chunk);
                point.Payload.Add("source", fileName);

                points.Add(point);
            }

            _logger.LogInformation("İşlendi (eski chunk'lar temizlendi): {File}", fileName);
        }

        if (points.Count > 0)
        {
            await _client.UpsertAsync(_options.Qdrant.CollectionName, points, cancellationToken: ct);
        }

        _logger.LogInformation("RAG ingestion tamamlandı: {FileCount} dosya, {ChunkCount} chunk.",
            files.Count, points.Count);

        return points.Count;
    }

    private async Task EnsureCollectionAsync(CancellationToken ct)
    {
        var exists = await _client.CollectionExistsAsync(_options.Qdrant.CollectionName, cancellationToken: ct);
        if (exists)
            return;

        await _client.CreateCollectionAsync(
            collectionName: _options.Qdrant.CollectionName,
            vectorsConfig: new VectorParams
            {
                Size = (ulong)_options.EmbeddingDimensions,
                Distance = Distance.Cosine
            },
            cancellationToken: ct);

        _logger.LogInformation("Qdrant koleksiyonu oluşturuldu: {Collection}", _options.Qdrant.CollectionName);
    }
}
