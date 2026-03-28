using System.Windows;
using System.Windows.Input;
using ZModManager.ViewModels;

namespace ZModManager;

public partial class SettingsWindow : Window
{
    public SettingsWindow(MainWindowViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
