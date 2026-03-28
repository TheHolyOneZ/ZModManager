using System.Collections.Specialized;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using ZModManager.ViewModels;

namespace ZModManager;

public partial class MainWindow : Window
{
    private MainWindowViewModel Vm => (MainWindowViewModel)DataContext;
    private NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        Loaded   += OnLoaded;
        Closing  += OnClosing;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var (l, t, w, h, max) = Vm.LoadWindowBounds();
        Left = l; Top = t; Width = w; Height = h;
        if (max) WindowState = WindowState.Maximized;

        Vm.LogEntries.CollectionChanged += ScrollLogToBottom;
        Vm.MinimizeToTrayRequested      += DoMinimizeToTray;

        InitTrayIcon();
    }

    // ── Tray icon ─────────────────────────────────────────────────────────────

    private void InitTrayIcon()
    {
        // Load icon from embedded resource
        Icon? icon = null;
        try
        {
            var uri    = new Uri("pack://application:,,,/app_icon.ico");
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream != null) icon = new Icon(stream);
        }
        catch { /* fallback: no icon */ }

        _trayIcon = new NotifyIcon
        {
            Icon    = icon,
            Text    = "ZModManager",
            Visible = false,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open ZModManager", null, (_, _) => RestoreFromTray());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => { _trayIcon.Visible = false; Close(); });
        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick     += (_, _) => RestoreFromTray();
    }

    private void DoMinimizeToTray()
    {
        if (_trayIcon == null) return;
        _trayIcon.Visible = true;
        _trayIcon.ShowBalloonTip(2000, "ZModManager", "Game launched — ZModManager is in the tray.", ToolTipIcon.Info);
        Hide();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState       = WindowState.Normal;
        Activate();
        if (_trayIcon != null) _trayIcon.Visible = false;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (Vm.IsGameRunning)
        {
            var result = System.Windows.MessageBox.Show(
                "The game is currently running.\nClose ZModManager anyway?",
                "Game Running", System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning);
            if (result != System.Windows.MessageBoxResult.Yes) { e.Cancel = true; return; }
        }

        Vm.SaveWindowBounds(Left, Top, Width, Height, WindowState == WindowState.Maximized);
        Vm.Save();
        Vm.Cleanup();

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
    }

    // ── Custom chrome handlers ─────────────────────────────────────────────────

    private void OnMinimize(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnMaximizeRestore(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e)
        => Close();

    // ── Auto-scroll ───────────────────────────────────────────────────────────

    private void ScrollLogToBottom(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add) return;
        LogScroller.ScrollToBottom();
    }

}
