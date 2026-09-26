using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Emby.Plugins.Eporner.Api;
using Emby.Plugins.Eporner.Configuration;
using Emby.Plugins.Eporner.Models;
using Emby.Plugins.Eporner.Scraping;
using Emby.Plugins.Eporner.Services;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Drawing;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;
using MediaBrowser.Model.Serialization;

namespace Emby.Plugins.Eporner.Channels
{
    public class EpornerChannel : IChannel, IHasChannelFeatures, IRequiresMediaInfoCallback, ISupportsMediaProbe
    {
        private const string VideoPrefix = "video:";
        private const string QueryPrefix = "q:";
        private const string CategoryPrefix = "cat:";

        private readonly EpornerApiClient _api;
        private readonly StreamScraper _scraper;
        private readonly CategoryScraper _categories;
        private readonly ILogger _log;

        public EpornerChannel(IHttpClient httpClient, IJsonSerializer json, ILogManager logManager)
        {
            var hub = EpornerHub.GetOrCreate(httpClient, json, logManager);
            _log = hub.Log;
            _api = hub.Api;
            _scraper = hub.Streams;
            _categories = hub.Categories;
        }

        public string Name => "Eporner";
        public string Description => "Unofficial channel for ePorner videos, not affiliated with ePorner";
        public ChannelParentalRating ParentalRating => ChannelParentalRating.Adult;

        // false: Emby lists the channel as a single entry instead of every category
        public ChannelFeatures Features => new ChannelFeatures { ShowRootFoldersAtTopLevel = false };

        public IEnumerable<ImageType> GetSupportedChannelImages() => new List<ImageType> { ImageType.Primary, ImageType.Thumb };

        public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        {
            var stream = GetType().Assembly.GetManifestResourceStream(GetType().Namespace.Replace(".Channels", "") + ".Resources.logo.png");
            return Task.FromResult(new DynamicImageResponse { Stream = stream, Format = ImageFormat.Png });
        }

