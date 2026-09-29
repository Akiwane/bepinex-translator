using System;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using BepInExTranslator.Core;
using BepInExTranslator.Services;
using HarmonyLib;

namespace BepInExTranslator.Hooks
{
    /// <summary>
    /// IL2CPP：反射安装 UGUI / TMP / TextMesh 文本 Hook。
    /// 支持强制加载 interop 后立即挂钩，以及后续延迟重试（程序集晚到）。
    /// </summary>
    public static class TextHookInstaller
    {
        private static readonly object Gate = new object();
        private static DeferredHookTracker? _tracker;
        private static Harmony? _harmony;
        private static ManualLogSource? _log;
        private static bool _assemblyLoadHooked;
        private static int _retryPasses;
        private const int MaxRetryPasses = 64;

        /// <summary>
        /// 首次应用：强制加载 interop → 构建待办 → 立即尝试 → 订阅 AssemblyLoad。
        /// </summary>
        public static void Apply(Harmony harmony, ManualLogSource log)
        {
            lock (Gate)
            {
                _harmony = harmony ?? throw new ArgumentNullException(nameof(harmony));
                _log = log ?? throw new ArgumentNullException(nameof(log));

                // 关键：未引用的 interop DLL 不会进入 AppDomain
                Il2CppInteropAssemblyLoader.EnsureLoaded(log);

                var settings = TranslatorPlugin.Settings;
                var targets = DefaultTextHookTargets.Create(
                    settings.EnableUgui.Value,
                    settings.EnableTextMesh.Value,
                    settings.EnableTextMeshPro.Value);
                _tracker = new DeferredHookTracker(targets);

                TryInstallPending_NoLock(forceLogMisses: true);
                EnsureAssemblyLoadSubscription_NoLock();
            }
        }

        /// <summary>
        /// 由主线程泵或 AssemblyLoad 触发的重试。
        /// </summary>
        public static void RetryPending(bool quiet = true)
        {
            lock (Gate)
            {
                if (_tracker == null || _harmony == null || _log == null)
                {
                    return;
                }

                if (_tracker.IsComplete)
                {
                    return;
                }

                if (_retryPasses >= MaxRetryPasses)
                {
                    return;
                }

                _retryPasses++;
                // 晚到的程序集：再确保 loader 已跑过（幂等）
                Il2CppInteropAssemblyLoader.EnsureLoaded(_log);
                TryInstallPending_NoLock(forceLogMisses: !quiet && _retryPasses == MaxRetryPasses);
            }
        }

        /// <summary>是否仍有未挂钩目标（测试/诊断用）。</summary>
        public static int PendingCount
        {
            get
            {
                lock (Gate)
                {
                    return _tracker?.PendingCount ?? 0;
                }
            }
        }

        private static void EnsureAssemblyLoadSubscription_NoLock()
        {
            if (_assemblyLoadHooked)
            {
                return;
            }

            _assemblyLoadHooked = true;
            AppDomain.CurrentDomain.AssemblyLoad += (_, __) =>
            {
                try
                {
                    RetryPending(quiet: true);
                }
                catch
                {
                    // ignore
                }
            };
        }

        private static void TryInstallPending_NoLock(bool forceLogMisses)
        {
            var tracker = _tracker!;
            var harmony = _harmony!;
            var log = _log!;
            var pending = tracker.Pending;

            foreach (var target in pending)
            {
                var ok = target.Kind switch
                {
                    DeferredHookMemberKind.StringPropertySetter =>
                        TryPatchStringSetter(harmony, log, target, forceLogMisses),
                    DeferredHookMemberKind.StringMethod =>
                        TryPatchStringMethod(harmony, log, target, forceLogMisses),
                    _ => false,
                };

                if (ok)
                {
                    tracker.MarkCompleted(target.Key);
                }
            }

            if (tracker.IsComplete)
            {
                log.LogInfo("IL2CPP text hooks: all requested targets attached.");
            }
            else if (forceLogMisses)
            {
                var still = string.Join(", ", tracker.Pending.Select(t => t.Key));
                log.LogInfo(
                    $"IL2CPP text hooks pending ({tracker.PendingCount}): {still}. " +
                    "Will retry as interop assemblies load / on main-thread pump.");
            }
        }

