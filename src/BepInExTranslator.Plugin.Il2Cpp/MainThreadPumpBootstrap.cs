using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using BepInExTranslator.Core;
using BepInExTranslator.Hooks;
using BepInExTranslator.Services;
using HarmonyLib;

namespace BepInExTranslator
{
    /// <summary>
    /// 通用 IL2CPP 主线程泵：不依赖游戏特化 MonoBehaviour。
    /// 策略：Harmony 挂钩每帧/每次 Canvas 刷新都会走的静态方法，再调用 UnityMainThread.Pump，
    /// 并顺带触发延迟 Hook 重试。
    /// </summary>
    internal static class MainThreadPumpBootstrap
    {
        private static bool _enabled;

        public static bool TryEnable(Harmony harmony, ManualLogSource log)
        {
            if (_enabled)
            {
                return true;
            }

            // 确保 interop 已装入，否则找不到 Canvas 等类型
            Il2CppInteropAssemblyLoader.EnsureLoaded(log);

            // 按常见 Unity 2020.3 / UGUI 路径尝试帧回调
            var candidates = new (string TypeName, string MethodName, string[]? PreferredAssemblies)[]
            {
                ("UnityEngine.Canvas", "SendWillRenderCanvases", new[] { "UnityEngine.UIModule", "UnityEngine.UI", "UnityEngine.CoreModule" }),
                ("UnityEngine.Canvas", "ForceUpdateCanvases", new[] { "UnityEngine.UIModule", "UnityEngine.UI", "UnityEngine.CoreModule" }),
                ("UnityEngine.UI.CanvasUpdateRegistry", "PerformUpdate", new[] { "UnityEngine.UI" }),
            };

            foreach (var candidate in candidates)
            {
                if (TryPatchFrameCallback(harmony, log, candidate.TypeName, candidate.MethodName, candidate.PreferredAssemblies))
                {
                    _enabled = true;
                    UnityMainThread.Enable();
                    log.LogInfo(
                        $"IL2CPP main-thread pump enabled via {candidate.TypeName}.{candidate.MethodName}. " +
                        "Async translations will apply on the Unity main thread.");
                    return true;
                }
            }

            // 回退：场景加载时至少能重试 Hook；异步 UI 回写仍可能失败并落到 JSON
            if (TryPatchSceneLoaded(harmony, log))
            {
                log.LogWarning(
                    "IL2CPP frame pump unavailable; hooked SceneManager.sceneLoaded for deferred hook retry only. " +
                    "Cached translations still apply synchronously on set_text; async UI apply may require restart.");
                return false;
            }

            log.LogWarning(
                "IL2CPP main-thread pump unavailable (Canvas/SceneManager not resolved yet). " +
                "Cached translations still apply synchronously on set_text. See BUILD.md.");
            return false;
        }

        /// <summary>Harmony 后缀：泵送队列并重试 pending hooks。</summary>
        public static void PostfixPump()
        {
            try
            {
                TextHookInstaller.RetryPending(quiet: true);
            }
            catch
            {
                // ignore
            }

            UnityMainThread.Pump();
        }

        private static bool TryPatchFrameCallback(
            Harmony harmony,
            ManualLogSource log,
            string typeName,
            string methodName,
            string[]? preferredAssemblies)
        {
            try
            {
                var type = ManagedTypeResolver.FindType(
                    AppDomain.CurrentDomain.GetAssemblies(),
                    typeName,
                    preferredAssemblies);
                if (type == null)
                {
                    return false;
                }

                // 优先无参静态/实例方法；否则取首个同名方法
                var method = type.GetMethod(
                    methodName,
                    BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);

                method ??= type.GetMethods(
                        BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => string.Equals(m.Name, methodName, StringComparison.Ordinal));

                if (method == null)
                {
                    return false;
                }

                var postfix = typeof(MainThreadPumpBootstrap).GetMethod(
                    nameof(PostfixPump),
                    BindingFlags.Static | BindingFlags.Public);
                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                return true;
            }
            catch (Exception ex)
            {
                log.LogDebug($"Frame pump candidate {typeName}.{methodName} failed: {ex.Message}");
                return false;
            }
        }

        private static bool TryPatchSceneLoaded(Harmony harmony, ManualLogSource log)
        {
            try
            {
                var type = ManagedTypeResolver.FindType(
                    AppDomain.CurrentDomain.GetAssemblies(),
                    "UnityEngine.SceneManagement.SceneManager",
                    new[] { "UnityEngine.CoreModule", "UnityEngine" });
                if (type == null)
                {
                    return false;
                }

                // Internal_SceneLoaded / OnSceneLoaded 等因版本而异；优先 add_sceneLoaded 不可靠。
                // 挂钩公开的 GetActiveScene 太频繁；用 Internal_ActiveSceneChanged 若存在。
                var method = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m =>
                        m.Name.IndexOf("ActiveSceneChanged", StringComparison.OrdinalIgnoreCase) >= 0
                        || m.Name.IndexOf("Internal_SceneLoaded", StringComparison.OrdinalIgnoreCase) >= 0);

                if (method == null)
                {
                    return false;
                }

                var postfix = typeof(MainThreadPumpBootstrap).GetMethod(
                    nameof(PostfixPump),
                    BindingFlags.Static | BindingFlags.Public);
                harmony.Patch(method, postfix: new HarmonyMethod(postfix));
                log.LogInfo($"Deferred hook retry via {type.FullName}.{method.Name}");
                return true;
            }
            catch (Exception ex)
            {
                log.LogDebug("SceneLoaded pump fallback failed: " + ex.Message);
                return false;
            }
        }
    }
}
