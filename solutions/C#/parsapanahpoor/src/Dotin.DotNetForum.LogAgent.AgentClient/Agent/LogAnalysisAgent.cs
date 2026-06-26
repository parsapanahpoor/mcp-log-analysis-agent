using System.Text.Json;
using Dotin.DotNetForum.LogAgent.AgentClient.Analysis;
using Dotin.DotNetForum.LogAgent.AgentClient.Mcp;
using Microsoft.Extensions.Logging;

namespace Dotin.DotNetForum.LogAgent.AgentClient.Agent;

/// <summary>
/// Orchestrates the full log-investigation pipeline:
///   User message → Tool selection → MCP call → Log analysis → Final report.
///
/// This class coordinates the three major abstractions (IAgentModel, IMcpClientService,
/// ILogAnalyzer) but contains no business logic itself. Every layer is replaceable
/// through its interface without touching this class.
/// </summary>
public sealed class LogAnalysisAgent(
    IAgentModel model,
    IMcpClientService mcpClient,
    ILogAnalyzer analyzer,
    ILogger<LogAnalysisAgent> logger)
{
    /// <summary>
    /// Accepts a natural-language user question and returns a human-readable
    /// investigation report.
    /// </summary>
    /// <param name="userMessage">Raw user question.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Plain-English report of the investigation findings.</returns>
    public async Task<string> InvestigateAsync(
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Investigation started — message: '{Message}'", userMessage);

        // ── Step 1: Discover available tools from the MCP server ───────────────
        IReadOnlyList<AvailableTool> availableTools;
        try
        {
            availableTools = await mcpClient.GetAvailableToolsAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to list tools from MCP server");
            return
                "I was unable to connect to the log analysis server. " +
                "Please ensure the MCP server is running and try again. " +
                $"Detail: {ex.Message}";
        }

        logger.LogInformation(
            "Discovered {Count} tool(s): [{Tools}]",
            availableTools.Count,
            string.Join(", ", availableTools.Select(t => t.Name)));

        // ── Step 2: Let the model decide which tool to call ────────────────────
        AgentDecision decision;
        try
        {
            decision = await model.DecideAsync(userMessage, availableTools, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Agent model threw an unexpected exception");
            return $"The agent encountered an internal error: {ex.Message}";
        }

        // ── Step 3: Handle follow-up / clarification requests ─────────────────
        if (decision.NeedsMoreInformation)
        {
            logger.LogInformation("Agent needs more information: {Question}", decision.FollowUpQuestion);
            return decision.FollowUpQuestion
                   ?? "Please provide more details so I can investigate the issue.";
        }

        if (!decision.IsToolCall || decision.SelectedToolName is null)
        {
            logger.LogWarning("Model produced no actionable decision");
            return "I could not determine the appropriate action for your request. " +
                   "Try asking about a specific service error, for example: " +
                   "'Why has the payment service been returning HTTP 500 errors during the last 15 minutes?'";
        }

        // Log the tool call decision — visible in debug output and satisfies the
        // acceptance criterion: "The Tool Call is visible in logs or Debug output."
        logger.LogInformation(
            "TOOL CALL → name: '{ToolName}', arguments: {Arguments}",
            decision.SelectedToolName,
            JsonSerializer.Serialize(decision.Arguments));

        // ── Step 4: Execute the tool call through MCP ──────────────────────────
        string rawLogs;
        try
        {
            rawLogs = await mcpClient.CallToolAsync(
                decision.SelectedToolName,
                decision.Arguments,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "MCP tool call failed — tool: '{Tool}'", decision.SelectedToolName);
            return
                $"I retrieved the tool to use ({decision.SelectedToolName}), but the call to the " +
                $"MCP server failed. Detail: {ex.Message}";
        }

        logger.LogInformation("Tool response received ({Length} chars)", rawLogs.Length);

        // ── Step 5: Analyze the logs and build the final report ────────────────
        var report = analyzer.Analyze(rawLogs, decision);

        // Prepend any assumption note so the user knows about defaults.
        var prefix = decision.AssumptionNote is not null
            ? $"Note: {decision.AssumptionNote}\n\n"
            : string.Empty;

        return prefix + report.Summary;
    }
}
