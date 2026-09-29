using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx.Logging;
using BepInExTranslator.Core;
using UnityEngine;

namespace BepInExTranslator.Services
{
    /// <summary>
    /// 运行时翻译服务：缓存命中同步替换；未命中则异步请求并回写组件。
    /// P0：异步回写前校验 expectedSource；字号相对 baseline，避免缓存命中反复缩小。
    /// </summary>
    public sealed class TranslationRuntime
    {
        private readonly ConcurrentDictionary<int, string> _lastApplied =
            new ConcurrentDictionary<int, string>();

        // 组件当前期望的原文（世代令牌）；异步完成时须仍匹配
        private readonly ConcurrentDictionary<int, string> _expectedSource =
            new ConcurrentDictionary<int, string>();

        // 首次见到组件时的原始字号，缩排始终相对此值
        private readonly ConcurrentDictionary<int, float> _baselineFontSize =
            new ConcurrentDictionary<int, float>();

        // 已对「原文→译文」做过布局的组件，避免重复缩字号
        private readonly ConcurrentDictionary<int, string> _layoutAppliedKey =
            new ConcurrentDictionary<int, string>();

        private readonly ConcurrentDictionary<int, byte> _mutating =
            new ConcurrentDictionary<int, byte>();

        public OnDemandTranslator Translator { get; }
        public PluginSettings Settings { get; }
        public ManualLogSource Log { get; }
        public FontProvider Fonts { get; }

        public TranslationRuntime(OnDemandTranslator translator, PluginSettings settings, ManualLogSource log)
        {
            Translator = translator;
            Settings = settings;
            Log = log;
            Fonts = new FontProvider(settings, log);
        }

        public bool IsMutating(object component)
        {
            return component != null && _mutating.ContainsKey(component.GetHashCode());
        }

        /// <summary>
        /// 处理一次文本变更。返回应写入组件的文本（可能仍是原文，异步补译中）。
        /// </summary>
        public string ProcessTextChange(object component, string? originalText, Action<string> applyText)
        {
            if (component == null || string.IsNullOrEmpty(originalText))
            {
                return originalText ?? string.Empty;
            }

            if (ShouldSkip(originalText!))
            {
                return originalText!;
            }

            var id = component.GetHashCode();
            // 登记本轮期望原文（使旧异步回调失效）
            _expectedSource[id] = originalText!;

            // 缓存命中：立即替换；若已是同一译文则跳过布局（防反复缩字号）
            var cached = Translator.TryResolveCached(originalText);
            if (cached != null)
            {
                if (_lastApplied.TryGetValue(id, out var last) && last == cached)
                {
                    return cached;
                }

                ApplyPresentation(component, originalText!, cached, forceLayout: false);
                _lastApplied[id] = cached;
                return cached;
            }

            // 负缓存 / 退避中：不再每帧打 API
            if (Translator.IsInCooldown(originalText!))
            {
                return originalText!;
            }

            _ = TranslateAndApplyAsync(component, originalText!, applyText);
            return originalText!;
        }

