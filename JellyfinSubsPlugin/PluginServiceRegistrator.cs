using System.Net.Http.Headers;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Controller.Subtitles;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.SubsRo;

/// <summary>
/// Register subtitle provider and HTTP client.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(
        IServiceCollection serviceCollection,
        IServerApplicationHost applicationHost
    )
    {
        serviceCollection.AddHttpClient(
            "SubsRo",
            c =>
            {
                c.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue(
                        applicationHost.Name.Replace(' ', '_'),
                        applicationHost.ApplicationVersionString
                    )
                );
                c.DefaultRequestHeaders.UserAgent.Add(
                    new ProductInfoHeaderValue(
                        "Jellyfin-Plugin-SubsRo",
                        System
                            .Reflection.Assembly.GetExecutingAssembly()
                            .GetName()
                            .Version!.ToString()
                    )
                );
                c.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json")
                );
            }
        );

        serviceCollection.AddSingleton<SubsRoApiV1>();
        serviceCollection.AddSingleton<ISubtitleProvider, SubsRoDownloader>();
    }
}
