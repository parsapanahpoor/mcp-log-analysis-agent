using Dotin.DotNetForum.LogAgent.AgentClient.Agent;
using Dotin.DotNetForum.LogAgent.AgentClient.Analysis;
using Dotin.DotNetForum.LogAgent.AgentClient.Mcp;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Dotin.DotNetForum.LogAgent.AgentClient.Tests;

/// <summary>
/// Integration-style unit tests for <see cref="LogAnalysisAgent"/>.
/// All three dependencies are substituted so the orchestration logic can be
/// tested in isolation.
/// </summary>
public sealed class LogAnalysisAgentTests : IAsyncDisposable
{
    private static readonly IReadOnlyList<AvailableTool> DefaultTools =
    [
        new AvailableTool
        {
            Name = "GetLogs",
            Description = "Returns application logs for a specified service within a requested time range.",
            Parameters =
            [
                new ToolParameter { Name = "serviceName", Required = true },
                new ToolParameter { Name = "minutesAgo",  Required = true },
            ],
        }
    ];

    private static readonly IReadOnlyDictionary<string, object> DefaultArgs =
        new Dictionary<string, object> { ["serviceName"] = "PaymentService", ["minutesAgo"] = 15 };

    private static readonly AgentDecision SuccessDecision = new()
    {
        SelectedToolName = "GetLogs",
        Arguments = DefaultArgs,
    };

    private const string DatabaseTimeoutLog =
        "Logs for 'PaymentService' — last 15 minute(s):\n" +
        "[ERROR] - PaymentService - Database connection timeout after 30000ms on SQL-Server-01\n";

    private readonly IAgentModel _model = Substitute.For<IAgentModel>();
    private readonly IMcpClientService _mcpClient = Substitute.For<IMcpClientService>();
    private readonly ILogAnalyzer _analyzer;
    private readonly LogAnalysisAgent _sut;

    public LogAnalysisAgentTests()
    {
        _analyzer = new LogAnalyzer(NullLogger<LogAnalyzer>.Instance);
        _sut = new LogAnalysisAgent(_model, _mcpClient, _analyzer, NullLogger<LogAnalysisAgent>.Instance);

        // Default: MCP returns one tool
        _mcpClient
            .GetAvailableToolsAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<AvailableTool>>(DefaultTools));
    }

    // ── Main scenario ─────────────────────────────────────────────────────────

    [Fact]
    public async Task InvestigateAsync_MainScenario_ReturnsSummaryWithRootCause()
    {
        _model
            .DecideAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<AvailableTool>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDecision));

        _mcpClient
            .CallToolAsync("GetLogs", DefaultArgs, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DatabaseTimeoutLog));

        var result = await _sut.InvestigateAsync(
            "Why has the payment service been returning HTTP 500 errors during the last 15 minutes?");

        result.Should().Contain("SQL-Server-01");
        result.Should().ContainAny("30000ms", "30 seconds");
    }

    [Fact]
    public async Task InvestigateAsync_MainScenario_ToolCallIsVisible()
    {
        // The agent must invoke the MCP client with the correct tool and arguments.
        _model
            .DecideAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<AvailableTool>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDecision));

        _mcpClient
            .CallToolAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(DatabaseTimeoutLog));

        await _sut.InvestigateAsync("Why has the payment service been returning errors?");

        await _mcpClient.Received(1)
            .CallToolAsync(
                "GetLogs",
                Arg.Is<IReadOnlyDictionary<string, object>>(d =>
                    d.ContainsKey("serviceName") && d["serviceName"].ToString() == "PaymentService" &&
                    d.ContainsKey("minutesAgo")  && d["minutesAgo"].ToString()  == "15"),
                Arg.Any<CancellationToken>());
    }

    // ── Model selection validation ────────────────────────────────────────────

    [Fact]
    public async Task InvestigateAsync_ModelReturnsNeedsMoreInfo_PropagatesQuestion()
    {
        _model
            .DecideAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<AvailableTool>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new AgentDecision
            {
                NeedsMoreInformation = true,
                FollowUpQuestion = "Which service should I investigate?",
            }));

        var result = await _sut.InvestigateAsync("something went wrong");

        result.Should().Contain("Which service should I investigate?");
        await _mcpClient.DidNotReceive()
            .CallToolAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object>>(), Arg.Any<CancellationToken>());
    }

    // ── MCP server unavailable ────────────────────────────────────────────────

    [Fact]
    public async Task InvestigateAsync_McpServerUnavailable_ReturnsUserFriendlyMessage()
    {
        _mcpClient
            .GetAvailableToolsAsync(Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Could not connect to the MCP server."));

        var result = await _sut.InvestigateAsync("Why is the payment service down?");

        // Must NOT surface a raw stack trace.
        result.Should().NotContain("System.");
        result.Should().NotContain("at ");
        result.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task InvestigateAsync_ToolCallFails_ReturnsUserFriendlyMessage()
    {
        _model
            .DecideAsync(Arg.Any<string>(), Arg.Any<IReadOnlyCollection<AvailableTool>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessDecision));

        _mcpClient
            .CallToolAsync(Arg.Any<string>(), Arg.Any<IReadOnlyDictionary<string, object>>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("Tool execution failed."));

        var result = await _sut.InvestigateAsync("Why is the payment service down?");

        result.Should().NotContain("System.");
        result.Should().NotBeNullOrEmpty();
    }

    // ── Provider replaceability ───────────────────────────────────────────────

    [Fact]
    public void LogAnalysisAgent_AcceptsAnyIAgentModelImplementation()
    {
        // The agent must accept any IAgentModel — this validates the abstraction contract.
        var differentModel = Substitute.For<IAgentModel>();
        var agent = new LogAnalysisAgent(
            differentModel, _mcpClient, _analyzer, NullLogger<LogAnalysisAgent>.Instance);

        agent.Should().NotBeNull();
    }

    public ValueTask DisposeAsync() => _mcpClient.DisposeAsync();
}
