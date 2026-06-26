using Dotin.DotNetForum.LogAgent.Contracts;

namespace Dotin.DotNetForum.LogAgent.McpServer.Services;

/// <summary>
/// Abstraction for retrieving application log entries.
/// Implementations may read from Elasticsearch, a database, an in-memory store, or any other source.
/// </summary>
public interface ILogProvider
{
    /// <summary>
    /// Returns log entries for the specified service that occurred within the given time window.
    /// </summary>
    /// <param name="serviceName">Technical name of the service to query.</param>
    /// <param name="minutesAgo">How many minutes back to include in the result window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ordered (oldest first) list of matching log entries.</returns>
    Task<IReadOnlyList<LogEntry>> GetLogsAsync(
        string serviceName,
        int minutesAgo,
        CancellationToken cancellationToken = default);
}
