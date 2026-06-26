namespace Dotin.DotNetForum.LogAgent.AgentClient.Configuration;

/// <summary>
/// General agent behaviour settings.
/// Bound from the "Agent" section of appsettings.json.
/// </summary>
public sealed class AgentOptions
{
    /// <summary>
    /// Minutes used as the time window when the user does not specify one.
    /// The agent will surface this default explicitly rather than silently assuming a value.
    /// </summary>
    public int DefaultMinutesAgo { get; set; } = 30;

    /// <summary>Timeout in seconds for each investigation request.</summary>
    public int RequestTimeoutSeconds { get; set; } = 120;
}
