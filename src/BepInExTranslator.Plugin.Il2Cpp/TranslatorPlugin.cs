using System;
using System.IO;
using System.Reflection;
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
    /// BepInEx 6 Unity IL2CPP 入口。
    /// 刻意避免编译期绑定具体 Unity/Il2Cpp 类型，便于在无游戏 interop 程序集时构建通用骨架。
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

            // 主线程泵：通过反射创建 DontDestroyOnLoad 对象（若运行时 Unity 类型可用）
            try
            {
                if (MainThreadPumpBootstrap.TryEnable(Log))
                {
                    UnityMainThread.Enable();
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Main thread pump unavailable: {ex.Message}. Translations still persist to cache.");
            }

            Log.LogInfo($"{PluginName} {PluginVersion} (IL2CPP) ready. Target={Settings.TargetLanguage.Value}");
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

            var pluginDir = Path.Combine(Paths.PluginPath, "BepInExTranslator");
            Directory.CreateDirectory(pluginDir);
            return Path.Combine(pluginDir, "translations.json");
        }
    }

    /// <summary>
    /// 在 IL2CPP 运行时用反射挂一个 Update 泵（不编译期引用 UnityEngine）。
    /// 若 ClassInjector / MonoBehaviour 不可用，则仅依赖缓存同步替换。
    /// </summary>
    internal static class MainThreadPumpBootstrap
    {
        public static bool TryEnable(ManualLogSource log)
        {
            // 优先：已有游戏侧每帧回调时可扩展；此处尝试 Il2CppInterop ClassInjector 模式。
            var injector = FindType("Il2CppInterop.Runtime.Injection.ClassInjector");
            var monoBehaviour = FindType("UnityEngine.MonoBehaviour") ?? FindType("Il2CppUnityEngine.MonoBehaviour");
            if (injector == null || monoBehaviour == null)
            {
                log.LogWarning("Unity MonoBehaviour / ClassInjector not visible at Load(); skip pump.");
                return false;
            }

            // 无编译期子类时，退化为：仅标记 IsAvailable=false 的安全路径。
            // 游戏特化构建可注册自定义 Il2Cpp MonoBehaviour 并调用 UnityMainThread.Pump()。
            log.LogInfo(
                "IL2CPP universal build: main-thread pump requires a game-specific MonoBehaviour injector. " +
                "Cached translations still apply synchronously on set_text. See BUILD.md.");
            return false;
        }

        private static Type? FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null)
                    {
                        return t;
                    }
                }
                catch
                {
                    // ignore
                }
            }

            return null;
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
