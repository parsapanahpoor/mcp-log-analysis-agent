using Dotin.DotNetForum.LogAgent.Contracts;
using Microsoft.Extensions.Logging;

namespace Dotin.DotNetForum.LogAgent.McpServer.Services;

/// <summary>
/// In-memory log provider that simulates Elasticsearch data.
/// All timestamps are computed relative to the current wall-clock time so the
/// entries always fall within a realistic recent window.
/// </summary>
public sealed class SimulatedLogProvider(ILogger<SimulatedLogProvider> logger) : ILogProvider
{
    // Each entry stores how many minutes ago (from "now") the event occurred.
    private sealed record CatalogueEntry(string Level, string ServiceName, string Message, double MinutesAgo);

    private static readonly CatalogueEntry[] Catalogue =
    [
        // ── PaymentService ────────────────────────────────────────────────────
        new("INFO",  "PaymentService", "Service started and ready to accept requests", 14.5),
        new("INFO",  "PaymentService", "Received payment request from user 8421", 12.0),
        new("INFO",  "PaymentService", "Initiating database query on SQL-Server-01", 11.5),
        new("WARN",  "PaymentService", "Database response time is elevated: 2500ms (threshold: 1000ms)", 9.0),
        new("ERROR", "PaymentService", "Database connection timeout after 30000ms on SQL-Server-01", 7.0),
        new("ERROR", "PaymentService", "HTTP 500 Internal Server Error returned to client (POST /api/payments)", 6.8),
        new("WARN",  "PaymentService", "Retry attempt 1 of 3 for database connection to SQL-Server-01", 5.5),
        new("ERROR", "PaymentService", "Database connection timeout after 30000ms on SQL-Server-01", 5.0),
        new("WARN",  "PaymentService", "Retry attempt 2 of 3 for database connection to SQL-Server-01", 4.5),
        new("ERROR", "PaymentService", "Database connection timeout after 30000ms on SQL-Server-01", 4.0),
        new("WARN",  "PaymentService", "Circuit breaker opened for SQL-Server-01 after 3 consecutive failures", 3.8),
        new("ERROR", "PaymentService", "HTTP 500 Internal Server Error returned to client (POST /api/payments)", 3.5),
        new("ERROR", "PaymentService", "HTTP 500 Internal Server Error returned to client (POST /api/payments)", 2.0),
        new("WARN",  "PaymentService", "All payment processing suspended until database connectivity is restored", 1.5),

        // ── OrderService ──────────────────────────────────────────────────────
        new("INFO",  "OrderService", "Order processing started for order 12847", 10.0),
        new("INFO",  "OrderService", "Order 12847 processed successfully", 9.5),
        new("INFO",  "OrderService", "Inventory check passed for SKU-99123", 8.0),

        // ── AuthService ───────────────────────────────────────────────────────
        new("INFO",  "AuthService", "User authentication successful for user 8421", 13.0),
        new("INFO",  "AuthService", "JWT token issued for user 8421 (expires in 3600s)", 12.9),
    ];

    /// <inheritdoc />
    public Task<IReadOnlyList<LogEntry>> GetLogsAsync(
        string serviceName,
        int minutesAgo,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var cutoff = now.AddMinutes(-minutesAgo);

        var entries = Catalogue
            .Where(e => e.ServiceName.Equals(serviceName, StringComparison.OrdinalIgnoreCase))
            .Select(e => new LogEntry
            {
                Level = e.Level,
                ServiceName = e.ServiceName,
                Message = e.Message,
                Timestamp = now.AddMinutes(-e.MinutesAgo),
            })
            .Where(e => e.Timestamp >= cutoff)
            .OrderBy(e => e.Timestamp)
            .ToList();

        logger.LogInformation(
            "Retrieved {Count} log entries for service '{ServiceName}' in the last {MinutesAgo} minutes",
            entries.Count, serviceName, minutesAgo);

        return Task.FromResult<IReadOnlyList<LogEntry>>(entries);
    }
}
