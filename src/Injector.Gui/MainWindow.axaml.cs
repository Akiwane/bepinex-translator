using Avalonia.Controls;
using Avalonia.Interactivity;
using BepInExTranslator.Injector.Core;

namespace BepInExTranslator.Injector.Gui;

/// <summary>
/// 前端将替换/扩展此窗口。此处仅保留可编译 stub，并引用 Core 契约。
/// </summary>
public partial class MainWindow : Window
{
    private readonly IGameProbe _probe = new GameDetector();
    private readonly IPackageResolver _packages = new PackageResolver();
    private readonly IInstaller _installer = new GameInstaller();

    public MainWindow()
    {
        InitializeComponent();
        // 保留对契约类型的静态引用，避免被裁剪，并在 UI 上展示 pin 信息。
        _ = _installer;
        CoreSummaryText.Text =
            $"Core 就绪。默认 BepInEx Mono={PackageCatalog.BepInEx5Version}；" +
            $"IL2CPP={PackageCatalog.BepInEx6Version}。" +
            $" 解析器类型：{_packages.GetType().Name}";
    }

    private void OnSmokeDetectClick(object? sender, RoutedEventArgs e)
    {
        var result = _probe.Detect(string.Empty);
        SmokeResultText.Text = result.Error != null
            ? $"探测错误（预期）：{result.Error.Kind} — {result.Error.Message}"
            : $"意外成功：{result.RuntimeDisplayName}";
    }
}
