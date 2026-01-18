using Jellyfin.Plugin.SubsRo.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// The main plugin for Subs.ro.
/// </summary>
public class SubsRoPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubsRoPlugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public SubsRoPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;

        ConfigurationChanged += (_, _) =>
        {
            SubsRoDownloader.Instance?.ConfigurationChanged();
        };

        SubsRoDownloader.Instance?.ConfigurationChanged();
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static SubsRoPlugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Subs.ro";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("949bf0ee-811c-4c92-af1f-2df08bfa7dd1");

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
            },
        ];
    }
}
