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
    /// </summary>
    public sealed class TranslationRuntime
    {
        private readonly ConcurrentDictionary<int, string> _lastApplied =
            new ConcurrentDictionary<int, string>();

        // 防止我们自己 set_text 触发递归
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

            // 忽略纯空白 / 纯数字等无翻译价值文本
            if (ShouldSkip(originalText))
            {
                return originalText;
            }

            var id = component.GetHashCode();

            // 缓存命中：立即替换
            var cached = Translator.TryResolveCached(originalText);
            if (cached != null)
            {
                ApplyPresentation(component, originalText, cached);
                _lastApplied[id] = cached;
                return cached;
            }

            // 未命中：先显示原文，后台翻译后再回写
            _ = TranslateAndApplyAsync(component, originalText, applyText);
            return originalText;
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

                // 回到主线程：Unity API 非线程安全。BepInEx Mono 上 ConfigureAwait(true) 不一定回到 Unity 线程，
                // 因此用主线程调度器（若可用）或直接尝试；失败则仅写缓存，下次加载生效。
                void Apply()
                {
                    var id = component.GetHashCode();
                    _mutating[id] = 1;
                    try
                    {
                        ApplyPresentation(component, originalText, translated);
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

        private void ApplyPresentation(object component, string source, string translation)
        {
            try
            {
                Fonts.TryApplyFont(component);

                if (!Settings.AutoResizeFont.Value)
                {
                    return;
                }

                LayoutAdjuster.Apply(component, source, translation, Settings);
            }
            catch (Exception ex)
            {
                Log.LogDebug($"Presentation adjust skipped: {ex.Message}");
            }
        }

        private static bool ShouldSkip(string text)
        {
            var trimmed = text.Trim();
            if (trimmed.Length == 0)
            {
                return true;
            }

            // 过短且无字母：多为符号
            if (trimmed.Length <= 1)
            {
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// 简易主线程队列：由插件 Update 泵送。
    /// </summary>
    public static class UnityMainThread
    {
        private static readonly ConcurrentQueue<Action> Queue = new ConcurrentQueue<Action>();
        public static bool IsAvailable { get; private set; }

        public static void Enable()
        {
            IsAvailable = true;
        }

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
                    // ignore per-item errors
                }
            }
        }
    }

    /// <summary>按组件类型反射调整字号/换行。</summary>
    public static class LayoutAdjuster
    {
        public static void Apply(object component, string source, string translation, PluginSettings settings)
        {
            var type = component.GetType();
            var newSize = FontSizeAdjuster.ComputeAdjustedFontSize(
                ReadFontSize(component, type),
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

        private static float ReadFontSize(object component, Type type)
        {
            // UGUI Text.fontSize (int), TMP fontSize (float), TextMesh.fontSize (int)
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
            // UGUI: horizontalOverflow = Wrap (0), verticalOverflow = Overflow
            var hOverflow = type.GetProperty("horizontalOverflow", BindingFlags.Instance | BindingFlags.Public);
            if (hOverflow != null && hOverflow.CanWrite)
            {
                // HorizontalWrapMode.Wrap = 0
                hOverflow.SetValue(component, Enum.ToObject(hOverflow.PropertyType, 0), null);
            }

            // TMP: enableWordWrapping = true
            var wrap = type.GetProperty("enableWordWrapping", BindingFlags.Instance | BindingFlags.Public);
            if (wrap != null && wrap.CanWrite && wrap.PropertyType == typeof(bool))
            {
                wrap.SetValue(component, true, null);
            }

            // TextMesh: 无标准 wrap，跳过
        }
    }
}
