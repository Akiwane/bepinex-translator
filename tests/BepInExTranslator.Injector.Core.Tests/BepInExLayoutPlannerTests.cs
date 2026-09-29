using System.IO.Compression;
using BepInExTranslator.Injector.Core;

namespace BepInExTranslator.Injector.Core.Tests;

public sealed class BepInExLayoutPlannerTests
{
    [Fact]
    public void ExpectedRootEntries_Mono_IncludesDoorstopAndWinhttp()
    {
        var entries = BepInExLayoutPlanner.ExpectedRootEntries(UnityRuntimeKind.Mono);
        Assert.Contains("winhttp.dll", entries);
        Assert.Contains("doorstop_config.ini", entries);
        Assert.Contains("BepInEx/core", entries);
        Assert.DoesNotContain("dotnet", entries);
    }

    [Fact]
    public void ExpectedRootEntries_Il2Cpp_IncludesDotnet()
    {
        var entries = BepInExLayoutPlanner.ExpectedRootEntries(UnityRuntimeKind.Il2Cpp);
        Assert.Contains("winhttp.dll", entries);
        Assert.Contains("doorstop_config.ini", entries);
        Assert.Contains("dotnet", entries);
        Assert.Contains("BepInEx/core", entries);
    }

    [Fact]
    public void PlanTranslatorLayout_RelativePaths()
    {
        var plan = BepInExLayoutPlanner.PlanTranslatorLayout("/games/Sample");
        Assert.Equal("BepInEx/plugins/Translator", plan.RelativePluginDirectory);
        Assert.Equal("BepInEx/config", plan.RelativeConfigDirectory);
        Assert.EndsWith("Translator.cfg.example", plan.ConfigExampleDestination);
    }

    [Fact]
    public void PlanZipExtractRelativePaths_NormalizesNestedRoot()
    {
        var zipEntries = new[]
        {
            "BepInEx_win_x64_5.4.23.5/winhttp.dll",
            "BepInEx_win_x64_5.4.23.5/doorstop_config.ini",
            "BepInEx_win_x64_5.4.23.5/BepInEx/core/BepInEx.dll",
        };

        var planned = BepInExLayoutPlanner.PlanZipExtractRelativePaths(zipEntries, UnityRuntimeKind.Mono);

        Assert.Contains("winhttp.dll", planned);
        Assert.Contains("doorstop_config.ini", planned);
        Assert.Contains("BepInEx/core/BepInEx.dll", planned);
    }