        private async Task TranslateAndApplyAsync(object component, string originalText, Action<string> applyText)
        {
            try
            {
                var translated = await Translator.TranslateAsync(originalText).ConfigureAwait(true);
                if (string.IsNullOrEmpty(translated) || translated == originalText)
                {
                    return;
                }

                void Apply()
                {
                    var id = component.GetHashCode();
                    _expectedSource.TryGetValue(id, out var pendingExpected);
                    var currentText = TryReadText(component);

                    // P0-1：过期则丢弃，不覆盖更新后的文本
                    if (!StaleWriteGuard.ShouldApply(originalText, translated, pendingExpected, currentText))
                    {
                        Log.LogDebug("Discard stale async translation apply.");
                        return;
                    }

                    _mutating[id] = 1;
                    try
                    {
                        ApplyPresentation(component, originalText, translated, forceLayout: true);
                        applyText(translated);
                        _lastApplied[id] = translated;
                    }
                    finally
                    {
                        _mutating.TryRemove(id, out _);
                    }
                }

                if (UnityMainThread.IsAvailable)
                {
                    UnityMainThread.Enqueue(Apply);
                }
                else
                {
                    Apply();
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Translate failed: {ex.Message}");
            }
        }

        private void ApplyPresentation(object component, string source, string translation, bool forceLayout)
        {
            try
            {
                Fonts.TryApplyFont(component);

                if (!Settings.AutoShrinkFontSize.Value)
                {
                    return;
                }

                var id = component.GetHashCode();
                var layoutKey = source + "\u001f" + translation;
                if (!forceLayout
                    && _layoutAppliedKey.TryGetValue(id, out var done)
                    && done == layoutKey)
                {
                    return;
                }

                var type = component.GetType();
                // P0-2：相对 baseline 缩放，绝不在已缩小字号上再叠缩
                var baseline = _baselineFontSize.GetOrAdd(id, _ => LayoutAdjuster.ReadFontSize(component, type));
                LayoutAdjuster.ApplyFromBaseline(component, type, baseline, source, translation, Settings);
                _layoutAppliedKey[id] = layoutKey;
            }
            catch (Exception ex)
            {
                Log.LogDebug($"Presentation adjust skipped: {ex.Message}");
            }
        }

        private static string? TryReadText(object component)
        {
            try
            {
                var prop = component.GetType().GetProperty("text", BindingFlags.Instance | BindingFlags.Public);
                if (prop != null && prop.CanRead)
                {
                    return prop.GetValue(component, null) as string;
                }
            }
            catch
            {
                // ignore
            }

            return null;
        }

        private static bool ShouldSkip(string text)
        {
            var trimmed = text.Trim();
            return trimmed.Length <= 1;
        }
    }

    /// <summary>简易主线程队列：由插件 Update 泵送。</summary>
    public static class UnityMainThread
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        public static bool IsAvailable { get; private set; }

        public static void Enable() => IsAvailable = true;

        public static void Enqueue(Action action)
        {
            if (action != null)
            {
                Queue.Enqueue(action);
            }
        }

        public static void Pump()
        {
            var budget = 32;
            while (budget-- > 0 && Queue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    /// <summary>按组件类型反射调整字号/换行（相对 baseline）。</summary>
    public static class LayoutAdjuster
    {
        public static void ApplyFromBaseline(
            object component,
            Type type,
            float baselineFontSize,
            string source,
            string translation,
            PluginSettings settings)
        {
            var newSize = FontSizeAdjuster.ComputeAdjustedFontSize(
                baselineFontSize,
                source,
                translation,
                settings.MinFontScale.Value,
                settings.MinFontSize.Value);

            WriteFontSize(component, type, newSize);

            if (FontSizeAdjuster.ShouldEnableWrap(source, translation))
            {
                EnableWrap(component, type);
            }
        }

        public static float ReadFontSize(object component, Type type)
        {
            var prop = type.GetProperty("fontSize", BindingFlags.Instance | BindingFlags.Public);
            if (prop != null)
            {
                var value = prop.GetValue(component, null);
                if (value is int i)
                {
                    return i;
                }

                if (value is float f)
                {
                    return f;
                }
            }

            return 16f;
        }

        private static void WriteFontSize(object component, Type type, float size)
        {
            var prop = type.GetProperty("fontSize", BindingFlags.Instance | BindingFlags.Public);
            if (prop == null || !prop.CanWrite)
            {
                return;
            }

            if (prop.PropertyType == typeof(int))
            {
                prop.SetValue(component, Mathf.Max(1, Mathf.RoundToInt(size)), null);
            }
            else if (prop.PropertyType == typeof(float))
            {
                prop.SetValue(component, size, null);
            }
        }

        private static void EnableWrap(object component, Type type)
        {
            var hOverflow = type.GetProperty("horizontalOverflow", BindingFlags.Instance | BindingFlags.Public);
            if (hOverflow != null && hOverflow.CanWrite)
            {
                hOverflow.SetValue(component, Enum.ToObject(hOverflow.PropertyType, 0), null);
            }

            var wrap = type.GetProperty("enableWordWrapping", BindingFlags.Instance | BindingFlags.Public);
            if (wrap != null && wrap.CanWrite && wrap.PropertyType == typeof(bool))
            {
                wrap.SetValue(component, true, null);
            }
        }
    }
}
