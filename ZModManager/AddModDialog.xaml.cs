using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ZModManager.Models;

namespace ZModManager;

public partial class AddModDialog : Window
{
    public ModEntry? Result { get; private set; }

    private readonly RuntimeType _runtimeType;
    private readonly string?     _existingId;
    private readonly bool        _existingEnabled;

    // ── Constructor ───────────────────────────────────────────────────────────

    public AddModDialog(RuntimeType runtimeType, ModEntry? existing = null)
    {
        _runtimeType     = runtimeType;
        _existingId      = existing?.Id;
        _existingEnabled = existing?.IsEnabled ?? true;

        InitializeComponent();

        ApplyRuntimeStyling();

        if (existing != null)
            PreFill(existing);

        TitleText.Text  = existing != null ? "Edit Mod"     : "Add Mod";
        SaveBtn.Content = existing != null ? "Save Changes" : "Add Mod";

        UpdateSaveState();
        NameBox.Focus();
    }

    // ── Styling ───────────────────────────────────────────────────────────────

    private void ApplyRuntimeStyling()
    {
        if (_runtimeType == RuntimeType.Mono)
        {
            RuntimeBanner.Background  = new SolidColorBrush(Color.FromRgb(0x0D, 0x28, 0x40));
            RuntimeBanner.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "BrMonoBg");
            RuntimeBadge.Background   = new SolidColorBrush(Color.FromRgb(0x0A, 0x1E, 0x30));
            RuntimeBadgeText.Text     = "MONO";
            RuntimeBadgeText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "BrMono");
            RuntimeHintText.Text      = "Managed mod — needs an assembly path and a static entry-point method.";
            MonoFields.Visibility     = Visibility.Visible;
            IL2CPPFields.Visibility   = Visibility.Collapsed;
        }
        else if (_runtimeType == RuntimeType.IL2CPP)
        {
            RuntimeBanner.Background  = new SolidColorBrush(Color.FromRgb(0x2E, 0x15, 0x08));
            RuntimeBanner.SetResourceReference(System.Windows.Controls.Border.BorderBrushProperty, "BrIL2CPPBg");
            RuntimeBadge.Background   = new SolidColorBrush(Color.FromRgb(0x22, 0x10, 0x04));
            RuntimeBadgeText.Text     = "IL2CPP";
            RuntimeBadgeText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, "BrIL2CPP");
            RuntimeHintText.Text      = "Native mod — only a path to your x64 DLL is needed.";
            MonoFields.Visibility     = Visibility.Collapsed;
            IL2CPPFields.Visibility   = Visibility.Visible;
        }
        else
        {
            RuntimeBanner.Visibility = Visibility.Collapsed;
            MonoFields.Visibility    = Visibility.Collapsed;
            IL2CPPFields.Visibility  = Visibility.Collapsed;
        }
    }

    private void PreFill(ModEntry mod)
    {
        NameBox.Text  = mod.Name;
        NotesBox.Text = mod.Notes;

        if (mod.MonoConfig != null)
        {
            AssemblyBox.Text  = mod.MonoConfig.AssemblyPath;
            NamespaceBox.Text = mod.MonoConfig.Namespace;
            ClassBox.Text     = mod.MonoConfig.ClassName;
            MethodBox.Text    = mod.MonoConfig.MethodName;
        }

        if (mod.IL2CPPConfig != null)
            DllBox.Text = mod.IL2CPPConfig.NativeDllPath;
    }

    // ── Validation ────────────────────────────────────────────────────────────

    private void UpdateSaveState()
    {
        if (SaveBtn == null) return;

        bool nameOk = !string.IsNullOrWhiteSpace(NameBox?.Text);
        bool dllOk  = _runtimeType switch
        {
            RuntimeType.Mono   => !string.IsNullOrWhiteSpace(AssemblyBox?.Text),
            RuntimeType.IL2CPP => !string.IsNullOrWhiteSpace(DllBox?.Text),
            _                  => true
        };

        SaveBtn.IsEnabled = nameOk && dllOk;
    }

    private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        => UpdateSaveState();

    // ── Actions ───────────────────────────────────────────────────────────────

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var mod = new ModEntry
        {
            Id          = _existingId ?? Guid.NewGuid().ToString(),
            Name        = NameBox.Text.Trim(),
            Notes       = NotesBox.Text.Trim(),
            IsEnabled   = _existingEnabled,
            RuntimeType = _runtimeType,
        };

        if (_runtimeType == RuntimeType.Mono)
        {
            mod.MonoConfig = new MonoInjectionConfig
            {
                AssemblyPath = AssemblyBox.Text.Trim(),
                Namespace    = NamespaceBox.Text.Trim(),
                ClassName    = ClassBox.Text.Trim(),
                MethodName   = MethodBox.Text.Trim(),
            };
        }
        else if (_runtimeType == RuntimeType.IL2CPP)
        {
            mod.IL2CPPConfig = new IL2CPPInjectionConfig
            {
                NativeDllPath = DllBox.Text.Trim(),
            };
        }

        Result       = mod;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
    private void OnClose(object sender,  RoutedEventArgs e) => DialogResult = false;

    private void OnBrowseAssembly(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Select Mod Assembly DLL", Filter = "DLL (*.dll)|*.dll" };
        if (dlg.ShowDialog(this) == true) AssemblyBox.Text = dlg.FileName;
    }

    private void OnBrowseNativeDll(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Select Native Mod DLL", Filter = "DLL (*.dll)|*.dll" };
        if (dlg.ShowDialog(this) == true) DllBox.Text = dlg.FileName;
    }
}
