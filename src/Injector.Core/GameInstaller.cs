using System.IO.Compression;
using System.Net.Http;

namespace BepInExTranslator.Injector.Core;

public sealed class InstallOptions
{
    public required GameDetectionResult Detection { get; init; }

    public PackageSourceOptions PackageSources { get; init; } = new();

    public OverwritePolicy OverwritePolicy { get; init; } = OverwritePolicy.BackupThenOverwrite;

    /// <summary>下载缓存目录；默认游戏根下 `.bepinex-translator-cache`（勿提交）。</summary>
    public string? CacheDirectory { get; init; }

    /// <summary>仓库内 config/Translator.cfg.example 的绝对路径；空则按 RepositoryRoot 推断。</summary>
    public string? ConfigExampleSourcePath { get; init; }
}

public sealed class InstallResult
{
    public required bool Success { get; init; }

    public InjectorError? Error { get; init; }

    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> CopiedFiles { get; init; } = Array.Empty<string>();

    public string? BackupDirectory { get; init; }
}

/// <summary>
/// 安装编排：检测结果 → 解析包 → 下载/物化 → 解压 BepInEx → 放入 plugins + 配置示例。
/// </summary>
public sealed class GameInstaller : IInstaller
{
    private readonly IPackageResolver _resolver;
    private readonly PackageDownloader _downloader;

    public GameInstaller(IPackageResolver? resolver = null, PackageDownloader? downloader = null)
    {
        _resolver = resolver ?? new PackageResolver();
        _downloader = downloader ?? new PackageDownloader();
    }