        public async Task<ChannelItemResult> GetChannelItems(InternalChannelItemQuery query, CancellationToken cancellationToken)
        {
            var folder = query.FolderId;
            _log.Debug("Eporner: GetChannelItems folder={0} start={1} limit={2} sortBy={3} desc={4}", folder, query.StartIndex, query.Limit, query.SortBy, query.SortDescending);
            try
            {
                if (string.IsNullOrEmpty(folder)) return await CategoryFoldersAsync(cancellationToken).ConfigureAwait(false);
                if (folder.StartsWith(CategoryPrefix, StringComparison.Ordinal))
                    return await LoadVideosAsync(folder, folder.Substring(CategoryPrefix.Length), query, cancellationToken, CategoryVideoCount(folder.Substring(CategoryPrefix.Length))).ConfigureAwait(false);
                if (folder.StartsWith(QueryPrefix, StringComparison.Ordinal))
                    return await LoadVideosAsync(folder, folder.Substring(QueryPrefix.Length), query, cancellationToken, CategoryVideoCount(null)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // API down or bad JSON: show an empty folder instead of breaking the channel view
                _log.ErrorException("Eporner: failed to load folder '" + folder + "'", ex);
                return new ChannelItemResult { Items = new List<ChannelItemInfo>(), TotalRecordCount = 0 };
            }

            // folder ids from older plugin versions end up here
            _log.Warn("Eporner: unknown folder id '{0}'", folder);
            return new ChannelItemResult { Items = new List<ChannelItemInfo>() };
        }

        private async Task<ChannelItemResult> CategoryFoldersAsync(CancellationToken ct)
        {
            var items = new List<ChannelItemInfo>();
            var custom = (Plugin.Instance.Configuration.CustomQueries ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0);
            items.AddRange(custom.Select(x => Folder(QueryPrefix + x, Capitalize(x))));

            var cats = await _categories.GetCategoriesAsync(ct).ConfigureAwait(false);
            if (cats.Count > 0)
            {
                var settings = (Plugin.Instance.Configuration.CategorySettings ?? new CategorySetting[0])
                    .GroupBy(x => x.Slug).ToDictionary(g => g.Key, g => g.First());
                var hideTrans = Plugin.Instance.Configuration.TransMode == 0;
                items.AddRange(cats.Where(c => settings.TryGetValue(c.Slug, out var st) && st.Enabled && !(hideTrans && c.Slug == "shemale")).Select(c =>
                {
                    var f = Folder(CategoryPrefix + c.Slug, c.Name);
                    f.ImageUrl = c.ImageUrl;
                    return f;
                }));
            }

            items = items.GroupBy(i => i.Id).Select(g => g.First()).ToList();
            return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
        }

        private static int CategoryVideoCount(string slug)
        {
            var cfg = Plugin.Instance.Configuration;
            var own = slug == null ? 0 : cfg.CategorySettings?.FirstOrDefault(x => x.Slug == slug)?.Count ?? 0;
            return Math.Min(1000, Math.Max(1, own > 0 ? own : cfg.VideosPerCategory));
        }

        private static ChannelItemInfo Folder(string id, string name) => new ChannelItemInfo
        {
            Id = id,
            Name = name,
            Type = ChannelItemType.Folder,
            FolderType = ChannelFolderType.Container
        };

        // upper bound for API requests per folder while backfilling
        private const int MaxRequestsPerFolder = 20;

        /// <summary>
        /// Emby requests each folder once without StartIndex/Limit and caches the whole list, so all
        /// videos are loaded here. If de-duplication hides videos, more API pages are fetched until the
        /// limit is reached, the API runs dry or MaxRequestsPerFolder is hit.
        /// </summary>
        private async Task<ChannelItemResult> LoadVideosAsync(string folderId, string searchQuery, InternalChannelItemQuery q, CancellationToken ct, int maxVideos)
        {
            var cfg = Plugin.Instance.Configuration;
            const int perPage = 48;
            var videos = new List<EpVideo>();
            var total = 0;

            if (q.Limit.HasValue || q.StartIndex.HasValue)
            {
                var start = q.StartIndex ?? 0;
                var resp = await _api.SearchAsync(searchQuery, null, start / perPage + 1, perPage, ct).ConfigureAwait(false);
                total = resp.TotalCount;
                videos = FilterTrans(resp.Videos).Skip(start % perPage).ToList();
                if (q.Limit.HasValue) videos = videos.Take(q.Limit.Value).ToList();
            }
            else
            {
                var pageSize = Math.Min(1000, Math.Max(1, maxVideos));
                var seen = new HashSet<string>();
                for (var page = 1; page <= MaxRequestsPerFolder && videos.Count < maxVideos; page++)
                {
                    var resp = await _api.SearchAsync(searchQuery, null, page, pageSize, ct).ConfigureAwait(false);
                    total = resp.TotalCount;
                    var batch = FilterTrans(resp.Videos.Where(v => seen.Add(v.Id))).ToList();
                    videos.AddRange(cfg.DeduplicateAcrossFolders
                        ? ClaimVideos(folderId, batch, maxVideos - videos.Count)
                        : batch.Take(maxVideos - videos.Count));
                    if (page >= resp.TotalPages || resp.Videos.Count == 0) break;
                }

                if (cfg.DeduplicateAcrossFolders) ReleaseStaleClaims(folderId, videos);
            }

            return new ChannelItemResult
            {
                Items = videos.Select(ToItem).ToList(),
                TotalRecordCount = Math.Max(total, videos.Count)
            };
        }

        // the API has no trans parameter, so trans content is recognised by words in title and keywords
        private static readonly Regex TransRegex = new Regex(
            @"\b(shemales?|she-?males?|trans|transgender|transsexual|trannys?|ladyboys?|lady-?boys?|t-?girls?|newhalf)\b",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static bool IsTrans(EpVideo v) => TransRegex.IsMatch((v.Title ?? "") + " , " + (v.Keywords ?? ""));

        private static IEnumerable<EpVideo> FilterTrans(IEnumerable<EpVideo> videos)
        {
            switch (Plugin.Instance.Configuration.TransMode)
            {
                case 0: return videos.Where(v => !IsTrans(v));
                case 2: return videos.Where(IsTrans);
                default: return videos;
            }
        }

        // Emby keeps one item per (folder, video), which makes a video appear several times in the channel's
        // "Latest" row. Each video is shown only in the first folder that claims it (in memory, rebuilt on refresh).
        private readonly object _ownerLock = new object();
        private readonly Dictionary<string, string> _owners = new Dictionary<string, string>();

        private List<EpVideo> ClaimVideos(string folderId, List<EpVideo> batch, int limit)
        {
            var kept = new List<EpVideo>();
            lock (_ownerLock)
            {
                foreach (var v in batch)
                {
                    if (kept.Count >= limit) break;
                    if (_owners.TryGetValue(v.Id, out var owner) && owner != folderId)
                        continue;
                    _owners[v.Id] = folderId;
                    kept.Add(v);
                }
            }
            return kept;
        }

        private void ReleaseStaleClaims(string folderId, List<EpVideo> current)
        {
            var ids = new HashSet<string>(current.Select(v => v.Id));
            lock (_ownerLock)
            {
                foreach (var id in _owners.Where(o => o.Value == folderId && !ids.Contains(o.Key)).Select(o => o.Key).ToList())
                    _owners.Remove(id);
            }
        }

        private static ChannelItemInfo ToItem(EpVideo v)
        {
            var item = new ChannelItemInfo
            {
                Id = VideoPrefix + v.Id,
                Name = FixMojibake(v.Title),
                Type = ChannelItemType.Media,
                MediaType = ChannelMediaType.Video,
                ContentType = ChannelMediaContentType.Clip,
                ImageUrl = PickThumb(v),
                Overview = BuildOverview(v),
                RunTimeTicks = v.LengthSec > 0 ? TimeSpan.FromSeconds(v.LengthSec).Ticks : (long?)null,
                Tags = SplitKeywords(FixMojibake(v.Keywords)),
                };
            item.ProviderIds = new ProviderIdDictionary();
            item.ProviderIds["Eporner"] = v.Id;

            if (DateTime.TryParse(v.Added, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var addedUtc))
            {
                var added = new DateTimeOffset(addedUtc, TimeSpan.Zero);
                item.DateCreated = added;
                item.PremiereDate = added;
                item.ProductionYear = addedUtc.Year;
            }

            // Eporner rates on a 0-5 scale; Emby expects 0-10.
            if (float.TryParse(v.Rate, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate) && rate > 0)
                item.CommunityRating = rate <= 5 ? rate * 2 : Math.Min(10, rate / 10);

            return item;
        }

        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        /// <summary>The API double-encodes non-ASCII text (UTF-8 bytes as Latin-1 chars); undoes that when the string is valid UTF-8 that way.</summary>
        internal static string FixMojibake(string s)
        {
            if (string.IsNullOrEmpty(s) || s.All(c => c < 0x80) || s.Any(c => c > 0xFF)) return s;
            try { return StrictUtf8.GetString(Latin1.GetBytes(s)); }
            catch (ArgumentException) { return s; }
        }

        private static string PickThumb(EpVideo v)
        {
            var preferred = Plugin.Instance.Configuration.ThumbSize ?? "big";
            return v.Thumbs?.FirstOrDefault(t => t.Size == preferred)?.Src
                   ?? v.DefaultThumb?.Src
                   ?? v.Thumbs?.OrderByDescending(t => t.Width).FirstOrDefault()?.Src;
        }

        private static string BuildOverview(EpVideo v)
        {
            var parts = new List<string>();
            if (v.Views > 0) parts.Add(v.Views.ToString("N0", CultureInfo.InvariantCulture) + " views");
            if (!string.IsNullOrEmpty(v.Keywords)) parts.Add("Keywords: " + FixMojibake(v.Keywords));
            return string.Join("\n", parts);
        }

        private static List<string> SplitKeywords(string kw) =>
            string.IsNullOrWhiteSpace(kw)
                ? new List<string>()
                : kw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).Take(30).ToList();

        private static string Capitalize(string s) =>
            CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s);

