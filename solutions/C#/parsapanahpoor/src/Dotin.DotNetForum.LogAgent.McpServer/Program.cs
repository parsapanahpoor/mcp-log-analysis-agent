using Dotin.DotNetForum.LogAgent.McpServer.Services;
using Dotin.DotNetForum.LogAgent.McpServer.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The MCP server uses stdio transport, so all diagnostic output must go to stderr
// to avoid corrupting the MCP message stream on stdout.
var builder = Host.CreateApplicationBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

// Register the log provider abstraction — swap this for a real Elasticsearch
// implementation without touching any other part of the application.
builder.Services.AddSingleton<ILogProvider, SimulatedLogProvider>();

// Register and configure the MCP server using the official SDK.
// Tools are discovered via reflection from the assembly.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<GetLogsTool>();

var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("MCP Log Analysis Server starting — transport: stdio");

await host.RunAsync();
