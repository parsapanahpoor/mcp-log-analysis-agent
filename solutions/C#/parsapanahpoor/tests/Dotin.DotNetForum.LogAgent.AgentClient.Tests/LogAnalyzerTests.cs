using Dotin.DotNetForum.LogAgent.AgentClient.Agent;
using Dotin.DotNetForum.LogAgent.AgentClient.Analysis;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dotin.DotNetForum.LogAgent.AgentClient.Tests;

/// <summary>
/// Unit tests for <see cref="LogAnalyzer"/> — verifies that the root-cause
/// identification and report generation logic work correctly.
/// </summary>
public sealed class LogAnalyzerTests
{
    private readonly LogAnalyzer _sut = new(NullLogger<LogAnalyzer>.Instance);

    private static AgentDecision DecisionFor(string service, int minutes) =>
        new()
        {
            SelectedToolName = "GetLogs",
            Arguments = new Dictionary<string, object>
            {
                ["serviceName"] = service,
                ["minutesAgo"]  = minutes,
            },
        };

    // ── Database timeout root cause ───────────────────────────────────────────

    [Fact]
    public void Analyze_DatabaseTimeoutLog_IdentifiesRootCause()
    {
        const string logs = """
            Logs for 'PaymentService' — last 15 minute(s) (5 entries):
            ----------------------------------------------------------------------
            [INFO] - PaymentService - Service started
            [ERROR] - PaymentService - Database connection timeout after 30000ms on SQL-Server-01
            [ERROR] - PaymentService - HTTP 500 Internal Server Error returned to client
            """;

        var report = _sut.Analyze(logs, DecisionFor("PaymentService", 15));

        report.ErrorCount.Should().BeGreaterThan(0);
        report.RootCause.Should().NotBeNullOrEmpty();
        report.RootCause!.Should().Contain("SQL-Server-01");
        report.RootCause.Should().ContainAny("30000ms", "30 seconds");
    }

    [Fact]
    public void Analyze_DatabaseTimeoutLog_SummaryMentionsSqlServer01()
    {
        const string logs = """
            [ERROR] - PaymentService - Database connection timeout after 30000ms on SQL-Server-01
            """;

        var report = _sut.Analyze(logs, DecisionFor("PaymentService", 15));

        report.Summary.Should().Contain("SQL-Server-01");
    }

    [Fact]
    public void Analyze_DatabaseTimeoutLog_SummaryMentionsTimeoutDuration()
    {
        const string logs = """
            [ERROR] - PaymentService - Database connection timeout after 30000ms on SQL-Server-01
            """;

        var report = _sut.Analyze(logs, DecisionFor("PaymentService", 15));

        report.Summary.Should().ContainAny("30000ms", "30 seconds");
    }

    [Fact]
    public void Analyze_DatabaseTimeoutLog_ReportMentionsServiceNameAndWindow()
    {
        const string logs = """
            [ERROR] - PaymentService - Database connection timeout after 30000ms on SQL-Server-01
            """;

        var report = _sut.Analyze(logs, DecisionFor("PaymentService", 15));

        report.Summary.Should().Contain("15");
        report.Summary.Should().Contain("PaymentService");
    }

    // ── Empty / no error logs ─────────────────────────────────────────────────

    [Fact]
    public void Analyze_EmptyLogs_ReturnsNoErrorReport()
    {
        var report = _sut.Analyze(string.Empty, DecisionFor("PaymentService", 15));

        report.ErrorCount.Should().Be(0);
        report.Summary.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Analyze_OnlyInfoLogs_ReturnsHealthyReport()
    {
        const string logs = """
            [INFO] - PaymentService - Service started
            [INFO] - PaymentService - Request processed successfully
            """;

        var report = _sut.Analyze(logs, DecisionFor("PaymentService", 15));

        report.ErrorCount.Should().Be(0);
        report.Summary.Should().ContainAny("healthy", "No errors", "no error");
    }

    // ── Final answer is not a fixed string ───────────────────────────────────

    [Fact]
    public void Analyze_DifferentServerName_SummaryContainsThatServerName()
    {
        const string logs = """
            [ERROR] - OrderService - Database connection timeout after 5000ms on SQL-Server-99
            """;

        var report = _sut.Analyze(logs, DecisionFor("OrderService", 30));

        report.Summary.Should().Contain("SQL-Server-99",
            "the report must be generated from the actual log content, not a fixed string");
    }

    [Fact]
    public void Analyze_DifferentTimeoutDuration_SummaryContainsThatDuration()
    {
        const string logs = """
            [ERROR] - PaymentService - Database connection timeout after 5000ms on SQL-Server-01
            """;

        var report = _sut.Analyze(logs, DecisionFor("PaymentService", 15));

        report.Summary.Should().ContainAny("5000ms", "5 seconds");
    }

    // ── Error line tracking ───────────────────────────────────────────────────

    [Fact]
    public void Analyze_MultipleErrors_CountsAllOfThem()
    {
        const string logs = """
            [ERROR] - PaymentService - Database connection timeout after 30000ms on SQL-Server-01
            [ERROR] - PaymentService - HTTP 500 Internal Server Error
            [ERROR] - PaymentService - HTTP 500 Internal Server Error
            """;

        var report = _sut.Analyze(logs, DecisionFor("PaymentService", 15));

        report.ErrorCount.Should().Be(3);
        report.ErrorLines.Should().HaveCount(3);
    }
}
