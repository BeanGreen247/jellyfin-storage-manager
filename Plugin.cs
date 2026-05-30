using System.Globalization;
using Jellyfin.Plugin.StorageManager.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.StorageManager;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => "Storage Manager";

    // Stable GUID — do not change after first release.
    public override Guid Id => Guid.Parse("2c4d6a7b-1e3f-4589-ad2c-ef0987654321");

    public override string Description =>
        "Monitor storage usage and manage media drive contents from the Jellyfin dashboard.";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        var ns = GetType().Namespace;
        return new[]
        {
            new PluginPageInfo
            {
                Name = "StorageManager",
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Web.storageManager.html",
                    ns),
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "storage",
                DisplayName = "Storage Manager",
            },
            new PluginPageInfo
            {
                Name = "StorageManagerConfig",
                EmbeddedResourcePath = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}.Web.configPage.html",
                    ns),
            },
        };
    }
}
