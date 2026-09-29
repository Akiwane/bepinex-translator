using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInExTranslator.Core;

namespace BepInExTranslator.Services
{
    /// <summary>
    /// 强制将 BepInEx IL2CPP interop 程序集装入 AppDomain。
    /// 根因：通用插件不编译期引用 UnityEngine.UI / TMP，AssemblyResolve 不会按需加载这些 DLL，
    /// 导致 Load() 时 AppDomain.GetAssemblies() 找不到类型并静默跳过 Hook。
    /// </summary>
    internal static class Il2CppInteropAssemblyLoader
    {
        private static bool _attempted;
        private static string? _loadedDirectory;
        private static int _loadedCount;

        public static string? LoadedDirectory => _loadedDirectory;
        public static int LoadedCount => _loadedCount;

        /// <summary>
        /// 尝试 Preload（若 BepInEx 内部 API 可用）并手动 Assembly.Load 全部候选 DLL。
        /// 可重复调用；仅首次执行实际加载。
        /// </summary>
        public static void EnsureLoaded(ManualLogSource log)
        {
            if (_attempted)
            {
                return;
            }

            _attempted = true;

            // 1) 反射调用 BepInEx 内部 PreloadInteropAssemblies（若存在）
            TryInvokeBepInExPreload(log);

            // 2) 解析 interop 目录并强制 Load
            var configured = TryReadInteropAssemblyPath(log);
            var candidates = InteropAssemblyCatalog.ResolveInteropDirectoryCandidates(
                configured,
                Paths.BepInExRootPath,
                Paths.GameRootPath);

            var (directory, dllPaths) = InteropAssemblyCatalog.SelectFirstPopulatedDirectory(candidates);
            if (directory == null || dllPaths.Count == 0)
            {
                log.LogWarning(
                    "IL2CPP interop directory not found or empty. Checked: " +
                    string.Join(", ", candidates));
                return;
            }

            _loadedDirectory = directory;
            var loaded = 0;
            var failed = 0;

            foreach (var path in dllPaths)
            {
                if (TryLoadAssembly(path, log))
                {
                    loaded++;
                }
                else
                {
                    failed++;
                }
            }

            _loadedCount = loaded;
            log.LogInfo(
                $"IL2CPP interop force-load: dir={directory}, loaded={loaded}, failed={failed}, candidates={dllPaths.Count}");
        }

        private static bool TryLoadAssembly(string path, ManualLogSource log)
        {
            try
            {
                // 优先按 AssemblyName 加载，便于与已加载实例合并
                var assemblyName = AssemblyName.GetAssemblyName(path);
                Assembly.Load(assemblyName);
                return true;
            }
            catch (FileLoadException)
            {
                // 已加载等同成功
                return true;
            }
            catch (BadImageFormatException)
            {
                // 原生/非托管 DLL 跳过
                return false;
            }
            catch (Exception ex)
            {
                try
                {
                    Assembly.LoadFrom(path);
                    return true;
                }
                catch (Exception ex2)
                {
                    log.LogDebug($"Skip interop dll {Path.GetFileName(path)}: {ex.Message}; fallback: {ex2.Message}");
                    return false;
                }
            }
        }

        private static void TryInvokeBepInExPreload(ManualLogSource log)
        {
            try
            {
                var mgrType = typeof(BepInEx.Unity.IL2CPP.IL2CPPChainloader).Assembly
                    .GetType("BepInEx.Unity.IL2CPP.Il2CppInteropManager", throwOnError: false);
                var preload = mgrType?.GetMethod(
                    "PreloadInteropAssemblies",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                preload?.Invoke(null, null);
            }
            catch (Exception ex)
            {
                log.LogDebug("Il2CppInteropManager.PreloadInteropAssemblies skipped: " + ex.Message);
            }
        }

        private static string? TryReadInteropAssemblyPath(ManualLogSource log)
        {
            try
            {
                var mgrType = typeof(BepInEx.Unity.IL2CPP.IL2CPPChainloader).Assembly
                    .GetType("BepInEx.Unity.IL2CPP.Il2CppInteropManager", throwOnError: false);
                var prop = mgrType?.GetProperty(
                    "IL2CPPInteropAssemblyPath",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                return prop?.GetValue(null) as string;
            }
            catch (Exception ex)
            {
                log.LogDebug("Read IL2CPPInteropAssemblyPath failed: " + ex.Message);
                return null;
            }
        }
    }
}
