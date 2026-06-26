using Dotin.DotNetForum.LogAgent.Contracts;
using Dotin.DotNetForum.LogAgent.McpServer.Services;
using Dotin.DotNetForum.LogAgent.McpServer.Tools;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Dotin.DotNetForum.LogAgent.McpServer.Tests;

/// <summary>
/// Unit tests for <see cref="GetLogsTool"/> input validation and output formatting.
/// The log provider is replaced with a substitute to isolate the tool's own logic.
/// </summary>
public sealed class GetLogsToolTests
{
    private readonly ILogProvider _logProvider = Substitute.For<ILogProvider>();
    private readonly GetLogsTool _sut;

    public GetLogsToolTests()
    {
        _sut = new GetLogsTool(_logProvider, NullLogger<GetLogsTool>.Instance);
    }

    // ── Input validation — service name ───────────────────────────────────────

    [Fact]
    public async Task GetLogsAsync_EmptyServiceName_ReturnsError()
    {
        var result = await _sut.GetLogsAsync(string.Empty, 15);

        result.Should().Contain("Error");
        result.Should().Contain("serviceName");
    }

    [Fact]
    public async Task GetLogsAsync_WhitespaceServiceName_ReturnsError()
    {
        var result = await _sut.GetLogsAsync("   ", 15);

        result.Should().Contain("Error");
        result.Should().Contain("serviceName");
    }

    // ── Input validation — minutesAgo ─────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public async Task GetLogsAsync_InvalidMinutesAgo_ReturnsError(int minutesAgo)
    {
        var result = await _sut.GetLogsAsync("PaymentService", minutesAgo);

        result.Should().Contain("Error");
        result.Should().Contain("minutesAgo");
    }

    [Fact]
    public async Task GetLogsAsync_MinutesAgoExceedsMaximum_ReturnsError()
    {
        var result = await _sut.GetLogsAsync("PaymentService", 99_999);

        result.Should().Contain("Error");
    }

    // ── Happy path ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLogsAsync_PaymentService_ReturnsExpectedErrorLog()
    {
        _logProvider
            .GetLogsAsync("PaymentService", 15, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LogEntry>>(
            [
                new LogEntry
                {
                    Level = "ERROR",
                    ServiceName = "PaymentService",
                    Message = "Database connection timeout after 30000ms on SQL-Server-01",
                    Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5),
                }
            ]));

        var result = await _sut.GetLogsAsync("PaymentService", 15);

        result.Should().Contain("[ERROR]");
        result.Should().Contain("PaymentService");
        result.Should().Contain("Database connection timeout after 30000ms on SQL-Server-01");
        result.Should().Contain("SQL-Server-01");
    }

    [Fact]
    public async Task GetLogsAsync_ValidInput_DoesNotCrash()
    {
        _logProvider
            .GetLogsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LogEntry>>([]));

        var act = async () => await _sut.GetLogsAsync("AnyService", 5);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetLogsAsync_NoLogsFound_ReturnsInformativeMessage()
    {
        _logProvider
            .GetLogsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LogEntry>>([]));

        var result = await _sut.GetLogsAsync("UnknownService", 10);

        result.Should().Contain("No logs found");
    }

    // ── Output format ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLogsAsync_MultipleEntries_FormatsAllLines()
    {
        _logProvider
            .GetLogsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<LogEntry>>(
            [
                new LogEntry { Level = "INFO", ServiceName = "PaymentService", Message = "Started", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10) },
                new LogEntry { Level = "ERROR", ServiceName = "PaymentService", Message = "Database connection timeout after 30000ms on SQL-Server-01", Timestamp = DateTimeOffset.UtcNow.AddMinutes(-5) },
            ]));

        var result = await _sut.GetLogsAsync("PaymentService", 15);

        result.Should().Contain("[INFO]");
        result.Should().Contain("[ERROR]");
        result.Should().Contain("Database connection timeout after 30000ms on SQL-Server-01");
    }
}
