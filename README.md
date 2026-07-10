# AI Agent Platform — İskelet (v0)

Semantic Kernel (.NET) tabanlı, function calling + RAG + memory + güvenlik
katmanlarına sahip müşteri destek platformunun ilk iskeleti.

## Mimari

```
Kullanıcı → API (ASP.NET Core Minimal API)
         → ApiKeyAuthMiddleware (API'nin kendisini koru)
         → AgentOrchestrator (Core katmanı)
              ├─ IAgentKernelFactory  → Kernel (sağlayıcı: OpenAI/AzureOpenAI/Anthropic*)
              ├─ IConversationStore  → short-term memory (şu an in-memory)
              ├─ IRetriever          → RAG (şu an no-op, NullRetriever)
              └─ Plugins/*           → function calling (örnek: TimePlugin)
         → LLM → (gerekirse) Plugin/Function çağrısı → Yanıt
```

*Anthropic henüz implemente edilmedi — bkz. "Bilinen Sınırlamalar".

## Neden bu yapı?

| Karar | Gerekçe |
|---|---|
| Katmanlı monolit (mikroservis değil) | Bu aşamada mikroservis overhead'i (deployment, network, observability) maliyeti haklı çıkarmıyor. RAG büyüdükçe ayrı servise çıkarılabilir — arayüzler (`IRetriever`) buna hazır. |
| Sağlayıcı seçimi `Kernel` içinde izole | `AgentOrchestrator` hangi LLM'in arkada çalıştığını bilmiyor. Sağlayıcı değişimi = tek config satırı (`Llm:Provider`). |
| Kernel + plugin'ler Singleton | Her istekte yeniden kurulum maliyeti yok. `ChatHistory` session bazlı ayrıştırıldığı için state karışmıyor. |
| Yetkilendirme LLM dışında | Plugin fonksiyonları kendi içinde yetki/iş kuralı kontrolü yapar. LLM sadece "hangi fonksiyon, hangi parametre" kararını verir — icraya karışmaz. |
| RAG'ı Qdrant + no-op fallback ile bağla | `Rag:Enabled=false` yapılırsa `NullRetriever`'a düşer — Qdrant hiç ayağa kaldırılmamışsa bile sistem çalışmaya devam eder. |
| RAG context history'ye kalıcı eklenmez | Her turn'de yeniden retrieval yapılır (`AddSystemMessage` her seferinde). History şişmez, context güncel kalır — ama şu an her mesajda embedding çağrısı = ekstra maliyet. Kabul edilebilir bir trade-off, opsiyonel cache eklenebilir. |
| RAG başarısız olursa sohbet kesilmez | `QdrantRetriever` içinde try/catch var — Qdrant erişilemezse boş context ile LLM'e devam edilir, kullanıcı hata görmez. |

## Bilinen Sınırlamalar / Sonraki Adımlar

1. **Anthropic connector implemente edilmedi.** Semantic Kernel'in resmi paketinde
   Anthropic desteği yok (Anthropic'in mesaj/tool-use şeması OpenAI'dan farklı).
   `AgentKernelFactory.cs` içinde net bir hata ve yorum bırakıldı.
2. **Embedding üretimi şu an sadece OpenAI üzerinden.** Sağlayıcı olarak Azure/Anthropic
   seçsen bile embedding OpenAI ApiKey gerektiriyor (`Program.cs`'de bu kısıt açıkça
   loglanıyor/hata fırlatıyor). İstersen `Embedding:Provider` diye ayrı bir seçenek
   ekleyip bunu da soyutlayabiliriz.
3. **Qdrant.Client API notu:** `PointStruct`/`Vectors`/`Payload` kullanımı Qdrant.Client
   sürümleri arasında küçük farklar gösterebilir. `DocumentIngestionService.cs` içinde
   bunu işaretleyen bir yorum var — derleme hatası alırsan qdrant-dotnet GitHub
   örneklerine bakıp güncelle.
