# AI Agent Platform

Semantic Kernel (.NET) tabanlı, function calling + RAG + memory + güvenlik
katmanlarına sahip müşteri destek platformu.

## Mimari

```
Kullanıcı → API (HTTP) veya Telegram
         → [API] ApiKeyAuthMiddleware → RateLimitingMiddleware
         → [Telegram] Allowlist kontrolü → Rate limiter kontrolü
         → AgentOrchestrator (ORTAK, tek kod yolu — iki kanal da aynı akışı kullanır)
              ├─ IAgentKernelFactory  → Kernel (sağlayıcı: OpenAI/AzureOpenAI/Anthropic*)
              ├─ IConversationStore  → Postgres (sohbet geçmişi, kalıcı)
              ├─ IRetriever          → Qdrant + OpenAI embedding (RAG, idempotent)
              ├─ IUserProfileStore   → Postgres (long-term memory)
              └─ Plugins/
                  ├─ TimePlugin       → function calling örneği
                  └─ MemoryPlugin     → remember/recall (Postgres'e yazar/okur)
         → LLM → (gerekirse) Plugin çağrısı → Yanıt
```

*Anthropic henüz implemente edilmedi — bkz. "Bilinen Sınırlamalar".

## Neden bu yapı?

| Karar | Gerekçe |
|---|---|
| Katmanlı monolit (mikroservis değil) | Bu ölçekte mikroservis overhead'i (deployment, network, observability) maliyeti haklı çıkarmıyor. |
| Sağlayıcı seçimi `Kernel` içinde izole | `AgentOrchestrator` hangi LLM'in arkada çalıştığını bilmiyor. Sağlayıcı değişimi = tek config satırı (`Llm:Provider`). |
| Kernel + plugin'ler Singleton | Her istekte yeniden kurulum maliyeti yok. `ChatHistory` session bazlı ayrıştırıldığı için state karışmıyor. |
| Yetkilendirme LLM dışında | Plugin fonksiyonları kendi içinde yetki/iş kuralı kontrolü yapar. LLM sadece "hangi fonksiyon, hangi parametre" kararını verir — icraya karışmaz. |
| Telegram, aynı `AgentOrchestrator`'ı çağıran ayrı bir kanal | Kod tekrarı yok — RAG, memory, function calling otomatik olarak Telegram'da da çalışır. Long polling kullanıldı (webhook değil), çünkü public HTTPS endpoint gerektirmiyor. |
| Long-term memory + sohbet geçmişi: Postgres | Redis yerine ilişkisel/kalıcı bir çözüm seçildi — sistem ileride çok kullanıcılı bir yapıya evrilecek. |
| Memory: function calling ile (`MemoryPlugin`) | LLM ne zaman hatırlanacağına karar verir, ama veri yazımı deterministik kodda doğrulanıp Postgres'e yazılır — LLM'e asla ham yazma yetkisi verilmez. |
| Rate limiting: kanal-agnostik `IRateLimiter` | Hem HTTP middleware hem Telegram servisi aynı implementasyonu kullanır — tek kural, iki kanal. |
| RAG'ı Qdrant + no-op fallback ile bağla | `Rag:Enabled=false` yapılırsa `NullRetriever`'a düşer — Qdrant hiç ayağa kaldırılmamışsa bile sistem çalışmaya devam eder. |
| RAG context history'ye kalıcı eklenmez | Her turn'de yeniden retrieval yapılır. History şişmez, context güncel kalır. |
| RAG ingestion idempotent | Aynı dosya tekrar ingest edilmeden önce, o dosyaya ait eski chunk'lar Qdrant'tan silinir — tekrar tekrar duplicate birikmez. |
| RAG başarısız olursa sohbet kesilmez | `QdrantRetriever` içinde try/catch var — Qdrant erişilemezse boş context ile LLM'e devam edilir. |
| Prompt injection savunması: sistem mesajı + RAG etiketleme | RAG'dan gelen doküman içeriği `[KAYNAK] (GÜVENİLMEYEN İÇERİK)` etiketiyle işaretlenir, LLM'e bunların talimat değil bilgi olduğu öğretilir. Tam bağışıklık garantisi vermez, pratik bir savunma katmanı. |
| BackgroundService'ler try/catch ile korunur | Bir kanalın (örn. Telegram) yanlış konfigürasyonu, diğer sağlıklı kanalları (chat, RAG) düşürmemeli. `HostOptions.BackgroundServiceExceptionBehavior = Ignore` ek güvenlik katmanı olarak eklendi. |

