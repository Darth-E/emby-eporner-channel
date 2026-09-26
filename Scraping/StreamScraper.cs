using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugins.Eporner.Api;
using Emby.Plugins.Eporner.Models;
using Emby.Plugins.Eporner.Services;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.Eporner.Scraping
{
    /// <summary>
    /// Resolves MP4/HLS URLs: reads the player hash from the video page and calls the player XHR (all qualities);
    /// falls back to parsing the page HTML. Results are cached briefly because the CDN links expire.
    /// </summary>
    public class StreamScraper
    {
        private const string Host = "www.eporner.com";

        private static readonly Regex HashRegex = new Regex(
            @"EP\.video\.player\.hash\s*=\s*[""']([0-9a-fA-F]{32})[""']", RegexOptions.Compiled);
        private static readonly Regex LooseHashRegex = new Regex(
            @"hash[""']?\s*[:=]\s*[""']([0-9a-fA-F]{32})[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SourceTagRegex = new Regex(
            @"<source[^>]+src=[""']([^""']+\.(?:mp4|m3u8)[^""']*)[""'][^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex DirectMp4Regex = new Regex(
            @"https?:\\?/\\?/[^""'\s<>\\]+(?:\\/[^""'\s<>\\]+)*?\.mp4[^""'\s<>]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex HeightRegex = new Regex(@"(\d{3,4})\s*p", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly HttpGate _gate;
        private readonly EpornerApiClient _api;
        private readonly IJsonSerializer _json;
        private readonly ILogger _log;
        private readonly TtlCache _cache = new TtlCache();

        public StreamScraper(HttpGate gate, EpornerApiClient api, IJsonSerializer json, ILogger log)
        {
            _gate = gate;
            _api = api;
            _json = json;
            _log = log;
        }

        public async Task<List<StreamInfo>> GetStreamsAsync(string id, CancellationToken ct)
        {
            if (_cache.TryGet(id, out List<StreamInfo> cached)) return cached;

            var streams = new List<StreamInfo>();
            try
            {
                var pageUrls = await GetCandidatePageUrlsAsync(id, ct).ConfigureAwait(false);
                foreach (var pageUrl in pageUrls)
                {
                    string html;
                    try { html = await _gate.GetStringAsync(pageUrl, "https://" + Host + "/", ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _log.Warn("Eporner: page fetch failed ({0}): {1}", ex.Message, pageUrl);
                        continue;
                    }
                    if (string.IsNullOrEmpty(html)) continue;

                    streams = await TryXhrAsync(id, html, ct).ConfigureAwait(false);
                    if (streams.Count == 0) streams = ParseHtmlSources(html);
                    if (streams.Count > 0) break;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.ErrorException("Eporner: stream extraction failed for " + id, ex);
            }

            streams = Order(streams);
            if (streams.Count == 0)
                _log.Warn("Eporner: no stream found for video {0} (removed, or page layout changed)", id);
            else
                _cache.Set(id, streams, TimeSpan.FromMinutes(5));
            return streams;
        }

        public static string EmbedUrl(string id) => "https://" + Host + "/embed/" + id + "/";

        private async Task<List<string>> GetCandidatePageUrlsAsync(string id, CancellationToken ct)
        {
            var urls = new List<string>();
            // /video-{id}/ redirects to the slug URL, so no slug is needed
            urls.Add("https://" + Host + "/video-" + id + "/");
            try
            {
                var v = await _api.GetByIdAsync(id, ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(v?.Url) && !urls.Contains(v.Url)) urls.Add(v.Url);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { _log.Debug("Eporner: id lookup failed: {0}", ex.Message); }
            urls.Add(EmbedUrl(id));
            return urls;
        }

        private async Task<List<StreamInfo>> TryXhrAsync(string id, string html, CancellationToken ct)
        {
            var result = new List<StreamInfo>();
            var m = HashRegex.Match(html);
            if (!m.Success) m = LooseHashRegex.Match(html);
            if (!m.Success) return result;

            var url = "https://" + Host + "/xhr/video/" + id
                + "?hash=" + EncodeHash(m.Groups[1].Value)
                + "&domain=" + Host + "&fallback=false&embed=false&supportedFormats=dash,mp4"
                + "&_=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);

            try
            {
                var body = await _gate.GetStringAsync(url, "https://" + Host + "/video-" + id + "/", ct).ConfigureAwait(false);
                var resp = _json.DeserializeFromString<XhrVideoResponse>(body);
                if (resp == null) return result;
                if (resp.Available == false)
                {
                    _log.Info("Eporner: video {0} not available: {1}", id, resp.Message);
                    return result;
                }
                if (resp.Sources == null) return result;

                foreach (var kind in resp.Sources)
                    foreach (var q in kind.Value)
                    {
                        var src = q.Value?.Src;
                        if (string.IsNullOrEmpty(src)) continue;
                        var isHls = kind.Key.IndexOf("hls", StringComparison.OrdinalIgnoreCase) >= 0 || src.Contains(".m3u8");
                        var isDash = src.Contains(".mpd") || kind.Key.IndexOf("dash", StringComparison.OrdinalIgnoreCase) >= 0;
                        if (isDash) continue;
                        result.Add(new StreamInfo
                        {
                            Url = src,
                            Container = isHls ? "m3u8" : "mp4",
                            Height = ParseHeight(q.Value.LabelShort) ?? ParseHeight(q.Key) ?? ParseHeight(src) ?? 0,
                            Label = q.Key
                        });
                    }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Warn("Eporner: player XHR failed for {0}: {1}", id, ex.Message);
            }
            return result;
        }

        /// <summary>The player sends the hash as four 8-hex chunks, each in base 36 (as in yt-dlp's eporner extractor).</summary>
        public static string EncodeHash(string hex32)
        {
            var sb = new System.Text.StringBuilder();
            for (var i = 0; i < 32; i += 8)
                sb.Append(ToBase36(Convert.ToUInt32(hex32.Substring(i, 8), 16)));
            return sb.ToString();
        }

        private static string ToBase36(uint value)
        {
            const string digits = "0123456789abcdefghijklmnopqrstuvwxyz";
            if (value == 0) return "0";
            var chars = new Stack<char>();
            while (value > 0) { chars.Push(digits[(int)(value % 36)]); value /= 36; }
            return new string(chars.ToArray());
        }

        private static List<StreamInfo> ParseHtmlSources(string html)
        {
            var found = new Dictionary<string, StreamInfo>();
            foreach (Match m in SourceTagRegex.Matches(html))
                Add(found, m.Groups[1].Value);
            foreach (Match m in DirectMp4Regex.Matches(html))
                Add(found, m.Value);
            return found.Values.ToList();
        }

        private static void Add(Dictionary<string, StreamInfo> found, string raw)
        {
            var url = raw.Replace("\\/", "/").Replace("&amp;", "&");
            if (url.StartsWith("//")) url = "https:" + url;
            if (!url.StartsWith("http")) return;
            if (found.ContainsKey(url)) return;
            found[url] = new StreamInfo
            {
                Url = url,
                Container = url.Contains(".m3u8") ? "m3u8" : "mp4",
                Height = ParseHeight(url) ?? 0,
                Label = "auto"
            };
        }

        private static int? ParseHeight(string s)
        {
            if (string.IsNullOrEmpty(s)) return null;
            var m = HeightRegex.Match(s);
            return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : (int?)null;
        }

        private static List<StreamInfo> Order(List<StreamInfo> streams)
        {
            var max = Plugin.Instance?.Configuration.MaxStreamHeight ?? 0;
            var list = streams.GroupBy(s => s.Url).Select(g => g.First()).ToList();
            if (max > 0 && list.Any(s => s.Height == 0 || s.Height <= max))
                list = list.Where(s => s.Height == 0 || s.Height <= max).ToList();
            return list.OrderByDescending(s => s.Container == "mp4").ThenByDescending(s => s.Height).ToList();
        }
    }
}
