using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using Injector.Core;
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

        ServiceCollection services = new();
        services.AddInjectorCore();
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