## Telegram Entegrasyonu

`TelegramBotHostedService`, mevcut `AgentOrchestrator`'ı çağıran ayrı bir kanal
(`BackgroundService`). Her Telegram sohbeti `telegram-{chatId}` şeklinde bir
session'a eşlenir.

### Kurulum

**1.** BotFather'dan token al, `.env`'e ekle:
```
TELEGRAM_ENABLED=true
TELEGRAM_BOT_TOKEN=123456:ABC-DEF...
```

**2. (Şiddetle önerilir) Erişimi kısıtla.** Telegram'da `@userinfobot`'a mesaj at,
user ID'ni öğren:
```
TELEGRAM_ALLOWED_USER_IDS=123456789
```
Birden fazla kişi için virgülle ayır. **Boş bırakırsan bot herkese açık olur**
ve her mesaj senin OpenAI hesabından ücretli istek tüketir.

**3.** Yeniden başlat:
```bash
docker compose down
docker compose up --build
```
Logda `Telegram botu başlatıldı: @bot_adın` görmen lazım.

## Memory (Long-term + Sohbet Geçmişi)

İki Postgres tablosu kullanılıyor:

- **`user_profiles`** — `MemoryPlugin` üzerinden function calling ile yazılıp okunuyor
  (`remember_user_preference`, `recall_user_preferences`). Kullanıcı "adımı hatırla"
  dediğinde LLM otomatik olarak bu fonksiyonu çağırır.
- **`conversation_history`** — her session'ın sohbet geçmişi, container restart'ında
  kaybolmaz.

**Önemli:** Sadece `User`/`Assistant` rolündeki düz metin mesajlar Postgres'e
kaydedilir. Function-calling ara mesajları (tool-call/tool-result) kasıtlı olarak
kaydedilmez — bunlar serileştirilip geri yüklenince OpenAI'ın beklediği yapı
bozulur (`No function result provided` hatasına yol açar).

**Bilinen sınırlama:** `userId` olarak `sessionId` kullanılıyor. API üzerinden
her istekte farklı bir `sessionId` gönderirsen, memory farklı "kullanıcı" gibi
davranır — aynı gerçek kullanıcı için hep aynı `sessionId`'yi kullan.

## Güvenlik Katmanı

- **API Key auth** (`ApiKeyAuthMiddleware`) — HTTP kanalını korur.
- **Rate limiting** (`FixedWindowRateLimiter`) — hem HTTP hem Telegram için, kanal-agnostik
  tek bir implementasyon. Varsayılan: `RateLimit:WindowSeconds` içinde
  `RateLimit:MaxRequestsPerWindow` istek.
- **Function şema doğrulama** — `MemoryPlugin` içinde anahtar/değer uzunluğu,
  boşluk kontrolü ve kullanıcı başına maksimum tercih sayısı sınırlaması. LLM'in
  gönderdiği parametrelere körü körüne güvenilmiyor.
- **Prompt injection temel önlemi** — sistem talimatlarının değiştirilemeyeceği
  açıkça belirtiliyor, RAG içeriği "güvenilmeyen içerik" olarak etiketleniyor.

## Bilinen Sınırlamalar / Sonraki Adımlar

