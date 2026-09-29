using Injector.Core.Abstractions;
using Injector.Core.Localization;
using Injector.Core.Models;

namespace Injector.Core.Install;

/// <summary>
/// Stub installer for GUI demo: simulates §7 stages with short delays and progress reports.
/// Does <b>not</b> download or install real BepInEx (backend owns that).
/// Optionally writes a demo marker / mini layout when <c>INJECTOR_STUB_WRITE=1</c>
/// or the target looks like a test fixture — useful for overwrite UX, not a real install.
/// </summary>
public sealed class StubInstaller : IInstaller
{
    private readonly IPackageResolver _packageResolver;
    private readonly TimeSpan _stageDelay;

    /// <summary>
    /// Environment variable that enables writing marker/layout files even outside fixtures.
    /// </summary>
    public const string StubWriteEnvVar = "INJECTOR_STUB_WRITE";

    public StubInstaller(IPackageResolver packageResolver, TimeSpan? stageDelay = null)
    {
        _packageResolver = packageResolver;
        // Keep delays short so unit tests and demos finish quickly.
        _stageDelay = stageDelay ?? TimeSpan.FromMilliseconds(40);
    }

    public async Task<InstallResult> InstallAsync(
        InstallRequest request,
        IProgress<InstallProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Probe);

        string gamePath = string.IsNullOrWhiteSpace(request.GamePath)
            ? request.Probe.GamePath
            : request.GamePath;

