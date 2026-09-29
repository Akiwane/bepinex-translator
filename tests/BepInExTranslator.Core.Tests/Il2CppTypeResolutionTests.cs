using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace BepInExTranslator.Core.Tests
{
    public class InteropAssemblyCatalogTests
    {
        [Theory]
        [InlineData("UnityEngine.UI.dll", true)]
        [InlineData("Unity.TextMeshPro.dll", true)]
        [InlineData("netstandard.dll", false)]
        [InlineData("Il2Cppnetstandard.dll", false)]
        [InlineData("readme.txt", false)]
        [InlineData("", false)]
        public void IsCandidateDll_FiltersBlacklistAndExtension(string fileName, bool expected)
        {
            Assert.Equal(expected, InteropAssemblyCatalog.IsCandidateDll(fileName));
        }

        [Fact]
        public void ResolveInteropDirectoryCandidates_OrdersAndDedups()
        {
            var configured = Path.Combine(Path.GetTempPath(), "cfg-interop-" + Guid.NewGuid().ToString("N"));
            var bep = Path.Combine(Path.GetTempPath(), "bep-" + Guid.NewGuid().ToString("N"));
            var game = Path.Combine(Path.GetTempPath(), "game-" + Guid.NewGuid().ToString("N"));

            var result = InteropAssemblyCatalog.ResolveInteropDirectoryCandidates(configured, bep, game);

            Assert.Equal(Path.GetFullPath(configured), result[0]);
            Assert.Contains(Path.GetFullPath(Path.Combine(bep, "interop")), result);
            Assert.Contains(Path.GetFullPath(Path.Combine(bep, "unhollowed")), result);
            Assert.Contains(Path.GetFullPath(Path.Combine(game, "BepInEx", "interop")), result);
            Assert.Equal(result.Count, result.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void SelectFirstPopulatedDirectory_PicksFirstWithDlls()
        {
            var empty = Path.Combine(Path.GetTempPath(), "empty-interop-" + Guid.NewGuid().ToString("N"));
            var filled = Path.Combine(Path.GetTempPath(), "filled-interop-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(empty);
            Directory.CreateDirectory(filled);
            var dllPath = Path.Combine(filled, "UnityEngine.UI.dll");
            File.WriteAllText(dllPath, "not-a-real-dll");

            try
            {
                var (dir, dlls) = InteropAssemblyCatalog.SelectFirstPopulatedDirectory(new[] { empty, filled });
                Assert.Equal(Path.GetFullPath(filled), dir);
                Assert.Contains(dlls, p => p.EndsWith("UnityEngine.UI.dll", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                try { Directory.Delete(empty, true); } catch { /* ignore */ }
                try { Directory.Delete(filled, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void EnumerateCandidateDllPaths_SkipsBlacklisted()
        {
            var dir = Path.Combine(Path.GetTempPath(), "catalog-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "UnityEngine.UI.dll"), "x");
            File.WriteAllText(Path.Combine(dir, "netstandard.dll"), "x");

            try
            {
                var dlls = InteropAssemblyCatalog.EnumerateCandidateDllPaths(dir);
                Assert.Single(dlls);
                Assert.EndsWith("UnityEngine.UI.dll", dlls[0], StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }
    }

    public class ManagedTypeResolverTests
    {
        [Fact]
        public void FindType_PrefersPreferredAssembly()
        {
            var asmA = EmitAssemblyWithType("PrefA", "Demo.Ns", "Widget");
            var asmB = EmitAssemblyWithType("PrefB", "Demo.Ns", "Widget");

            var found = ManagedTypeResolver.FindType(
                new[] { asmA, asmB },
                "Demo.Ns.Widget",
                new[] { "PrefB" });

            Assert.NotNull(found);
            Assert.Equal("PrefB", found!.Assembly.GetName().Name);
        }

        [Fact]
        public void FindType_FallsBackToFullScan()
        {
            var asm = EmitAssemblyWithType("OnlyAsm", "Game.UI", "Label");
            var found = ManagedTypeResolver.FindType(
                new[] { asm },
                "Game.UI.Label",
                new[] { "MissingPreferred" });

            Assert.NotNull(found);
            Assert.Equal("Game.UI.Label", found!.FullName);
        }

        [Fact]
        public void FindStringSetter_UsesPropertyThenSetMethodFallback()
        {
            var setter = ManagedTypeResolver.FindStringSetter(typeof(SampleWithProperty), "text");
            Assert.NotNull(setter);
            Assert.Equal("set_text", setter!.Name);

            var methodOnly = ManagedTypeResolver.FindStringSetter(typeof(SampleWithSetMethodOnly), "text");
            Assert.NotNull(methodOnly);
            Assert.Equal("set_text", methodOnly!.Name);
        }

        private static Assembly EmitAssemblyWithType(string assemblyName, string ns, string typeName)
        {
            var an = new AssemblyName(assemblyName);
            var ab = AssemblyBuilder.DefineDynamicAssembly(an, AssemblyBuilderAccess.Run);
            var mb = ab.DefineDynamicModule(assemblyName);
            var tb = mb.DefineType(ns + "." + typeName, TypeAttributes.Public);
            tb.CreateType();
            return ab;
        }

        private sealed class SampleWithProperty
        {
            public string text { get; set; } = string.Empty;
        }

        private sealed class SampleWithSetMethodOnly
        {
            private string _text = string.Empty;

            public void set_text(string value) => _text = value;
        }
    }

    public class DeferredHookTrackerTests
    {
        [Fact]
        public void DefaultTargets_ContainUguiTextMeshAndTmp()
        {
            var targets = DefaultTextHookTargets.Create(true, true, true);
            Assert.Contains(targets, t => t.Key == "ugui.text");
            Assert.Contains(targets, t => t.Key == "textmesh.text");
            Assert.Contains(targets, t => t.Key == "tmp.text");
            Assert.Contains(targets, t => t.Key == "tmp.SetText");
            // TMP 两个成员共用类型名，但 Key 不同 —— 避免「Type not found」对同一类型重复刷两次完成态混淆
            Assert.Equal(2, targets.Count(t => t.TypeName == "TMPro.TMP_Text"));
        }

        [Fact]
        public void Tracker_MarkCompleted_RemovesPending()
        {
            var targets = DefaultTextHookTargets.Create(true, false, false);
            var tracker = new DeferredHookTracker(targets);
            Assert.Equal(1, tracker.PendingCount);
            Assert.True(tracker.MarkCompleted("ugui.text"));
            Assert.True(tracker.IsComplete);
            Assert.False(tracker.MarkCompleted("ugui.text"));
        }

        [Fact]
        public void DefaultTargets_RespectsFeatureFlags()
        {
            var none = DefaultTextHookTargets.Create(false, false, false);
            Assert.Empty(none);

            var onlyMesh = DefaultTextHookTargets.Create(false, true, false);
            Assert.Single(onlyMesh);
            Assert.Equal("textmesh.text", onlyMesh[0].Key);
        }
    }
}
