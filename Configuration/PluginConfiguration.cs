using MediaBrowser.Model.Plugins;

namespace Emby.Plugins.Eporner.Configuration
{
    public class PluginConfiguration : BasePluginConfiguration
    {
        public int VideosPerCategory { get; set; } = 96;

        /// <summary>Categories without an entry are not shown (opt-in).</summary>
        public CategorySetting[] CategorySettings { get; set; } = new CategorySetting[0];

        public bool DeduplicateAcrossFolders { get; set; } = true;

        public string DefaultOrder { get; set; } = "latest";

        /// <summary>0 = exclude, 1 = include, 2 = only.</summary>
        public int GayMode { get; set; } = 0;

        /// <summary>0 = exclude, 1 = include, 2 = only. Filtered by the plugin, the API has no such parameter.</summary>
        public int TransMode { get; set; } = 0;

        /// <summary>0 = exclude, 1 = include, 2 = only.</summary>
        public int LowQualityMode { get; set; } = 0;

        public string ThumbSize { get; set; } = "big";

        public string CustomQueries { get; set; } = "";

        public int CacheMinutes { get; set; } = 15;

        public int MinRequestIntervalMs { get; set; } = 500;

        public int MaxStreamHeight { get; set; } = 0;

        public bool AllowEmbedFallback { get; set; } = false;

        public bool FilterRemovedVideos { get; set; } = true;
    }

    public class CategorySetting
    {
        public string Slug { get; set; }
        public bool Enabled { get; set; } = true;

        /// <summary>0 = use VideosPerCategory.</summary>
        public int Count { get; set; }
    }
}
