using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace AgentPlatform.Core.Kernel;

/// <summary>
/// Kernel oluşturmanın TEK giriş noktası. Sağlayıcı değişimi burada izole edilir;
/// Orchestration ve Plugin katmanları IChatCompletionService ile konuşur,
/// hangi sağlayıcının aktif olduğunu bilmezler (Liskov / Dependency Inversion).
/// </summary>
public interface IAgentKernelFactory
{
    Microsoft.SemanticKernel.Kernel Create();
}

public sealed class AgentKernelFactory : IAgentKernelFactory
{
    private readonly LlmOptions _options;

    public AgentKernelFactory(IOptions<LlmOptions> options)
    {
        _options = options.Value;
    }

    public Microsoft.SemanticKernel.Kernel Create()
    {
        var builder = Microsoft.SemanticKernel.Kernel.CreateBuilder();

        switch (_options.Provider.Trim().ToLowerInvariant())
        {
            case "openai":
                ValidateOpenAi(_options.OpenAI);
                builder.AddOpenAIChatCompletion(
                    modelId: _options.OpenAI.Model,
                    apiKey: _options.OpenAI.ApiKey);
                break;

            case "azureopenai":
                ValidateAzure(_options.AzureOpenAI);
                builder.AddAzureOpenAIChatCompletion(
                    deploymentName: _options.AzureOpenAI.DeploymentName,
                    endpoint: _options.AzureOpenAI.Endpoint,
                    apiKey: _options.AzureOpenAI.ApiKey);
                break;

            case "anthropic":
                // NOT: Semantic Kernel'in ana paketinde (Microsoft.SemanticKernel.Connectors.OpenAI)
                // resmi bir Anthropic connector'ı YOK. Bu, bilinçli bırakılmış bir genişletme noktasıdır.
                // Seçenekler:
                //   1) Topluluk connector'ı (NuGet'te güncel paket adını kontrol et, sık değişiyor)
                //   2) Anthropic'in mesaj şemasını IChatCompletionService arayüzüne saran
                //      özel bir adapter yazmak (Anthropic API OpenAI ile birebir uyumlu değil:
                //      farklı mesaj rolleri, farklı tool-use şeması).
                // Şimdilik net bir hata fırlatıyoruz ki sessizce yanlış sağlayıcıya düşülmesin.
                throw new NotSupportedException(
                    "Anthropic provider henüz implemente edilmedi. " +
                    "IChatCompletionService için özel bir adapter yazılması gerekiyor " +
                    "(bkz. KernelFactory.cs içindeki yorum). Bu bilinçli bir sonraki adım.");

            default:
                throw new InvalidOperationException(
                    $"Bilinmeyen Llm:Provider değeri: '{_options.Provider}'. " +
                    "Geçerli değerler: OpenAI, AzureOpenAI, Anthropic.");
        }

        return builder.Build();
    }

    private static void ValidateOpenAi(OpenAiOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.ApiKey))
            throw new InvalidOperationException(
                "Llm:OpenAI:ApiKey boş. Ortam değişkeni (OPENAI_API_KEY) veya user-secrets ile ayarla, appsettings.json'a yazma.");
    }

    private static void ValidateAzure(AzureOpenAiOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.Endpoint) || string.IsNullOrWhiteSpace(o.ApiKey) || string.IsNullOrWhiteSpace(o.DeploymentName))
            throw new InvalidOperationException("Llm:AzureOpenAI altında Endpoint/ApiKey/DeploymentName eksik.");
    }
}
