namespace Dotin.DotNetForum.LogAgent.AgentClient.Configuration;

/// <summary>
/// Maps human-readable service aliases to technical service identifiers.
/// Bound from the "ServiceAliases" section of appsettings.json.
///
/// Adding a new service requires only a configuration change — no code change.
/// Example: { "inventory": "InventoryService", "stock": "InventoryService" }
/// </summary>
public sealed class ServiceAliasOptions
{
    /// <summary>
    /// Dictionary of alias (key, case-insensitive) → technical service name (value).
    /// </summary>
    public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
