using System.IO.Compression;

namespace BepInExTranslator.Injector.Core;

public sealed class InstallOptions
{
    public required GameDetectionResult Detection { get; init; }

    public PackageSourceOptions PackageSources { get; init; } = new();

    public OverwritePolicy OverwritePolicy { get; init; } = OverwritePolicy.BackupThenOverwrite;

    /// <summary>下载缓存目录；默认游戏根下 `.bepinex-translator-cache`（gitignore 友好，勿提交）。</summary>
    public string? CacheDirectory { get; init; }

    /// <summary>仓库内 config/Translator.cfg.example 的绝对路径；空则按 RepositoryRoot 推断。</summary>
    public string? ConfigExampleSourcePath { get; init; }
}

public sealed class InstallResult
{
    public required bool Success { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CopiedFiles { get; init; } = Array.Empty<string>();

    public string? BackupDirectory { get; init; }
}

/// <summary>
/// 安装编排：检测结果 → 解析包 → 下载/物化 → 解压 BepInEx → 放入 plugins + 配置示例。
/// </summary>
public sealed class GameInstaller
{
    private readonly PackageResolver _resolver;
    private readonly PackageDownloader _downloader;

    public GameInstaller(PackageResolver? resolver = null, PackageDownloader? downloader = null)
    {
        _resolver = resolver ?? new PackageResolver();
        _downloader = downloader ?? new PackageDownloader();
    }

    public async Task<InstallResult> InstallAsync(
        InstallOptions options,
        IProgress<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        var copied = new List<string>();
        void Report(string m)
        {
            messages.Add(m);
            log?.Report(m);
        }

        var detection = options.Detection;

        // —— 安全校验 ——
        if (!detection.IsValidUnityGame || detection.Runtime == UnityRuntimeKind.Unknown)
        {
            return Fail(messages, "探测失败：路径不像有效的 Unity Mono/IL2CPP 游戏，已拒绝安装。");
        }

        var gameRoot = detection.GameRoot;
        if (!Directory.Exists(gameRoot))
        {
            return Fail(messages, "游戏根目录不存在。");
        }

        Report($"目标：{gameRoot}");
        Report($"运行时：{detection.RuntimeDisplayName}");
        if (detection.UnityVersion != null)
        {
            Report($"Unity：{detection.UnityVersion}");
        }

        // —— 解析包 ——
        IReadOnlyList<ResolvedPackage> packages;
        try
        {
            packages = _resolver.Resolve(detection.Runtime, options.PackageSources);
        }
        catch (Exception ex)
        {
            return Fail(messages, "解析安装包失败：" + ex.Message);
        }

        foreach (var p in packages)
        {
            Report($"包：{p.DisplayName}");
        }

        var cache = options.CacheDirectory
                    ?? Path.Combine(gameRoot, ".bepinex-translator-cache");
        Directory.CreateDirectory(cache);
        Report($"缓存目录：{cache}");

        string? backupDir = null;
        if (options.OverwritePolicy == OverwritePolicy.BackupThenOverwrite)
        {
            backupDir = Path.Combine(
                gameRoot,
                ".injector-backup",
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
        }

        try
        {
            // —— BepInEx ——
            var bepinexPkg = packages.First(p => p.Kind == PackageKind.BepInEx);
            var bepinexMaterialized = await _downloader
                .MaterializeAsync(bepinexPkg, cache, log, cancellationToken)
                .ConfigureAwait(false);

            Report("正在解压 BepInEx 到游戏根目录…");
            ExtractZipToGameRoot(
                bepinexMaterialized,
                gameRoot,
                options.OverwritePolicy,
                backupDir,
                copied,
                Report);

            var missing = BepInExLayoutPlanner.MissingExpectedEntries(gameRoot, detection.Runtime);
            if (missing.Count > 0)
            {
                Report("警告：解压后缺少部分期望条目：" + string.Join(", ", missing)
                       + "（不同 BepInEx 包可能用 version.dll 代替 winhttp.dll，请人工确认）。");
            }
            else
            {
                Report("BepInEx 关键布局条目已就位。");
            }

            // —— Translator 模组 ——
            var modPkg = packages.First(p => p.Kind == PackageKind.TranslatorMod);
            var modMaterialized = await _downloader
                .MaterializeAsync(modPkg, cache, log, cancellationToken)
                .ConfigureAwait(false);

            var layout = BepInExLayoutPlanner.PlanTranslatorLayout(gameRoot);
            Directory.CreateDirectory(layout.PluginDirectory);
            Directory.CreateDirectory(layout.ConfigDirectory);

            Report($"安装翻译模组 → {layout.RelativePluginDirectory}");
            if (Directory.Exists(modMaterialized))
            {
                CopyTranslatorFromDirectory(
                    modMaterialized,
                    layout.PluginDirectory,
                    options.OverwritePolicy,
                    backupDir,
                    copied,
                    Report);
            }
            else
            {
                ExtractTranslatorZip(
                    modMaterialized,
                    layout.PluginDirectory,
                    options.OverwritePolicy,
                    backupDir,
                    copied,
                    Report);
            }

            // —— 配置示例 ——
            var cfgSource = ResolveConfigExampleSource(options);
            if (cfgSource != null && File.Exists(cfgSource))
            {
                CopyFileWithPolicy(
                    cfgSource,
                    layout.ConfigExampleDestination,
                    options.OverwritePolicy,
                    backupDir,
                    copied,
                    Report);
                Report($"已复制配置示例 → {layout.RelativeConfigDirectory}/{BepInExLayoutPlanner.ConfigExampleFileName}");
            }
            else
            {
                Report("未找到 config/Translator.cfg.example；跳过配置示例复制。可稍后手动放置。");
            }

            Report("安装完成。请启动游戏一次，检查 BepInEx/LogOutput.log 是否生成，并确认插件已加载。");
            return new InstallResult
            {
                Success = true,
                Messages = messages,
                CopiedFiles = copied,
                BackupDirectory = backupDir,
            };
        }
        catch (Exception ex)
        {
            Report("安装失败：" + ex.Message);
            return new InstallResult
            {
                Success = false,
                Messages = messages,
                CopiedFiles = copied,
                BackupDirectory = backupDir,
            };
        }
    }

    private static InstallResult Fail(List<string> messages, string msg)
    {
        messages.Add(msg);
        return new InstallResult { Success = false, Messages = messages };
    }

    internal static string? ResolveConfigExampleSource(InstallOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConfigExampleSourcePath))
        {
            return Path.GetFullPath(options.ConfigExampleSourcePath);
        }

