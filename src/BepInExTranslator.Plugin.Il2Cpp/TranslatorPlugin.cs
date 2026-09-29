using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BepInExTranslator.Core;
using BepInExTranslator.Core.Backends;
using BepInExTranslator.Hooks;
using BepInExTranslator.Services;
using HarmonyLib;

namespace BepInExTranslator
{
    /// <summary>
    /// BepInEx 6 Unity IL2CPP 入口。配置契约与 Mono 版一致（config/Translator.cfg.example）。
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public sealed class TranslatorPlugin : BasePlugin
    {
        public const string PluginGuid = "com.akiwane.bepinextranslator";
        public const string PluginName = "BepInEx Translator";
        public const string PluginVersion = "1.0.0";

        internal static new ManualLogSource Log = null!;
        internal static PluginSettings Settings = null!;
        internal static TranslationRuntime Runtime = null!;

        private Harmony? _harmony;

        public override void Load()
        {
            Log = base.Log;
            Settings = PluginSettings.Bind(Config);

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

            // Hook 安装会强制加载 interop；主线程泵依赖同一批类型
            TextHookInstaller.Apply(_harmony, Log);

            try
            {
                MainThreadPumpBootstrap.TryEnable(_harmony, Log);
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Main thread pump unavailable: {ex.Message}. Translations still persist to product JSON.");
            }

            Log.LogInfo($"{PluginName} {PluginVersion} (IL2CPP) ready. Target={Settings.TargetLanguage.Value}");
        }

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

            var fromBepInEx = Path.GetFullPath(Path.Combine(Paths.BepInExRootPath, configured));
            var fromGame = Path.GetFullPath(Path.Combine(Paths.GameRootPath, configured));
            string chosen =
                configured.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)
                    ? fromGame
                    : fromBepInEx;
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