    [Fact]
    public void MissingExpectedEntries_ReportsGaps()
    {
        var root = Path.Combine(Path.GetTempPath(), "bepinex-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "winhttp.dll"), "x");
            Directory.CreateDirectory(Path.Combine(root, "BepInEx", "core"));
            // 故意缺少 doorstop_config.ini
            var missing = BepInExLayoutPlanner.MissingExpectedEntries(root, UnityRuntimeKind.Mono);
            Assert.Contains("doorstop_config.ini", missing);
            Assert.DoesNotContain("winhttp.dll", missing);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public sealed class PackageResolverTests
{
    [Fact]
    public void ResolveBepInEx_Mono_UsesPinnedUrl()
    {
        var resolver = new PackageResolver();
        var pkg = resolver.ResolveBepInEx(UnityRuntimeKind.Mono, new PackageSourceOptions());
        Assert.Equal(PackageSourceKind.RemoteUrl, pkg.SourceKind);
        Assert.Equal(PackageCatalog.BepInEx5Version, pkg.VersionLabel);
        Assert.Equal(PackageCatalog.BepInEx5DownloadUrl, pkg.DownloadUrl);
    }

    [Fact]
    public void ResolveBepInEx_Il2Cpp_UsesPinnedUrl()
    {
        var resolver = new PackageResolver();
        var pkg = resolver.ResolveBepInEx(UnityRuntimeKind.Il2Cpp, new PackageSourceOptions());
        Assert.Equal(PackageCatalog.BepInEx6Version, pkg.VersionLabel);
        Assert.Contains("Unity.IL2CPP-win-x64", pkg.DownloadUrl);
    }

    [Fact]
    public void ResolveTranslator_PrefersLocalArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "bepinex-artifacts-" + Guid.NewGuid().ToString("N"));
        var mono = Path.Combine(root, "artifacts", "mono");
        Directory.CreateDirectory(mono);
        File.WriteAllText(Path.Combine(mono, "BepInExTranslator.dll"), "dll");
        File.WriteAllText(Path.Combine(mono, "BepInExTranslator.Core.dll"), "dll");
        try
        {
            var resolver = new PackageResolver();
            var pkg = resolver.ResolveTranslator(UnityRuntimeKind.Mono, new PackageSourceOptions
            {
                RepositoryRoot = root,
            });
            Assert.Equal(PackageSourceKind.LocalDirectory, pkg.SourceKind);
            Assert.Equal(mono, pkg.LocalPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

public sealed class GameInstallerLayoutTests
{
    [Fact]
    public async Task Install_FromLocalZips_LaysOutPluginsAndConfig()
    {
        using var game = GameFixtures.CreateMonoGame("InstallTarget");
        var work = Path.Combine(Path.GetTempPath(), "bepinex-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);

        try
        {
            // —— 伪造 BepInEx zip ——
            var bepinexZip = Path.Combine(work, "bepinex.zip");
            CreateMinimalBepInExZip(bepinexZip, includeDotnet: false);

            // —— 伪造模组 artifacts ——
            var artifacts = Path.Combine(work, "artifacts", "mono");
            Directory.CreateDirectory(artifacts);
            File.WriteAllText(Path.Combine(artifacts, "BepInExTranslator.dll"), "plugin");
            File.WriteAllText(Path.Combine(artifacts, "BepInExTranslator.Core.dll"), "core");

            // —— 配置示例 ——
            var cfg = Path.Combine(work, "config", "Translator.cfg.example");
            Directory.CreateDirectory(Path.GetDirectoryName(cfg)!);
            File.WriteAllText(cfg, "[General]\nTargetLanguage = zh-CN\n");

            var detection = new GameDetector().Detect(game.GameRoot);
            var installer = new GameInstaller();
            var result = await installer.InstallAsync(new InstallOptions
            {
                Detection = detection,
                OverwritePolicy = OverwritePolicy.SkipExisting,
                CacheDirectory = Path.Combine(work, "cache"),
                ConfigExampleSourcePath = cfg,
                PackageSources = new PackageSourceOptions
                {
                    BepInExLocalZipPath = bepinexZip,
                    TranslatorLocalArtifactsDirectory = artifacts,
                    RepositoryRoot = work,
                },
            });

            Assert.True(result.Success, string.Join("\n", result.Messages));
            Assert.True(File.Exists(Path.Combine(game.GameRoot, "winhttp.dll")));
            Assert.True(File.Exists(Path.Combine(game.GameRoot, "doorstop_config.ini")));
            Assert.True(Directory.Exists(Path.Combine(game.GameRoot, "BepInEx", "core")));
            Assert.True(File.Exists(Path.Combine(game.GameRoot, "BepInEx", "plugins", "Translator", "BepInExTranslator.dll")));
            Assert.True(File.Exists(Path.Combine(game.GameRoot, "BepInEx", "plugins", "Translator", "BepInExTranslator.Core.dll")));
            Assert.True(File.Exists(Path.Combine(game.GameRoot, "BepInEx", "config", "Translator.cfg.example")));
        }
        finally
        {
            Directory.Delete(work, true);
        }
    }

    private static void CreateMinimalBepInExZip(string zipPath, bool includeDotnet)
    {
        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        void Add(string name, string content)
        {
            var e = zip.CreateEntry(name);
            using var w = new StreamWriter(e.Open());
            w.Write(content);
        }

        Add("winhttp.dll", "dll");
        Add("doorstop_config.ini", "[General]\n");
        Add("BepInEx/core/BepInEx.dll", "dll");
        if (includeDotnet)
        {
            Add("dotnet/dotnet.exe", "x");
        }
    }
}
