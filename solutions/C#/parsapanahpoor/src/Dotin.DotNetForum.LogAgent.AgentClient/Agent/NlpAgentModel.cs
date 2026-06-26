using System.Text.RegularExpressions;
using Dotin.DotNetForum.LogAgent.AgentClient.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dotin.DotNetForum.LogAgent.AgentClient.Agent;

/// <summary>
/// A locally-running, offline-capable agent model that uses natural-language
/// parsing to select tools and extract their arguments from user messages.
///
/// This implementation is intentionally generic and extensible:
/// • Tool selection is scored against ALL tools discovered from the MCP server —
///   adding new tools on the server side does not require code changes here.
/// • Service-name resolution is driven by <see cref="ServiceAliasOptions"/>
///   loaded from configuration — adding a new service requires only a config change.
/// • Time-range extraction uses compiled regex patterns covering common English
///   temporal expressions.
///
/// To replace this with a real LLM, implement <see cref="IAgentModel"/> and swap
/// the DI registration in Program.cs — no other code changes are needed.
/// </summary>
public sealed class NlpAgentModel : IAgentModel
{
    // ── Compiled regex patterns for time-range extraction ─────────────────────
    // Ordered from most-specific to least-specific to avoid ambiguous matches.
    private static readonly Regex[] TimePatterns =
    [
        new(@"(?:last|past|previous|recent)\s+(\d+)\s+hours?",        RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(?:last|past|previous|recent)\s+(\d+)\s+minutes?",      RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(?:last|past|previous|recent)\s+(\d+)\s+mins?",         RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(\d+)\s+minutes?\s+ago",                                 RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(\d+)\s+mins?\s+ago",                                    RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(?:last|past)\s+(?:an?\s+)?hour",                       RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"(\d+)\s+hours?\s+ago",                                   RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    // Keywords that strongly suggest a log-retrieval intent.
    private static readonly HashSet<string> LogIntentKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "error", "errors", "crash", "crashing", "crashed", "fail", "failing",
        "failed", "failure", "500", "http500", "logs", "log", "issue", "problem",
        "down", "broken", "not working", "investigate", "investigation",
        "why", "what", "happened", "check", "see", "show", "look",
    };

    private readonly ServiceAliasOptions _aliases;
    private readonly AgentOptions _agentOptions;
    private readonly ILogger<NlpAgentModel> _logger;

    public NlpAgentModel(
        IOptions<ServiceAliasOptions> aliases,
        IOptions<AgentOptions> agentOptions,
        ILogger<NlpAgentModel> logger)
    {
        _aliases = aliases.Value;
        _agentOptions = agentOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<AgentDecision> DecideAsync(
        string userMessage,
        IReadOnlyCollection<AvailableTool> availableTools,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("NlpAgentModel.DecideAsync — message: '{Message}'", userMessage);

        if (availableTools.Count == 0)
        {
            _logger.LogWarning("No tools available from MCP server");
            return Task.FromResult(new AgentDecision
            {
                NeedsMoreInformation = true,
                FollowUpQuestion = "No tools are currently available. Please ensure the MCP server is running.",
            });
        }

        // ── Step 1: Select the best matching tool ─────────────────────────────
        var selectedTool = SelectBestTool(userMessage, availableTools);
        if (selectedTool is null)
        {
            _logger.LogWarning("No suitable tool found for message: '{Message}'", userMessage);
            return Task.FromResult(new AgentDecision
            {
                NeedsMoreInformation = true,
                FollowUpQuestion =
                    "I could not identify the appropriate action for your request. " +
                    "Please ask about service errors (e.g. 'Why is the payment service returning 500 errors?').",
            });
        }

        _logger.LogInformation("Selected tool: '{ToolName}'", selectedTool.Name);

        // ── Step 2: Extract arguments for the selected tool ───────────────────
        var (arguments, assumptionNote, missingParam) = ExtractArguments(userMessage, selectedTool);

        if (missingParam is not null)
        {
            return Task.FromResult(new AgentDecision
            {
                SelectedToolName = selectedTool.Name,
                NeedsMoreInformation = true,
                FollowUpQuestion = missingParam,
            });
        }

        _logger.LogInformation(
            "Tool call ready — tool: '{ToolName}', args: {Args}",
            selectedTool.Name,
            string.Join(", ", arguments.Select(kv => $"{kv.Key}={kv.Value}")));

        return Task.FromResult(new AgentDecision
        {
            SelectedToolName = selectedTool.Name,
            Arguments = arguments,
            AssumptionNote = assumptionNote,
        });
    }

    // ── Tool selection ────────────────────────────────────────────────────────

    /// <summary>
    /// Scores each available tool against the user message and returns the best match.
    /// A score of zero means the tool is unrelated to the request.
    /// </summary>
    private AvailableTool? SelectBestTool(string message, IReadOnlyCollection<AvailableTool> tools)
    {
        var messageWords = Tokenize(message);
        var intentScore = messageWords.Count(w => LogIntentKeywords.Contains(w));

        if (intentScore == 0)
        {
            // No log-related intent detected — require at least one matching keyword.
            _logger.LogDebug("No log-intent keywords found in message");
        }

        AvailableTool? best = null;
        var bestScore = -1;

        foreach (var tool in tools)
        {
            var score = ScoreTool(tool, messageWords, intentScore);
            _logger.LogDebug("Tool '{ToolName}' scored {Score}", tool.Name, score);

            if (score > bestScore)
            {
                bestScore = score;
                best = tool;
            }
        }

        return bestScore > 0 ? best : null;
    }

    private static int ScoreTool(AvailableTool tool, IReadOnlyCollection<string> messageWords, int intentScore)
    {
        var score = 0;

        // Reward direct token overlap with the tool name (camelCase split).
        foreach (var nameToken in SplitCamelCase(tool.Name))
        {
            if (messageWords.Contains(nameToken, StringComparer.OrdinalIgnoreCase))
                score += 5;
        }

        // Reward meaningful token overlap with the tool description.
        var descWords = Tokenize(tool.Description).Where(w => w.Length > 3).ToHashSet(StringComparer.OrdinalIgnoreCase);
        score += messageWords.Count(w => descWords.Contains(w)) * 2;

        // Bonus for matching log-retrieval intent.
        score += intentScore;

        return score;
    }

    // ── Argument extraction ───────────────────────────────────────────────────

    /// <summary>
    /// Extracts argument values from the user message for each parameter of the chosen tool.
    /// Returns the argument dictionary, an optional assumption note, and an optional
    /// clarification message when a required parameter could not be resolved.
    /// </summary>
    private (IReadOnlyDictionary<string, object> args, string? assumptionNote, string? missingParam)
        ExtractArguments(string message, AvailableTool tool)
    {
        var args = new Dictionary<string, object>();
        string? assumptionNote = null;

        foreach (var param in tool.Parameters)
        {
            var combinedHint = $"{param.Name} {param.Description}".ToLowerInvariant();

            if (combinedHint.Contains("service") || combinedHint.Contains("name"))
            {
                var resolved = ResolveServiceName(message);
                if (resolved is null && param.Required)
                {
                    return (args, null,
                        "Which service should I investigate? " +
                        "Please mention the service name (e.g. 'payment service', 'order service').");
                }
                if (resolved is not null)
                    args[param.Name] = resolved;
            }
            else if (combinedHint.Contains("minute") || combinedHint.Contains("time") || combinedHint.Contains("ago"))
            {
                var minutes = ExtractMinutesAgo(message);
                if (minutes is null)
                {
                    // Use a documented default rather than silently picking a value.
                    var defaultMinutes = _agentOptions.DefaultMinutesAgo;
                    args[param.Name] = defaultMinutes;
                    assumptionNote =
                        $"No time range was specified. Defaulting to the last {defaultMinutes} minutes " +
                        $"(configured via Agent:DefaultMinutesAgo). " +
                        $"You can ask again with an explicit range, e.g. '... during the last 30 minutes'.";
                    _logger.LogInformation(
                        "No time range in message; using default of {Default} minutes", defaultMinutes);
                }
                else
                {
                    args[param.Name] = minutes.Value;
                }
            }
        }

        return (args, assumptionNote, null);
    }

    // ── Service-name resolution ───────────────────────────────────────────────

    /// <summary>
    /// Looks up the technical service identifier by checking every configured alias
    /// against the user message (case-insensitive, longest match wins).
    /// </summary>
    private string? ResolveServiceName(string message)
    {
        // Sort by alias length descending so "payment gateway" beats "payment".
        var matches = _aliases.Aliases
            .Where(kv => message.Contains(kv.Key, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(kv => kv.Key.Length)
            .ToList();

        if (matches.Count == 0)
        {
            _logger.LogDebug("Service name could not be resolved from message");
            return null;
        }

        var resolved = matches[0].Value;
        _logger.LogDebug("Resolved service alias '{Alias}' → '{ServiceName}'", matches[0].Key, resolved);
        return resolved;
    }

    // ── Time-range extraction ─────────────────────────────────────────────────

    /// <summary>
    /// Extracts a number-of-minutes value from natural-language temporal expressions.
    /// Returns <see langword="null"/> when no recognisable expression is found.
    /// </summary>
    private int? ExtractMinutesAgo(string message)
    {
        foreach (var pattern in TimePatterns)
        {
            var match = pattern.Match(message);
            if (!match.Success) continue;

            // Patterns for "last hour" / "past hour" have no capture group → default 60.
            if (match.Groups.Count < 2 || string.IsNullOrEmpty(match.Groups[1].Value))
            {
                _logger.LogDebug("Time pattern matched (hour literal) → 60 minutes");
                return 60;
            }

            if (!int.TryParse(match.Groups[1].Value, out var value))
                continue;

            // Convert hours to minutes where necessary.
            var isHourPattern = pattern.ToString().Contains("hour", StringComparison.OrdinalIgnoreCase);
            var minutes = isHourPattern ? value * 60 : value;

            _logger.LogDebug("Time pattern matched → {Minutes} minutes (raw value={Value}, hours={IsHour})",
                minutes, value, isHourPattern);
            return minutes;
        }

        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IReadOnlyList<string> Tokenize(string text) =>
        text.Split([' ', '\t', '\n', '\r', '.', ',', '!', '?', '\'', '"', '(', ')'],
                  StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length > 1)
            .ToList();

    private static IEnumerable<string> SplitCamelCase(string name) =>
        Regex.Split(name, @"(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])");
}
