using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BepInExTranslator.Core
{
    /// <summary>
    /// IL2CPP interop / proxy 程序集发现：纯路径与过滤逻辑，无 Unity/BepInEx 依赖，便于单测。
    /// 插件在 Load 时若不主动加载这些 DLL，AppDomain 中不会出现 UnityEngine.UI / TMP 等类型。
    /// </summary>
    public static class InteropAssemblyCatalog
    {
        /// <summary>强制加载时跳过的文件名（大小写不敏感）。</summary>
        private static readonly HashSet<string> BlacklistedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "netstandard.dll",
            "Il2Cppnetstandard.dll",
            "System.Runtime.dll",
            "mscorlib.dll",
            "Il2Cppmscorlib.dll",
        };

        /// <summary>
        /// 解析可能的 interop 目录候选（按优先级去重）。
        /// </summary>
        /// <param name="configuredInteropPath">反射得到的 IL2CPPInteropAssemblyPath，可空。</param>
        /// <param name="bepInExRoot">BepInEx 根目录，可空。</param>
        /// <param name="gameRoot">游戏根目录，可空。</param>
        public static IReadOnlyList<string> ResolveInteropDirectoryCandidates(
            string? configuredInteropPath,
            string? bepInExRoot,
            string? gameRoot)
        {
            var ordered = new List<string>();

            // 1) BepInEx 官方生成路径（若可反射取得）
            AddIfPresent(ordered, configuredInteropPath);

            // 2) 常规 BepInEx 6 布局：<BepInEx>/interop
            if (!string.IsNullOrWhiteSpace(bepInExRoot))
            {
                AddIfPresent(ordered, Path.Combine(bepInExRoot, "interop"));
                // 旧 Unhollower 布局兼容
                AddIfPresent(ordered, Path.Combine(bepInExRoot, "unhollowed"));
            }

            // 3) 相对游戏根的常见路径
            if (!string.IsNullOrWhiteSpace(gameRoot))
            {
                AddIfPresent(ordered, Path.Combine(gameRoot, "BepInEx", "interop"));
                AddIfPresent(ordered, Path.Combine(gameRoot, "BepInEx", "unhollowed"));
            }

            return DedupPreserveOrder(ordered);
        }

        /// <summary>文件名是否适合作强制 Load 候选。</summary>
        public static bool IsCandidateDll(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !BlacklistedFileNames.Contains(Path.GetFileName(fileName));
        }

        /// <summary>
        /// 列出目录下应按名排序的候选 DLL 完整路径；目录不存在则返回空。
        /// </summary>
        public static IReadOnlyList<string> EnumerateCandidateDllPaths(string? directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            return Directory.GetFiles(directory, "*.dll")
                .Where(path => IsCandidateDll(Path.GetFileName(path)))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 从候选目录列表中选出第一个含有候选 DLL 的目录，并返回其 DLL 列表。
        /// </summary>
        public static (string? Directory, IReadOnlyList<string> DllPaths) SelectFirstPopulatedDirectory(
            IEnumerable<string> directoryCandidates)
        {
            foreach (var dir in directoryCandidates ?? Array.Empty<string>())
            {
                var dlls = EnumerateCandidateDllPaths(dir);
                if (dlls.Count > 0)
                {
                    return (dir, dlls);
                }
            }

            return (null, Array.Empty<string>());
        }

        private static void AddIfPresent(List<string> sink, string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                // IsNullOrWhiteSpace 已排除 null/空白；Trim 后交给 GetFullPath
                var trimmed = path!.Trim();
                sink.Add(Path.GetFullPath(trimmed));
            }
            catch
            {
                // 非法路径忽略
            }
        }

        private static IReadOnlyList<string> DedupPreserveOrder(List<string> paths)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>();
            foreach (var path in paths)
            {
                if (seen.Add(path))
                {
                    result.Add(path);
                }
            }

            return result;
        }
    }
}
