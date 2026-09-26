using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Model.Logging;

namespace Emby.Plugins.Eporner.Api
{
    public class HttpGate
    {
        public const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36";

        private readonly IHttpClient _http;
        private readonly ILogger _log;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private DateTime _last = DateTime.MinValue;

        public HttpGate(IHttpClient http, ILogger log)
        {
            _http = http;
            _log = log;
        }

        public async Task<string> GetStringAsync(string url, string referer, CancellationToken ct)
        {
            for (var attempt = 1; ; attempt++)
            {
                await WaitTurnAsync(ct).ConfigureAwait(false);
                try
                {
                    var options = new HttpRequestOptions
                    {
                        Url = url,
                        CancellationToken = ct,
                        UserAgent = UserAgent,
                        AcceptHeader = "application/json, text/html, */*",
                        TimeoutMs = 20000
                    };
                    if (!string.IsNullOrEmpty(referer)) options.RequestHeaders["Referer"] = referer;

                    using (var stream = await _http.Get(options).ConfigureAwait(false))
                    using (var reader = new StreamReader(stream))
                    {
                        return await reader.ReadToEndAsync().ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (attempt < 2)
                {
                    _log.Warn("Eporner request failed ({0}), retrying: {1}", ex.Message, url);
                    await Task.Delay(1500, ct).ConfigureAwait(false);
                }
            }
        }

        private async Task WaitTurnAsync(CancellationToken ct)
        {
            await _lock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var interval = Math.Max(0, Plugin.Instance?.Configuration.MinRequestIntervalMs ?? 500);
                var wait = _last.AddMilliseconds(interval) - DateTime.UtcNow;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct).ConfigureAwait(false);
                _last = DateTime.UtcNow;
            }
            finally
            {
                _lock.Release();
            }
        }
    }
}
