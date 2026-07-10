using AgentPlatform.Core.Orchestration;

namespace AgentPlatform.Api.Endpoints;

public sealed record ChatRequest(string SessionId, string Message);
public sealed record ChatResponse(string Reply);

public static class ChatEndpoints
{
    public static void MapChatEndpoints(this WebApplication app)
    {
        app.MapPost("/api/chat", async (ChatRequest request, IAgentOrchestrator orchestrator, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest(new { error = "SessionId ve Message zorunludur." });

            var reply = await orchestrator.SendMessageAsync(request.SessionId, request.Message, ct);
            return Results.Ok(new ChatResponse(reply));
        })
        .WithName("Chat");

        app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));
    }
}
