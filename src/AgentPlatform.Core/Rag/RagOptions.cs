namespace AgentPlatform.Core.Rag;

/// <summary>"Rag" config bölümüne bind edilir.</summary>
public sealed class RagOptions
{
    public const string SectionName = "Rag";

    /// <summary>RAG etkin mi? False ise IRetriever olarak NullRetriever kullanılır.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>.txt/.md dosyalarının okunacağı klasör (recursive taranır).</summary>
    public string DocumentsFolder { get; set; } = "documents";

    public int ChunkSize { get; set; } = 800;
    public int ChunkOverlap { get; set; } = 150;

    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>text-embedding-3-small = 1536. Model değişirse burası da değişmeli.</summary>
    public int EmbeddingDimensions { get; set; } = 1536;

    public QdrantSettings Qdrant { get; set; } = new();
}

public sealed class QdrantSettings
{
    public string Host { get; set; } = "localhost";

    /// <summary>Qdrant.Client gRPC portu kullanır (varsayılan 6334, REST 6333 değil).</summary>
    public int Port { get; set; } = 6334;

    public string CollectionName { get; set; } = "agent-platform-docs";
}
