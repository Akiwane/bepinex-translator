using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BepInExTranslator.Core.Backends;

namespace BepInExTranslator.Core
{
    /// <summary>
    /// 按需翻译编排：缓存命中跳过 API；失败/空译文进入负缓存 TTL；异常指数退避。
    /// </summary>
    public sealed class OnDemandTranslator
    {
        public static readonly TimeSpan DefaultNegativeTtl = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan DefaultInitialBackoff = TimeSpan.FromSeconds(2);
        public static readonly TimeSpan DefaultMaxBackoff = TimeSpan.FromMinutes(5);

        private readonly TranslationCache _cache;
        private readonly ITranslationBackend _backend;
        private readonly string _targetLang;
        private readonly TimeSpan _negativeTtl;
        private readonly TimeSpan _initialBackoff;
        private readonly TimeSpan _maxBackoff;
        private readonly Func<DateTimeOffset> _utcNow;

        private readonly object _inflightLock = new object();
        private readonly HashSet<string> _inflight = new HashSet<string>(StringComparer.Ordinal);

        // hash → 负缓存过期时刻（空译文 / 失败，避免每帧打 API）
        private readonly Dictionary<string, DateTimeOffset> _negativeUntil =
            new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);

        // hash → 连续失败次数（用于指数退避）
        private readonly Dictionary<string, int> _failureCounts =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public OnDemandTranslator(
            TranslationCache cache,
            ITranslationBackend backend,
            string targetLang,
            TimeSpan? negativeTtl = null,
            TimeSpan? initialBackoff = null,
            TimeSpan? maxBackoff = null,
            Func<DateTimeOffset>? utcNow = null)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _targetLang = string.IsNullOrWhiteSpace(targetLang) ? "zh-CN" : targetLang;
            _negativeTtl = negativeTtl ?? DefaultNegativeTtl;
            _initialBackoff = initialBackoff ?? DefaultInitialBackoff;
            _maxBackoff = maxBackoff ?? DefaultMaxBackoff;
            _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
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

        /// <summary>该原文是否处于负缓存 / 退避冷却中（不应再打 API）。</summary>
        public bool IsInCooldown(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return false;
            }

            var hash = TextHasher.ComputeHash(source);
            lock (_inflightLock)
            {
                return IsInCooldownUnlocked(hash);
            }
        }

        /// <summary>
        /// 按需翻译：缓存命中且非空则不调 API；冷却中直接回原文；否则请求并持久化。
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

            // 负缓存 / 异常退避：TTL 内不再请求
            lock (_inflightLock)
            {
                if (IsInCooldownUnlocked(hash))
                {
                    return source;
                }
            }

            var weOwnRequest = false;
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

                lock (_inflightLock)
                {
                    if (IsInCooldownUnlocked(hash))
                    {
                        return source;
                    }

                    weOwnRequest = _inflight.Add(hash);
                }

                if (!weOwnRequest)
                {
                    return source;
                }
            }

            try
            {
                if (_cache.TryGetTranslation(source, out cached))
                {
                    ClearFailure(hash);
                    return cached;
                }

                string translated;
                try
                {
                    translated = await _backend.TranslateAsync(source, _targetLang, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // 异常：指数退避，不持久化空译文为「已译」
                    RegisterFailure(hash, useExponentialBackoff: true);
                    throw;
                }

                if (string.IsNullOrWhiteSpace(translated))
                {
                    // 空译文：占位写入产物 + 负缓存 TTL，避免每帧重打
                    _cache.EnsurePlaceholder(source);
                    _cache.SaveIfDirty();
                    RegisterFailure(hash, useExponentialBackoff: false);
                    return source;
                }

                var trimmed = translated.Trim();
                _cache.Upsert(source, trimmed);
                _cache.SaveIfDirty();
                ClearFailure(hash);
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

        private bool IsInCooldownUnlocked(string hash)
        {
            if (!_negativeUntil.TryGetValue(hash, out var until))
            {
                return false;
            }

            if (_utcNow() < until)
            {
                return true;
            }

            _negativeUntil.Remove(hash);
            return false;
        }

        private void RegisterFailure(string hash, bool useExponentialBackoff)
        {
            lock (_inflightLock)
            {
                var count = 0;
                _failureCounts.TryGetValue(hash, out count);
                count++;
                _failureCounts[hash] = count;

                TimeSpan delay;
                if (useExponentialBackoff)
                {
                    // 2s, 4s, 8s … 封顶 MaxBackoff
                    var ticks = _initialBackoff.Ticks * (1L << Math.Min(count - 1, 8));
                    delay = TimeSpan.FromTicks(Math.Min(ticks, _maxBackoff.Ticks));
                }
                else
                {
                    delay = _negativeTtl;
                }

                _negativeUntil[hash] = _utcNow() + delay;
            }
        }

        private void ClearFailure(string hash)
        {
            lock (_inflightLock)
            {
                ClearFailureUnlocked(hash);
            }
        }

        private void ClearFailureUnlocked(string hash)
        {
            _negativeUntil.Remove(hash);
            _failureCounts.Remove(hash);
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
