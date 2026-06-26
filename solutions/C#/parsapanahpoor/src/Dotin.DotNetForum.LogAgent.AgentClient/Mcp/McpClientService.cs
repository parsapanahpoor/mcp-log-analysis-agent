using System.Text.Json;
using Dotin.DotNetForum.LogAgent.AgentClient.Agent;
using Dotin.DotNetForum.LogAgent.AgentClient.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Dotin.DotNetForum.LogAgent.AgentClient.Mcp;

/// <summary>
/// Wraps the official MCP C# SDK client, launching the MCP server as a
/// child process over stdio and exposing a clean domain-specific interface.
/// </summary>
public sealed class McpClientService : IMcpClientService
{
    private McpClient? _client;
    private readonly McpServerOptions _serverOptions;
    private readonly ILogger<McpClientService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public McpClientService(
        IOptions<McpServerOptions> serverOptions,
        ILogger<McpClientService> logger)
    {
        _serverOptions = serverOptions.Value;
        _logger = logger;
    }

    // ── IMcpClientService ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<AvailableTool>> GetAvailableToolsAsync(
        CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(cancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        return tools
            .Select(MapToAvailableTool)
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<string> CallToolAsync(
        string toolName,
        IReadOnlyDictionary<string, object> arguments,
        CancellationToken cancellationToken = default)
    {
        var client = await GetClientAsync(cancellationToken);

        _logger.LogInformation(
            "MCP tool call — tool: '{ToolName}', arguments: {Arguments}",
            toolName,
            JsonSerializer.Serialize(arguments));

        var nullableArgs = arguments.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        var result = await client.CallToolAsync(
            toolName,
            nullableArgs,
            cancellationToken: cancellationToken);

        if (result.IsError == true)
        {
            _logger.LogWarning("MCP server returned an error response for tool '{ToolName}'", toolName);
        }

        var textContent = result.Content
            .OfType<TextContentBlock>()
            .Select(c => c.Text)
            .FirstOrDefault() ?? string.Empty;

        _logger.LogInformation(
            "MCP tool response received — {Length} characters", textContent.Length);

        return textContent;
    }

    // ── Connection management ─────────────────────────────────────────────────

    private async Task<McpClient> GetClientAsync(CancellationToken cancellationToken)
    {
        if (_client is not null) return _client;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_client is not null) return _client;

            _logger.LogInformation(
                "Connecting to MCP server — command: '{Command}', args: [{Args}]",
                _serverOptions.Command,
                string.Join(", ", _serverOptions.Arguments));

            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "McpLogServer",
                Command = _serverOptions.Command,
                Arguments = _serverOptions.Arguments,
            });

            _client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            _logger.LogInformation("MCP client connected successfully");
            return _client;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to connect to MCP server");
            throw new InvalidOperationException(
                $"Could not connect to the MCP server. " +
                $"Ensure the server project is built and the command '{_serverOptions.Command} {string.Join(" ", _serverOptions.Arguments)}' " +
                $"is executable from the current working directory.",
                ex);
        }
        finally
        {
            _initLock.Release();
        }
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static AvailableTool MapToAvailableTool(McpClientTool tool)
    {
        var parameters = ParseParameters(tool.JsonSchema);

        return new AvailableTool
        {
            Name = tool.Name,
            Description = tool.Description ?? string.Empty,
            Parameters = parameters,
        };
    }

    private static IReadOnlyList<ToolParameter> ParseParameters(JsonElement schema)
    {
        var result = new List<ToolParameter>();

        if (!schema.TryGetProperty("properties", out var properties))
            return result;

        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (schema.TryGetProperty("required", out var requiredArr))
        {
            foreach (var item in requiredArr.EnumerateArray())
            {
                var name = item.GetString();
                if (name is not null) required.Add(name);
            }
        }

        foreach (var prop in properties.EnumerateObject())
        {
            var description = prop.Value.TryGetProperty("description", out var desc)
                ? desc.GetString() ?? string.Empty
                : string.Empty;

            var type = prop.Value.TryGetProperty("type", out var t)
                ? t.GetString() ?? "string"
                : "string";

            result.Add(new ToolParameter
            {
                Name = prop.Name,
                Type = type,
                Description = description,
                Required = required.Contains(prop.Name),
            });
        }

        return result.AsReadOnly();
    }

    // ── IAsyncDisposable ──────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        if (_client is IAsyncDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else if (_client is IDisposable disposable)
            disposable.Dispose();

        _initLock.Dispose();
    }
}
