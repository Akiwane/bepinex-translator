using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInExTranslator.Core;
using BepInExTranslator.Core.Backends;
using BepInExTranslator.Hooks;
using BepInExTranslator.Services;
using HarmonyLib;

namespace BepInExTranslator
{
    /// <summary>
    /// BepInEx 5（Unity Mono）通用翻译插件入口。
    /// 配置字段与 config/Translator.cfg.example 契约对齐。
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed partial class TranslatorPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.akiwane.bepinextranslator";
        public const string PluginName = "BepInEx Translator";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log = null!;
        internal static PluginSettings Settings = null!;
        internal static TranslationRuntime Runtime = null!;

        private Harmony? _harmony;

        private void Awake()
        {
            Log = Logger;
            Settings = PluginSettings.Bind(Config);

            // 产物路径：General.ProductJsonPath（默认 BepInEx/plugins/Translator/translations.json）
            var cachePath = ResolveProductJsonPath(Settings.ProductJsonPath.Value);
            var cache = new TranslationCache(cachePath);
            try
            {
                cache.Load();
                Log.LogInfo($"Loaded translation product: {cache.Count} entries from {cachePath}");
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to load translation product: {ex.Message}");
            }

            ITranslationBackend backend;
            try
            {
                backend = BackendFactory.Create(Settings);
                Log.LogInfo($"Translation backend: {backend.Name} ({Settings.BackendType.Value})");
            }
            catch (Exception ex)
            {
                Log.LogError($"Backend init failed, falling back to passthrough: {ex.Message}");
                backend = new PassthroughBackend();
            }

            var translator = new OnDemandTranslator(cache, backend, Settings.TargetLanguage.Value);
            Runtime = new TranslationRuntime(translator, Settings, Log);

            _harmony = new Harmony(PluginGuid);
            TextHookInstaller.Apply(_harmony, Log);

            Log.LogInfo($"{PluginName} {PluginVersion} ready. Target={Settings.TargetLanguage.Value}");
        }

        private void OnDestroy()
        {
            try
            {
                Runtime?.Translator.Cache.SaveIfDirty();
            }
            catch
            {
                // ignore shutdown errors
            }

            _harmony?.UnpatchSelf();
        }

        /// <summary>
        /// 解析产物路径：绝对路径原样；相对路径依次相对 BepInEx 根、游戏根。
        /// </summary>
        internal static string ResolveProductJsonPath(string? configured)
        {
            if (string.IsNullOrWhiteSpace(configured))
            {
                configured = "BepInEx/plugins/Translator/translations.json";
            }

            if (Path.IsPathRooted(configured))
            {
                EnsureParentDirectory(configured);
                return configured;
            }

            // 相对 BepInEx 根
            var fromBepInEx = Path.GetFullPath(Path.Combine(Paths.BepInExRootPath, configured));
            // 若配置已含 BepInEx/ 前缀，相对游戏根更自然
            var fromGame = Path.GetFullPath(Path.Combine(Paths.GameRootPath, configured));

            string chosen;
            if (configured.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)
                || configured.StartsWith("BepInEx/", StringComparison.OrdinalIgnoreCase)
                || configured.StartsWith("BepInEx\\", StringComparison.OrdinalIgnoreCase))
            {
                chosen = fromGame;
            }
            else
            {
                chosen = fromBepInEx;
            }

            EnsureParentDirectory(chosen);
            return chosen;
        }

        private static void EnsureParentDirectory(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }
    }

    /// <summary>无后端时的直通实现（仅缓存命中可用）。</summary>
    internal sealed class PassthroughBackend : ITranslationBackend
    {
        public string Name => "Passthrough";

        public System.Threading.Tasks.Task<string> TranslateAsync(
            string source,
            string targetLang,
            System.Threading.CancellationToken cancellationToken)
        {
            return System.Threading.Tasks.Task.FromResult(string.Empty);
        }
    }
}
