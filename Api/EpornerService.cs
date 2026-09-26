using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Emby.Plugins.Eporner.Models;
using Emby.Plugins.Eporner.Services;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;

namespace Emby.Plugins.Eporner.Api
{
    [Route("/Eporner/Categories", "GET", Summary = "Lists eporner.com categories")]
    [Authenticated(Roles = "Admin")]
    public class GetCategories : IReturn<List<EpCategory>>
    {
    }

    public class EpornerService : IService
    {
        private readonly EpornerHub _hub;

        public EpornerService(IHttpClient http, IJsonSerializer json, ILogManager logManager)
        {
            _hub = EpornerHub.GetOrCreate(http, json, logManager);
        }

        public async Task<object> Get(GetCategories request)
        {
            var cats = await _hub.Categories.GetCategoriesAsync(System.Threading.CancellationToken.None).ConfigureAwait(false);
            return cats.OrderBy(c => c.Name).ToList();
        }
    }
}
