using Dotin.DotNetForum.LogAgent.AgentClient.Agent;
using Dotin.DotNetForum.LogAgent.AgentClient.Analysis;
using Dotin.DotNetForum.LogAgent.AgentClient.Configuration;
using Dotin.DotNetForum.LogAgent.AgentClient.Mcp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// ── Host / DI setup ────────────────────────────────────────────────────────────
var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
    .AddEnvironmentVariables();

builder.Services
    .Configure<McpServerOptions>(builder.Configuration.GetSection("McpServer"))
    .Configure<ServiceAliasOptions>(builder.Configuration.GetSection("ServiceAliases"))
    .Configure<AgentOptions>(builder.Configuration.GetSection("Agent"));

// ── Agent layer — swap NlpAgentModel for a real LLM implementation by changing
//    only this single line (or use a feature flag / factory). ──────────────────
builder.Services.AddSingleton<IAgentModel, NlpAgentModel>();

// ── Infrastructure layer ───────────────────────────────────────────────────────
builder.Services.AddSingleton<IMcpClientService, McpClientService>();
builder.Services.AddSingleton<ILogAnalyzer, LogAnalyzer>();
builder.Services.AddSingleton<LogAnalysisAgent>();

using var host = builder.Build();

var agent = host.Services.GetRequiredService<LogAnalysisAgent>();
var agentOptions = host.Services.GetRequiredService<IOptions<AgentOptions>>().Value;
var appLogger = host.Services.GetRequiredService<ILogger<Program>>();
var mcpClient = host.Services.GetRequiredService<IMcpClientService>();

// ── Banner ─────────────────────────────────────────────────────────────────────
Console.ForegroundColor = ConsoleColor.Cyan;
Console.WriteLine("╔═══════════════════════════════════════════════════════╗");
Console.WriteLine("║     MCP Log Analysis Agent  —  .NET 10 / MCP SDK     ║");
Console.WriteLine("╠═══════════════════════════════════════════════════════╣");
Console.WriteLine("║  Ask about production errors, e.g.:                  ║");
Console.WriteLine("║  'Why has the payment service been returning          ║");
Console.WriteLine("║   HTTP 500 errors during the last 15 minutes?'       ║");
Console.WriteLine("║  Type 'exit' or press Ctrl+C to quit.                ║");
Console.WriteLine("╚═══════════════════════════════════════════════════════╝");
Console.ResetColor();
Console.WriteLine();

// ── Graceful cancellation ──────────────────────────────────────────────────────
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// ── Main interaction loop ──────────────────────────────────────────────────────
try
{
    while (!cts.Token.IsCancellationRequested)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write("You: ");
        Console.ResetColor();

        var input = Console.ReadLine();

        if (input is null || cts.Token.IsCancellationRequested)
            break;

        input = input.Trim();

        if (string.IsNullOrWhiteSpace(input))
            continue;

        if (input.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
            input.Equals("quit", StringComparison.OrdinalIgnoreCase))
            break;

        Console.WriteLine();

        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        requestCts.CancelAfter(TimeSpan.FromSeconds(agentOptions.RequestTimeoutSeconds));

        try
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("[Investigating — please wait …]");
            Console.ResetColor();

            var response = await agent.InvestigateAsync(input, requestCts.Token);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("Agent: ");
            Console.ResetColor();
            Console.WriteLine(response);
        }
        catch (OperationCanceledException) when (requestCts.IsCancellationRequested && !cts.IsCancellationRequested)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Agent: Request timed out after {agentOptions.RequestTimeoutSeconds}s. Please try again.");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            appLogger.LogError(ex, "Unhandled error during investigation");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Agent: Investigation failed — {ex.Message}");
            Console.ResetColor();
        }

        Console.WriteLine();
    }
}
finally
{
    await mcpClient.DisposeAsync();

    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine("Goodbye.");
    Console.ResetColor();
}
