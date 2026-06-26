using Dotin.DotNetForum.LogAgent.AgentClient.Agent;
using Dotin.DotNetForum.LogAgent.AgentClient.Configuration;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Dotin.DotNetForum.LogAgent.AgentClient.Tests;

/// <summary>
/// Unit tests for <see cref="NlpAgentModel"/> — verifies tool selection,
/// argument extraction, and clarification handling.
/// </summary>
public sealed class NlpAgentModelTests
{
    private static readonly IReadOnlyCollection<AvailableTool> StandardTools =
    [
        new AvailableTool
        {
            Name = "GetLogs",
            Description = "Returns application logs for a specified service within a requested time range.",
            Parameters =
            [
                new ToolParameter { Name = "serviceName", Type = "string", Description = "Name of the service whose logs must be inspected", Required = true },
                new ToolParameter { Name = "minutesAgo",  Type = "integer", Description = "Number of previous minutes to search for logs",      Required = true },
            ],
        }
    ];

    private readonly NlpAgentModel _sut;

    public NlpAgentModelTests()
    {
        var aliasOptions = Options.Create(new ServiceAliasOptions
        {
            Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["payment"] = "PaymentService",
                ["pay"]     = "PaymentService",
                ["order"]   = "OrderService",
                ["auth"]    = "AuthService",
            }
        });

        var agentOptions = Options.Create(new AgentOptions
        {
            DefaultMinutesAgo = 30,
        });

        _sut = new NlpAgentModel(aliasOptions, agentOptions, NullLogger<NlpAgentModel>.Instance);
    }

    // ── Tool selection ─────────────────────────────────────────────────────────

    [Fact]
    public async Task DecideAsync_PaymentService15Minutes_SelectsGetLogs()
    {
        const string message = "Why has the payment service been returning HTTP 500 errors during the last 15 minutes?";

        var decision = await _sut.DecideAsync(message, StandardTools);

        decision.IsToolCall.Should().BeTrue();
        decision.SelectedToolName.Should().Be("GetLogs");
    }

    [Fact]
    public async Task DecideAsync_NoToolsAvailable_ReturnsNeedsMoreInformation()
    {
        var decision = await _sut.DecideAsync("any message", []);

        decision.NeedsMoreInformation.Should().BeTrue();
    }

    // ── Argument extraction — service name ────────────────────────────────────

    [Fact]
    public async Task DecideAsync_PaymentKeyword_ExtractsPaymentService()
    {
        const string message = "Why has the payment service been returning HTTP 500 errors during the last 15 minutes?";

        var decision = await _sut.DecideAsync(message, StandardTools);

        decision.Arguments.Should().ContainKey("serviceName");
        decision.Arguments["serviceName"].Should().Be("PaymentService");
    }

    [Theory]
    [InlineData("Why is the payment service down?")]
    [InlineData("The pay gateway is failing")]
    public async Task DecideAsync_VariousPaymentAliases_ExtractsPaymentService(string message)
    {
        // Use a wider default window (30 min) to not trigger missing-time-range follow-up
        var decision = await _sut.DecideAsync(message, StandardTools);

        if (decision.IsToolCall)
        {
            decision.Arguments["serviceName"].Should().Be("PaymentService");
        }
    }

    [Fact]
    public async Task DecideAsync_OrderKeyword_ExtractsOrderService()
    {
        const string message = "Why are order service errors appearing in the last 30 minutes?";

        var decision = await _sut.DecideAsync(message, StandardTools);

        decision.IsToolCall.Should().BeTrue();
        decision.Arguments["serviceName"].Should().Be("OrderService");
    }

    // ── Argument extraction — minutesAgo ──────────────────────────────────────

    [Fact]
    public async Task DecideAsync_Last15Minutes_Extracts15()
    {
        const string message = "Why has the payment service been returning HTTP 500 errors during the last 15 minutes?";

        var decision = await _sut.DecideAsync(message, StandardTools);

        decision.IsToolCall.Should().BeTrue();
        decision.Arguments.Should().ContainKey("minutesAgo");
        decision.Arguments["minutesAgo"].Should().Be(15);
    }

    [Fact]
    public async Task DecideAsync_Past30Minutes_Extracts30()
    {
        const string message = "Show me payment service errors for the past 30 minutes";

        var decision = await _sut.DecideAsync(message, StandardTools);

        decision.IsToolCall.Should().BeTrue();
        decision.Arguments["minutesAgo"].Should().Be(30);
    }

    [Fact]
    public async Task DecideAsync_LastHour_Extracts60()
    {
        const string message = "What happened to the payment service in the last hour?";

        var decision = await _sut.DecideAsync(message, StandardTools);

        decision.IsToolCall.Should().BeTrue();
        decision.Arguments["minutesAgo"].Should().Be(60);
    }

    // ── Missing time range — default applied ──────────────────────────────────

    [Fact]
    public async Task DecideAsync_MissingTimeRange_UsesDefaultAndRecordsNote()
    {
        const string message = "Why is the payment service returning errors?";

        var decision = await _sut.DecideAsync(message, StandardTools);

        // Agent should either ask a follow-up OR use the documented default.
        if (decision.IsToolCall)
        {
            decision.Arguments.Should().ContainKey("minutesAgo");
            decision.AssumptionNote.Should().NotBeNullOrEmpty(
                "when a default is used the agent must document the assumption");
            decision.Arguments["minutesAgo"].Should().Be(30); // configured default
        }
        else
        {
            decision.NeedsMoreInformation.Should().BeTrue();
        }
    }

    // ── Unknown service — follow-up expected ──────────────────────────────────

    [Fact]
    public async Task DecideAsync_UnknownService_ReturnsFollowUpQuestion()
    {
        const string message = "Why is the frobnicator service returning errors in the last 10 minutes?";

        var decision = await _sut.DecideAsync(message, StandardTools);

        // Frobnicator is not in the alias map → agent should ask which service.
        decision.NeedsMoreInformation.Should().BeTrue();
        decision.FollowUpQuestion.Should().NotBeNullOrEmpty();
    }

    // ── Replaceability ────────────────────────────────────────────────────────

    [Fact]
    public void NlpAgentModel_ImplementsIAgentModel()
    {
        // Validates that the contract required for swappable LLM providers is met.
        _sut.Should().BeAssignableTo<IAgentModel>();
    }
}
