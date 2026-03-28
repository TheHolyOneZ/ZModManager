using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using ZModManager.Models;
using ZModManager.Services;

namespace ZModManager.ViewModels;

public class MainWindowViewModel : ViewModelBase
{
    // ── Services ──────────────────────────────────────────────────────────────
    private readonly ConfigService             _config    = new();
    private readonly RuntimeDetector           _detector  = new();
    private readonly LogService                _log       = new();
    private readonly MonoInjectionService      _monoSvc;
    private readonly IL2CPPInjectionService    _il2Svc;
    private readonly LaunchAndInjectService    _launchSvc;
    private readonly BootstrapService          _bootstrap  = new();
    private readonly FrameworkInstallerService _installer;
    private readonly EngineAnalyzer            _engineAnalyzer = new();
    private readonly DispatcherTimer           _gameRunningTimer;
    private CancellationTokenSource?           _launchCts;
    private CancellationTokenSource?           _installCts;

    // ── Collections ───────────────────────────────────────────────────────────
    public ObservableCollection<GameProfile> Profiles    { get; } = new();
    public ObservableCollection<ModEntry>    CurrentMods { get; } = new();
    public ObservableCollection<LogEntry>    LogEntries  { get; } = new();
    public ObservableCollection<string>      RunningMods { get; } = new();

    // ── Filtered views (search) ───────────────────────────────────────────────
    private ICollectionView? _modsView;
    private ICollectionView? _profilesView;

    private string _modFilter = string.Empty;
    public string ModFilter
    {
        get => _modFilter;
        set { Set(ref _modFilter, value); _modsView?.Refresh(); }
    }

    private string _profileFilter = string.Empty;
    public string ProfileFilter
    {
        get => _profileFilter;
        set { Set(ref _profileFilter, value); _profilesView?.Refresh(); }
    }

    public ICollectionView? FilteredMods     => _modsView;
    public ICollectionView? FilteredProfiles => _profilesView;

