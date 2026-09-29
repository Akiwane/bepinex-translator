using Avalonia.Platform.Storage;
using BepInExTranslator.Injector.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Injector.Gui.Services;

namespace Injector.Gui.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IGameProbe _gameProbe;
    private readonly IInstaller _installer;
    private readonly PackageSourceOptions _packageSources;
    private readonly GuiRuntimeOptions _runtimeOptions;
    private CancellationTokenSource? _installCts;
    private GameDetectionResult? _lastDetection;

    public MainWindowViewModel(
        IGameProbe gameProbe,
        IInstaller installer,
        PackageSourceOptions packageSources,
        GuiRuntimeOptions runtimeOptions)
    {
        _gameProbe = gameProbe;
        _installer = installer;
        _packageSources = packageSources;
        _runtimeOptions = runtimeOptions;
        StatusText = UiStrings.Ready;
        SelectedOverwriteItem = OverwritePolicies.First(p => p.Policy == _runtimeOptions.DefaultOverwritePolicy);
        // 默认 pin 说明；自定义 URL 由下方高级选项写入 BepInExIl2CppUrl。
        PinNote =
            $"BepInEx pin：Mono={PackageCatalog.BepInEx5Version}；IL2CPP={PackageCatalog.BepInEx6Version}" +
            $"（插件 NuGet {PackageCatalog.BepInEx6PluginNuGetVersion}）。" +
            $" 模组默认远程：GitHub latest → {PackageCatalog.DefaultTranslatorReleaseAssetPattern}；优先本地 artifacts/。";
    }

    public IStorageProvider? StorageProvider { get; set; }

    public IReadOnlyList<OverwritePolicyItem> OverwritePolicies { get; } =
    [
        new(OverwritePolicy.BackupThenOverwrite, UiStrings.PolicyBackup),
        new(OverwritePolicy.SkipExisting, UiStrings.PolicySkip),
        new(OverwritePolicy.Overwrite, UiStrings.PolicyOverwrite),
    ];

    [ObservableProperty]
    private string _gamePath = string.Empty;

    [ObservableProperty]
    private string _unityVersionText = UiStrings.NotDetected;

    [ObservableProperty]
    private string _runtimeText = UiStrings.NotDetected;

    [ObservableProperty]
    private string _evidenceText = UiStrings.NotDetected;

    [ObservableProperty]
    private string _notesText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _canInstall;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _progressIsIndeterminate;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _bannerText;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsErrorBanner))]
    [NotifyPropertyChangedFor(nameof(IsWarningBanner))]
    [NotifyPropertyChangedFor(nameof(IsSuccessBanner))]
    private BannerKind _bannerKind = BannerKind.None;

    [ObservableProperty]
    private OverwritePolicyItem _selectedOverwriteItem;

    public OverwritePolicy SelectedOverwritePolicy => SelectedOverwriteItem.Policy;

    [ObservableProperty]
    private string _pinNote = string.Empty;

    /// <summary>true = 不覆盖 Core 目录 pin；false = 安装前写入 BepInExIl2CppUrl。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UseCustomIl2CppUrl))]
    [NotifyPropertyChangedFor(nameof(IsCustomIl2CppUrlEnabled))]
    private bool _useDefaultIl2CppPin = true;

    [ObservableProperty]
    private string _customIl2CppUrl = string.Empty;

    /// <summary>与 UseDefaultIl2CppPin 互斥，供第二个 RadioButton 双向绑定。</summary>
    public bool UseCustomIl2CppUrl
    {
        get => !UseDefaultIl2CppPin;
        set => UseDefaultIl2CppPin = !value;
    }

    public bool IsCustomIl2CppUrlEnabled => !UseDefaultIl2CppPin && !IsBusy;

    public bool IsErrorBanner => BannerKind == BannerKind.Error;
    public bool IsWarningBanner => BannerKind == BannerKind.Warning;
    public bool IsSuccessBanner => BannerKind == BannerKind.Success;

    public string Title => UiStrings.AppTitle;
    public string SelectGameLabel => UiStrings.SelectGame;
    public string BrowseFolderLabel => UiStrings.BrowseFolder;
    public string BrowseExeLabel => UiStrings.BrowseExe;
    public string PathPlaceholder => UiStrings.PathPlaceholder;
    public string ProbeResultsLabel => UiStrings.ProbeResults;
    public string UnityVersionLabel => UiStrings.UnityVersion;
    public string RuntimeLabel => UiStrings.Runtime;
    public string EvidenceLabel => UiStrings.Evidence;
    public string NotesLabel => UiStrings.Notes;
    public string InstallButtonLabel => UiStrings.InstallButton;
    public string OverwritePolicyLabel => UiStrings.OverwritePolicyLabel;
    public string ProbeHint => UiStrings.ProbeHint;
    public string Il2CppPackageSourceLabel => UiStrings.Il2CppPackageSourceLabel;
    public string Il2CppUseDefaultPinLabel => UiStrings.Il2CppUseDefaultPin;
    public string Il2CppUseCustomUrlLabel => UiStrings.Il2CppUseCustomUrl;
    public string Il2CppCustomUrlPlaceholder => UiStrings.Il2CppCustomUrlPlaceholder;
    public string Il2CppCustomUrlHint => UiStrings.Il2CppCustomUrlHint;

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        if (StorageProvider is null)
        {
            return;
        }

        IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions
            {
                Title = UiStrings.SelectGame,
                AllowMultiple = false,
            });

        if (folders.Count == 0)
        {
            return;
        }

        string? path = folders[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            GamePath = path;
            RunProbe(path);
        }
    }

    [RelayCommand]
    private async Task BrowseExeAsync()
    {
        if (StorageProvider is null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title = UiStrings.BrowseExe,
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("游戏可执行文件") { Patterns = ["*.exe"] },
                ],
            });

        if (files.Count == 0)
        {
            return;
        }

        string? path = files[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            GamePath = path;
            RunProbe(path);
        }
    }

    [RelayCommand]
    private void ProbePath()
    {
        if (!string.IsNullOrWhiteSpace(GamePath))
        {
            RunProbe(GamePath);
        }
    }

    partial void OnGamePathChanged(string value) => ClearBanner();

    private void RunProbe(string path)
    {
        StatusText = UiStrings.Probing;
        ClearBanner();

        GameDetectionResult result = _gameProbe.Detect(path);
        _lastDetection = result;
        GamePath = result.GameRoot;

        UnityVersionText = result.UnityVersion ?? UiStrings.Unknown;
        RuntimeText = result.RuntimeDisplayName;
        EvidenceText = result.EvidencePaths.Count == 0
            ? UiStrings.NotDetected
            : string.Join("\n", result.EvidencePaths);
        NotesText = result.Notes.Count == 0
            ? string.Empty
            : string.Join("\n", result.Notes);

        bool installable = result.IsValidUnityGame
                           && result.Runtime is UnityRuntimeKind.Mono or UnityRuntimeKind.Il2Cpp
                           && result.Error is null;
        CanInstall = installable;

        if (result.Error is not null)
        {
            ShowError(FormatDetectionError(result.Error));
        }
        else if (!installable)
        {
            ShowWarning(UiStrings.KindHint(InjectorErrorKind.UnknownRuntime));
        }

        StatusText = UiStrings.Ready;
    }

    private bool CanExecuteInstall() => CanInstall && !IsBusy;

    // AllowConcurrentExecutions：忙碌态完全由 IsBusy/CanExecuteInstall 控制。
    // 默认 AsyncRelayCommand 在方法仍在 finally 内时 IsRunning=true，此时 NotifyCanExecuteChanged
    // 仍会得到 false；若完成后的二次通知未被 Avalonia 吃到，Install 会一直灰掉无法重试。
    [RelayCommand(CanExecute = nameof(CanExecuteInstall), AllowConcurrentExecutions = true)]
    private async Task InstallAsync()
    {
        if (_lastDetection is null
            || !_lastDetection.IsValidUnityGame
            || _lastDetection.Runtime is not (UnityRuntimeKind.Mono or UnityRuntimeKind.Il2Cpp))
        {
            ShowError(UiStrings.KindHint(InjectorErrorKind.NotUnityGame));
            return;
        }

        // 取消并丢弃上一轮 token，避免失败/取消后留下已取消的 CTS 影响下一次。
        _installCts?.Cancel();
        _installCts?.Dispose();
        _installCts = new CancellationTokenSource();
        IsBusy = true;
        ClearBanner();
        StatusText = UiStrings.Installing;
        ProgressValue = 0;
        ProgressIsIndeterminate = true;

        // Ensure RepositoryRoot / config example flow into Core each install.
        if (!string.IsNullOrWhiteSpace(_runtimeOptions.RepositoryRoot))
        {
            _packageSources.RepositoryRoot = _runtimeOptions.RepositoryRoot;
        }

        // IL2CPP 高级覆盖：默认 pin → 清空；自定义 URL → 写入 BepInExIl2CppUrl（Mono 路径忽略该字段）。
        ApplyIl2CppPackageSourceOverride();

        var options = new InstallOptions
        {
            Detection = _lastDetection,
            PackageSources = _packageSources,
            OverwritePolicy = SelectedOverwriteItem.Policy,
            ConfigExampleSourcePath = _runtimeOptions.ConfigExampleSourcePath,
        };

        var progress = new Progress<InstallProgress>(OnProgress);

        try
        {
            InstallResult result = await _installer.InstallAsync(options, progress, _installCts.Token);

            if (result.Success)
            {
                string msg = result.Messages.Count > 0
                    ? string.Join("\n", result.Messages.TakeLast(3))
                    : "安装完成。";
                if (!string.IsNullOrWhiteSpace(result.BackupDirectory))
                {
                    msg += $"\n备份目录：{result.BackupDirectory}";
                }

                ShowSuccess(msg);
                StatusText = "完成";
                ProgressValue = 100;
            }
            else
            {
                // 失败：保留探测结果与错误横幅，仅恢复可重试状态（在 finally 中统一复位）。
                ShowError(FormatInstallError(result));
                StatusText = UiStrings.Ready;
            }
        }
        catch (OperationCanceledException)
        {
            ShowError(UiStrings.KindHint(InjectorErrorKind.Cancelled));
            StatusText = UiStrings.Ready;
        }
        catch (Exception ex)
        {
            ShowError($"{ex.Message}");
            StatusText = UiStrings.Ready;
        }
        finally
        {
            // 先清 IsBusy，丢弃可能仍在排队的 Progress 回调，再复位进度条与 CanInstall。
            IsBusy = false;
            ProgressIsIndeterminate = false;
            CanInstall = _lastDetection is
            {
                IsValidUnityGame: true,
                Runtime: UnityRuntimeKind.Mono or UnityRuntimeKind.Il2Cpp,
                Error: null,
            };

            // 显式刷新 CanExecute：保证失败后 Install 可再次点击（不依赖 AsyncRelayCommand IsRunning 收尾通知）。
            InstallCommand.NotifyCanExecuteChanged();

            _installCts?.Dispose();
            _installCts = null;
        }
    }

    private void OnProgress(InstallProgress p)
    {
        // 忽略安装结束后才送达的进度回调，避免把 ProgressIsIndeterminate / StatusText 再次打回忙碌态。
        if (!IsBusy)
        {
            return;
        }

        StatusText = string.IsNullOrWhiteSpace(p.Message)
            ? p.Phase.ToString()
            : p.Message;

        if (p.Percent is int percent)
        {
            ProgressIsIndeterminate = false;
            ProgressValue = Math.Clamp(percent, 0, 100);
        }
        else
        {
            ProgressIsIndeterminate = true;
        }
    }

    /// <summary>
    /// 按 GUI 选项同步 <see cref="PackageSourceOptions.BepInExIl2CppUrl"/>：
    /// 默认 pin 或空 URL → null（Core 用目录 6.0.0-pre.2）；否则 Trim 后写入。
    /// </summary>
    private void ApplyIl2CppPackageSourceOverride()
    {
        if (UseDefaultIl2CppPin || string.IsNullOrWhiteSpace(CustomIl2CppUrl))
        {
            _packageSources.BepInExIl2CppUrl = null;
            return;
        }

        _packageSources.BepInExIl2CppUrl = CustomIl2CppUrl.Trim();
    }

    partial void OnIsBusyChanged(bool value) =>
        OnPropertyChanged(nameof(IsCustomIl2CppUrlEnabled));


    private static string FormatDetectionError(InjectorError error)
    {
        string hint = UiStrings.KindHint(error.Kind);
        string body = UiStrings.FormatError(error);
        return string.IsNullOrEmpty(hint) ? body : $"{body}\n{hint}";
    }

    private static string FormatInstallError(InstallResult result)
    {
        if (result.Error is not null)
        {
            string hint = UiStrings.KindHint(result.Error.Kind);
            string body = UiStrings.FormatError(result.Error);
            return string.IsNullOrEmpty(hint) ? body : $"{body}\n{hint}";
        }

        return result.Messages.Count > 0
            ? string.Join("\n", result.Messages.TakeLast(3))
            : "安装失败。";
    }

    private void ShowError(string message)
    {
        BannerKind = BannerKind.Error;
        BannerText = message;
    }

    private void ShowWarning(string message)
    {
        BannerKind = BannerKind.Warning;
        BannerText = message;
    }

    private void ShowSuccess(string message)
    {
        BannerKind = BannerKind.Success;
        BannerText = message;
    }

    private void ClearBanner()
    {
        BannerKind = BannerKind.None;
        BannerText = null;
    }
}

public enum BannerKind
{
    None,
    Error,
    Warning,
    Success,
}

public sealed record OverwritePolicyItem(OverwritePolicy Policy, string DisplayName)
{
    public override string ToString() => DisplayName;
}
