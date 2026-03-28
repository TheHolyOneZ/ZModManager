using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace ZModManager;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private static void OpenUrl(string url)
        => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void OpenZLogic(object sender, RoutedEventArgs e)
        => OpenUrl("https://zlogic.eu/");

    private void OpenZLogic(object sender, MouseButtonEventArgs e)
        => OpenUrl("https://zlogic.eu/");

    private void OpenSharpMonoInjector(object sender, RoutedEventArgs e)
        => OpenUrl("https://github.com/TheHolyOneZ/SharpMonoInjector-2.7-TheHolyOneZ-Edition-");

    private void OpenZDBFSite(object sender, RoutedEventArgs e)
        => OpenUrl("https://zsync.eu/zdbf/");

    private void OpenZDBFGitHub(object sender, RoutedEventArgs e)
        => OpenUrl("https://github.com/TheHolyOneZ/discord-bot-framework");
}
