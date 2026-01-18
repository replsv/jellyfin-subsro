using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.SubsRo.Configuration;

/// <summary>
/// Plugin configuration for Subs.ro.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the API key for Subs.ro.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