4. **In-memory conversation store** üretime uygun değil (restart'ta veri kaybı,
   çoklu instance'ta tutarsızlık). Redis implementasyonu gerekiyor.
5. **Chunking basit (karakter tabanlı).** Cümle/paragraf sınırına duyarlı veya
   semantic chunking ile değiştirilebilir — `IRetriever`/`DocumentChunker` arayüzü
   sabit kaldığı sürece çağıran kod etkilenmez.
6. **Ingestion senkron ve manuel** (`/api/rag/ingest`). Doküman sayısı büyüdükçe
   arka plan job'ına (Hangfire/Quartz) taşınmalı — aksi halde embedding süresi
   HTTP timeout'una takılabilir.
7. **Long-term memory (`IUserProfileStore`) implemente edilmedi** — yol haritasında #6.
8. **Rate limiting / prompt injection filtresi yok** — şu an sadece API-key auth var.
   Function call parametrelerine JSON schema doğrulaması eklenmedi (kritik pluginler
   yazılırken mutlaka eklenmeli).
9. **Test projesi yok.** Yapı netleşince xUnit ile unit + integration test eklenmeli.

## Çalıştırma

### Yerel (dotnet CLI)

```bash
# API anahtarını user-secrets ile ayarla (appsettings.json'a YAZMA)
cd src/AgentPlatform.Api
dotnet user-secrets init
dotnet user-secrets set "Llm:OpenAI:ApiKey" "sk-..."

dotnet restore
dotnet run
```

Test:
```bash
curl -X POST http://localhost:5000/api/chat \
  -H "Content-Type: application/json" \
  -d '{"sessionId":"test-1","message":"Saat kaç?"}'
```

### Docker

```bash
cp .env.example .env
# .env dosyasını doldur (OPENAI_API_KEY, API_KEY vb.)
docker compose up --build
```

Bu, `qdrant` ve `agent-api` servislerini birlikte ayağa kaldırır.

### RAG'ı Test Etme

```bash
# 1) documents/ klasöründeki .md/.txt dosyalarını Qdrant'a yükle
curl -X POST http://localhost:8080/api/rag/ingest -H "X-Api-Key: <API_KEY>"

# 2) İlgili bir soru sor — retriever otomatik devreye girer
curl -X POST http://localhost:8080/api/chat \
  -H "Content-Type: application/json" -H "X-Api-Key: <API_KEY>" \
  -d '{"sessionId":"test-1","message":"İade süresi kaç gün?"}'
```

Kendi dokümanlarını eklemek için `documents/` klasörüne `.md`/`.txt` dosyaları
koy ve `/api/rag/ingest`'i tekrar çağır (idempotent değil — aynı dosyayı tekrar
ingest edersen şu an duplicate chunk oluşur; bu bilinen bir sınırlama, ileride
dosya bazlı silme/upsert stratejisi eklenmeli).

## Klasör Yapısı

```
AgentPlatform.sln
src/
  AgentPlatform.Api/       ← HTTP katmanı, middleware, Program.cs
  AgentPlatform.Core/
    Kernel/                ← Sağlayıcı soyutlaması (LlmOptions, AgentKernelFactory)
    Orchestration/         ← AgentOrchestrator — akışın kalbi, RAG context injection burada
    Plugins/SamplePlugins/ ← Function calling örnekleri
    Memory/                ← Short-term (in-memory) + Long-term (stub) arayüzler
    Rag/                   ← RagOptions, DocumentChunker, QdrantRetriever, DocumentIngestionService
documents/                 ← RAG kaynağı (.md/.txt) — örnek dosya var
docker-compose.yml         ← agent-api + qdrant
.env.example
```

## Sırada Ne Var?

RAG akışı (#5) çalışır durumda: chunk → embed → Qdrant → retrieval → context injection.
Yol haritanıza göre sıradaki konular **#6 Memory** (long-term, `IUserProfileStore`'un
gerçek implementasyonu) ve **#7 Güvenlik** (rate limiting, prompt injection filtresi,
function call şema doğrulaması). Anthropic adapter'ı da bekleyen bir iş olarak duruyor.
