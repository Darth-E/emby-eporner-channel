using Emby.Plugins.Eporner.Api;
using Emby.Plugins.Eporner.Scraping;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.Eporner.Services
{
    /// <summary>Shared by the channel and the config-page service so rate limiting and caches are common.</summary>
    public class EpornerHub
    {
        private static readonly object Lock = new object();
        private static EpornerHub _instance;

        public ILogger Log { get; }
        public HttpGate Gate { get; }
        public EpornerApiClient Api { get; }
        public StreamScraper Streams { get; }
        public CategoryScraper Categories { get; }

        private EpornerHub(IHttpClient http, IJsonSerializer json, ILogManager logManager)
        {
            Log = logManager.GetLogger("Eporner");
            Gate = new HttpGate(http, Log);
            Api = new EpornerApiClient(Gate, json, Log);
            Streams = new StreamScraper(Gate, Api, json, Log);
            Categories = new CategoryScraper(Gate, Log);
        }

        public static EpornerHub GetOrCreate(IHttpClient http, IJsonSerializer json, ILogManager logManager)
        {
            lock (Lock)
            {
                return _instance ?? (_instance = new EpornerHub(http, json, logManager));
            }
        }
    }
}
