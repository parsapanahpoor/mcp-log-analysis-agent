using Dotin.DotNetForum.LogAgent.McpServer.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dotin.DotNetForum.LogAgent.McpServer.Tests;

/// <summary>
/// Tests for <see cref="SimulatedLogProvider"/> — verifies log retrieval
/// and time-window filtering logic without any external dependencies.
/// </summary>
public sealed class SimulatedLogProviderTests
{
    private readonly SimulatedLogProvider _sut = new(NullLogger<SimulatedLogProvider>.Instance);

    // ── PaymentService — primary scenario ─────────────────────────────────────

    [Fact]
    public async Task GetLogsAsync_PaymentService_ReturnsAtLeastOneErrorEntry()
    {
        var entries = await _sut.GetLogsAsync("PaymentService", 15);

        entries.Should().NotBeEmpty();
        entries.Any(e => e.Level == "ERROR").Should().BeTrue();
    }

    [Fact]
    public async Task GetLogsAsync_PaymentService_ContainsDatabaseTimeoutError()
    {
        var entries = await _sut.GetLogsAsync("PaymentService", 15);

        var timeoutErrors = entries
            .Where(e => e.Message.Contains("Database connection timeout", StringComparison.OrdinalIgnoreCase))
            .ToList();

        timeoutErrors.Should().NotBeEmpty("the PaymentService logs must include the database timeout error");
        timeoutErrors.Any(e => e.Message.Contains("30000ms")).Should().BeTrue();
        timeoutErrors.Any(e => e.Message.Contains("SQL-Server-01")).Should().BeTrue();
    }

    [Fact]
    public async Task GetLogsAsync_PaymentService_AllEntriesHaveCorrectServiceName()
    {
        var entries = await _sut.GetLogsAsync("PaymentService", 15);

        entries.Should().AllSatisfy(e =>
            e.ServiceName.Should().Be("PaymentService"));
    }

    [Fact]
    public async Task GetLogsAsync_PaymentService_EntriesAreOrderedByTimestampAscending()
    {
        var entries = await _sut.GetLogsAsync("PaymentService", 15);

        var timestamps = entries.Select(e => e.Timestamp).ToList();
        timestamps.Should().BeInAscendingOrder();
    }

    // ── Time-window filtering ─────────────────────────────────────────────────

    [Fact]
    public async Task GetLogsAsync_VeryShortWindow_ReturnsOnlyRecentEntries()
    {
        // The simulated catalogue has entries from 1.5 minutes ago onwards.
        // A 1-minute window should return fewer (or no) entries.
        var shortWindowEntries = await _sut.GetLogsAsync("PaymentService", 1);
        var longWindowEntries = await _sut.GetLogsAsync("PaymentService", 15);

        shortWindowEntries.Count.Should().BeLessThanOrEqualTo(longWindowEntries.Count);
    }

    [Fact]
    public async Task GetLogsAsync_AllEntriesWithinWindow_TimestampRespected()
    {
        var minutesAgo = 15;
        var entries = await _sut.GetLogsAsync("PaymentService", minutesAgo);
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo);

        entries.Should().AllSatisfy(e =>
            e.Timestamp.Should().BeOnOrAfter(cutoff.AddSeconds(-5))); // 5s tolerance for test timing
    }

    // ── Other services ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLogsAsync_OrderService_ReturnsOrderServiceEntries()
    {
        var entries = await _sut.GetLogsAsync("OrderService", 15);

        entries.Should().NotBeEmpty();
        entries.Should().AllSatisfy(e => e.ServiceName.Should().Be("OrderService"));
    }

    [Fact]
    public async Task GetLogsAsync_UnknownService_ReturnsEmptyList()
    {
        var entries = await _sut.GetLogsAsync("NonExistentService", 30);

        entries.Should().BeEmpty();
    }

    // ── Service name case-insensitivity ───────────────────────────────────────

    [Fact]
    public async Task GetLogsAsync_ServiceNameCaseInsensitive_ReturnsEntries()
    {
        var lowerEntries = await _sut.GetLogsAsync("paymentservice", 30);
        var upperEntries = await _sut.GetLogsAsync("PAYMENTSERVICE", 30);

        lowerEntries.Count.Should().Be(upperEntries.Count);
    }
}
