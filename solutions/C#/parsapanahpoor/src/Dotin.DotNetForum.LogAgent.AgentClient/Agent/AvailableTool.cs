namespace Dotin.DotNetForum.LogAgent.AgentClient.Agent;

/// <summary>
/// Describes a tool that was discovered from the MCP server at runtime.
/// </summary>
public sealed record AvailableTool
{
    /// <summary>Unique tool name used when invoking via MCP.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Human-readable description of what the tool does.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Ordered list of parameters the tool accepts.</summary>
    public IReadOnlyList<ToolParameter> Parameters { get; init; } = [];
}