        private static bool TryPatchStringSetter(
            Harmony harmony,
            ManualLogSource log,
            DeferredHookTarget target,
            bool logMiss)
        {
            try
            {
                var type = ResolveType(target);
                if (type == null)
                {
                    if (logMiss)
                    {
                        log.LogInfo($"Type not found (will retry): {target.TypeName} [{target.Key}]");
                    }

                    return false;
                }

                var setter = ManagedTypeResolver.FindStringSetter(type, target.MemberName);
                if (setter == null)
                {
                    log.LogWarning($"No string setter for {target.TypeName}.{target.MemberName}");
                    // 类型已在但成员缺失：视为完成以免无限重试
                    return true;
                }

                var prefix = typeof(TextHooks).GetMethod(
                    nameof(TextHooks.PrefixSetText),
                    BindingFlags.Static | BindingFlags.Public);
                harmony.Patch(setter, prefix: new HarmonyMethod(prefix));
                log.LogInfo($"Hooked {target.TypeName}.{target.MemberName} setter [{target.Key}]");
                return true;
            }
            catch (Exception ex)
            {
                log.LogWarning($"Failed to hook {target.Key}: {ex.Message}");
                return false;
            }
        }

        private static bool TryPatchStringMethod(
            Harmony harmony,
            ManualLogSource log,
            DeferredHookTarget target,
            bool logMiss)
        {
            try
            {
                var type = ResolveType(target);
                if (type == null)
                {
                    if (logMiss)
                    {
                        log.LogInfo($"Type not found (will retry): {target.TypeName} [{target.Key}]");
                    }

                    return false;
                }

                var method = type.GetMethod(
                    target.MemberName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string) },
                    null);

                // Il2Cpp 上 SetText 可能参数不是 System.String
                if (method == null)
                {
                    method = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .FirstOrDefault(m =>
                            string.Equals(m.Name, target.MemberName, StringComparison.Ordinal)
                            && m.GetParameters().Length == 1);
                }

                if (method == null)
                {
                    if (logMiss)
                    {
                        log.LogDebug($"Method not found: {target.TypeName}.{target.MemberName}(string)");
                    }

                    // 可选方法：找不到则完成，避免挡住其它目标
                    return true;
                }

                var prefix = typeof(TextHooks).GetMethod(
                    nameof(TextHooks.PrefixSetTextMethod),
                    BindingFlags.Static | BindingFlags.Public);
                harmony.Patch(method, prefix: new HarmonyMethod(prefix));
                log.LogInfo($"Hooked {target.TypeName}.{target.MemberName}(string) [{target.Key}]");
                return true;
            }
            catch (Exception ex)
            {
                log.LogWarning($"Failed to hook {target.Key}: {ex.Message}");
                return false;
            }
        }

        private static Type? ResolveType(DeferredHookTarget target)
        {
            return ManagedTypeResolver.FindType(
                AppDomain.CurrentDomain.GetAssemblies(),
                target.TypeName,
                target.PreferredAssemblies);
        }
    }

    public static class TextHooks
    {
        public static void PrefixSetText(object __instance, ref string value) => Handle(__instance, ref value);

        public static void PrefixSetTextMethod(object __instance, ref string text) => Handle(__instance, ref text);

        private static void Handle(object instance, ref string text)
        {
            var runtime = TranslatorPlugin.Runtime;
            if (runtime == null || instance == null || runtime.IsMutating(instance))
            {
                return;
            }

            var original = text;
            text = runtime.ProcessTextChange(instance, original, translated => TryWriteText(instance, translated));
        }

        private static void TryWriteText(object instance, string translated)
        {
            try
            {
                var type = instance.GetType();
                var setter = ManagedTypeResolver.FindStringSetter(type, "text");
                if (setter != null)
                {
                    setter.Invoke(instance, new object[] { translated });
                    return;
                }

                var setText = type.GetMethod(
                    "SetText",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(string) },
                    null);
                setText?.Invoke(instance, new object[] { translated });
            }
            catch
            {
                // ignore
            }
        }
    }
}
