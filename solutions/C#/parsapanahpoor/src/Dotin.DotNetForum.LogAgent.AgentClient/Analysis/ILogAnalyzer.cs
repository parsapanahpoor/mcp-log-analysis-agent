using Dotin.DotNetForum.LogAgent.AgentClient.Agent;

namespace Dotin.DotNetForum.LogAgent.AgentClient.Analysis;

/// <summary>
/// Parses raw log output returned by an MCP tool and identifies root causes.
/// </summary>
public interface ILogAnalyzer
{
    /// <summary>
    /// Analyzes the raw log text from a tool call and produces a human-readable report.
    /// </summary>
    /// <param name="rawLogs">Raw log output returned by the MCP tool.</param>
    /// <param name="decision">The agent decision that produced the tool call (provides context).</param>
    /// <returns>Structured <see cref="AnalysisReport"/> with a human-readable summary.</returns>
    AnalysisReport Analyze(string rawLogs, AgentDecision decision);
}
