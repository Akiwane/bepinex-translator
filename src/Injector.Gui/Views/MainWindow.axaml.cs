using Avalonia.Controls;
using Injector.Gui.ViewModels;

namespace Injector.Gui.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.StorageProvider = StorageProvider;
        }
    }
}
