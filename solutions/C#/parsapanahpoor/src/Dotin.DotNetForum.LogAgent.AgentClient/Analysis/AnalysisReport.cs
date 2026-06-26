namespace Dotin.DotNetForum.LogAgent.AgentClient.Analysis;

/// <summary>
/// Structured result of a log analysis pass.
/// </summary>
public sealed record AnalysisReport
{
    /// <summary>Number of error-level entries found.</summary>
    public int ErrorCount { get; init; }

    /// <summary>Plain-English description of the identified root cause, or null if none found.</summary>
    public string? RootCause { get; init; }

    /// <summary>Full human-readable summary presented to the user.</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Individual error lines extracted from the raw log.</summary>
    public IReadOnlyList<string> ErrorLines { get; init; } = [];
}
