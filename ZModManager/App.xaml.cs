using System.Windows;

namespace ZModManager;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.ToString(), "Unhandled Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
    }
}
