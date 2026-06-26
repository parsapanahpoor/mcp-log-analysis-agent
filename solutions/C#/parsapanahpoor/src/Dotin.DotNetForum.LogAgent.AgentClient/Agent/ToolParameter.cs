namespace Dotin.DotNetForum.LogAgent.AgentClient.Agent;

/// <summary>
/// Describes a single parameter accepted by an MCP tool.
/// </summary>
public sealed record ToolParameter
{
    /// <summary>Parameter name as declared by the tool.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>JSON Schema type (e.g. "string", "integer", "boolean").</summary>
    public string Type { get; init; } = "string";

    /// <summary>Human-readable description of what this parameter represents.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Whether the tool requires this parameter to be provided.</summary>
    public bool Required { get; init; }
}
