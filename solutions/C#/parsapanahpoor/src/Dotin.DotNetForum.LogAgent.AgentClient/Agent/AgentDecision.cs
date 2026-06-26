namespace Dotin.DotNetForum.LogAgent.AgentClient.Agent;

/// <summary>
/// Represents the decision made by the agent model for a given user message.
/// Either a tool call with extracted arguments, or a follow-up question when
/// the message does not contain enough information.
/// </summary>
public sealed record AgentDecision
{
    /// <summary>
    /// When true, the model selected a tool and produced arguments.
    /// When false, <see cref="FollowUpQuestion"/> contains a clarification prompt.
    /// </summary>
    public bool IsToolCall => SelectedToolName is not null && !NeedsMoreInformation;

    /// <summary>Name of the tool to invoke, or <see langword="null"/> if unknown.</summary>
    public string? SelectedToolName { get; init; }

    /// <summary>
    /// Arguments to pass to the tool, keyed by parameter name.
    /// Values may be <see langword="string"/> or <see langword="int"/> depending on the parameter type.
    /// </summary>
    public IReadOnlyDictionary<string, object> Arguments { get; init; }
        = new Dictionary<string, object>();

    /// <summary>Whether the agent needs more information from the user before it can act.</summary>
    public bool NeedsMoreInformation { get; init; }

    /// <summary>
    /// When <see cref="NeedsMoreInformation"/> is <see langword="true"/>, the question
    /// the agent would like the user to answer.
    /// </summary>
    public string? FollowUpQuestion { get; init; }

    /// <summary>
    /// Optional note added by the model when it used a default value (e.g. default time range).
    /// Surfaced to the user so the assumption is always transparent.
    /// </summary>
    public string? AssumptionNote { get; init; }
}