    public async Task<InstallResult> InstallAsync(
        InstallOptions options,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var messages = new List<string>();
        var copied = new List<string>();

        void Report(string message, InstallPhase phase = InstallPhase.General, int? percent = null)
        {
            messages.Add(message);
            progress?.Report(new InstallProgress
            {
                Message = message,
                Phase = phase,
                Percent = percent,
            });
        }

        var detection = options.Detection;

        // —— 安全校验 ——
        if (!detection.IsValidUnityGame || detection.Runtime == UnityRuntimeKind.Unknown)
        {
            var err = detection.Error
                      ?? InjectorError.NotUnityGame("探测失败：路径不像有效的 Unity Mono/IL2CPP 游戏，已拒绝安装。");
            Report(err.Message, InstallPhase.Detect);
            return Fail(messages, err, copied);
        }

        var gameRoot = detection.GameRoot;
        if (!Directory.Exists(gameRoot))
        {
            var err = InjectorError.InvalidPath("游戏根目录不存在。", gameRoot);
            Report(err.Message, InstallPhase.Detect);
            return Fail(messages, err, copied);
        }

        Report($"目标：{gameRoot}", InstallPhase.Detect, 5);
        Report($"运行时：{detection.RuntimeDisplayName}", InstallPhase.Detect, 8);
        if (detection.UnityVersion != null)
        {
            Report($"Unity：{detection.UnityVersion}", InstallPhase.Detect, 10);
        }

        // —— 解析包 ——
        Report("正在解析安装包…", InstallPhase.ResolvePackages, 15);
        var resolve = _resolver.Resolve(detection.Runtime, options.PackageSources);
        if (!resolve.Success || resolve.Error != null)
        {
            var err = resolve.Error ?? InjectorError.PackageNotFound("解析安装包失败。");
            Report(err.Message, InstallPhase.ResolvePackages);
            return Fail(messages, err, copied);
        }

        foreach (var p in resolve.Packages)
        {
            Report($"包：{p.DisplayName}", InstallPhase.ResolvePackages, 18);
        }

        var cache = options.CacheDirectory
                    ?? Path.Combine(gameRoot, ".bepinex-translator-cache");
        try
        {
            Directory.CreateDirectory(cache);
        }
        catch (UnauthorizedAccessException ex)
        {
            var err = InjectorError.PermissionDenied("无法创建缓存目录。", ex.Message);
            Report(err.Message, InstallPhase.Download);
            return Fail(messages, err, copied);
        }

        Report($"缓存目录：{cache}", InstallPhase.Download, 20);

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
            cancellationToken.ThrowIfCancellationRequested();

            // —— BepInEx ——
            var bepinexPkg = resolve.Packages.First(p => p.Kind == PackageKind.BepInEx);
            Report("正在获取 BepInEx 包…", InstallPhase.Download, 30);
            string bepinexMaterialized;
            try
            {
                bepinexMaterialized = await _downloader
                    .MaterializeAsync(
                        bepinexPkg,
                        cache,
                        new Progress<string>(m => Report(m, InstallPhase.Download)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                var err = InjectorError.DownloadFailed("下载 BepInEx 失败。", ex.Message);
                Report(err.Message, InstallPhase.Download);
                return Fail(messages, err, copied, backupDir);
            }
            catch (InvalidOperationException ex)
            {
                // URL 信任策略拒绝等
                var err = InjectorError.DownloadFailed(ex.Message);
                Report(err.Message, InstallPhase.Download);
                return Fail(messages, err, copied, backupDir);
            }
            catch (OperationCanceledException)
            {
                var err = InjectorError.Cancelled();
                Report(err.Message);
                return Fail(messages, err, copied, backupDir);
            }

            Report("正在解压 BepInEx 到游戏根目录…", InstallPhase.Extract, 50);
            try
            {
                var extractErr = ExtractZipToGameRoot(
                    bepinexMaterialized,
                    gameRoot,
                    options.OverwritePolicy,
                    backupDir,
                    copied,
                    m => Report(m, InstallPhase.Extract));
                if (extractErr != null)
                {
                    Report(extractErr.Message, InstallPhase.Extract);
                    return Fail(messages, extractErr, copied, backupDir);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                var err = InjectorError.PermissionDenied("解压 BepInEx 时权限不足。", ex.Message);
                Report(err.Message, InstallPhase.Extract);
                return Fail(messages, err, copied, backupDir);
            }
            catch (IOException ex)
            {
                var err = InjectorError.ExtractFailed("解压 BepInEx 失败。", ex.Message);
                Report(err.Message, InstallPhase.Extract);
                return Fail(messages, err, copied, backupDir);
            }

            var missing = BepInExLayoutPlanner.MissingExpectedEntries(gameRoot, detection.Runtime);
            if (missing.Count > 0)
            {
                Report("警告：解压后缺少部分期望条目：" + string.Join(", ", missing)
                       + "（不同 BepInEx 包可能用 version.dll 代替 winhttp.dll，请人工确认）。",
                    InstallPhase.Extract, 60);
            }
            else
            {
                Report("BepInEx 关键布局条目已就位。", InstallPhase.Extract, 60);
            }

            // —— Translator 模组 ——
            var modPkg = resolve.Packages.First(p => p.Kind == PackageKind.TranslatorMod);
            Report("正在获取翻译模组包…", InstallPhase.Download, 70);
            string modMaterialized;
            try
            {
                modMaterialized = await _downloader
                    .MaterializeAsync(
                        modPkg,
                        cache,
                        new Progress<string>(m => Report(m, InstallPhase.Download)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                var err = InjectorError.DownloadFailed(
                    "下载翻译模组失败。若 GitHub Release 尚未发布对应资产，请先构建 artifacts/mono 或 artifacts/il2cpp，或设置本地 TranslatorLocalZipPath。详见 docs/injector.md。",
                    ex.Message);
                Report(err.Message, InstallPhase.Download);
                return Fail(messages, err, copied, backupDir);
            }
            catch (InvalidOperationException ex)
            {
                // URL 信任策略拒绝等
                var err = InjectorError.DownloadFailed(ex.Message);
                Report(err.Message, InstallPhase.Download);
                return Fail(messages, err, copied, backupDir);
            }

            var layout = BepInExLayoutPlanner.PlanTranslatorLayout(gameRoot);
            Directory.CreateDirectory(layout.PluginDirectory);
            Directory.CreateDirectory(layout.ConfigDirectory);

            Report($"安装翻译模组 → {layout.RelativePluginDirectory}", InstallPhase.WritePlugins, 80);
            try
            {
                if (Directory.Exists(modMaterialized))
                {
                    CopyTranslatorFromDirectory(
                        modMaterialized,
                        layout.PluginDirectory,
                        options.OverwritePolicy,
                        backupDir,
                        copied,
                        m => Report(m, InstallPhase.WritePlugins));
                }
                else
                {
                    var modExtractErr = ExtractTranslatorZip(
                        modMaterialized,
                        layout.PluginDirectory,
                        options.OverwritePolicy,
                        backupDir,
                        copied,
                        m => Report(m, InstallPhase.WritePlugins));
                    if (modExtractErr != null)
                    {
                        Report(modExtractErr.Message, InstallPhase.WritePlugins);
                        return Fail(messages, modExtractErr, copied, backupDir);
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                var err = InjectorError.PermissionDenied("写入翻译模组时权限不足。", ex.Message);
                Report(err.Message, InstallPhase.WritePlugins);
                return Fail(messages, err, copied, backupDir);
            }
            catch (IOException ex)
            {
                var err = InjectorError.ExtractFailed("安装翻译模组失败。", ex.Message);
                Report(err.Message, InstallPhase.WritePlugins);
                return Fail(messages, err, copied, backupDir);
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
                    m => Report(m, InstallPhase.WriteConfig));
                Report(
                    $"已复制配置示例 → {layout.RelativeConfigDirectory}/{BepInExLayoutPlanner.ConfigExampleFileName}",
                    InstallPhase.WriteConfig,
                    90);
            }
            else
            {
                Report("未找到 config/Translator.cfg.example；跳过配置示例复制。可稍后手动放置。",
                    InstallPhase.WriteConfig, 90);
            }

            Report("安装完成。请启动游戏一次，检查 BepInEx/LogOutput.log 是否生成，并确认插件已加载。",
                InstallPhase.Done, 100);
            return new InstallResult
            {
                Success = true,
                Messages = messages,
                CopiedFiles = copied,
                BackupDirectory = backupDir,
            };
        }
        catch (OperationCanceledException)
        {
            var err = InjectorError.Cancelled();
            Report(err.Message);
            return Fail(messages, err, copied, backupDir);
        }
        catch (Exception ex)
        {
            var err = InjectorError.Unexpected("安装失败：" + ex.Message, ex.ToString());
            Report(err.Message);
            return Fail(messages, err, copied, backupDir);
        }
    }

    private static InstallResult Fail(
        List<string> messages,
        InjectorError error,
        List<string>? copied = null,
        string? backupDir = null)
    {
        if (messages.Count == 0 || messages[^1] != error.Message)
        {
            messages.Add(error.Message);
        }

        return new InstallResult
        {
            Success = false,
            Error = error,
            Messages = messages,
            CopiedFiles = copied ?? new List<string>(),
            BackupDirectory = backupDir,
        };
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
    /// 解压 BepInEx zip 到游戏根。拒绝 zip-slip（<c>..</c> / 逃逸目标根）。
    /// 成功返回 <c>null</c>，违规则返回类型化 <see cref="InjectorError"/>。
    /// </summary>
    internal static InjectorError? ExtractZipToGameRoot(
        string zipPath,
        string gameRoot,
        OverwritePolicy policy,
        string? backupDir,
        List<string> copied,
        Action<string> report)
    {
        // —— 先探测顶层布局路径冲突（如 0 字节文件占住 BepInEx 目录名）——
        var probeErr = ProbeTopLevelLayoutConflicts(gameRoot);
        if (probeErr != null)
        {
            return probeErr;
        }

        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            var relative = BepInExLayoutPlanner.NormalizeZipEntry(entry.FullName);
            if (relative is null)
            {
                return InjectorError.InvalidPath(
                    "拒绝解压：zip 条目包含非法路径（可能为路径穿越）。",
                    entry.FullName);
            }

            // —— 显式目录条目（尾斜杠）或无尾斜杠的目录标记 ——
            if (string.IsNullOrEmpty(relative)
                || relative.EndsWith('/')
                || IsDirectoryMarkerEntry(entry, relative))
            {
                var dirRel = relative.TrimEnd('/');
                if (string.IsNullOrEmpty(dirRel))
                {
                    continue;
                }

                var dir = Path.Combine(gameRoot, dirRel.Replace('/', Path.DirectorySeparatorChar));
                if (!BepInExLayoutPlanner.IsStrictlyUnderDestination(gameRoot, dir))
                {
                    return InjectorError.InvalidPath(
                        "拒绝解压：目录条目落在游戏根之外。",
                        entry.FullName);
                }

                var dirErr = EnsureDirectoryForExtract(dir);
                if (dirErr != null)
                {
                    return dirErr;
                }

                continue;
            }

            var dest = Path.Combine(gameRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!BepInExLayoutPlanner.IsStrictlyUnderDestination(gameRoot, dest))
            {
                return InjectorError.InvalidPath(
                    "拒绝解压：文件条目落在游戏根之外（zip-slip）。",
                    entry.FullName);
            }

            // —— 目标已是目录：勿 ExtractToFile（否则 UnauthorizedAccessException → 误报权限不足）——
            if (Directory.Exists(dest))
            {
                report($"跳过目录标记条目（目标已是目录）：{relative}");
                continue;
            }

            var parent = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(parent))
            {
                var parentErr = EnsureDirectoryForExtract(parent);
                if (parentErr != null)
                {
                    return parentErr;
                }
            }

            var extractErr = ExtractEntryWithPolicy(entry, dest, policy, backupDir, copied, report);
            if (extractErr != null)
            {
                return extractErr;
            }
        }

        return null;
    }

    /// <summary>
    /// 安装前/解压前探测游戏根顶层布局冲突：必须为目录的路径被文件占用，或必须为文件的路径被目录占用。
    /// </summary>
    internal static InjectorError? ProbeTopLevelLayoutConflicts(string gameRoot)
    {
        foreach (var name in KnownLayoutDirectoryNames)
        {
            var path = Path.Combine(gameRoot, name);
            if (File.Exists(path))
            {
                return InjectorError.PathConflict(
                    $"游戏根目录下已存在名为「{name}」的文件，无法创建同名 BepInEx/布局目录。"
                    + "请先删除或重命名该文件后再安装（这不是权限问题）。",
                    path);
            }
        }

        foreach (var name in KnownLayoutFileNames)
        {
            var path = Path.Combine(gameRoot, name);
            if (Directory.Exists(path))
            {
                return InjectorError.PathConflict(
                    $"游戏根目录下「{name}」已是目录，但安装需要同名文件。"
                    + "请先删除或重命名该目录后再安装（这不是权限问题）。",
                    path);
            }
        }

        return null;
    }

    /// <summary>BepInEx / doorstop 布局中应为目录的顶层名。</summary>
    private static readonly string[] KnownLayoutDirectoryNames = { "BepInEx", "dotnet" };

    /// <summary>BepInEx / doorstop 布局中应为文件的顶层名。</summary>
    private static readonly string[] KnownLayoutFileNames =
    {
        "winhttp.dll",
        "doorstop_config.ini",
        ".doorstop_version",
        "changelog.txt",
    };

    /// <summary>
    /// 无尾斜杠但仍应视为目录标记的 zip 条目（常见于部分打包工具写出的 <c>BepInEx</c>）。
    /// </summary>
    internal static bool IsDirectoryMarkerEntry(ZipArchiveEntry entry, string normalizedRelative)
    {
        if (string.IsNullOrEmpty(normalizedRelative) || normalizedRelative.EndsWith('/'))
        {
            return true;
        }

        // 已有明确文件扩展名的不当作目录标记
        var leaf = normalizedRelative.Contains('/')
            ? normalizedRelative[(normalizedRelative.LastIndexOf('/') + 1)..]
            : normalizedRelative;
        if (leaf.Contains('.') && !leaf.StartsWith('.'))
        {
            return false;
        }

        // 已知顶层布局目录，或 0 字节且无扩展名的条目
        if (KnownLayoutDirectoryNames.Any(d =>
                normalizedRelative.Equals(d, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return entry.Length == 0 && !leaf.Contains('.');
    }

    /// <summary>
    /// 确保 <paramref name="directoryPath"/> 可作为目录使用：若同名文件已存在则返回 <see cref="InjectorErrorKind.PathConflict"/>。
    /// </summary>
    internal static InjectorError? EnsureDirectoryForExtract(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            return null;
        }

        // —— 检查目标及其尚未存在的祖先是否被文件占用 ——
        var current = directoryPath;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(current))
            {
                var name = Path.GetFileName(current);
                return InjectorError.PathConflict(
                    $"无法创建目录「{name}」：该路径已存在同名文件。"
                    + "请先删除或重命名该文件后再安装（这不是权限问题）。",
                    current);
            }

            if (Directory.Exists(current))
            {
                break;
            }

            current = Path.GetDirectoryName(current);
        }

        Directory.CreateDirectory(directoryPath);
        return null;
    }

    /// <summary>
    /// 解压翻译模组 zip 到 plugins/Translator。拒绝 zip-slip。
    /// 成功返回 <c>null</c>，违规则返回类型化 <see cref="InjectorError"/>。
    /// </summary>
    internal static InjectorError? ExtractTranslatorZip(
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

            var name = entry.FullName.Replace('\\', '/');

            // —— 原始条目先做穿越/绝对路径检测（避免 GetFileName 吞掉 ..）——
            if (HasZipSlipRisk(name))
            {
                return InjectorError.InvalidPath(
                    "拒绝解压翻译模组：zip 条目包含非法路径（可能为路径穿越）。",
                    entry.FullName);
            }

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

            // 对相对段同样拒绝 .. / 绝对路径
            var safeRelative = BepInExLayoutPlanner.NormalizeZipEntry(relative);
            if (safeRelative is null)
            {
                return InjectorError.InvalidPath(
                    "拒绝解压翻译模组：zip 条目包含非法路径（可能为路径穿越）。",
                    entry.FullName);
            }

            if (string.IsNullOrWhiteSpace(safeRelative) || safeRelative.EndsWith('/'))
            {
                continue;
            }

            var dest = Path.Combine(pluginDirectory, safeRelative.Replace('/', Path.DirectorySeparatorChar));
            if (!BepInExLayoutPlanner.IsStrictlyUnderDestination(pluginDirectory, dest))
            {
                return InjectorError.InvalidPath(
                    "拒绝解压翻译模组：文件条目落在插件目录之外（zip-slip）。",
                    entry.FullName);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            var extractErr = ExtractEntryWithPolicy(entry, dest, policy, backupDir, copied, report);
            if (extractErr != null)
            {
                return extractErr;
            }
        }

        return null;
    }

    /// <summary>条目名是否含 <c>..</c> 段、绝对路径或盘符（zip-slip 风险）。</summary>
    internal static bool HasZipSlipRisk(string entryFullName)
    {
        if (string.IsNullOrWhiteSpace(entryFullName))
        {
            return true;
        }

        var raw = entryFullName.Replace('\\', '/');
        if (raw.StartsWith('/') || raw.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        if (raw.Length >= 2 && char.IsLetter(raw[0]) && raw[1] == ':')
        {
            return true;
        }

        foreach (var segment in raw.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == "..")
            {
                return true;
            }
        }

        return false;
    }

    internal static void CopyTranslatorFromDirectory(
        string sourceDir,
        string pluginDirectory,
        OverwritePolicy policy,
        string? backupDir,
        List<string> copied,
        Action<string> report)
    {
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

    private static InjectorError? ExtractEntryWithPolicy(
        ZipArchiveEntry entry,
        string dest,
        OverwritePolicy policy,
        string? backupDir,
        List<string> copied,
        Action<string> report)
    {
        // —— 目标已是目录：绝不能 ExtractToFile ——
        if (Directory.Exists(dest))
        {
            report($"跳过目录标记条目（目标已是目录）：{dest}");
            return null;
        }

        if (File.Exists(dest))
        {
            switch (policy)
            {
                case OverwritePolicy.SkipExisting:
                    report($"跳过已存在：{dest}");
                    return null;
                case OverwritePolicy.BackupThenOverwrite:
                    BackupFile(dest, backupDir, report);
                    break;
                case OverwritePolicy.Overwrite:
                    break;
            }
        }

        entry.ExtractToFile(dest, overwrite: true);
        copied.Add(dest);
        return null;
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
