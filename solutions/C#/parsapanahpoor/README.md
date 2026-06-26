# MCP Log Analysis Agent — Solution by parsapanahpoor

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4)]()
[![MCP SDK](https://img.shields.io/badge/MCP%20SDK-1.4.0-informational)]()
[![Tests](https://img.shields.io/badge/tests-46%20passing-brightgreen)]()
[![Build](https://img.shields.io/badge/build-passing-brightgreen)]()

---

## Table of Contents

- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [How to Run](#how-to-run)
- [How to Run Tests](#how-to-run-tests)
- [Agent Provider Used](#agent-provider-used)
- [Replacing the Mock with a Real LLM](#replacing-the-mock-with-a-real-llm)
- [Limitations and Assumptions](#limitations-and-assumptions)
- [Sample Execution Output](#sample-execution-output)

---

## Architecture

```
User Question (Console Input)
        │
        ▼
 LogAnalysisAgent          ← orchestrator; holds no business logic
        │
        ├─ IAgentModel.DecideAsync()
        │       └─ NlpAgentModel      ← NLP parser (offline, testable)
        │              ├─ ToolSelector (scores tools vs. message)
        │              ├─ ServiceAliasResolver (config-driven mapping)
        │              └─ TimeRangeExtractor (regex-based)
        │
        ├─ IMcpClientService.GetAvailableToolsAsync()
        │       └─ McpClientService   ← wraps the official MCP SDK
        │              └─ StdioClientTransport → MCP Server (child process)
        │
        ├─ IMcpClientService.CallToolAsync("GetLogs", {…})
        │       └─ MCP Server Process
        │              └─ GetLogsTool
        │                     └─ ILogProvider
        │                            └─ SimulatedLogProvider (in-memory)
        │
        └─ ILogAnalyzer.Analyze()
                └─ LogAnalyzer (regex-based root-cause identification)
                       └─ User-facing report (dynamically generated)
```

### Execution Flow

```
Console App
  → LogAnalysisAgent.InvestigateAsync(userMessage)
      1. McpClientService.GetAvailableToolsAsync()   — discover tools via MCP
      2. NlpAgentModel.DecideAsync()                 — select tool + extract args
      3. [TOOL CALL logged] McpClientService.CallToolAsync("GetLogs", args)
      4. LogAnalyzer.Analyze(rawLogs, decision)      — identify root cause
      5. Return plain-English report to user
```

---

## Project Structure

```
solutions/csharp/parsapanahpoor/
├── src/
│   ├── Dotin.DotNetForum.LogAgent.Contracts/          Shared domain models (LogEntry)
│   ├── Dotin.DotNetForum.LogAgent.McpServer/          MCP Server application
│   │   ├── Services/
│   │   │   ├── ILogProvider.cs                        Log-retrieval abstraction
│   │   │   └── SimulatedLogProvider.cs                In-memory Elasticsearch simulation
│   │   ├── Tools/
│   │   │   └── GetLogsTool.cs                         MCP tool (stdio transport)
│   │   └── Program.cs
│   └── Dotin.DotNetForum.LogAgent.AgentClient/        Console Agent application
│       ├── Agent/
│       │   ├── IAgentModel.cs                         Model abstraction (swap for any LLM)
│       │   ├── AgentDecision.cs
│       │   ├── AvailableTool.cs
│       │   ├── ToolParameter.cs
│       │   ├── NlpAgentModel.cs                       Offline NLP-based implementation
│       │   └── LogAnalysisAgent.cs                    Orchestrator
│       ├── Analysis/
│       │   ├── ILogAnalyzer.cs
│       │   ├── LogAnalyzer.cs                         Regex-based root-cause analysis
│       │   └── AnalysisReport.cs
│       ├── Mcp/
│       │   ├── IMcpClientService.cs
│       │   └── McpClientService.cs                    MCP SDK wrapper
│       ├── Configuration/
│       │   ├── AgentOptions.cs
│       │   ├── McpServerOptions.cs
│       │   └── ServiceAliasOptions.cs
│       ├── appsettings.json
│       └── Program.cs
├── tests/
│   ├── Dotin.DotNetForum.LogAgent.McpServer.Tests/
│   │   ├── GetLogsToolTests.cs
│   │   └── SimulatedLogProviderTests.cs
│   └── Dotin.DotNetForum.LogAgent.AgentClient.Tests/
│       ├── NlpAgentModelTests.cs
│       ├── LogAnalyzerTests.cs
│       └── LogAnalysisAgentTests.cs
├── README.md
└── Dotin.DotNetForum.LogAgent.sln
```

---

## How to Run

### Prerequisites

```bash
dotnet --version   # must be 10.x
```

### Step 1 — Build

Run from the solution root (the `parsapanahpoor/` directory):

```bash
dotnet build
```

### Step 2 — Run the Agent Client

```bash
dotnet run --project src/Dotin.DotNetForum.LogAgent.AgentClient
```

> **Important:** Run this command from the `parsapanahpoor/` directory.
> The client automatically spawns the MCP server as a child process over stdio — no separate server startup is needed.

The server command is configured in `src/Dotin.DotNetForum.LogAgent.AgentClient/appsettings.json`:

```json
{
  "McpServer": {
    "Command": "dotnet",
    "Arguments": ["run", "--project", "src/Dotin.DotNetForum.LogAgent.McpServer", "--no-launch-profile"]
  }
}
```

### Running the MCP Server Independently (optional)

```bash
dotnet run --project src/Dotin.DotNetForum.LogAgent.McpServer
```

---

## How to Run Tests

```bash
dotnet test
```

**46 tests, all passing.** Coverage includes:

| Category | Tests |
|---|---|
| `GetLogsTool` input validation (empty/invalid args) | 6 |
| `SimulatedLogProvider` log retrieval and filtering | 9 |
| `NlpAgentModel` tool selection and argument extraction | 11 |
| `LogAnalyzer` root-cause identification and report generation | 11 |
| `LogAnalysisAgent` orchestration and error handling | 9 |

---

## Agent Provider Used

**`NlpAgentModel`** — a locally-running, offline, zero-dependency natural-language parser.

### How It Works

1. **Tool Selection** — scores every tool discovered at runtime by computing keyword overlap between the user message and each tool's name + description. The tool with the highest score is selected.

2. **Service-Name Resolution** — reads a configurable alias dictionary from `appsettings.json` and maps human-readable names to technical service identifiers. Example: `"payment"` → `"PaymentService"`.

3. **Time-Range Extraction** — applies a set of compiled regex patterns against the user message to extract natural-language temporal expressions:
   - `"last 15 minutes"` → 15
   - `"past 30 mins"` → 30
   - `"last hour"` → 60
   - `"15 minutes ago"` → 15

4. **Missing Information** — if no time range is found, the agent uses the configured default (`Agent:DefaultMinutesAgo = 30`) and **explicitly surfaces this assumption** in the response. If the service name cannot be resolved, the agent asks a clarifying follow-up question.

### Adding New Services

Edit `appsettings.json` — no code changes required:

```json
{
  "ServiceAliases": {
    "Aliases": {
      "inventory": "InventoryService",
      "stock":     "InventoryService",
      "shipping":  "ShippingService"
    }
  }
}
```

---

## Replacing the Mock with a Real LLM

The **only change needed** is a single DI registration line in `Program.cs`:

```csharp
// Current (offline mock):
builder.Services.AddSingleton<IAgentModel, NlpAgentModel>();

// Replace with OpenAI / Azure OpenAI:
builder.Services.AddSingleton<IAgentModel, OpenAiAgentModel>();

// Replace with Ollama:
builder.Services.AddSingleton<IAgentModel, OllamaAgentModel>();
```

Your new implementation must implement `IAgentModel`:

```csharp
public interface IAgentModel
{
    Task<AgentDecision> DecideAsync(
        string userMessage,
        IReadOnlyCollection<AvailableTool> availableTools,
        CancellationToken cancellationToken = default);
}
```

`LogAnalysisAgent`, `McpClientService`, and `LogAnalyzer` require **zero changes** when swapping the model provider.

### Example Real-LLM Skeleton

```csharp
public sealed class OpenAiAgentModel(OpenAIClient client) : IAgentModel
{
    public async Task<AgentDecision> DecideAsync(
        string userMessage,
        IReadOnlyCollection<AvailableTool> availableTools,
        CancellationToken cancellationToken = default)
    {
        // Convert availableTools to OpenAI function definitions
        // Call the Chat Completions API with tool_choice
        // Map the resulting tool_call back to AgentDecision
        throw new NotImplementedException();
    }
}
```

---

## Limitations and Assumptions

| Item | Detail |
|---|---|
| **Log data** | Simulated in-memory. Entries are generated relative to `DateTimeOffset.UtcNow` at request time. No real Elasticsearch connection is made. |
| **MCP transport** | stdio only. The agent client launches the server as a child process. |
| **Working directory** | `dotnet run` must be invoked from the `parsapanahpoor/` directory so that the server project path in `appsettings.json` resolves correctly. |
| **NLP accuracy** | The NLP model covers common English temporal expressions and a predefined service-alias vocabulary. Unusual phrasings may not be recognised. |
| **Default time range** | When no time range is specified, the agent uses 30 minutes (configurable via `Agent:DefaultMinutesAgo`). This default is **always stated explicitly** in the response. |
| **Concurrency** | The MCP client connection is initialised lazily with a `SemaphoreSlim(1,1)` guard — safe for single-threaded console use. |
| **No secrets** | The solution requires no API keys and no internet access. |

---

## Sample Execution Output

```
╔═══════════════════════════════════════════════════════╗
║     MCP Log Analysis Agent  —  .NET 10 / MCP SDK     ║
╠═══════════════════════════════════════════════════════╣
║  Ask about production errors, e.g.:                  ║
║  'Why has the payment service been returning          ║
║   HTTP 500 errors during the last 15 minutes?'       ║
║  Type 'exit' or press Ctrl+C to quit.                ║
╚═══════════════════════════════════════════════════════╝

You: Why has the payment service been returning HTTP 500 errors during the last 15 minutes?

[Investigating — please wait …]

Agent: The logs from the last 15 minutes show that the PaymentService
has been returning HTTP 500 errors.

Root Cause: The 'SQL-Server-01' database server did not respond within 30 seconds
(30000ms). The service is unable to complete database operations, causing HTTP 500
errors for all requests that depend on the database.

Error Summary (5 error(s), 3 warning(s) detected):
  • [ERROR] - PaymentService - Database connection timeout after 30000ms on SQL-Server-01
  • [ERROR] - PaymentService - HTTP 500 Internal Server Error returned to client (POST /api/payments)
  • [ERROR] - PaymentService - Database connection timeout after 30000ms on SQL-Server-01
  • [ERROR] - PaymentService - Database connection timeout after 30000ms on SQL-Server-01
  • [ERROR] - PaymentService - HTTP 500 Internal Server Error returned to client (POST /api/payments)

You: Why is the payment service returning errors?

[Investigating — please wait …]

Agent: Note: No time range was specified. Defaulting to the last 30 minutes
(configured via Agent:DefaultMinutesAgo). You can ask again with an explicit range,
e.g. '... during the last 30 minutes'.

The logs from the last 30 minutes show that the PaymentService has been returning
HTTP 500 errors.

Root Cause: The 'SQL-Server-01' database server did not respond within 30 seconds
(30000ms). …

You: exit
Goodbye.
```

### Debug / Log Output (visible in terminal)

```
info: LogAnalysisAgent[0]
      Investigation started — message: 'Why has the payment service...'
info: McpClientService[0]
      Connecting to MCP server — command: 'dotnet', args: [run, --project, ...]
info: McpClientService[0]
      MCP client connected successfully
info: NlpAgentModel[0]
      Selected tool: 'GetLogs'
info: LogAnalysisAgent[0]
      TOOL CALL → name: 'GetLogs', arguments: {"serviceName":"PaymentService","minutesAgo":15}
info: McpClientService[0]
      MCP tool call — tool: 'GetLogs', arguments: {"serviceName":"PaymentService","minutesAgo":15}
info: McpClientService[0]
      MCP tool response received — 612 characters
info: LogAnalyzer[0]
      Root cause identified: DB timeout — 30000ms on SQL-Server-01
```
