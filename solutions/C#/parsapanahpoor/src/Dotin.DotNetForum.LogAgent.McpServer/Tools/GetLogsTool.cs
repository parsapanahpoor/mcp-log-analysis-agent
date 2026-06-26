using System.ComponentModel;
using System.Text;
using Dotin.DotNetForum.LogAgent.McpServer.Services;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Dotin.DotNetForum.LogAgent.McpServer.Tools;

/// <summary>
/// MCP tool that exposes application log retrieval to AI agents.
/// </summary>
[McpServerToolType]
public sealed class GetLogsTool(ILogProvider logProvider, ILogger<GetLogsTool> logger)
{
    /// <summary>
    /// Returns application logs for a specified service within a requested time range.
    /// </summary>
    [McpServerTool(Name = "GetLogs")]
    [Description("Returns application logs for a specified service within a requested time range.")]
    public async Task<string> GetLogsAsync(
        [Description("Name of the service whose logs must be inspected")]
        string serviceName,
        [Description("Number of previous minutes to search for logs")]
        int minutesAgo,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "GetLogs invoked — serviceName: '{ServiceName}', minutesAgo: {MinutesAgo}",
            serviceName, minutesAgo);

        if (string.IsNullOrWhiteSpace(serviceName))
        {
            logger.LogWarning("GetLogs rejected: serviceName is empty");
            return "Error: The 'serviceName' parameter is required and cannot be empty or whitespace.";
        }

        if (minutesAgo <= 0)
        {
            logger.LogWarning("GetLogs rejected: minutesAgo={MinutesAgo} is invalid", minutesAgo);
            return $"Error: The 'minutesAgo' parameter must be a positive integer greater than zero. Received: {minutesAgo}.";
        }

        if (minutesAgo > 10_080)
        {
            logger.LogWarning("GetLogs rejected: minutesAgo={MinutesAgo} exceeds maximum", minutesAgo);
            return "Error: The 'minutesAgo' parameter cannot exceed 10,080 (7 days).";
        }

        var entries = await logProvider.GetLogsAsync(serviceName, minutesAgo, cancellationToken);

        if (entries.Count == 0)
        {
            return $"No logs found for service '{serviceName}' in the last {minutesAgo} minute(s). " +
                   "The service may not exist or may not have produced any events in this window.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Logs for '{serviceName}' — last {minutesAgo} minute(s) ({entries.Count} entries):");
        sb.AppendLine(new string('-', 70));

        foreach (var entry in entries)
        {
            sb.AppendLine($"[{entry.Level}] - {entry.ServiceName} - {entry.Message}");
        }

        return sb.ToString().TrimEnd();
    }
}
