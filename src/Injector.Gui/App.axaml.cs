using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using BepInExTranslator.Injector.Core;
using Injector.Gui.Services;
using Injector.Gui.ViewModels;
using Injector.Gui.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Injector.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // Avoid duplicate validations from Avalonia + CommunityToolkit.Mvvm.
        BindingPlugins.DataValidators.RemoveAt(0);

        string repoRoot = RepositoryLocator.FindRepositoryRoot();
        var packageSources = new PackageSourceOptions
        {
            RepositoryRoot = repoRoot,
        };

        string? configExample = string.IsNullOrEmpty(repoRoot)
            ? null
            : Path.Combine(repoRoot, "config", "Translator.cfg.example");

        ServiceCollection services = new();
        // Wire real Core from PR #7 (not Gui-side stubs).
        services.AddSingleton<IGameProbe, GameDetector>();
        services.AddSingleton<IPackageResolver, PackageResolver>();
        services.AddSingleton<IInstaller, GameInstaller>();
        services.AddSingleton(packageSources);
        services.AddSingleton(new GuiRuntimeOptions
        {
            RepositoryRoot = repoRoot,
            ConfigExampleSourcePath = configExample is not null && File.Exists(configExample)
                ? configExample
                : null,
            DefaultOverwritePolicy = OverwritePolicy.BackupThenOverwrite,
        });
        services.AddTransient<MainWindowViewModel>();

        ServiceProvider provider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = provider.GetRequiredService<MainWindowViewModel>(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
