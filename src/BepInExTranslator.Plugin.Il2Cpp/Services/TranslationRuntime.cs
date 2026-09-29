using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx.Logging;
using BepInExTranslator.Core;

namespace BepInExTranslator.Services
{
    /// <summary>
    /// IL2CPP 运行时翻译服务（与 Mono 版 P0/P1 行为对齐，纯反射）。
    /// </summary>
    public sealed class TranslationRuntime
    {
        private readonly ConcurrentDictionary<int, string> _lastApplied = new();
        private readonly ConcurrentDictionary<int, string> _expectedSource = new();
        private readonly ConcurrentDictionary<int, float> _baselineFontSize = new();
        private readonly ConcurrentDictionary<int, string> _layoutAppliedKey = new();
        private readonly ConcurrentDictionary<int, byte> _mutating = new();

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

        public bool IsMutating(object component) =>
            component != null && _mutating.ContainsKey(component.GetHashCode());

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
            _expectedSource[id] = originalText!;

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
                var translated = await Translator.TranslateAsync(originalText).ConfigureAwait(false);
                if (string.IsNullOrEmpty(translated) || translated == originalText)
                {
                    return;
                }

                void Apply()
                {
                    var id = component.GetHashCode();
                    _expectedSource.TryGetValue(id, out var pendingExpected);
                    var currentText = TryReadText(component);

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
                    try
                    {
                        Apply();
                    }
                    catch (Exception ex)
                    {
                        Log.LogDebug("Deferred UI apply failed (cache saved): " + ex.Message);
                    }
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
                return prop?.GetValue(component) as string;
            }
            catch
            {
                return null;
            }
        }

        private static bool ShouldSkip(string text) => text.Trim().Length <= 1;
    }

    public static class UnityMainThread
    {
        private static readonly ConcurrentQueue<Action> Queue = new();
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
            var value = prop?.GetValue(component);
            if (value is int i)
            {
                return i;
            }

            if (value is float f)
            {
                return f;
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
                prop.SetValue(component, Math.Max(1, (int)Math.Round(size)));
            }
            else if (prop.PropertyType == typeof(float))
            {
                prop.SetValue(component, size);
            }
        }

        private static void EnableWrap(object component, Type type)
        {
            var hOverflow = type.GetProperty("horizontalOverflow", BindingFlags.Instance | BindingFlags.Public);
            if (hOverflow != null && hOverflow.CanWrite)
            {
                hOverflow.SetValue(component, Enum.ToObject(hOverflow.PropertyType, 0));
            }

            var wrap = type.GetProperty("enableWordWrapping", BindingFlags.Instance | BindingFlags.Public);
            if (wrap != null && wrap.CanWrite && wrap.PropertyType == typeof(bool))
            {
                wrap.SetValue(component, true);
            }
        }
    }
}