        // Stage: Detecting — re-validate probe inputs for clear Chinese errors.
        Report(progress, InstallStage.Detecting, UiStrings.StageDetecting, 5);
        await DelayAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(gamePath) || !Directory.Exists(gamePath))
        {
            return InstallResult.Fail(InstallErrorCode.InvalidPath, UiStrings.ErrInvalidPath);
        }

        if (!request.Probe.IsValid)
        {
            InstallErrorCode code = request.Probe.ErrorCode ?? InstallErrorCode.NotUnity;
            return InstallResult.Fail(code, request.Probe.ErrorMessage ?? UiStrings.ErrorMessage(code));
        }

        if (request.Probe.Runtime is RuntimeKind.Unknown)
        {
            return InstallResult.Fail(InstallErrorCode.UnknownRuntime, UiStrings.ErrUnknownRuntime);
        }

        // Already-installed guard: BepInEx folder or stub marker without Overwrite.
        bool alreadyPresent = LooksAlreadyInstalled(gamePath);
        if (alreadyPresent && !request.Overwrite)
        {
            return InstallResult.Fail(InstallErrorCode.AlreadyInstalled, UiStrings.ErrAlreadyInstalled);
        }

        // Stage: Resolving packages for Mono / IL2CPP.
        Report(progress, InstallStage.Resolving, UiStrings.StageResolving, 15);
        await DelayAsync(cancellationToken).ConfigureAwait(false);

        PackageIds? packages = _packageResolver.Resolve(request.Probe.Runtime);
        if (packages is null)
        {
            return InstallResult.Fail(InstallErrorCode.UnknownRuntime, UiStrings.ErrUnknownRuntime);
        }

        // Stage: "Downloading" — stub skips real HTTP (IPackageDownloader reserved for backend).
        Report(progress, InstallStage.DownloadingBepInEx,
            $"{UiStrings.StageDownloadingBepInEx}（stub 跳过：{packages.BepInExPackageId}）", 30);
        await DelayAsync(cancellationToken).ConfigureAwait(false);

        Report(progress, InstallStage.DownloadingPlugin,
            $"{UiStrings.StageDownloadingPlugin}（stub 跳过：{packages.PluginPackageId}）", 45);
        await DelayAsync(cancellationToken).ConfigureAwait(false);

        Report(progress, InstallStage.Extracting, UiStrings.StageExtracting, 60);
        await DelayAsync(cancellationToken).ConfigureAwait(false);

        // Stage: WritingLayout — optionally materialize a demo layout for fixtures / INJECTOR_STUB_WRITE.
        Report(progress, InstallStage.WritingLayout, UiStrings.StageWritingLayout, 75);
        await DelayAsync(cancellationToken).ConfigureAwait(false);

        bool shouldWrite = ShouldWriteLayout(gamePath);
        if (shouldWrite)
        {
            try
            {
                WriteStubLayout(gamePath, request.Probe.Runtime, packages);
            }
            catch (UnauthorizedAccessException)
            {
                return InstallResult.Fail(InstallErrorCode.PermissionDenied, UiStrings.ErrPermission);
            }
            catch (IOException ex)
            {
                return InstallResult.Fail(
                    InstallErrorCode.UnpackOrWriteFailed,
                    $"{UiStrings.ErrUnpackWrite} ({ex.Message})");
            }
        }

        // Stage: CopyingConfig — copy Translator.cfg.example when repo config is discoverable.
        Report(progress, InstallStage.CopyingConfig, UiStrings.StageCopyingConfig, 90);
        await DelayAsync(cancellationToken).ConfigureAwait(false);

        if (shouldWrite)
        {
            try
            {
                TryCopyConfigExample(gamePath);
            }
            catch (UnauthorizedAccessException)
            {
                return InstallResult.Fail(InstallErrorCode.PermissionDenied, UiStrings.ErrPermission);
            }
            catch (IOException ex)
            {
                return InstallResult.Fail(
                    InstallErrorCode.UnpackOrWriteFailed,
                    $"{UiStrings.ErrUnpackWrite} ({ex.Message})");
            }
        }

        Report(progress, InstallStage.Done, UiStrings.StageDone, 100);
        string suffix = shouldWrite
            ? "已写入 stub 布局标记。"
            : "未写入磁盘（设置 INJECTOR_STUB_WRITE=1 或对 fixtures 路径才会落盘）。";
        return InstallResult.Ok($"{UiStrings.SuccessBanner} {suffix} 包：{packages.Description}");
    }

    private static bool LooksAlreadyInstalled(string gamePath)
    {
        return Directory.Exists(Path.Combine(gamePath, InjectorPaths.BepInExDirectoryName))
               || File.Exists(Path.Combine(gamePath, InjectorPaths.StubInstallMarkerFileName));
    }

    /// <summary>
    /// Write layout for fixtures always; elsewhere only when INJECTOR_STUB_WRITE=1.
    /// </summary>
    private static bool ShouldWriteLayout(string gamePath)
    {
        string? env = Environment.GetEnvironmentVariable(StubWriteEnvVar);
        if (string.Equals(env, "1", StringComparison.Ordinal) ||
            string.Equals(env, "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Fixture heuristic: path contains tests/fixtures (any separator).
        string normalized = gamePath.Replace('\\', '/');
        return normalized.Contains("tests/fixtures/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/fixtures/", StringComparison.OrdinalIgnoreCase);
    }

    private static void WriteStubLayout(string gamePath, RuntimeKind runtime, PackageIds packages)
    {
        // Minimal BepInEx-like tree so "already installed" and overwrite demos work.
        string pluginsDir = Path.Combine(gamePath, InjectorPaths.PluginsTranslatorRelativePath);
        string configDir = Path.Combine(gamePath, InjectorPaths.ConfigRelativeDirectory);
        Directory.CreateDirectory(pluginsDir);
        Directory.CreateDirectory(configDir);

        // Doorstop / winhttp placeholders (not real binaries — documentation only).
        File.WriteAllText(
            Path.Combine(gamePath, "doorstop_config.ini.stub"),
            "; stub doorstop marker — replace with real BepInEx install by backend\n");
        File.WriteAllText(
            Path.Combine(gamePath, "winhttp.dll.stub"),
            "stub-winhttp-placeholder");

        File.WriteAllText(
            Path.Combine(pluginsDir, "README.stub.txt"),
            $"Stub plugin slot for {packages.PluginPackageId} ({runtime}).\n");

        File.WriteAllText(
            Path.Combine(gamePath, InjectorPaths.StubInstallMarkerFileName),
            $"runtime={runtime}\nbepinex={packages.BepInExPackageId}\nplugin={packages.PluginPackageId}\n");
    }

    private static void TryCopyConfigExample(string gamePath)
    {
        string? source = FindConfigExamplePath();
        string destDir = Path.Combine(gamePath, InjectorPaths.ConfigRelativeDirectory);
        Directory.CreateDirectory(destDir);
        string dest = Path.Combine(destDir, InjectorPaths.ConfigExampleDestFileName);

        if (source is not null && File.Exists(source))
        {
            File.Copy(source, dest, overwrite: true);
            return;
        }

        // Fallback placeholder so the config stage still leaves a file under fixtures.
        File.WriteAllText(
            dest,
            "# stub: copy of config/Translator.cfg.example was not found next to the app.\n" +
            "# Backend should ship or locate the real example via InjectorPaths.ConfigExampleRelativePath.\n");
    }

    /// <summary>
    /// Walk up from the current directory / base directory looking for config/Translator.cfg.example.
    /// </summary>
    public static string? FindConfigExamplePath()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? dir = new(start);
            for (int i = 0; i < 8 && dir is not null; i++)
            {
                string candidate = Path.Combine(dir.FullName, InjectorPaths.ConfigExampleRelativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                dir = dir.Parent;
            }
        }

        return null;
    }

    private async Task DelayAsync(CancellationToken cancellationToken)
    {
        if (_stageDelay > TimeSpan.Zero)
        {
            await Task.Delay(_stageDelay, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void Report(
        IProgress<InstallProgress>? progress,
        InstallStage stage,
        string message,
        int? percent)
    {
        progress?.Report(InstallProgress.Create(stage, message, percent));
    }
}
