namespace Dotin.DotNetForum.LogAgent.AgentClient.Configuration;

/// <summary>
/// Configuration for launching the MCP server process over stdio.
/// Bound from the "McpServer" section of appsettings.json.
/// </summary>
public sealed class McpServerOptions
{
    /// <summary>Executable to run (e.g. "dotnet").</summary>
    public string Command { get; set; } = "dotnet";

    /// <summary>
    /// Command-line arguments passed to <see cref="Command"/>
    /// (e.g. ["run", "--project", "src/...McpServer"]).
    /// </summary>
    public string[] Arguments { get; set; } = [];
}
