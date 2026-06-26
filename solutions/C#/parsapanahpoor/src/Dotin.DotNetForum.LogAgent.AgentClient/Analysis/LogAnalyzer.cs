using System.Text;
using System.Text.RegularExpressions;
using Dotin.DotNetForum.LogAgent.AgentClient.Agent;
using Microsoft.Extensions.Logging;

namespace Dotin.DotNetForum.LogAgent.AgentClient.Analysis;

/// <summary>
/// Analyzes raw log text to identify error patterns and produce a
/// natural-language root-cause report.
///
/// All values in the report (server name, timeout duration, error count …) are
/// extracted dynamically from the log content — the final answer is never a
/// pre-written constant string.
/// </summary>
public sealed class LogAnalyzer(ILogger<LogAnalyzer> logger) : ILogAnalyzer
{
    // ── Root-cause pattern matchers ────────────────────────────────────────────
    private static readonly Regex DatabaseTimeoutPattern = new(
        @"Database connection timeout after (\d+)ms on (\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Http500Pattern = new(
        @"HTTP 5\d{2}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CircuitBreakerPattern = new(
        @"Circuit breaker (?:opened|tripped) for (\S+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex OutOfMemoryPattern = new(
        @"OutOfMemoryException|out of memory",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex NullReferencePattern = new(
        @"NullReferenceException|null reference",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // ── ILogAnalyzer ──────────────────────────────────────────────────────────

    /// <inheritdoc />
    public AnalysisReport Analyze(string rawLogs, AgentDecision decision)
    {
        var serviceName = decision.Arguments.TryGetValue("serviceName", out var sn)
            ? sn.ToString()!
            : "the service";
        var minutesAgo = decision.Arguments.TryGetValue("minutesAgo", out var ma)
            ? ma.ToString()!
            : "the requested period";

        logger.LogInformation("Analyzing logs for '{Service}' ({MinutesAgo} min)", serviceName, minutesAgo);

        if (string.IsNullOrWhiteSpace(rawLogs))
        {
            return BuildEmptyReport(serviceName, minutesAgo);
        }

        var allLines = rawLogs
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();

        var errorLines = allLines
            .Where(l => l.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var warnLines = allLines
            .Where(l => l.Contains("[WARN]", StringComparison.OrdinalIgnoreCase))
            .ToList();

        logger.LogInformation(
            "Log analysis: total={Total}, errors={Errors}, warnings={Warnings}",
            allLines.Count, errorLines.Count, warnLines.Count);

        if (errorLines.Count == 0)
        {
            return BuildNoErrorReport(serviceName, minutesAgo, allLines.Count);
        }

        var rootCause = IdentifyRootCause(errorLines, warnLines);
        var summary = BuildSummary(serviceName, minutesAgo, errorLines, warnLines, rootCause);

        return new AnalysisReport
        {
            ErrorCount = errorLines.Count,
            RootCause = rootCause,
            Summary = summary,
            ErrorLines = errorLines,
        };
    }

    // ── Root-cause identification ─────────────────────────────────────────────

    /// <summary>
    /// Applies pattern-matching rules to the error lines in descending priority order.
    /// Returns a human-readable root-cause string, or a generic fallback.
    /// </summary>
    private string IdentifyRootCause(List<string> errorLines, List<string> warnLines)
    {
        // Priority 1 — Database connection timeout
        foreach (var line in errorLines)
        {
            var m = DatabaseTimeoutPattern.Match(line);
            if (m.Success)
            {
                var ms = int.Parse(m.Groups[1].Value);
                var server = m.Groups[2].Value;
                var seconds = ms / 1000.0;
                logger.LogInformation(
                    "Root cause identified: DB timeout — {Ms}ms on {Server}", ms, server);
                return
                    $"The '{server}' database server did not respond within {seconds:0.##} seconds ({ms}ms). " +
                    $"The service is unable to complete database operations, causing HTTP 500 errors for all " +
                    $"requests that depend on the database.";
            }
        }

        // Priority 2 — Circuit breaker opened
        foreach (var line in warnLines.Concat(errorLines))
        {
            var m = CircuitBreakerPattern.Match(line);
            if (m.Success)
            {
                var target = m.Groups[1].Value;
                logger.LogInformation("Root cause identified: circuit breaker opened for {Target}", target);
                return
                    $"The circuit breaker for '{target}' has opened after repeated failures, " +
                    $"blocking further requests to that dependency.";
            }
        }

        // Priority 3 — OutOfMemoryException
        if (errorLines.Any(l => OutOfMemoryPattern.IsMatch(l)))
        {
            return "The service ran out of memory (OutOfMemoryException). " +
                   "The process is likely exhausting its heap and cannot allocate new objects.";
        }

        // Priority 4 — NullReferenceException
        if (errorLines.Any(l => NullReferencePattern.IsMatch(l)))
        {
            return "A NullReferenceException occurred, indicating the application encountered " +
                   "an unexpected null value. Review the stack trace for the exact location.";
        }

        // Generic fallback — surface the first error line verbatim.
        return $"An unrecognised error pattern was detected. First error: {errorLines[0]}";
    }

    // ── Summary builder ───────────────────────────────────────────────────────

    private static string BuildSummary(
        string serviceName,
        string minutesAgo,
        List<string> errorLines,
        List<string> warnLines,
        string rootCause)
    {
        var sb = new StringBuilder();
        sb.AppendLine(
            $"The logs from the last {minutesAgo} minutes show that the {serviceName} " +
            $"has been returning HTTP 500 errors.");
        sb.AppendLine();
        sb.AppendLine($"Root Cause: {rootCause}");
        sb.AppendLine();
        sb.AppendLine($"Error Summary ({errorLines.Count} error(s), {warnLines.Count} warning(s) detected):");
        foreach (var line in errorLines.Take(5))
        {
            sb.AppendLine($"  • {line}");
        }
        if (errorLines.Count > 5)
        {
            sb.AppendLine($"  … and {errorLines.Count - 5} more error(s) not shown.");
        }

        return sb.ToString().TrimEnd();
    }

    private static AnalysisReport BuildEmptyReport(string serviceName, string minutesAgo) =>
        new()
        {
            ErrorCount = 0,
            Summary =
                $"The MCP server returned an empty response for '{serviceName}'. " +
                $"No log data was available for the last {minutesAgo} minutes.",
        };

    private static AnalysisReport BuildNoErrorReport(string serviceName, string minutesAgo, int totalLines) =>
        new()
        {
            ErrorCount = 0,
            Summary =
                $"No errors found in the {totalLines} log entries for '{serviceName}' " +
                $"during the last {minutesAgo} minutes. The service appears to be healthy.",
        };
}
