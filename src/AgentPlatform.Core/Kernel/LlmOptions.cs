namespace AgentPlatform.Core.Kernel;

/// <summary>
/// "Llm" config bölümüne bind edilir. Sağlayıcı değişimi tamamen buradan yönetilir;
/// orchestration/plugin kodu hangi sağlayıcının aktif olduğunu bilmek zorunda değildir.
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>"OpenAI" | "AzureOpenAI" | "Anthropic"</summary>
    public string Provider { get; set; } = "OpenAI";

    public OpenAiOptions OpenAI { get; set; } = new();
    public AzureOpenAiOptions AzureOpenAI { get; set; } = new();
    public AnthropicOptions Anthropic { get; set; } = new();
}

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-4o-mini";
}

public sealed class AzureOpenAiOptions
{
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
}

public sealed class AnthropicOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "claude-sonnet-4-6";
}
