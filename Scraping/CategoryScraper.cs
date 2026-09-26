using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugins.Eporner.Api;
using Emby.Plugins.Eporner.Models;
using Emby.Plugins.Eporner.Services;
using MediaBrowser.Model.Logging;

namespace Emby.Plugins.Eporner.Scraping
{
    // The API has no category endpoint: names and images come from eporner.com/cats/, and the slug
    // (e.g. "hd-1080p") works as search query.
    public class CategoryScraper
    {
        private const string Url = "https://www.eporner.com/cats/";

        // one block per category: <a href="/cat/slug/" title="Name"> ... <img src="...">
        private static readonly Regex BoxRegex = new Regex(
            "class=\"categoriesbox\".*?<a href=\"/cat/([^/\"]+)/\"\\s+title=\"([^\"]*)\".*?<img src=\"([^\"]+)\"",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private readonly HttpGate _gate;
        private readonly ILogger _log;
        private readonly TtlCache _cache = new TtlCache();

        public CategoryScraper(HttpGate gate, ILogger log)
        {
            _gate = gate;
            _log = log;
        }

        public async Task<List<EpCategory>> GetCategoriesAsync(CancellationToken ct)
        {
            if (_cache.TryGet("cats", out List<EpCategory> cached)) return cached;

            var list = new List<EpCategory>();
            try
            {
                var html = await _gate.GetStringAsync(Url, "https://www.eporner.com/", ct).ConfigureAwait(false);
                foreach (Match m in BoxRegex.Matches(html ?? ""))
                {
                    var img = m.Groups[3].Value;
                    if (img.StartsWith("//")) img = "https:" + img;
                    list.Add(new EpCategory
                    {
                        Slug = m.Groups[1].Value,
                        Name = WebUtility.HtmlDecode(m.Groups[2].Value),
                        ImageUrl = img
                    });
                }
                list = list.GroupBy(c => c.Slug).Select(g => g.First()).ToList();
                if (list.Count == 0) _log.Warn("Eporner: no categories found on {0} (layout changed?)", Url);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Warn("Eporner: category page failed: {0}", ex.Message);
            }

            if (list.Count > 0) _cache.Set("cats", list, TimeSpan.FromHours(24));
            return list;
        }
    }
}