        public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(string id, CancellationToken cancellationToken)
        {
            var videoId = id.StartsWith(VideoPrefix, StringComparison.Ordinal) ? id.Substring(VideoPrefix.Length) : id;
            var streams = await _scraper.GetStreamsAsync(videoId, cancellationToken).ConfigureAwait(false);
            var sources = new List<MediaSourceInfo>();

            foreach (var s in streams)
            {
                var isHls = s.Container == "m3u8";
                sources.Add(new MediaSourceInfo
                {
                    Id = videoId + "_" + (s.Height > 0 ? s.Height + "p" : "auto") + "_" + s.Container,
                    Name = (s.Height > 0 ? s.Height + "p" : s.Label) + (isHls ? " (HLS)" : ""),
                    Path = s.Url,
                    Protocol = MediaProtocol.Http,
                    Container = s.Container,
                    IsRemote = true,
                    // links are bound to the requesting IP (the server), so clients must not fetch them directly
                    SupportsDirectPlay = false,
                    SupportsDirectStream = !isHls,
                    SupportsTranscoding = true,
                    RequiredHttpHeaders = new Dictionary<string, string>
                    {
                        { "Referer", "https://www.eporner.com/" },
                        { "User-Agent", HttpGate.UserAgent }
                    }
                    // no MediaStreams on purpose: ISupportsMediaProbe lets Emby probe the URL (real codec and bitrate)
                });
            }

            if (sources.Count == 0 && Plugin.Instance.Configuration.AllowEmbedFallback)
            {
                _log.Warn("Eporner: falling back to embed URL for {0} (not playable in Emby)", videoId);
                sources.Add(new MediaSourceInfo
                {
                    Id = videoId + "_embed",
                    Name = "Embed (fallback)",
                    Path = StreamScraper.EmbedUrl(videoId),
                    Protocol = MediaProtocol.Http,
                    IsRemote = true,
                    SupportsDirectPlay = false,
                    SupportsDirectStream = false,
                    SupportsTranscoding = false
                });
            }

            return sources;
        }
    }
}
