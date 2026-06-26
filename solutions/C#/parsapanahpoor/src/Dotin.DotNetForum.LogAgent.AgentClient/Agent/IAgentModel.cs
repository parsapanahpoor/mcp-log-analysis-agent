namespace Dotin.DotNetForum.LogAgent.AgentClient.Agent;

/// <summary>
/// Abstraction over the AI decision layer.
/// Implementations may be a real LLM (OpenAI, Azure OpenAI, Ollama …) or a local
/// rule-based / NLP model used for offline / testable scenarios.
/// Replacing the implementation never requires changes to the core agent logic.
/// </summary>
public interface IAgentModel
{
    /// <summary>
    /// Given a natural-language user message and the set of tools currently available
    /// on the MCP server, return a decision about which tool to call and with what arguments.
    /// </summary>
    /// <param name="userMessage">The raw user question or command.</param>
    /// <param name="availableTools">Tools discovered from the MCP server at runtime.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="AgentDecision"/> that is either a ready-to-execute tool call
    /// or a follow-up question to ask the user.
    /// </returns>
    Task<AgentDecision> DecideAsync(
        string userMessage,
        IReadOnlyCollection<AvailableTool> availableTools,
        CancellationToken cancellationToken = default);
}
