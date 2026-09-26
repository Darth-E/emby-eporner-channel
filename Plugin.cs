using System;
using System.Collections.Generic;
using System.IO;
using Emby.Plugins.Eporner.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.Eporner
{
    public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages, IHasThumbImage
    {
        public static Plugin Instance { get; private set; }

        public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
            : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        public override string Name => "Eporner";

        public override string Description => "Unofficial plugin, not affiliated with or endorsed by ePorner. Browse and play ePorner videos via the ePorner API v2.";

        // must match PluginUniqueId in epornerConfig.js
        public override Guid Id => new Guid("8a7c1f5e-3b2d-4f6a-9c1e-5d4b7a2e6f30");

        public Stream GetThumbImage() => GetType().Assembly.GetManifestResourceStream(GetType().Namespace + ".Resources.logo.png");

        public ImageFormat ThumbImageFormat => ImageFormat.Png;

        public IEnumerable<PluginPageInfo> GetPages()
        {
            yield return new PluginPageInfo
            {
                // no MenuSection: listed under "Advanced" in the dashboard menu
                Name = "epornerconfig",
                DisplayName = "Eporner",
                MenuIcon = "explicit",
                EnableInMainMenu = true,
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.configPage.html"
            };

            // controller referenced by data-controller in the page above
            yield return new PluginPageInfo
            {
                Name = "epornerjs",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.epornerConfig.js"
            };
        }
    }
}
