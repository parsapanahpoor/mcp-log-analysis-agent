namespace Dotin.DotNetForum.LogAgent.Contracts;

/// <summary>
/// Represents a single log entry from a service.
/// </summary>
public sealed record LogEntry
{
    /// <summary>Log severity level (e.g. ERROR, WARN, INFO).</summary>
    public string Level { get; init; } = string.Empty;

    /// <summary>Name of the service that produced the log entry.</summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>Human-readable message describing the event.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>UTC timestamp at which the event occurred.</summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <inheritdoc />
    public override string ToString() =>
        $"[{Level}] - {ServiceName} - {Message}";
}
