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
    public void Resolve_Mono_UsesPinnedBepInExUrl()
    {
        IPackageResolver resolver = new PackageResolver();
        var result = resolver.Resolve(UnityRuntimeKind.Mono, new PackageSourceOptions
        {
            // 允许列表内的覆盖 URL（仅测 BepInEx pin；模组用显式 GitHub URL）
            TranslatorDownloadUrl =
                "https://github.com/Akiwane/bepinex-translator/releases/latest/download/BepInExTranslator-mono-win.zip",
        });
        Assert.True(result.Success);
        var bepinex = Assert.Single(result.Packages, p => p.Kind == PackageKind.BepInEx);
        Assert.Equal(PackageSourceKind.RemoteUrl, bepinex.SourceKind);
        Assert.Equal(PackageCatalog.BepInEx5Version, bepinex.VersionLabel);
        Assert.Equal(PackageCatalog.BepInEx5DownloadUrl, bepinex.DownloadUrl);
    }

    [Fact]
    public void Resolve_Il2Cpp_UsesPinnedBepInExUrl()
    {
        IPackageResolver resolver = new PackageResolver();
        var result = resolver.Resolve(UnityRuntimeKind.Il2Cpp, new PackageSourceOptions
        {
            TranslatorDownloadUrl =
                "https://github.com/Akiwane/bepinex-translator/releases/latest/download/BepInExTranslator-il2cpp-win.zip",
        });
        Assert.True(result.Success);
        var bepinex = Assert.Single(result.Packages, p => p.Kind == PackageKind.BepInEx);
        Assert.Equal(PackageCatalog.BepInEx6Version, bepinex.VersionLabel);
        Assert.Contains("Unity.IL2CPP-win-x64", bepinex.DownloadUrl);
    }

    [Fact]
    public void Resolve_Il2Cpp_LocalZipOverridesDefaultPin()
    {
        var work = Path.Combine(Path.GetTempPath(), "bepinex-be-override-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var zip = Path.Combine(work, "BepInEx-Unity.IL2CPP-win-x64-be.733.zip");
        File.WriteAllBytes(zip, new byte[] { 0x50, 0x4B }); // minimal PK stub is enough for resolve
        try
        {
            IPackageResolver resolver = new PackageResolver();
            var result = resolver.Resolve(UnityRuntimeKind.Il2Cpp, new PackageSourceOptions
            {
                BepInExLocalZipPath = zip,
                TranslatorDownloadUrl =
                    "https://github.com/Akiwane/bepinex-translator/releases/latest/download/BepInExTranslator-il2cpp-win.zip",
            });
            Assert.True(result.Success, result.Error?.Message);
            var bepinex = Assert.Single(result.Packages, p => p.Kind == PackageKind.BepInEx);
            Assert.Equal(PackageSourceKind.LocalZip, bepinex.SourceKind);
            Assert.Equal(zip, bepinex.LocalPath);
        }
        finally
        {
            Directory.Delete(work, true);
        }
    }

    [Fact]
    public void Resolve_Il2Cpp_UrlOverride_BuildsBepInExDevAllowed()
    {
        IPackageResolver resolver = new PackageResolver();
        var beUrl =
            "https://builds.bepinex.dev/projects/bepinex_be/builds/733/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.733.zip";
        var result = resolver.Resolve(UnityRuntimeKind.Il2Cpp, new PackageSourceOptions
        {
            BepInExIl2CppUrl = beUrl,
            TranslatorDownloadUrl =
                "https://github.com/Akiwane/bepinex-translator/releases/latest/download/BepInExTranslator-il2cpp-win.zip",
        });
        Assert.True(result.Success, result.Error?.Message);
        var bepinex = Assert.Single(result.Packages, p => p.Kind == PackageKind.BepInEx);
        Assert.Equal(beUrl, bepinex.DownloadUrl);
        Assert.Equal("url-override", bepinex.VersionLabel);
    }

    [Fact]
    public void Resolve_PrefersLocalTranslatorArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "bepinex-artifacts-" + Guid.NewGuid().ToString("N"));
        var mono = Path.Combine(root, "artifacts", "mono");
        Directory.CreateDirectory(mono);
        File.WriteAllText(Path.Combine(mono, "BepInExTranslator.dll"), "dll");
        File.WriteAllText(Path.Combine(mono, "BepInExTranslator.Core.dll"), "dll");
        try
        {
            IPackageResolver resolver = new PackageResolver();
            var result = resolver.Resolve(UnityRuntimeKind.Mono, new PackageSourceOptions
            {
                RepositoryRoot = root,
            });
            Assert.True(result.Success);
            var mod = Assert.Single(result.Packages, p => p.Kind == PackageKind.TranslatorMod);
            Assert.Equal(PackageSourceKind.LocalDirectory, mod.SourceKind);
            Assert.Equal(mono, mod.LocalPath);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Resolve_WithoutArtifacts_UsesDefaultLatestTranslatorRelease()
    {
        IPackageResolver resolver = new PackageResolver();
        // 无 RepositoryRoot / 无本地 zip / 无显式 URL → 默认 latest 远程
        var result = resolver.Resolve(UnityRuntimeKind.Mono, new PackageSourceOptions());
        Assert.True(result.Success, result.Error?.Message);
        var mod = Assert.Single(result.Packages, p => p.Kind == PackageKind.TranslatorMod);
        Assert.Equal(PackageSourceKind.RemoteUrl, mod.SourceKind);
        Assert.Equal(PackageCatalog.DefaultTranslatorReleaseTag, mod.VersionLabel);
        Assert.Equal(
            PackageCatalog.BuildTranslatorReleaseDownloadUrl(UnityRuntimeKind.Mono, "latest"),
            mod.DownloadUrl);
        Assert.Contains("/releases/latest/download/BepInExTranslator-mono-win.zip", mod.DownloadUrl);
    }

    [Fact]
    public void Resolve_TranslatorReleaseTag_UsesSpecificTagUrl()
    {
        IPackageResolver resolver = new PackageResolver();
        var result = resolver.Resolve(UnityRuntimeKind.Il2Cpp, new PackageSourceOptions
        {
            TranslatorReleaseTag = "v1.2.3",
        });
        Assert.True(result.Success, result.Error?.Message);
        var mod = Assert.Single(result.Packages, p => p.Kind == PackageKind.TranslatorMod);
        Assert.Equal(
            "https://github.com/Akiwane/bepinex-translator/releases/download/v1.2.3/BepInExTranslator-il2cpp-win.zip",
            mod.DownloadUrl);
    }

    [Fact]
    public void Resolve_RejectsDisallowedTranslatorHost()
    {
        IPackageResolver resolver = new PackageResolver();
        var result = resolver.Resolve(UnityRuntimeKind.Mono, new PackageSourceOptions
        {
            TranslatorDownloadUrl = "https://example.com/mod.zip",
        });
        Assert.False(result.Success);
        Assert.Equal(InjectorErrorKind.DownloadFailed, result.Error!.Kind);
        Assert.Contains("允许列表", result.Error.Message);
    }

    [Fact]
    public void Resolve_UnknownRuntime_ReturnsTypedError()
    {
        IPackageResolver resolver = new PackageResolver();
        var result = resolver.Resolve(UnityRuntimeKind.Unknown, new PackageSourceOptions());
        Assert.False(result.Success);
        Assert.NotNull(result.Error);
        Assert.Equal(InjectorErrorKind.UnknownRuntime, result.Error!.Kind);
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

public sealed class ZipSlipTests
{
    [Theory]
    [InlineData("../evil.dll")]
    [InlineData("BepInEx/../../evil.dll")]
    [InlineData("/tmp/evil.dll")]
    [InlineData("C:/Windows/evil.dll")]
    public void NormalizeZipEntry_RejectsTraversalAndAbsolute(string entry)
    {
        Assert.Null(BepInExLayoutPlanner.NormalizeZipEntry(entry));
    }

    [Fact]
    public void NormalizeZipEntry_AcceptsSafeRelative()
    {
        Assert.Equal("winhttp.dll", BepInExLayoutPlanner.NormalizeZipEntry("winhttp.dll"));
        Assert.Equal("BepInEx/core/BepInEx.dll",
            BepInExLayoutPlanner.NormalizeZipEntry("BepInEx_win_x64_5.4.23.5/BepInEx/core/BepInEx.dll"));
    }

    [Fact]
    public void ExtractZipToGameRoot_RejectsDotDotEntry()
    {
        var work = Path.Combine(Path.GetTempPath(), "bepinex-zipslip-" + Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(work, "game");
        Directory.CreateDirectory(gameRoot);
        var zipPath = Path.Combine(work, "evil.zip");
        try
        {
            CreateZipWithEntries(zipPath, ("../evil.dll", "malware"));
            var copied = new List<string>();
            var err = GameInstaller.ExtractZipToGameRoot(
                zipPath,
                gameRoot,
                OverwritePolicy.Overwrite,
                backupDir: null,
                copied,
                _ => { });

            Assert.NotNull(err);
            Assert.Equal(InjectorErrorKind.InvalidPath, err!.Kind);
            Assert.Empty(copied);
            // 不得写出到游戏根之外
            Assert.False(File.Exists(Path.Combine(work, "evil.dll")));
        }
        finally
        {
            Directory.Delete(work, true);
        }
    }

    [Fact]
    public void ExtractTranslatorZip_RejectsDotDotEntry()
    {
        var work = Path.Combine(Path.GetTempPath(), "bepinex-modslip-" + Guid.NewGuid().ToString("N"));
        var plugins = Path.Combine(work, "game", "BepInEx", "plugins", "Translator");
        Directory.CreateDirectory(plugins);
        var zipPath = Path.Combine(work, "evil-mod.zip");
        try
        {
            CreateZipWithEntries(zipPath, ("../evil.dll", "malware"));
            var copied = new List<string>();
            var err = GameInstaller.ExtractTranslatorZip(
                zipPath,
                plugins,
                OverwritePolicy.Overwrite,
                backupDir: null,
                copied,
                _ => { });

            Assert.NotNull(err);
            Assert.Equal(InjectorErrorKind.InvalidPath, err!.Kind);
            Assert.Empty(copied);
            Assert.False(File.Exists(Path.Combine(work, "game", "BepInEx", "plugins", "evil.dll")));
        }
        finally
        {
            Directory.Delete(work, true);
        }
    }

    [Fact]
    public void ExtractZipToGameRoot_RejectsAbsoluteEntry()
    {
        var work = Path.Combine(Path.GetTempPath(), "bepinex-zipabs-" + Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(work, "game");
        Directory.CreateDirectory(gameRoot);
        var zipPath = Path.Combine(work, "abs.zip");
        try
        {
            // Unix 风格绝对路径条目
            CreateZipWithEntries(zipPath, ("/tmp/evil-abs.dll", "x"));
            var copied = new List<string>();
            var err = GameInstaller.ExtractZipToGameRoot(
                zipPath,
                gameRoot,
                OverwritePolicy.Overwrite,
                null,
                copied,
                _ => { });

            Assert.NotNull(err);
            Assert.Equal(InjectorErrorKind.InvalidPath, err!.Kind);
            Assert.Empty(copied);
        }
        finally
        {
            Directory.Delete(work, true);
        }
    }

    [Fact]
    public void IsStrictlyUnderDestination_PrefixBoundary()
    {
        var root = Path.Combine(Path.GetTempPath(), "games-root-foo");
        Assert.True(BepInExLayoutPlanner.IsStrictlyUnderDestination(root, Path.Combine(root, "winhttp.dll")));
        Assert.False(BepInExLayoutPlanner.IsStrictlyUnderDestination(root, root + "bar" + Path.DirectorySeparatorChar + "x"));
    }

    private static void CreateZipWithEntries(string zipPath, params (string Name, string Content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var e = zip.CreateEntry(name);
            using var w = new StreamWriter(e.Open());
            w.Write(content);
        }
    }
}

/// <summary>
/// 复现：已有 BepInEx 目录 + zip 条目「BepInEx」（无尾斜杠）不应 UnauthorizedAccess；
/// 以及 0 字节文件占住 BepInEx 时应返回 PathConflict 而非 PermissionDenied。
/// </summary>
public sealed class BepInExPathConflictExtractTests
{
    [Fact]
    public void ExtractZipToGameRoot_ExistingBepInExDirectory_SkipsFileStyleDirectoryMarker()
    {
        var work = Path.Combine(Path.GetTempPath(), "bepinex-dir-marker-" + Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(work, "game");
        Directory.CreateDirectory(Path.Combine(gameRoot, "BepInEx"));
        var zipPath = Path.Combine(work, "bepinex.zip");
        try
        {
            // 无尾斜杠的目录标记 + 真实文件条目
            CreateZipWithEntries(
                zipPath,
                ("BepInEx", ""),
                ("BepInEx/core/BepInEx.dll", "dll"),
                ("winhttp.dll", "dll"));

            var copied = new List<string>();
            var err = GameInstaller.ExtractZipToGameRoot(
                zipPath,
                gameRoot,
                OverwritePolicy.Overwrite,
                backupDir: null,
                copied,
                _ => { });

            Assert.Null(err);
            Assert.True(Directory.Exists(Path.Combine(gameRoot, "BepInEx")));
            Assert.False(File.Exists(Path.Combine(gameRoot, "BepInEx"))); // 不得写成文件
            Assert.True(File.Exists(Path.Combine(gameRoot, "BepInEx", "core", "BepInEx.dll")));
            Assert.True(File.Exists(Path.Combine(gameRoot, "winhttp.dll")));
            Assert.Contains(copied, p => p.EndsWith("BepInEx.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }
        }
    }

    [Fact]
    public void ExtractZipToGameRoot_ZeroByteBepInExFile_ReturnsPathConflictNotPermissionDenied()
    {
        var work = Path.Combine(Path.GetTempPath(), "bepinex-file-clash-" + Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(work, "game");
        Directory.CreateDirectory(gameRoot);
        // 0 字节文件占住本应为目录的 BepInEx
        File.WriteAllBytes(Path.Combine(gameRoot, "BepInEx"), Array.Empty<byte>());
        var zipPath = Path.Combine(work, "bepinex.zip");
        try
        {
            CreateZipWithEntries(
                zipPath,
                ("BepInEx", ""),
                ("BepInEx/core/BepInEx.dll", "dll"));

            var copied = new List<string>();
            var err = GameInstaller.ExtractZipToGameRoot(
                zipPath,
                gameRoot,
                OverwritePolicy.Overwrite,
                backupDir: null,
                copied,
                _ => { });

            Assert.NotNull(err);
            Assert.Equal(InjectorErrorKind.PathConflict, err!.Kind);
            Assert.NotEqual(InjectorErrorKind.PermissionDenied, err.Kind);
            Assert.Contains("文件", err.Message, StringComparison.Ordinal);
            Assert.Contains("BepInEx", err.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(copied);
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }
        }
    }

    [Fact]
    public void ProbeTopLevelLayoutConflicts_DetectsBepInExFile()
    {
        var work = Path.Combine(Path.GetTempPath(), "bepinex-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "BepInEx"), "");
        try
        {
            var err = GameInstaller.ProbeTopLevelLayoutConflicts(work);
            Assert.NotNull(err);
            Assert.Equal(InjectorErrorKind.PathConflict, err!.Kind);
        }
        finally
        {
            Directory.Delete(work, true);
        }
    }

    [Fact]
    public void ProbeTopLevelLayoutConflicts_AllowsExistingBepInExDirectory()
    {
        var work = Path.Combine(Path.GetTempPath(), "bepinex-probe-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(work, "BepInEx"));
        try
        {
            Assert.Null(GameInstaller.ProbeTopLevelLayoutConflicts(work));
        }
        finally
        {
            Directory.Delete(work, true);
        }
    }

    [Fact]
    public void ExtractZipToGameRoot_WinhttpDirectoryClash_ReturnsPathConflict()
    {
        var work = Path.Combine(Path.GetTempPath(), "bepinex-winhttp-dir-" + Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(work, "game");
        Directory.CreateDirectory(Path.Combine(gameRoot, "winhttp.dll"));
        var zipPath = Path.Combine(work, "bepinex.zip");
        try
        {
            CreateZipWithEntries(zipPath, ("winhttp.dll", "dll"));
            var copied = new List<string>();
            var err = GameInstaller.ExtractZipToGameRoot(
                zipPath,
                gameRoot,
                OverwritePolicy.Overwrite,
                null,
                copied,
                _ => { });

            Assert.NotNull(err);
            Assert.Equal(InjectorErrorKind.PathConflict, err!.Kind);
            Assert.Empty(copied);
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }
        }
    }

    [Fact]
    public void ExtractZipToGameRoot_NestedDllPathIsDirectory_ReturnsPathConflict()
    {
        // Probe 只查顶层；嵌套「本应为文件的路径已是目录」必须由解压路径返回 PathConflict
        var work = Path.Combine(Path.GetTempPath(), "bepinex-nested-clash-" + Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(work, "game");
        Directory.CreateDirectory(Path.Combine(gameRoot, "BepInEx", "core", "BepInEx.dll"));
        var zipPath = Path.Combine(work, "bepinex.zip");
        try
        {
            CreateZipWithEntries(
                zipPath,
                ("BepInEx/core/BepInEx.dll", "dll-bytes"),
                ("winhttp.dll", "dll"));

            var copied = new List<string>();
            var err = GameInstaller.ExtractZipToGameRoot(
                zipPath,
                gameRoot,
                OverwritePolicy.Overwrite,
                backupDir: null,
                copied,
                _ => { });

            Assert.NotNull(err);
            Assert.Equal(InjectorErrorKind.PathConflict, err!.Kind);
            Assert.NotEqual(InjectorErrorKind.PermissionDenied, err.Kind);
            Assert.Contains("目录", err.Message, StringComparison.Ordinal);
            Assert.Empty(copied);
            // 冲突后不得继续写出后续条目
            Assert.False(File.Exists(Path.Combine(gameRoot, "winhttp.dll")));
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }
        }
    }

    [Fact]
    public void ExtractTranslatorZip_DestIsDirectory_ReturnsPathConflict()
    {
        var work = Path.Combine(Path.GetTempPath(), "bepinex-mod-clash-" + Guid.NewGuid().ToString("N"));
        var plugins = Path.Combine(work, "game", "BepInEx", "plugins", "Translator");
        Directory.CreateDirectory(Path.Combine(plugins, "BepInExTranslator.dll"));
        var zipPath = Path.Combine(work, "mod.zip");
        try
        {
            CreateZipWithEntries(zipPath, ("BepInExTranslator.dll", "dll"));
            var copied = new List<string>();
            var err = GameInstaller.ExtractTranslatorZip(
                zipPath,
                plugins,
                OverwritePolicy.Overwrite,
                backupDir: null,
                copied,
                _ => { });

            Assert.NotNull(err);
            Assert.Equal(InjectorErrorKind.PathConflict, err!.Kind);
            Assert.Empty(copied);
        }
        finally
        {
            if (Directory.Exists(work))
            {
                Directory.Delete(work, true);
            }
        }
    }

    private static void CreateZipWithEntries(string zipPath, params (string Name, string Content)[] entries)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);
        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var e = zip.CreateEntry(name);
            using var w = new StreamWriter(e.Open());
            w.Write(content);
        }
    }
}

public sealed class DownloadUrlTrustTests
{
    [Theory]
    [InlineData("https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/a.zip")]
    [InlineData("https://objects.githubusercontent.com/github-production-release-asset-2e65be/x")]
    [InlineData("https://release-assets.githubusercontent.com/foo/bar.zip")]
    [InlineData("https://builds.bepinex.dev/projects/bepinex_be/x.zip")]
    public void ValidateDownloadUrl_AllowsTrustedHttpsHosts(string url)
    {
        Assert.Null(PackageDownloader.ValidateDownloadUrl(url));
    }

    [Theory]
    [InlineData("http://github.com/BepInEx/BepInEx/a.zip")] // 非 HTTPS
    [InlineData("https://example.com/mod.zip")]
    [InlineData("https://evil.github.com.attacker.example/x.zip")]
    [InlineData("ftp://github.com/a.zip")]
    [InlineData("not-a-url")]
    public void ValidateDownloadUrl_DeniesUntrusted(string url)
    {
        var err = PackageDownloader.ValidateDownloadUrl(url);
        Assert.NotNull(err);
        Assert.Equal(InjectorErrorKind.DownloadFailed, err!.Kind);
    }

    [Fact]
    public async Task DownloadUrlAsync_ThrowsOnDisallowedHost()
    {
        var cache = Path.Combine(Path.GetTempPath(), "bepinex-dl-deny-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cache);
        try
        {
            var dl = new PackageDownloader();
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                dl.DownloadUrlAsync("https://example.com/mod.zip", cache));
            Assert.Contains("允许列表", ex.Message);
        }
        finally
        {
            Directory.Delete(cache, true);
        }
    }
}
