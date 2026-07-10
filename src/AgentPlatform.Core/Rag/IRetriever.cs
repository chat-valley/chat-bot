namespace AgentPlatform.Core.Rag;

/// <summary>
/// RAG akışının "Similarity Search → İlgili içerik" adımına karşılık gelen arayüz.
///
/// Akış: Doküman → Chunk (DocumentChunker) → Embedding (OpenAI) → Qdrant → Similarity Search → İlgili içerik → LLM
/// Gerçek implementasyon: QdrantRetriever. RAG kapalıysa (Rag:Enabled=false) NullRetriever kullanılır.
/// </summary>
public interface IRetriever
{
    Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int topK = 5, CancellationToken ct = default);
}

public sealed record RetrievedChunk(string Content, string SourceId, double Score);

/// <summary>
/// RAG henüz bağlanmadığı için no-op implementasyon — DI graph'ı kırmadan
/// Orchestration'ın bugünden IRetriever'a bağımlı olabilmesini sağlar.
/// </summary>
public sealed class NullRetriever : IRetriever
{
    public Task<IReadOnlyList<RetrievedChunk>> RetrieveAsync(string query, int topK = 5, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<RetrievedChunk>>(Array.Empty<RetrievedChunk>());
}
