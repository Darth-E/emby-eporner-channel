using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugins.Eporner.Models;
using Emby.Plugins.Eporner.Services;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.Eporner.Api
{
    public class EpornerApiClient
    {
        private const string BaseUrl = "https://www.eporner.com/api/v2/";

        private readonly HttpGate _gate;
        private readonly IJsonSerializer _json;
        private readonly ILogger _log;
        private readonly TtlCache _cache = new TtlCache();

        public EpornerApiClient(HttpGate gate, IJsonSerializer json, ILogger log)
        {
            _gate = gate;
            _json = json;
            _log = log;
        }

        private static TimeSpan CacheTtl =>
            TimeSpan.FromMinutes(Math.Max(0, Plugin.Instance?.Configuration.CacheMinutes ?? 15));

        public async Task<SearchResponse> SearchAsync(string query, string order, int page, int perPage, CancellationToken ct)
        {
            var cfg = Plugin.Instance.Configuration;
            perPage = Math.Min(1000, Math.Max(1, perPage));
            page = Math.Max(1, page);

            var url = BaseUrl + "video/search/?format=json"
                + "&query=" + Uri.EscapeDataString(string.IsNullOrWhiteSpace(query) ? "all" : query.Trim())
                + "&per_page=" + perPage.ToString(CultureInfo.InvariantCulture)
                + "&page=" + page.ToString(CultureInfo.InvariantCulture)
                + "&thumbsize=" + Uri.EscapeDataString(cfg.ThumbSize ?? "big")
                + "&order=" + Uri.EscapeDataString(string.IsNullOrEmpty(order) ? cfg.DefaultOrder : order)
                + "&gay=" + cfg.GayMode.ToString(CultureInfo.InvariantCulture)
                + "&lq=" + cfg.LowQualityMode.ToString(CultureInfo.InvariantCulture);

            var result = await GetCachedAsync<SearchResponse>(url, ct).ConfigureAwait(false)
                         ?? new SearchResponse();

            if (cfg.FilterRemovedVideos && result.Videos.Count > 0)
            {
                var removed = await GetRemovedIdsAsync(ct).ConfigureAwait(false);
                if (removed.Count > 0)
                {
                    var before = result.Videos.Count;
                    result.Videos = result.Videos.Where(v => !removed.Contains(v.Id)).ToList();
                    if (before != result.Videos.Count)
                        _log.Debug("Eporner: filtered {0} removed video(s)", before - result.Videos.Count);
                }
            }
            return result;
        }

        public async Task<EpVideo> GetByIdAsync(string id, CancellationToken ct)
        {
            var url = BaseUrl + "video/id/?format=json&id=" + Uri.EscapeDataString(id)
                + "&thumbsize=" + Uri.EscapeDataString(Plugin.Instance.Configuration.ThumbSize ?? "big");
            return await GetCachedAsync<EpVideo>(url, ct).ConfigureAwait(false);
        }

        public async Task<HashSet<string>> GetRemovedIdsAsync(CancellationToken ct)
        {
            const string key = "removed";
            if (_cache.TryGet(key, out HashSet<string> cached)) return cached;

            var set = new HashSet<string>();
            try
            {
                // The endpoint redirects to a plain-text file with one video id per line (not JSON).
                var body = await _gate.GetStringAsync(BaseUrl + "video/removed/?format=json", null, ct).ConfigureAwait(false);
                foreach (var line in (body ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var id = line.Trim();
                    if (id.Length > 0 && id.Length < 32 && !id.StartsWith("{")) set.Add(id);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Warn("Eporner: could not load removed list: {0}", ex.Message);
            }
            _cache.Set(key, set, TimeSpan.FromMinutes(Math.Max(60, CacheTtl.TotalMinutes)));
            return set;
        }

        public void ClearCache() => _cache.Clear();

        private async Task<T> GetCachedAsync<T>(string url, CancellationToken ct) where T : class
        {
            if (_cache.TryGet(url, out T hit)) return hit;
            var value = await GetAsync<T>(url, ct).ConfigureAwait(false);
            if (value != null) _cache.Set(url, value, CacheTtl);
            return value;
        }

        private async Task<T> GetAsync<T>(string url, CancellationToken ct) where T : class
        {
            _log.Debug("Eporner API: {0}", url);
            var body = await _gate.GetStringAsync(url, null, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                return _json.DeserializeFromString<T>(body);
            }
            catch (Exception ex)
            {
                _log.ErrorException("Eporner: unexpected API response for " + url, ex);
                throw;
            }
        }
    }
}