1. **Anthropic connector implemente edilmedi.** SK'nin resmi paketinde yok
   (mesaj/tool-use şeması OpenAI'dan farklı). `AgentKernelFactory.cs` içinde
   net bir hata ve yorum var.
2. **Embedding üretimi sadece OpenAI üzerinden.** Sağlayıcı Azure/Anthropic
   olsa bile embedding için ayrıca `Llm:OpenAI:ApiKey` gerekiyor.
3. **Qdrant.Client / Telegram.Bot API notları:** Bu kütüphanelerin API yüzeyi
   sürümler arası değişebiliyor. İlgili dosyalarda uyarı yorumları var —
   derleme hatası alırsan güncel GitHub örneklerine bak.
4. **Chunking basit (karakter tabanlı).** Semantic chunking ile değiştirilebilir.
5. **Ingestion senkron ve manuel** (`/api/rag/ingest`). Büyüdükçe arka plan
   job'ına (Hangfire/Quartz) taşınmalı.
6. **Sohbet geçmişi sınırsız büyüyor.** Kırpma/özetleme stratejisi yok — uzun
   konuşmalarda token/maliyet artar.
7. **Rate limiting in-memory ve fixed-window.** Çoklu API instance'ında
   (yatay ölçekleme) her instance kendi sayacını tutar, limit çarpılır.
   Dağıtık bir çözüm (Redis) gerekecek.
8. **TEK PAYLAŞILAN `API_KEY`.** Sistem şu an tek bir gizli anahtarla korunuyor.
   Nihai hedef (farklı müşterilerin giriş yapıp kullanması) için bu yetersiz —
   gerçek bir kullanıcı kimlik doğrulama sistemi (login/JWT) gerekiyor. **Bu, en
   kritik bekleyen iş.**
9. **Test projesi yok.**
10. **Web/görsel arayüz yok.** Sadece Telegram ve ham HTTP API var.
11. **Deployment ve ileri seviye observability ele alınmadı.**

## Çalıştırma

### Docker (önerilen)

```bash
cp .env.example .env
# .env dosyasını doldur: OPENAI_API_KEY, API_KEY, POSTGRES_*, TELEGRAM_* vb.
docker compose up --build
```

Bu, `agent-api` + `qdrant` + `postgres` servislerini birlikte ayağa kaldırır.

**`.env` değiştirdikten sonra mutlaka:**
```bash
docker compose down
docker compose up --build
```
(sadece `up` yeterli değil — container eski env değerini önbellekte tutabilir)

### Yerel (dotnet CLI)

```bash
cd src/AgentPlatform.Api
dotnet user-secrets init
dotnet user-secrets set "Llm:OpenAI:ApiKey" "sk-..."
dotnet restore
dotnet run
```

### RAG'ı Test Etme

```bash
# 1) documents/ klasöründeki .md/.txt dosyalarını Qdrant'a yükle
curl -X POST http://localhost:8080/api/rag/ingest -H "X-Api-Key: <API_KEY>"

# 2) İlgili bir soru sor
curl -X POST http://localhost:8080/api/chat \
  -H "Content-Type: application/json" -H "X-Api-Key: <API_KEY>" \
  -d '{"sessionId":"test-1","message":"İade süresi kaç gün?"}'
```

Kendi dokümanlarını eklemek için `documents/` klasörüne dosya koy, `/api/rag/ingest`'i
tekrar çağır — idempotent, aynı dosyayı tekrar tekrar ingest etsen bile duplicate
oluşmaz (o dosyaya ait eski chunk'lar önce silinir).

### Memory'yi Test Etme

Telegram'da veya `/api/chat` ile:
```
Ben Ferhat, mekatronik mühendisiyim. Bunu hatırla.
```
sonra yeni bir mesajda:
```
Ben kimim?
```

## Klasör Yapısı

```
AgentPlatform.sln
src/
  AgentPlatform.Api/
    Program.cs                      ← DI kayıtları, middleware pipeline, HostOptions
    appsettings.json
    Dockerfile
    Endpoints/
      ChatEndpoints.cs               ← POST /api/chat
      RagEndpoints.cs                ← POST /api/rag/ingest
    Middleware/
      ApiKeyAuthMiddleware.cs
      RateLimitingMiddleware.cs
    Telegram/
      TelegramBotHostedService.cs
  AgentPlatform.Core/
    Kernel/
      LlmOptions.cs
      AgentKernelFactory.cs
    Orchestration/
      AgentOrchestrator.cs           ← Akışın kalbi
    Plugins/
      MemoryPlugin.cs
      SamplePlugins/TimePlugin.cs
    Memory/
      IConversationStore.cs / PostgresConversationStore.cs
      IUserProfileStore.cs / PostgresUserProfileStore.cs
      PostgresOptions.cs
    Rag/
      RagOptions.cs / DocumentChunker.cs / IRetriever.cs
      QdrantRetriever.cs / DocumentIngestionService.cs
    Security/
      RateLimitOptions.cs / IRateLimiter.cs / FixedWindowRateLimiter.cs
    Telegram/
      TelegramOptions.cs
documents/                          ← RAG kaynağı (.md/.txt)
docker-compose.yml                   ← agent-api + qdrant + postgres
.env.example
```

## Sırada Ne Var?

Yol haritasındaki #1-8 tamamlandı (LLM, Prompt Architecture, SK, Function Calling,
RAG, Memory, Güvenlik, Docker). En kritik bekleyen iş: **çok kullanıcılı kimlik
doğrulama** (tek paylaşılan `API_KEY` yerine gerçek login/JWT sistemi) — sistemin
nihai hedefi (farklı müşterilerin kullanması) buna bağlı. Bunun dışında: web
arayüzü, Anthropic adapter'ı, test projesi, deployment ve ileri seviye observability.