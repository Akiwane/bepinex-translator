using System;
using System.Collections.Generic;
using System.Linq;

namespace BepInExTranslator.Core
{
    /// <summary>
    /// 记录仍待安装的文本 Hook 目标，支持重试直至成功或放弃。
    /// </summary>
    public sealed class DeferredHookTracker
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, DeferredHookTarget> _pending =
            new Dictionary<string, DeferredHookTarget>(StringComparer.Ordinal);

        public DeferredHookTracker(IEnumerable<DeferredHookTarget> initialTargets)
        {
            foreach (var target in initialTargets ?? Array.Empty<DeferredHookTarget>())
            {
                if (target == null || string.IsNullOrWhiteSpace(target.Key))
                {
                    continue;
                }

                _pending[target.Key] = target;
            }
        }

        /// <summary>当前尚未成功的目标（快照）。</summary>
        public IReadOnlyList<DeferredHookTarget> Pending
        {
            get
            {
                lock (_gate)
                {
                    return _pending.Values.ToList();
                }
            }
        }

        public int PendingCount
        {
            get
            {
                lock (_gate)
                {
                    return _pending.Count;
                }
            }
        }

        public bool IsComplete
        {
            get
            {
                lock (_gate)
                {
                    return _pending.Count == 0;
                }
            }
        }

        /// <summary>标记某 Key 已成功挂钩并移出队列。</summary>
        public bool MarkCompleted(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            lock (_gate)
            {
                return _pending.Remove(key);
            }
        }
    }

    /// <summary>单个 Hook 目标描述（与运行时 Harmony 细节解耦）。</summary>
    public sealed class DeferredHookTarget
    {
        public DeferredHookTarget(
            string key,
            string typeName,
            IReadOnlyList<string> preferredAssemblies,
            DeferredHookMemberKind kind,
            string memberName)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
            PreferredAssemblies = preferredAssemblies ?? Array.Empty<string>();
            Kind = kind;
            MemberName = memberName ?? throw new ArgumentNullException(nameof(memberName));
        }

        /// <summary>稳定唯一键，如 ugui.text / tmp.text / tmp.SetText。</summary>
        public string Key { get; }

        public string TypeName { get; }

        public IReadOnlyList<string> PreferredAssemblies { get; }

        public DeferredHookMemberKind Kind { get; }

        public string MemberName { get; }
    }

    public enum DeferredHookMemberKind
    {
        StringPropertySetter = 0,
        StringMethod = 1,
    }

    /// <summary>默认 UGUI / TextMesh / TMP 目标集（与配置开关无关的完整清单由调用方过滤）。</summary>
    public static class DefaultTextHookTargets
    {
        public static IReadOnlyList<DeferredHookTarget> Create(
            bool enableUgui,
            bool enableTextMesh,
            bool enableTmp)
        {
            var list = new List<DeferredHookTarget>();

            if (enableUgui)
            {
                list.Add(new DeferredHookTarget(
                    "ugui.text",
                    "UnityEngine.UI.Text",
                    new[] { "UnityEngine.UI" },
                    DeferredHookMemberKind.StringPropertySetter,
                    "text"));
            }

            if (enableTextMesh)
            {
                list.Add(new DeferredHookTarget(
                    "textmesh.text",
                    "UnityEngine.TextMesh",
                    new[] { "UnityEngine.TextRenderingModule", "UnityEngine.CoreModule", "UnityEngine" },
                    DeferredHookMemberKind.StringPropertySetter,
                    "text"));
            }

            if (enableTmp)
            {
                list.Add(new DeferredHookTarget(
                    "tmp.text",
                    "TMPro.TMP_Text",
                    new[] { "Unity.TextMeshPro", "TextMeshPro" },
                    DeferredHookMemberKind.StringPropertySetter,
                    "text"));

                list.Add(new DeferredHookTarget(
                    "tmp.SetText",
                    "TMPro.TMP_Text",
                    new[] { "Unity.TextMeshPro", "TextMeshPro" },
                    DeferredHookMemberKind.StringMethod,
                    "SetText"));
            }

            return list;
        }
    }
}