        var root = options.PackageSources.RepositoryRoot;
        if (string.IsNullOrWhiteSpace(root))
        {
            return null;
        }

        return Path.Combine(root, "config", "Translator.cfg.example");
    }

    /// <summary>
    /// 将 BepInEx zip 解压到游戏根；条目路径经 <see cref="BepInExLayoutPlanner.NormalizeZipEntry"/> 规范化。
    /// </summary>
    internal static void ExtractZipToGameRoot(
        string zipPath,
        string gameRoot,
        OverwritePolicy policy,
        string? backupDir,
        List<string> copied,
        Action<string> report)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) && entry.FullName.EndsWith('/'))
            {
                continue;
            }

            var relative = BepInExLayoutPlanner.NormalizeZipEntry(entry.FullName);
            if (string.IsNullOrEmpty(relative) || relative.EndsWith('/'))
            {
                var dir = Path.Combine(gameRoot, relative.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(dir);
                continue;
            }

            var dest = Path.Combine(gameRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            ExtractEntryWithPolicy(entry, dest, policy, backupDir, copied, report);
        }
    }

    internal static void ExtractTranslatorZip(
        string zipPath,
        string pluginDirectory,
        OverwritePolicy policy,
        string? backupDir,
        List<string> copied,
        Action<string> report)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            // zip 内可能已带 BepInEx/plugins/Translator/ 前缀，剥到文件名层
            var name = entry.FullName.Replace('\\', '/');
            var marker = "BepInEx/plugins/Translator/";
            string relative;
            var idx = name.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                relative = name[(idx + marker.Length)..];
            }
            else
            {
                relative = Path.GetFileName(name);
            }

            if (string.IsNullOrWhiteSpace(relative) || relative.EndsWith('/'))
            {
                continue;
            }

            var dest = Path.Combine(pluginDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            ExtractEntryWithPolicy(entry, dest, policy, backupDir, copied, report);
        }
    }

    internal static void CopyTranslatorFromDirectory(
        string sourceDir,
        string pluginDirectory,
        OverwritePolicy policy,
        string? backupDir,
        List<string> copied,
        Action<string> report)
    {
        // 仅复制插件 DLL（及同目录附属文件），避免把整个 artifacts 噪音带入
        var required = new[] { "BepInExTranslator.dll", "BepInExTranslator.Core.dll" };
        foreach (var file in required)
        {
            var src = Path.Combine(sourceDir, file);
            if (!File.Exists(src))
            {
                throw new FileNotFoundException("翻译模组目录缺少必要文件。", src);
            }

            var dest = Path.Combine(pluginDirectory, file);
            CopyFileWithPolicy(src, dest, policy, backupDir, copied, report);
        }

        // 可选：同目录下其它 dll / json
        foreach (var src in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(src);
            if (required.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("translations.json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dest = Path.Combine(pluginDirectory, name);
            CopyFileWithPolicy(src, dest, policy, backupDir, copied, report);
        }
    }

    private static void ExtractEntryWithPolicy(
        ZipArchiveEntry entry,
        string dest,
        OverwritePolicy policy,
        string? backupDir,
        List<string> copied,
        Action<string> report)
    {
        if (File.Exists(dest))
        {
            switch (policy)
            {
                case OverwritePolicy.SkipExisting:
                    report($"跳过已存在：{dest}");
                    return;
                case OverwritePolicy.BackupThenOverwrite:
                    BackupFile(dest, backupDir, report);
                    break;
                case OverwritePolicy.Overwrite:
                    break;
            }
        }

        entry.ExtractToFile(dest, overwrite: true);
        copied.Add(dest);
    }

    internal static void CopyFileWithPolicy(
        string source,
        string dest,
        OverwritePolicy policy,
        string? backupDir,
        List<string> copied,
        Action<string> report)
    {
        if (File.Exists(dest))
        {
            switch (policy)
            {
                case OverwritePolicy.SkipExisting:
                    report($"跳过已存在：{dest}");
                    return;
                case OverwritePolicy.BackupThenOverwrite:
                    BackupFile(dest, backupDir, report);
                    break;
                case OverwritePolicy.Overwrite:
                    break;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.Copy(source, dest, overwrite: true);
        copied.Add(dest);
    }

    private static void BackupFile(string dest, string? backupDir, Action<string> report)
    {
        if (string.IsNullOrWhiteSpace(backupDir))
        {
            return;
        }

        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, Path.GetFileName(dest) + "." + Guid.NewGuid().ToString("N")[..8]);
        File.Copy(dest, backupPath, overwrite: false);
        report($"已备份：{dest} → {backupPath}");
    }
}
