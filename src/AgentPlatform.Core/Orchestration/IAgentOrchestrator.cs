namespace AgentPlatform.Core.Orchestration;

public interface IAgentOrchestrator
{
    Task<string> SendMessageAsync(string sessionId, string userMessage, CancellationToken ct = default);
}
