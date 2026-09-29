using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using BepInExTranslator.Injector.Core;

namespace BepInExTranslator.Injector.Gui;

public partial class MainWindow : Window
{
    private readonly GameDetector _detector = new();
    private readonly GameInstaller _installer = new();
    private GameDetectionResult? _lastDetection;
    private readonly string _repositoryRoot;

    public MainWindow()
    {
        InitializeComponent();
        _repositoryRoot = LocateRepositoryRoot();
        AppendLog($"仓库根（用于 artifacts / config）：{_repositoryRoot}");
        AppendLog($"默认 BepInEx Mono：{PackageCatalog.BepInEx5Version} → {PackageCatalog.BepInEx5AssetName}");
        AppendLog($"默认 BepInEx IL2CPP：{PackageCatalog.BepInEx6Version} → {PackageCatalog.BepInEx6AssetName}");
    }

    private async void OnBrowseFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择游戏根目录",
            AllowMultiple = false,
        });
        if (folders.Count > 0)
        {
            GamePathBox.Text = folders[0].Path.LocalPath;
            RunDetect();
        }
    }

    private async void OnBrowseExeClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择游戏 .exe",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Executable") { Patterns = new[] { "*.exe" } },
            },
        });
        if (files.Count > 0)
        {
            GamePathBox.Text = files[0].Path.LocalPath;
            RunDetect();
        }
    }

    private void OnDetectClick(object? sender, RoutedEventArgs e) => RunDetect();

    private void RunDetect()
    {
        var path = GamePathBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            StatusText.Text = "请先选择路径。";
            return;
        }

        _lastDetection = _detector.Detect(path);
        RuntimeText.Text = $"运行时：{_lastDetection.RuntimeDisplayName}";
        UnityVersionText.Text = $"Unity 版本：{_lastDetection.UnityVersion ?? "（未解析）"}";
        NotesText.Text = string.Join("\n", _lastDetection.Notes);
        AppendLog("--- 探测 ---");
        foreach (var n in _lastDetection.Notes)
        {
            AppendLog(n);
        }

        StatusText.Text = _lastDetection.IsValidUnityGame ? "探测成功" : "探测失败 / 路径无效";
        InstallButton.IsEnabled = _lastDetection.IsValidUnityGame;
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (_lastDetection == null || !_lastDetection.IsValidUnityGame)
        {
            RunDetect();
        }

        if (_lastDetection == null || !_lastDetection.IsValidUnityGame)
        {
            StatusText.Text = "无法安装：探测未通过。";
            return;
        }

        InstallButton.IsEnabled = false;
        DetectButton.IsEnabled = false;
        StatusText.Text = "安装中…";

        var policy = OverwritePolicy.BackupThenOverwrite;
        if (PolicySkip.IsChecked == true)
        {
            policy = OverwritePolicy.SkipExisting;
        }
        else if (PolicyOverwrite.IsChecked == true)
        {
            policy = OverwritePolicy.Overwrite;
        }

        var sources = new PackageSourceOptions
        {
            RepositoryRoot = _repositoryRoot,
            BepInExLocalZipPath = NullIfEmpty(BepInExLocalZipBox.Text),
            TranslatorLocalZipPath = NullIfEmpty(TranslatorLocalZipBox.Text),
            TranslatorLocalArtifactsDirectory = NullIfEmpty(TranslatorArtifactsBox.Text),
            TranslatorDownloadUrl = NullIfEmpty(TranslatorUrlBox.Text),
            TranslatorReleaseTag = NullIfEmpty(TranslatorTagBox.Text),
        };

        var progress = new Progress<string>(AppendLog);
        try
        {
            var result = await _installer.InstallAsync(new InstallOptions
            {
                Detection = _lastDetection,
                PackageSources = sources,
                OverwritePolicy = policy,
            }, progress);

            StatusText.Text = result.Success ? "安装成功" : "安装失败";
            if (result.BackupDirectory != null)
            {
                AppendLog("备份目录：" + result.BackupDirectory);
            }
        }
        catch (Exception ex)
        {
            AppendLog("异常：" + ex);
            StatusText.Text = "安装异常";
        }
        finally
        {
            InstallButton.IsEnabled = true;
            DetectButton.IsEnabled = true;
        }
    }

    private void AppendLog(string line)
    {
        var sb = new StringBuilder(LogBox.Text);
        if (sb.Length > 0)
        {
            sb.AppendLine();
        }

        sb.Append(DateTime.Now.ToString("HH:mm:ss")).Append(' ').Append(line);
        LogBox.Text = sb.ToString();
    }

    private static string? NullIfEmpty(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>
    /// 从输出目录向上查找含 config/Translator.cfg.example 或 BepInExTranslator.sln 的仓库根。
    /// </summary>
    private static string LocateRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var cfg = Path.Combine(dir.FullName, "config", "Translator.cfg.example");
            var sln = Path.Combine(dir.FullName, "BepInExTranslator.sln");
            if (File.Exists(cfg) || File.Exists(sln))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
