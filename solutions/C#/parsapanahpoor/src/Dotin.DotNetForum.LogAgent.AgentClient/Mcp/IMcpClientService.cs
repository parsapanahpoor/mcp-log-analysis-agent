using Dotin.DotNetForum.LogAgent.AgentClient.Agent;

namespace Dotin.DotNetForum.LogAgent.AgentClient.Mcp;

/// <summary>
/// Abstraction over the MCP transport layer.
/// Allows the agent to be tested without a live MCP server.
/// </summary>
public interface IMcpClientService : IAsyncDisposable
{
    /// <summary>
    /// Fetches the list of tools currently registered on the MCP server.
    /// </summary>
    Task<IReadOnlyList<AvailableTool>> GetAvailableToolsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Invokes a tool on the MCP server and returns the raw text response.
    /// </summary>
    /// <param name="toolName">The tool's registered name.</param>
    /// <param name="arguments">Named arguments matching the tool's parameter schema.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object> arguments,
        CancellationToken cancellationToken = default);
}
