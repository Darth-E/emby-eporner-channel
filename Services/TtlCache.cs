using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Emby.Plugins.Eporner.Services
{
    /// <summary>In-memory cache with per-entry expiry (MemoryCache is not part of netstandard2.0).</summary>
    public class TtlCache
    {
        private readonly ConcurrentDictionary<string, (DateTime Expires, object Value)> _items =
            new ConcurrentDictionary<string, (DateTime, object)>();

        private const int MaxEntries = 500;

        public bool TryGet<T>(string key, out T value)
        {
            if (_items.TryGetValue(key, out var e) && e.Expires > DateTime.UtcNow && e.Value is T t)
            {
                value = t;
                return true;
            }
            value = default(T);
            return false;
        }

        public void Set(string key, object value, TimeSpan ttl)
        {
            if (ttl <= TimeSpan.Zero) return;
            if (_items.Count > MaxEntries) Prune();
            _items[key] = (DateTime.UtcNow + ttl, value);
        }

        public void Remove(string key) => _items.TryRemove(key, out _);

        public void Clear() => _items.Clear();

        private void Prune()
        {
            var now = DateTime.UtcNow;
            foreach (var k in _items.Where(kv => kv.Value.Expires <= now).Select(kv => kv.Key).ToList())
                _items.TryRemove(k, out _);
            if (_items.Count > MaxEntries)
                foreach (var k in _items.OrderBy(kv => kv.Value.Expires).Take(MaxEntries / 2).Select(kv => kv.Key).ToList())
                    _items.TryRemove(k, out _);
        }
    }
}
