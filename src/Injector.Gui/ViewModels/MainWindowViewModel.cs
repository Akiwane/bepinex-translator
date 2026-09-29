using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Injector.Core.Abstractions;
using Injector.Core.Localization;
using Injector.Core.Models;

namespace Injector.Gui.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IGameProbe _gameProbe;
    private readonly IInstaller _installer;
    private CancellationTokenSource? _installCts;
    private GameProbeResult? _lastProbe;

    public MainWindowViewModel(IGameProbe gameProbe, IInstaller installer)
    {
        _gameProbe = gameProbe;
        _installer = installer;
        StatusText = UiStrings.Ready;
        StubNote = UiStrings.StubNote;
    }

    /// <summary>Injected by the view so Browse commands can open storage pickers.</summary>
    public IStorageProvider? StorageProvider { get; set; }

    [ObservableProperty]
    private string _gamePath = string.Empty;

    [ObservableProperty]
    private string _unityVersionText = UiStrings.NotDetected;

    [ObservableProperty]
    private string _runtimeText = UiStrings.NotDetected;

    [ObservableProperty]
    private string _evidenceText = UiStrings.NotDetected;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _canInstall;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    [NotifyCanExecuteChangedFor(nameof(OverwriteInstallCommand))]
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
    private bool _showOverwriteActions;

    [ObservableProperty]
    private string _stubNote = string.Empty;

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
    public string InstallButtonLabel => UiStrings.InstallButton;
    public string OverwriteLabel => UiStrings.OverwriteAndInstall;
    public string SkipLabel => UiStrings.SkipInstall;

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
                    new FilePickerFileType("游戏可执行文件")
                    {
                        Patterns = ["*.exe"],
                    },
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

    partial void OnGamePathChanged(string value)
    {
        ClearBanner();
        ShowOverwriteActions = false;
    }

    private void RunProbe(string path)
    {
        StatusText = UiStrings.Probing;
        ClearBanner();
        ShowOverwriteActions = false;

        GameProbeResult result = _gameProbe.Detect(path);
        _lastProbe = result;
        GamePath = result.GamePath;

        UnityVersionText = result.UnityVersion ?? (result.IsValid ? UiStrings.Unknown : UiStrings.NotDetected);
        RuntimeText = result.IsValid || result.Runtime != RuntimeKind.Unknown
            ? UiStrings.RuntimeLabel(result.Runtime)
            : UiStrings.NotDetected;
        EvidenceText = string.IsNullOrWhiteSpace(result.Evidence)
            ? UiStrings.NotDetected
            : result.Evidence;

        if (!result.IsValid)
        {
            CanInstall = false;
            ShowError(result.ErrorMessage ?? UiStrings.ErrorMessage(result.ErrorCode ?? InstallErrorCode.Unexpected));
            StatusText = UiStrings.Ready;
            return;
        }

        CanInstall = result.Runtime is RuntimeKind.Mono or RuntimeKind.Il2Cpp;
        StatusText = UiStrings.Ready;
    }

    private bool CanExecuteInstall() => CanInstall && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanExecuteInstall))]
    private Task InstallAsync() => RunInstallAsync(overwrite: false);

    private bool CanExecuteOverwrite() => !IsBusy && ShowOverwriteActions;

    [RelayCommand(CanExecute = nameof(CanExecuteOverwrite))]
    private Task OverwriteInstallAsync() => RunInstallAsync(overwrite: true);

    [RelayCommand]
    private void SkipOverwrite()
    {
        ShowOverwriteActions = false;
        ClearBanner();
        StatusText = UiStrings.Ready;
    }

    partial void OnShowOverwriteActionsChanged(bool value) =>
        OverwriteInstallCommand.NotifyCanExecuteChanged();

    private async Task RunInstallAsync(bool overwrite)
    {
        if (_lastProbe is null || !_lastProbe.IsValid)
        {
            ShowError(UiStrings.ErrInvalidPath);
            return;
        }

        _installCts?.Cancel();
        _installCts = new CancellationTokenSource();
        IsBusy = true;
        ShowOverwriteActions = false;
        ClearBanner();
        StatusText = UiStrings.Installing;
        ProgressValue = 0;
        ProgressIsIndeterminate = true;

        var progress = new Progress<InstallProgress>(OnProgress);

        try
        {
            InstallResult result = await _installer.InstallAsync(
                new InstallRequest
                {
                    GamePath = _lastProbe.GamePath,
                    Probe = _lastProbe,
                    Overwrite = overwrite,
                },
                progress,
                _installCts.Token);

            if (result.Success)
            {
                ShowSuccess(result.Message);
                StatusText = UiStrings.StageDone;
                ProgressIsIndeterminate = false;
                ProgressValue = 100;
            }
            else if (result.AlreadyInstalled)
            {
                ShowWarning(result.Message);
                ShowOverwriteActions = true;
                StatusText = UiStrings.Ready;
            }
            else
            {
                ShowError(result.Message);
                StatusText = UiStrings.Ready;
            }
        }
        catch (OperationCanceledException)
        {
            ShowError(UiStrings.ErrCancelled);
            StatusText = UiStrings.Ready;
        }
        catch (Exception ex)
        {
            ShowError($"{UiStrings.ErrUnexpected} {ex.Message}");
            StatusText = UiStrings.Ready;
        }
        finally
        {
            IsBusy = false;
            CanInstall = _lastProbe is { IsValid: true, Runtime: RuntimeKind.Mono or RuntimeKind.Il2Cpp };
        }
    }

    private void OnProgress(InstallProgress p)
    {
        StatusText = string.IsNullOrWhiteSpace(p.Message)
            ? UiStrings.StageLabel(p.Stage)
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
