using AgentPlatform.Core.Rag;

namespace AgentPlatform.Api.Endpoints;

public static class RagEndpoints
{
    public static void MapRagEndpoints(this WebApplication app)
    {
        // Manuel tetikleme: documents/ klasörünü tara, chunk'la, embed et, Qdrant'a yükle.
        // Küçük veri setleri için yeterli; büyüdükçe arka plan job'ına taşınmalı (senkron
        // HTTP isteğinde uzun süren embedding işlemi timeout riski taşır).
        app.MapPost("/api/rag/ingest", async (DocumentIngestionService ingestion, CancellationToken ct) =>
        {
            var chunkCount = await ingestion.IngestFolderAsync(ct);
            return Results.Ok(new { status = "completed", chunksIndexed = chunkCount });
        })
        .WithName("RagIngest");
    }
}