    // ── Selected state ────────────────────────────────────────────────────────
    private GameProfile? _selectedProfile;
    public GameProfile? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            Set(ref _selectedProfile, value);
            RefreshMods();
            RefreshRunningMods();
            OnPropertyChanged(nameof(HasProfile));
            OnPropertyChanged(nameof(IsMonoGame));
            OnPropertyChanged(nameof(IsIL2CPPGame));
            OnPropertyChanged(nameof(IsUnknownRuntime));
            OnPropertyChanged(nameof(RuntimeBadge));
            OnPropertyChanged(nameof(WindowSubtitle));
            OnPropertyChanged(nameof(LaunchArgs));
            RefreshFrameworkProperties();
        }
    }

    // ── Computed properties ───────────────────────────────────────────────────
    public bool   HasProfile       => _selectedProfile != null;
    public bool   IsMonoGame       => _selectedProfile?.RuntimeType == RuntimeType.Mono;
    public bool   IsIL2CPPGame     => _selectedProfile?.RuntimeType == RuntimeType.IL2CPP;
    public bool   IsUnknownRuntime => _selectedProfile != null && _selectedProfile.RuntimeType == RuntimeType.Unknown;
    public string RuntimeBadge     => _selectedProfile?.RuntimeLabel ?? string.Empty;
    public string WindowSubtitle   => _selectedProfile?.Name ?? string.Empty;

    private bool _isGameRunning;
    public bool IsGameRunning
    {
        get => _isGameRunning;
        private set
        {
            Set(ref _isGameRunning, value);
            OnPropertyChanged(nameof(RunningModsLabel));
            OnPropertyChanged(nameof(WindowRunningIndicator));
        }
    }

    /// <summary>Green dot shown in title bar when the game process is detected.</summary>
    public string WindowRunningIndicator => _isGameRunning ? "●" : string.Empty;

    public string RunningModsLabel => IsGameRunning
        ? $"● Game running — {RunningMods.Count} mod{(RunningMods.Count == 1 ? "" : "s")} active"
        : string.Empty;

    // ── Settings pass-through properties ─────────────────────────────────────
    public IL2CPPFramework DefaultIL2CPPFramework
    {
        get => _settings.DefaultIL2CPPFramework;
        set { _settings.DefaultIL2CPPFramework = value; OnPropertyChanged(); Save(); }
    }

    public bool ConfirmBeforeLaunch
    {
        get => _settings.ConfirmBeforeLaunch;
        set { _settings.ConfirmBeforeLaunch = value; OnPropertyChanged(); Save(); }
    }

    public bool MinimizeToTray
    {
        get => _settings.MinimizeToTray;
        set { _settings.MinimizeToTray = value; OnPropertyChanged(); Save(); }
    }

    public bool AutoDisableIncompat
    {
        get => _settings.AutoDisableIncompat;
        set { _settings.AutoDisableIncompat = value; OnPropertyChanged(); Save(); }
    }

    // ── Per-profile pass-through properties ──────────────────────────────────
    public string LaunchArgs
    {
        get => _selectedProfile?.LaunchArgs ?? string.Empty;
        set
        {
            if (_selectedProfile == null) return;
            _selectedProfile.LaunchArgs = value;
            OnPropertyChanged();
            Save();
        }
    }

    // ── AllModsEnabled (tri-state checkbox) ──────────────────────────────────
    public bool? AllModsEnabled
    {
        get
        {
            if (!CurrentMods.Any()) return false;
            bool allOn  = CurrentMods.All(m => m.IsEnabled);
            bool allOff = CurrentMods.All(m => !m.IsEnabled);
            return allOn ? true : allOff ? false : (bool?)null;
        }
        set
        {
            bool enable = value ?? true;
            foreach (var m in CurrentMods) m.IsEnabled = enable;
            OnPropertyChanged();
            Save();
        }
    }

    // ── Framework version ─────────────────────────────────────────────────────
    public string FrameworkVersion
    {
        get
        {
            if (_selectedProfile == null) return string.Empty;
            var dir = _selectedProfile.GameDirectory;
            return _bootstrap.Detect(dir) switch
            {
                IL2CPPFramework.MelonLoader => GetDllVersion(Path.Combine(dir, "MelonLoader", "MelonLoader.dll")),
                IL2CPPFramework.BepInEx     => GetDllVersion(Path.Combine(dir, "BepInEx", "core", "BepInEx.dll")),
                _                           => string.Empty
            };
        }
    }

    public bool HasFrameworkVersion => !string.IsNullOrEmpty(FrameworkVersion);

    private static string GetDllVersion(string path)
    {
        if (!File.Exists(path)) return string.Empty;
        try { return FileVersionInfo.GetVersionInfo(path).FileVersion ?? string.Empty; }
        catch { return string.Empty; }
    }

    /// <summary>The currently-detected IL2CPP framework for the selected profile.</summary>
    public IL2CPPFramework DetectedFramework =>
        (_selectedProfile != null && _selectedProfile.RuntimeType == RuntimeType.IL2CPP)
            ? _bootstrap.Detect(_selectedProfile.GameDirectory)
            : IL2CPPFramework.None;

    /// <summary>Human-readable description of the detected IL2CPP framework (three phases).</summary>
    public string FrameworkStatus => _isInstallingFramework ? "Installing…" : DetectedFramework switch
    {
        IL2CPPFramework.MelonLoader   => "MelonLoader",
        IL2CPPFramework.BepInEx       => "BepInEx",
        IL2CPPFramework.ZModBootstrap => "ZMod Bootstrap",
        _                             => "Not Installed"
    };

    public bool IsBootstrapInstalled =>
        _selectedProfile != null && _bootstrap.IsInstalled(_selectedProfile.GameDirectory);

    private bool _isInstallingFramework;
    public bool IsInstallingFramework
    {
        get => _isInstallingFramework;
        set { Set(ref _isInstallingFramework, value); OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(FrameworkStatus)); }
    }

    private string _frameworkInstallProgress = string.Empty;
    public string FrameworkInstallProgress
    {
        get => _frameworkInstallProgress;
        set => Set(ref _frameworkInstallProgress, value);
    }

    private string _statusMessage = "Ready.";
    public string StatusMessage
    {
        get => _statusMessage;
        set => Set(ref _statusMessage, value);
    }

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set { Set(ref _isBusy, value); OnPropertyChanged(nameof(IsIdle)); }
    }

    private bool _isLaunching;
    public bool IsLaunching
    {
        get => _isLaunching;
        set => Set(ref _isLaunching, value);
    }

    public bool IsIdle => !_isBusy && !_isLaunching && !_isInstallingFramework;

    // ── Commands ──────────────────────────────────────────────────────────────
    public ICommand AddProfileCommand     { get; }
    public ICommand RemoveProfileCommand  { get; }
    public ICommand DetectRuntimeCommand  { get; }
    public ICommand BrowseExeCommand      { get; }

    public ICommand AddModCommand         { get; }
    public ICommand EditModCommand        { get; }
    public ICommand RemoveModCommand      { get; }
    public ICommand InjectAllCommand      { get; }
    public ICommand InjectModCommand      { get; }

    public ICommand LaunchInjectCommand      { get; }
    public ICommand CancelLaunchCommand     { get; }

    public ICommand InstallBootstrapCommand   { get; }
    public ICommand UninstallBootstrapCommand { get; }

    public ICommand InstallMelonLoaderCommand  { get; }
    public ICommand InstallBepInExCommand      { get; }
    public ICommand UninstallFrameworkCommand  { get; }

    public ICommand OpenSettingsCommand      { get; }
    public ICommand OpenAboutCommand         { get; }
    public ICommand DetectEngineCommand      { get; }
    public ICommand OpenGameFolderCommand    { get; }
    public ICommand OpenModsFolderCommand    { get; }
    public ICommand LaunchCleanCommand       { get; }
    public ICommand ExportProfileCommand     { get; }
    public ICommand ImportProfileCommand     { get; }
    public ICommand CopyLogCommand           { get; }
    public ICommand ClearLogCommand          { get; }

    // ── Settings ──────────────────────────────────────────────────────────────
    private AppSettings _settings;

    public MainWindowViewModel()
    {
        _monoSvc   = new MonoInjectionService(_log);
        _il2Svc    = new IL2CPPInjectionService(_log);
        _launchSvc = new LaunchAndInjectService(_monoSvc, _il2Svc, _log, _bootstrap);
        _installer = new FrameworkInstallerService(_log);

        _log.EntryAdded += e => Dispatch(() => LogEntries.Add(e));

        // ── Game-running poll timer ───────────────────────────────────────────
        _gameRunningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _gameRunningTimer.Tick += (_, _) => PollGameRunning();
        _gameRunningTimer.Start();

        // ── Commands ──────────────────────────────────────────────────────────
        AddProfileCommand    = new RelayCommand(_ => AddProfile());
        RemoveProfileCommand = new RelayCommand(_ => RemoveProfile(),                     _ => HasProfile);
        DetectRuntimeCommand = new RelayCommand(_ => DetectRuntime(),                     _ => HasProfile);
        BrowseExeCommand     = new RelayCommand(_ => BrowseExe(),                         _ => HasProfile);

        AddModCommand        = new RelayCommand(_ => AddMod(),                            _ => HasProfile);
        EditModCommand       = new RelayCommand(p => EditMod(p as ModEntry),              _ => HasProfile);
        RemoveModCommand     = new RelayCommand(p => RemoveMod(p as ModEntry),            _ => HasProfile);
        InjectAllCommand     = new RelayCommand(_ => InjectAll(),                         _ => HasProfile && IsIdle);
        InjectModCommand     = new RelayCommand(p => InjectSingle(p as ModEntry),         _ => HasProfile && IsIdle);

        LaunchInjectCommand       = new RelayCommand(_ => LaunchAndInject(),         _ => HasProfile && IsIdle);
        CancelLaunchCommand       = new RelayCommand(_ => CancelLaunch(),             _ => _isLaunching);

        InstallBootstrapCommand    = new RelayCommand(_ => InstallBootstrap(),              _ => HasProfile && IsIL2CPPGame);
        UninstallBootstrapCommand  = new RelayCommand(_ => UninstallBootstrap(),            _ => HasProfile && IsIL2CPPGame);

        InstallMelonLoaderCommand  = new RelayCommand(_ => InstallFramework(IL2CPPFramework.MelonLoader), _ => HasProfile && IsIL2CPPGame && IsIdle);
        InstallBepInExCommand      = new RelayCommand(_ => InstallFramework(IL2CPPFramework.BepInEx),     _ => HasProfile && IsIL2CPPGame && IsIdle);
        UninstallFrameworkCommand  = new RelayCommand(_ => UninstallCurrentFramework(),                   _ => HasProfile && IsIL2CPPGame && IsIdle
                                                           && DetectedFramework is IL2CPPFramework.MelonLoader or IL2CPPFramework.BepInEx);

        OpenSettingsCommand   = new RelayCommand(_ => OpenSettings());
        OpenAboutCommand      = new RelayCommand(_ => OpenAbout());
        DetectEngineCommand   = new RelayCommand(_ => DetectEngine());
        OpenGameFolderCommand = new RelayCommand(_ => OpenGameFolder(), _ => HasProfile);
        OpenModsFolderCommand = new RelayCommand(_ => OpenModsFolder(), _ => HasProfile && IsIL2CPPGame);
        LaunchCleanCommand    = new RelayCommand(_ => LaunchClean(),    _ => HasProfile && IsIdle);
        ExportProfileCommand  = new RelayCommand(_ => ExportProfile(),  _ => HasProfile);
        ImportProfileCommand  = new RelayCommand(_ => ImportProfile());
        CopyLogCommand        = new RelayCommand(_ => CopyLog());
        ClearLogCommand       = new RelayCommand(_ => LogEntries.Clear());

        // ── Load saved profiles ───────────────────────────────────────────────
        _settings = _config.Load();
        foreach (var p in _settings.Profiles) Profiles.Add(p);

        _profilesView = CollectionViewSource.GetDefaultView(Profiles);
        _profilesView.Filter = o => o is GameProfile g &&
            (string.IsNullOrWhiteSpace(_profileFilter) ||
             g.Name.Contains(_profileFilter, StringComparison.OrdinalIgnoreCase));

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == _settings.LastSelectedId)
                       ?? Profiles.FirstOrDefault();
    }

    // ── Profile management ────────────────────────────────────────────────────

    private void AddProfile()
    {
        var dlg = new OpenFileDialog { Title = "Select Game Executable", Filter = "Executable (*.exe)|*.exe" };
        if (dlg.ShowDialog() != true) return;

        var exe        = dlg.FileName;
        var engineInfo = _engineAnalyzer.Analyze(exe);
        var runtime    = engineInfo.RuntimeType;

        // Show engine detection result to the user
        MessageBox.Show(
            $"Engine detected: {engineInfo.Label}\n\n{engineInfo.Recommendation}",
            "Game Engine Analysis",
            MessageBoxButton.OK,
            engineInfo.Engine is EngineType.UnityMono or EngineType.UnityIL2CPP
                ? MessageBoxImage.Information
                : MessageBoxImage.Warning);

        var profile = new GameProfile
        {
            Name        = Path.GetFileNameWithoutExtension(exe),
            GameExePath = exe,
            RuntimeType = runtime
        };

        Profiles.Add(profile);
        SelectedProfile = profile;
        Save();
        _log.Info($"Added '{profile.Name}' — engine: {engineInfo.Label}, runtime: {profile.RuntimeLabel}.");
        StatusMessage = $"Added '{profile.Name}' [{engineInfo.Label}].";

        // If this is an IL2CPP game, check for an existing framework
        if (runtime == RuntimeType.IL2CPP)
        {
            var gameDir   = profile.GameDirectory;
            var framework = _bootstrap.Detect(gameDir);

            if (framework == IL2CPPFramework.None)
            {
                var choice = MessageBox.Show(
                    $"No mod framework detected in:\n{gameDir}\n\n" +
                    "Click YES to install MelonLoader (most popular — choose this if your mods say 'MelonLoader').\n" +
                    "Click NO to install BepInEx instead (choose this if your mods say 'BepInEx').\n" +
                    "Click CANCEL to skip (you can install later from the game panel).",
                    "Choose IL2CPP Framework",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (choice == MessageBoxResult.Yes)
                    InstallFramework(IL2CPPFramework.MelonLoader);
                else if (choice == MessageBoxResult.No)
                    InstallFramework(IL2CPPFramework.BepInEx);
            }
            else
            {
                _log.Info($"[IL2CPP] Framework already present: {framework}");
                StatusMessage = $"Added '{profile.Name}' — {framework} detected.";
            }
        }
    }

    private void DetectEngine()
    {
        var dlg = new OpenFileDialog { Title = "Select Game Executable to Analyse", Filter = "Executable (*.exe)|*.exe" };
        if (dlg.ShowDialog() != true) return;

        var info = _engineAnalyzer.Analyze(dlg.FileName);
        MessageBox.Show(
            $"Engine detected: {info.Label}\n\n{info.Recommendation}",
            "Game Engine Analysis",
            MessageBoxButton.OK,
            info.Engine is EngineType.UnityMono or EngineType.UnityIL2CPP
                ? MessageBoxImage.Information
                : MessageBoxImage.Warning);
    }

    private void RemoveProfile()
    {
        if (_selectedProfile == null) return;
        if (MessageBox.Show($"Remove '{_selectedProfile.Name}'?", "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        Profiles.Remove(_selectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
        Save();
        StatusMessage = "Profile removed.";
    }

    private void DetectRuntime()
    {
        if (_selectedProfile == null) return;
        var rt = _detector.Detect(_selectedProfile.GameDirectory);
        _selectedProfile.RuntimeType = rt;

        foreach (var mod in _selectedProfile.Mods)
            mod.RuntimeType = rt;

        OnSelectedProfileChanged();
        Save();
        _log.Info($"Runtime re-detected: {_selectedProfile.RuntimeLabel}");
        StatusMessage = $"Runtime: {_selectedProfile.RuntimeLabel}";
    }

    private void BrowseExe()
    {
        if (_selectedProfile == null) return;
        var dlg = new OpenFileDialog { Title = "Select Game Executable", Filter = "Executable (*.exe)|*.exe" };
        if (dlg.ShowDialog() != true) return;

        _selectedProfile.GameExePath = dlg.FileName;
        _selectedProfile.RuntimeType = _detector.Detect(_selectedProfile.GameDirectory);
        OnSelectedProfileChanged();
        Save();
    }

    // ── Mod management ────────────────────────────────────────────────────────

    private void AddMod()
    {
        if (_selectedProfile == null) return;

        var dlg = new AddModDialog(_selectedProfile.RuntimeType)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;

        var mod = dlg.Result;
        mod.PropertyChanged += OnModPropertyChanged;
        _selectedProfile.Mods.Add(mod);
        CurrentMods.Add(mod);
        OnPropertyChanged(nameof(AllModsEnabled));
        Save();
        _log.Info($"Added mod '{mod.Name}'.");
        StatusMessage = $"Added mod '{mod.Name}'.";
    }

    private void EditMod(ModEntry? mod)
    {
        if (mod == null || _selectedProfile == null) return;

        var dlg = new AddModDialog(_selectedProfile.RuntimeType, existing: mod)
        {
            Owner = Application.Current?.MainWindow
        };
        if (dlg.ShowDialog() != true || dlg.Result == null) return;

        var result = dlg.Result;
        mod.Name = result.Name;

        if (mod.MonoConfig != null && result.MonoConfig != null)
        {
            mod.MonoConfig.AssemblyPath = result.MonoConfig.AssemblyPath;
            mod.MonoConfig.Namespace    = result.MonoConfig.Namespace;
            mod.MonoConfig.ClassName    = result.MonoConfig.ClassName;
            mod.MonoConfig.MethodName   = result.MonoConfig.MethodName;
        }
        if (mod.IL2CPPConfig != null && result.IL2CPPConfig != null)
        {
            mod.IL2CPPConfig.NativeDllPath = result.IL2CPPConfig.NativeDllPath;
        }

        // Invalidate cache for old path before the path changes
        if (!string.IsNullOrEmpty(mod.ActiveDllPath))
            ModFrameworkDetector.Invalidate(mod.ActiveDllPath);

        mod.Notes = result.Notes;
        mod.NotifyDisplay();
        Save();
        RefreshModCompatibility();
        _log.Info($"Updated mod '{mod.Name}'.");
        StatusMessage = $"Saved '{mod.Name}'.";
    }

    private void RemoveMod(ModEntry? mod)
    {
        if (mod == null || _selectedProfile == null) return;

        mod.PropertyChanged -= OnModPropertyChanged;
        _selectedProfile.Mods.Remove(mod);
        CurrentMods.Remove(mod);
        OnPropertyChanged(nameof(AllModsEnabled));
        Save();
        StatusMessage = $"Removed '{mod.Name}'.";
    }

    // ── Injection ─────────────────────────────────────────────────────────────

    private async void InjectAll()
    {
        if (_selectedProfile == null || !IsIdle) return;

        IsBusy = true;
        StatusMessage = "Injecting all enabled mods…";

        var enabled = _selectedProfile.Mods.Where(m => m.IsEnabled).ToList();
        int ok = 0;

        foreach (var mod in enabled)
        {
            var r = await Inject(_selectedProfile, mod);
            if (r.Success) ok++;
        }

        IsBusy = false;
        StatusMessage = $"{ok}/{enabled.Count} mods injected.";
    }

    private async void InjectSingle(ModEntry? mod)
    {
        if (_selectedProfile == null || mod == null || !IsIdle) return;

        IsBusy = true;
        StatusMessage = $"Injecting '{mod.Name}'…";
        var r = await Inject(_selectedProfile, mod);
        IsBusy = false;
        StatusMessage = r.Message;
    }

    private async System.Threading.Tasks.Task<InjectionResult> Inject(GameProfile profile, ModEntry mod)
    {
        Save();
        return mod.RuntimeType switch
        {
            RuntimeType.Mono   => await _monoSvc.InjectAsync(profile, mod),
            RuntimeType.IL2CPP => await _il2Svc.InjectAsync(profile, mod),
            _ => InjectionResult.Fail($"[{mod.Name}] Unknown runtime.", RuntimeType.Unknown)
        };
    }

    // ── Launch + inject ───────────────────────────────────────────────────────

    /// <summary>Raised on the UI thread when the app should minimize to the system tray.</summary>
    public event Action? MinimizeToTrayRequested;

    private async void LaunchAndInject()
    {
        if (_selectedProfile == null || !IsIdle) return;

        if (_settings.ConfirmBeforeLaunch)
        {
            var r = MessageBox.Show($"Launch '{_selectedProfile.Name}' with mods?",
                "Confirm Launch", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;
        }

        // ── Auto-disable mods that are incompatible with the installed framework ──
        if (_settings.AutoDisableIncompat && _selectedProfile.RuntimeType == RuntimeType.IL2CPP)
        {
            var framework    = DetectedFramework;
            bool anyDisabled = false;
            foreach (var mod in CurrentMods.Where(m => m.IsEnabled).ToList())
            {
                var dllPath = mod.ActiveDllPath;
                if (string.IsNullOrWhiteSpace(dllPath)) continue;
                var target  = ModFrameworkDetector.Detect(dllPath);
                var warning = BuildCompatibilityWarning(target, framework);
                if (string.IsNullOrEmpty(warning)) continue;

                mod.IsEnabled = false;
                anyDisabled   = true;
                _log.Warn($"Auto-disabled '{mod.Name}': incompatible with {framework}.");
            }
            if (anyDisabled)
            {
                Save();
                // If nothing is enabled any more, abort — there's nothing to do
                if (!CurrentMods.Any(m => m.IsEnabled))
                {
                    _log.Warn("All mods were incompatible and have been disabled. Launch aborted.");
                    StatusMessage = "Launch aborted — no compatible mods enabled.";
                    MessageBox.Show(
                        "All enabled mods are incompatible with the installed framework and have been disabled.\n\n" +
                        "Enable compatible mods or install the correct framework before launching.",
                        "No Compatible Mods", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
        }

        _launchCts  = new CancellationTokenSource();
        IsLaunching = true;
        IsBusy      = true;
        StatusMessage = "Launching game…";
        Save();

        try
        {
            var results = await _launchSvc.LaunchAndInjectAsync(_selectedProfile, _launchCts.Token);
            int ok = results.Count(r => r.Success);
            StatusMessage = $"Launch complete — {ok}/{results.Count} mods injected.";

            if (_settings.MinimizeToTray)
                Dispatch(() => MinimizeToTrayRequested?.Invoke());
        }
        catch (OperationCanceledException)
        {
            _log.Warn("Launch + inject cancelled.");
            StatusMessage = "Cancelled.";
        }
        finally
        {
            IsLaunching = false;
            IsBusy      = false;
            _launchCts?.Dispose();
            _launchCts = null;
            Dispatch(() => ((RelayCommand)LaunchInjectCommand).Invalidate());
        }
    }

    private void CancelLaunch()
    {
        _launchCts?.Cancel();
        _log.Warn("Cancelling…");
    }

    private void LaunchClean()
    {
        if (_selectedProfile == null) return;
        try
        {
            // Remove all ZModManager-deployed mods from the framework folder
            // so the game starts with zero mods regardless of enabled/disabled state.
            var gameDir   = _selectedProfile.GameDirectory;
            var framework = _bootstrap.Detect(gameDir);
            if (framework is IL2CPPFramework.MelonLoader or IL2CPPFramework.BepInEx)
            {
                _bootstrap.RemoveDeployedMods(gameDir, framework, _selectedProfile.Mods);
                _log.Info($"[Clean launch] Removed deployed mods from {framework} folder.");
            }

            var psi = new ProcessStartInfo(_selectedProfile.GameExePath)
            {
                UseShellExecute  = true,
                WorkingDirectory = gameDir,
                Arguments        = _selectedProfile.LaunchArgs
            };
            Process.Start(psi);
            _log.Info($"Launched '{_selectedProfile.Name}' without mods.");
            StatusMessage = "Game launched without mods.";
        }
        catch (Exception ex)
        {
            _log.Error($"Clean launch failed: {ex.Message}");
            StatusMessage = $"Launch failed: {ex.Message}";
        }
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    private void OpenSettings()
    {
        var win = new SettingsWindow(this) { Owner = Application.Current?.MainWindow };
        win.ShowDialog();
    }

    private void OpenAbout()
    {
        var win = new AboutWindow { Owner = Application.Current?.MainWindow };
        win.ShowDialog();
    }

    // ── Folder actions ────────────────────────────────────────────────────────

    private void OpenGameFolder()
    {
        if (_selectedProfile == null) return;
        var dir = _selectedProfile.GameDirectory;
        if (!Directory.Exists(dir)) { StatusMessage = "Game folder not found."; return; }
        Process.Start("explorer", dir);
    }

    private void OpenModsFolder()
    {
        if (_selectedProfile == null) return;
        var framework = _bootstrap.Detect(_selectedProfile.GameDirectory);
        var folder    = _bootstrap.GetModsFolder(_selectedProfile.GameDirectory, framework);
        if (folder == null)
        {
            StatusMessage = "No mods folder — install a framework first.";
            return;
        }
        Directory.CreateDirectory(folder);
        Process.Start("explorer", folder);
    }

    // ── Log actions ───────────────────────────────────────────────────────────

    private void CopyLog()
    {
        if (!LogEntries.Any()) { StatusMessage = "Log is empty."; return; }
        var text = string.Join(Environment.NewLine,
            LogEntries.Select(e => $"[{e.FormattedTime}] {e.Message}"));
        Clipboard.SetText(text);
        StatusMessage = "Log copied to clipboard.";
    }

    // ── Profile import / export ───────────────────────────────────────────────

    private static readonly JsonSerializerOptions _profileJsonOpts = new()
    {
        WriteIndented = true,
        Converters    = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    private void ExportProfile()
    {
        if (_selectedProfile == null) return;
        var dlg = new SaveFileDialog
        {
            Title    = "Export Profile",
            Filter   = "JSON (*.json)|*.json",
            FileName = $"{_selectedProfile.Name}.json"
        };
        if (dlg.ShowDialog() != true) return;
        File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(_selectedProfile, _profileJsonOpts));
        StatusMessage = $"Exported '{_selectedProfile.Name}'.";
    }

    private void ImportProfile()
    {
        var dlg = new OpenFileDialog { Title = "Import Profile", Filter = "JSON (*.json)|*.json" };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var profile = JsonSerializer.Deserialize<GameProfile>(File.ReadAllText(dlg.FileName), _profileJsonOpts);
            if (profile == null) throw new InvalidOperationException("Invalid profile file.");
            profile.Id = Guid.NewGuid().ToString();
            Profiles.Add(profile);
            SelectedProfile = profile;
            Save();
            StatusMessage = $"Imported '{profile.Name}'.";
            if (profile.Mods.Any(m => !string.IsNullOrEmpty(m.ActiveDllPath)))
                _log.Warn("Imported profile contains absolute DLL paths — re-link any mods whose files have moved.");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Import Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Framework install / uninstall ─────────────────────────────────────────

    private async void InstallFramework(IL2CPPFramework framework)
    {
        if (_selectedProfile == null || !IsIdle) return;

        // If a different framework is already installed, offer to remove & replace
        var current = _bootstrap.Detect(_selectedProfile.GameDirectory);
        if (current != IL2CPPFramework.None && current != framework && current != IL2CPPFramework.ZModBootstrap)
        {
            var other  = current.ToString();
            var result = MessageBox.Show(
                $"{other} is already installed in this game.\n\n" +
                $"To use {framework} you need to remove {other} first.\n\n" +
                $"Remove {other} and install {framework}?",
                "Framework conflict",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (current == IL2CPPFramework.MelonLoader)
                    _installer.UninstallMelonLoader(_selectedProfile.GameDirectory);
                else
                    _installer.UninstallBepInEx(_selectedProfile.GameDirectory);
                _log.Info($"[Framework] Removed {other} before installing {framework}.");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to remove {other}: {ex.Message}");
                MessageBox.Show(ex.Message, $"Remove {other}", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        _installCts          = new CancellationTokenSource();
        IsInstallingFramework = true;
        FrameworkInstallProgress = $"Installing {framework}…";
        StatusMessage = $"Installing {framework}…";

        var progress = new Progress<string>(msg =>
        {
            FrameworkInstallProgress = msg;
            StatusMessage = msg;
        });

        try
        {
            if (framework == IL2CPPFramework.MelonLoader)
                await _installer.InstallMelonLoaderAsync(_selectedProfile.GameDirectory, progress, _installCts.Token);
            else
                await _installer.InstallBepInExAsync(_selectedProfile.GameDirectory, progress, _installCts.Token);

            StatusMessage = $"{framework} installed successfully.";
            _log.Success($"{framework} installed.");
            RefreshFrameworkProperties();
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Installation cancelled.";
            _log.Warn("Framework installation cancelled.");
        }
        catch (Exception ex)
        {
            _log.Error($"{framework} install failed: {ex.Message}");
            StatusMessage = $"Install failed: {ex.Message}";
            MessageBox.Show(ex.Message, $"Install {framework}", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsInstallingFramework    = false;
            FrameworkInstallProgress = string.Empty;
            _installCts?.Dispose();
            _installCts = null;
            // Force WPF to re-evaluate CanExecute on all commands so the Launch button re-enables
            Dispatch(() => ((RelayCommand)LaunchInjectCommand).Invalidate());
        }
    }

    private void UninstallCurrentFramework()
    {
        if (_selectedProfile == null) return;
        var framework = _bootstrap.Detect(_selectedProfile.GameDirectory);
        if (framework is not (IL2CPPFramework.MelonLoader or IL2CPPFramework.BepInEx)) return;

        if (MessageBox.Show(
                $"Uninstall {framework} from:\n{_selectedProfile.GameDirectory}\n\n" +
                "This will remove the framework folder and its proxy DLL. Your mods folder will also be removed.",
                $"Uninstall {framework}", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes) return;

        try
        {
            if (framework == IL2CPPFramework.MelonLoader)
                _installer.UninstallMelonLoader(_selectedProfile.GameDirectory);
            else
                _installer.UninstallBepInEx(_selectedProfile.GameDirectory);

            StatusMessage = $"{framework} uninstalled.";
            RefreshFrameworkProperties();
        }
        catch (Exception ex)
        {
            _log.Error($"Uninstall failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Uninstall", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Bootstrap ─────────────────────────────────────────────────────────────

    private void InstallBootstrap()
    {
        if (_selectedProfile == null) return;
        try
        {
            _bootstrap.Install(_selectedProfile.GameDirectory);
            _log.Success("ZMod Bootstrap installed. The game will now load your IL2CPP mods on startup.");
            StatusMessage = "Bootstrap installed.";
            RefreshFrameworkProperties();
        }
        catch (Exception ex)
        {
            _log.Error($"Bootstrap install failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Install Bootstrap", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void UninstallBootstrap()
    {
        if (_selectedProfile == null) return;
        if (MessageBox.Show(
                "Remove the ZMod Bootstrap (version.dll) from the game directory?",
                "Uninstall Bootstrap", MessageBoxButton.YesNo, MessageBoxImage.Question)
            != MessageBoxResult.Yes) return;
        try
        {
            _bootstrap.Uninstall(_selectedProfile.GameDirectory);
            _log.Info("ZMod Bootstrap removed.");
            StatusMessage = "Bootstrap uninstalled.";
            RefreshFrameworkProperties();
        }
        catch (Exception ex)
        {
            _log.Error($"Bootstrap uninstall failed: {ex.Message}");
            MessageBox.Show(ex.Message, "Uninstall Bootstrap", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // ── Game running detection ────────────────────────────────────────────────

    private void PollGameRunning()
    {
        if (_selectedProfile == null)
        {
            IsGameRunning = false;
            return;
        }
        var procName = _selectedProfile.ProcessName;
        if (string.IsNullOrWhiteSpace(procName))
        {
            IsGameRunning = false;
            return;
        }
        var running = Process.GetProcessesByName(procName).Length > 0;
        if (running != _isGameRunning)
        {
            IsGameRunning = running;
            RefreshRunningMods();
        }
    }

    private void RefreshRunningMods()
    {
        RunningMods.Clear();
        if (_selectedProfile == null || !_isGameRunning) return;
        foreach (var mod in _selectedProfile.Mods.Where(m => m.IsEnabled))
            RunningMods.Add(mod.Name);
        OnPropertyChanged(nameof(RunningModsLabel));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RefreshMods()
    {
        CurrentMods.Clear();
        if (_selectedProfile == null)
        {
            _modsView = null;
            OnPropertyChanged(nameof(FilteredMods));
            OnPropertyChanged(nameof(AllModsEnabled));
            return;
        }
        foreach (var m in _selectedProfile.Mods)
        {
            m.PropertyChanged += OnModPropertyChanged;
            CurrentMods.Add(m);
        }

        _modsView = CollectionViewSource.GetDefaultView(CurrentMods);
        _modsView.Filter = o => o is ModEntry m &&
            (string.IsNullOrWhiteSpace(_modFilter) ||
             m.Name.Contains(_modFilter, StringComparison.OrdinalIgnoreCase));
        OnPropertyChanged(nameof(FilteredMods));
        OnPropertyChanged(nameof(AllModsEnabled));
        RefreshModCompatibility();
    }

    private void OnModPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ModEntry.IsEnabled))
        {
            OnPropertyChanged(nameof(AllModsEnabled));
            // Re-scan compatibility when a mod is toggled — disabled mods don't need a warning
            RefreshModCompatibility();
        }
    }

    private void RefreshFrameworkProperties()
    {
        OnPropertyChanged(nameof(DetectedFramework));
        OnPropertyChanged(nameof(FrameworkStatus));
        OnPropertyChanged(nameof(FrameworkVersion));
        OnPropertyChanged(nameof(HasFrameworkVersion));
        OnPropertyChanged(nameof(IsBootstrapInstalled));
        RefreshModCompatibility();
    }

    private void RefreshModCompatibility()
    {
        // Compatibility checks only apply to IL2CPP games
        if (_selectedProfile?.RuntimeType != RuntimeType.IL2CPP)
        {
            foreach (var mod in CurrentMods)
            {
                mod.FrameworkTarget      = DetectedModFramework.Unknown;
                mod.CompatibilityWarning = string.Empty;
            }
            return;
        }

        var framework = DetectedFramework;

        foreach (var mod in CurrentMods)
        {
            // Disabled mods won't be deployed — no point warning about them
            if (!mod.IsEnabled)
            {
                mod.FrameworkTarget      = DetectedModFramework.Unknown;
                mod.CompatibilityWarning = string.Empty;
                continue;
            }

            var dllPath = mod.ActiveDllPath;
            var target  = string.IsNullOrWhiteSpace(dllPath)
                ? DetectedModFramework.Unknown
                : ModFrameworkDetector.Detect(dllPath);

            mod.FrameworkTarget      = target;
            mod.CompatibilityWarning = BuildCompatibilityWarning(target, framework);
        }
    }

    private static string BuildCompatibilityWarning(
        DetectedModFramework modTarget, IL2CPPFramework installed)
        => (modTarget, installed) switch
        {
            (DetectedModFramework.MelonLoader, IL2CPPFramework.BepInEx) =>
                "⚠ This mod requires MelonLoader — it will not load under BepInEx.",

            (DetectedModFramework.BepInEx, IL2CPPFramework.MelonLoader) =>
                "⚠ This mod is a BepInEx plugin — it will not load under MelonLoader.",

            (DetectedModFramework.Native, IL2CPPFramework.MelonLoader) =>
                "⚠ Native DLLs cannot be loaded by MelonLoader — use ZMod Bootstrap instead.",

            (DetectedModFramework.Native, IL2CPPFramework.BepInEx) =>
                "⚠ Native DLLs cannot be loaded by BepInEx — use ZMod Bootstrap instead.",

            (DetectedModFramework.Managed, IL2CPPFramework.ZModBootstrap) =>
                "⚠ Managed .NET mods only load under MelonLoader or BepInEx — not ZMod Bootstrap.",

            (DetectedModFramework.MelonLoader, IL2CPPFramework.None) or
            (DetectedModFramework.BepInEx,     IL2CPPFramework.None) or
            (DetectedModFramework.Managed,     IL2CPPFramework.None) =>
                "⚠ No framework installed — this mod will not load until you install MelonLoader or BepInEx.",

            _ => string.Empty
        };

    private void OnSelectedProfileChanged()
    {
        OnPropertyChanged(nameof(SelectedProfile));
        OnPropertyChanged(nameof(IsMonoGame));
        OnPropertyChanged(nameof(IsIL2CPPGame));
        OnPropertyChanged(nameof(IsUnknownRuntime));
        OnPropertyChanged(nameof(RuntimeBadge));
        OnPropertyChanged(nameof(WindowSubtitle));
        OnPropertyChanged(nameof(LaunchArgs));
        RefreshFrameworkProperties();
    }

    private static void Dispatch(Action a)
        => System.Windows.Application.Current?.Dispatcher.Invoke(a);

    // ── Persistence ───────────────────────────────────────────────────────────

    public void Save()
    {
        _settings.Profiles       = Profiles.ToList();
        _settings.LastSelectedId = _selectedProfile?.Id ?? string.Empty;
        _config.Save(_settings);
    }

    public void SaveWindowBounds(double l, double t, double w, double h, bool maximized)
    {
        _settings.WindowLeft   = l;
        _settings.WindowTop    = t;
        _settings.WindowWidth  = w;
        _settings.WindowHeight = h;
        _settings.IsMaximized  = maximized;
        _config.Save(_settings);
    }

    public (double L, double T, double W, double H, bool Max) LoadWindowBounds()
        => (_settings.WindowLeft, _settings.WindowTop,
            _settings.WindowWidth, _settings.WindowHeight, _settings.IsMaximized);

    public void Cleanup()
    {
        _gameRunningTimer.Stop();
        _installer.Dispose();
    }
}
