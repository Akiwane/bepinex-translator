using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BepInExTranslator.Core.Backends;

namespace BepInExTranslator.Core
{
    /// <summary>
    /// 按需翻译编排：先查缓存（非空译文直接返回），否则调后端并写回缓存。
    /// </summary>
    public sealed class OnDemandTranslator
    {
        private readonly TranslationCache _cache;
        private readonly ITranslationBackend _backend;
        private readonly string _targetLang;
        private readonly object _inflightLock = new object();
        private readonly HashSet<string> _inflight = new HashSet<string>(StringComparer.Ordinal);

        public OnDemandTranslator(TranslationCache cache, ITranslationBackend backend, string targetLang)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _targetLang = string.IsNullOrWhiteSpace(targetLang) ? "zh-CN" : targetLang;
        }

        public string TargetLang => _targetLang;
        public TranslationCache Cache => _cache;
        public ITranslationBackend Backend => _backend;

        /// <summary>
        /// 同步解析：有缓存译文立即返回；否则返回 null。
        /// </summary>
        public string? TryResolveCached(string? source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source;
            }

            if (_cache.TryGetTranslation(source, out var translation))
            {
                return translation;
            }

            return null;
        }

        /// <summary>
        /// 按需翻译：缓存命中且非空则不调 API；否则请求后端并持久化。
        /// </summary>
        public async Task<string> TranslateAsync(string source, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(source))
            {
                return source ?? string.Empty;
            }

            // 已译：直接用，不调接口
            if (_cache.TryGetTranslation(source, out var cached))
            {
                return cached;
            }

            var hash = TextHasher.ComputeHash(source);
            var weOwnRequest = false;

            // 尝试成为该原文的唯一请求者；否则等待飞行中结果
            lock (_inflightLock)
            {
                weOwnRequest = _inflight.Add(hash);
            }

            if (!weOwnRequest)
            {
                var waited = await WaitForInflightAsync(source, hash, cancellationToken).ConfigureAwait(false);
                if (waited != null)
                {
                    return waited;
                }

                // 飞行请求结束但仍无译文：尝试自己再请求一次
                lock (_inflightLock)
                {
                    weOwnRequest = _inflight.Add(hash);
                }

                if (!weOwnRequest)
                {
                    return source;
                }
            }

            try
            {
                // 双重检查：等待期间可能已被写入
                if (_cache.TryGetTranslation(source, out cached))
                {
                    return cached;
                }

                var translated = await _backend.TranslateAsync(source, _targetLang, cancellationToken)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(translated))
                {
                    _cache.EnsurePlaceholder(source);
                    _cache.SaveIfDirty();
                    return source;
                }

                var trimmed = translated.Trim();
                _cache.Upsert(source, trimmed);
                _cache.SaveIfDirty();
                return trimmed;
            }
            finally
            {
                lock (_inflightLock)
                {
                    _inflight.Remove(hash);
                }
            }
        }

        private async Task<string?> WaitForInflightAsync(
            string source,
            string hash,
            CancellationToken cancellationToken)
        {
            for (var i = 0; i < 40; i++)
            {
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);

                if (_cache.TryGetTranslation(source, out var cached))
                {
                    return cached;
                }

                lock (_inflightLock)
                {
                    if (!_inflight.Contains(hash))
                    {
                        break;
                    }
                }
            }

            if (_cache.TryGetTranslation(source, out var finalCached))
            {
                return finalCached;
            }

            return null;
        }
    }
}
