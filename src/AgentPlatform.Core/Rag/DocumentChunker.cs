namespace AgentPlatform.Core.Rag;

/// <summary>
/// Basit, bağımlılıksız chunking. Cümle/paragraf sınırına duyarlı bir chunker
/// (örn. token-bazlı, semantic chunking) sonraki iterasyonda bunun yerini alabilir —
/// arayüz sabit kalacağı için çağıran kod etkilenmez.
/// </summary>
public static class DocumentChunker
{
    public static IEnumerable<string> Chunk(string text, int chunkSize, int overlap)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        if (overlap >= chunkSize)
            throw new ArgumentException("overlap, chunkSize'dan küçük olmalı.");

        var normalized = text.Replace("\r\n", "\n").Trim();
        var position = 0;

        while (position < normalized.Length)
        {
            var length = Math.Min(chunkSize, normalized.Length - position);
            var end = position + length;

            // Kelime ortasında kesmemek için son boşluğa geri çekil (mümkünse)
            if (end < normalized.Length)
            {
                var lastSpace = normalized.LastIndexOf(' ', end - 1, Math.Min(length, 100));
                if (lastSpace > position)
                    end = lastSpace;
            }

            var chunk = normalized[position..end].Trim();
            if (chunk.Length > 0)
                yield return chunk;

            if (end >= normalized.Length)
                yield break;

            position = end - overlap;
            if (position < 0) position = end;
        }
    }
}
