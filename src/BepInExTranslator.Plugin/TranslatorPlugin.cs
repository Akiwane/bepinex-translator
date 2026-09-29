using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
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

            // 产物默认：BepInEx/plugins/BepInExTranslator/translations.json
            // 也可在配置中改到 BepInEx/config 下
            var cachePath = ResolveCachePath(Settings);
            var cache = new TranslationCache(cachePath);
            try
            {
                cache.Load();
                Log.LogInfo($"Loaded translation cache: {cache.Count} entries from {cachePath}");
            }
            catch (Exception ex)
            {
                Log.LogError($"Failed to load translation cache: {ex.Message}");
            }

            ITranslationBackend backend;
            try
            {
                backend = BackendFactory.Create(Settings);
                Log.LogInfo($"Translation backend: {backend.Name}");
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

        private static string ResolveCachePath(PluginSettings settings)
        {
            var configured = settings.CacheFilePath.Value;
            if (!string.IsNullOrWhiteSpace(configured))
            {
                if (Path.IsPathRooted(configured))
                {
                    return configured;
                }

                return Path.GetFullPath(Path.Combine(Paths.BepInExRootPath, configured));
            }

            // 默认写到插件目录，便于与 DLL 一起分发/人工精翻
            var pluginDir = Path.Combine(Paths.PluginPath, "BepInExTranslator");
            Directory.CreateDirectory(pluginDir);
            return Path.Combine(pluginDir, "translations.json");
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
