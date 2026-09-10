using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Imaginary.Core.Logging;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using Imaginary.Desktop.Models;
using Imaginary.Desktop.Views;
using Microsoft.Win32;
using CoreResizeMode = Imaginary.Core.Models.ResizeMode;

using Imaginary.Desktop.Services;

namespace Imaginary.Desktop.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IBatchProcessor _batchProcessor;
    private readonly IImageFormatDetector _formatDetector;
    private readonly IPresetManager _presetManager;
    private readonly ISettingsService _settingsService;
    private readonly IExplorerIntegration _explorerIntegration;
    private readonly IAutostartService _autostartService;
    private readonly IHotfolderWatcher _hotfolderWatcher;
    private readonly IWatermarkService _watermarkService;
    private readonly IUpdateService _updateService;
    private ITrayService? _trayService;

    private UpdateInfo? _latestUpdateInfo;
    private CancellationTokenSource? _cts;
    private bool _isApplyingPreset;

    public ObservableCollection<FileItemViewModel> Files { get; } = new();
    public ObservableCollection<Preset> Presets { get; } = new();

    public static string[] AvailableFormats => new[]
    {
        "Original beibehalten",
        "JPEG (.jpg)",
        "PNG (.png)",
        "WebP (.webp)",
        "AVIF (.avif)",
        "Windows Icon (.ico)",
        "GIF (.gif)",
        "BMP (.bmp)"
    };

    public static string[] AvailableUnits => new[] { "KB", "MB" };

    public static string[] AvailableResizeModes => new[]
    {
        "Keine Skalierung",
        "Prozentual skalieren",
        "Pixelmaße (Breite / Höhe)",
        "Einpassen mit Rand (Pad)",
        "Füllen & Beschneiden (FillCrop)",
        "Maximale Kantenlänge (MaxEdge)"
    };

    public static string[] AvailableFallbackStrategies => new[]
    {
        "Auflösung reduzieren (Resize)",
        "Farbreduktion (Quantisierung)",
        "Nur warnen (kein Eingriff)"
    };

    public static string[] AvailableWatermarkTypes => new[] { "Text", "Bild-Logo" };

    public static string[] AvailableWatermarkPositions => new[]
    {
        "Unten rechts",
        "Unten links",
        "Unten zentriert",
        "Zentriert",
        "Oben rechts",
        "Oben links",
        "Oben zentriert"
    };

    // Format & Metadata
    [ObservableProperty]
    private int _selectedFormatIndex = 0; // 0 = Keep original

    [ObservableProperty]
    private bool _stripMetadata = true;

    // Size limit
    [ObservableProperty]
    private bool _enableSizeLimit;

    [ObservableProperty]
    private double _sizeLimitValue = 500;

    [ObservableProperty]
    private int _selectedUnitIndex = 0; // 0 = KB, 1 = MB

    [ObservableProperty]
    private int _selectedFallbackStrategyIndex = 0;

    // Resizing
    [ObservableProperty]
    private int _selectedResizeModeIndex = 0;

    [ObservableProperty]
    private double _resizePercentage = 50.0;

    [ObservableProperty]
    private int? _targetWidth;

    [ObservableProperty]
    private int? _targetHeight;

    [ObservableProperty]
    private int? _maxEdgeLength = 1200;

    [ObservableProperty]
    private string _padColor = "#FFFFFF";

    [ObservableProperty]
    private bool _maintainAspectRatio = true;

    // Watermark
    [ObservableProperty]
    private bool _enableWatermark;

    [ObservableProperty]
    private int _selectedWatermarkTypeIndex = 0; // 0 = Text, 1 = Image

    [ObservableProperty]
    private string _watermarkText = "© Imaginary";

    [ObservableProperty]
    private string _watermarkImagePath = string.Empty;

    [ObservableProperty]
    private int _selectedWatermarkPositionIndex = 0; // BottomRight default

    [ObservableProperty]
    private double _watermarkOpacity = 0.5;

    [ObservableProperty]
    private int _watermarkFontSize = 36;

    [ObservableProperty]
    private string _watermarkTextColor = "#FFFFFF";

    [ObservableProperty]
    private bool _watermarkIncludeShadow = true;

    // Presets
    [ObservableProperty]
    private Preset? _selectedPreset;

    // Active Tab Navigation
    [ObservableProperty]
    private int _selectedTabIndex = 0; // 0 = Studio, 1 = Hotfolder, 2 = Einstellungen

    // Dark Mode & Settings
    [ObservableProperty]
    private bool _isDarkMode;

    [ObservableProperty]
    private bool _isExplorerIntegrationEnabled;

    // Hotfolder Settings & State
    [ObservableProperty]
    private string _hotfolderPath = string.Empty;

    [ObservableProperty]
    private string _hotfolderOutputPath = string.Empty;

    [ObservableProperty]
    private bool _isHotfolderActive;

    [ObservableProperty]
    private string _hotfolderStatusText = "Hotfolder nicht aktiv";

    [ObservableProperty]
    private bool _hotfolderIncludeSubdirectories;

    [ObservableProperty]
    private int _hotfolderOriginalActionIndex = 1; // 0 = Keep, 1 = Move to subfolder, 2 = Delete

    [ObservableProperty]
    private string _hotfolderOriginalSubfolder = "Originale";

    [ObservableProperty]
    private Preset? _hotfolderSelectedPreset;

    [ObservableProperty]
    private int _hotfolderTodayProcessedCount = 0;

    [ObservableProperty]
    private long _hotfolderTodaySavedBytes = 0;

    public string HotfolderSavedBytesFormatted
    {
        get
        {
            if (HotfolderTodaySavedBytes < 1024) return $"{HotfolderTodaySavedBytes} B";
            if (HotfolderTodaySavedBytes < 1024 * 1024) return $"{HotfolderTodaySavedBytes / 1024.0:F1} KB";
            return $"{HotfolderTodaySavedBytes / (1024.0 * 1024.0):F2} MB";
        }
    }

    public ObservableCollection<HotfolderHistoryItem> HotfolderHistory { get; } = new();

    // System & Autostart Settings
    [ObservableProperty]
    private bool _isAutostartEnabled;

    [ObservableProperty]
    private bool _minimizeToTrayOnClose = true;

    [ObservableProperty]
    private bool _startMinimizedInTray = false;

    [ObservableProperty]
    private bool _showTrayNotifications = true;

    [ObservableProperty]
    private bool _checkForUpdatesOnStartup = true;

    public bool HasShownTrayIntroBalloon => _settingsService.Settings.HasShownTrayIntroBalloon;

    public void MarkTrayIntroBalloonShown()
    {
        _settingsService.Settings.HasShownTrayIntroBalloon = true;
        _settingsService.Save();
    }

    public string AppVersionString => "v1.1.0";

    // Output & Execution
    [ObservableProperty]
    private string _outputDirectory = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _statusSummary = "Bereit";

    [ObservableProperty]
    private FileItemViewModel? _selectedFile;

    // Update state
    [ObservableProperty]
    private bool _isUpdateAvailable;

    [ObservableProperty]
    private string _updateBadgeText = string.Empty;

    [ObservableProperty]
    private bool _isCheckingForUpdates;

    public bool HasFiles => Files.Count > 0;
    public bool CanStart => HasFiles && !IsBusy;
    public bool CanCancel => IsBusy;
    public bool CanOpenPreview => SelectedFile != null;

    public MainViewModel(
        IBatchProcessor batchProcessor,
        IImageFormatDetector formatDetector,
        IPresetManager presetManager,
        ISettingsService settingsService,
        IExplorerIntegration explorerIntegration,
        IAutostartService autostartService,
        IHotfolderWatcher hotfolderWatcher,
        IWatermarkService watermarkService,
        IUpdateService updateService)
    {
        _batchProcessor = batchProcessor ?? throw new ArgumentNullException(nameof(batchProcessor));
        _formatDetector = formatDetector ?? throw new ArgumentNullException(nameof(formatDetector));
        _presetManager = presetManager ?? throw new ArgumentNullException(nameof(presetManager));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _explorerIntegration = explorerIntegration ?? throw new ArgumentNullException(nameof(explorerIntegration));
        _autostartService = autostartService ?? throw new ArgumentNullException(nameof(autostartService));
        _hotfolderWatcher = hotfolderWatcher ?? throw new ArgumentNullException(nameof(hotfolderWatcher));
        _watermarkService = watermarkService ?? throw new ArgumentNullException(nameof(watermarkService));
        _updateService = updateService ?? throw new ArgumentNullException(nameof(updateService));

        Files.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(HasFiles));
            OnPropertyChanged(nameof(CanStart));
        };

        // Load settings
        var settings = _settingsService.Settings;
        _isDarkMode = settings.IsDarkMode;
        App.SetTheme(_isDarkMode);

        _isExplorerIntegrationEnabled = _explorerIntegration.IsRegistered();
        _outputDirectory = settings.LastOutputDirectory ?? string.Empty;

        // Hotfolder settings
        _hotfolderPath = settings.HotfolderPath ?? string.Empty;
        _hotfolderOutputPath = settings.HotfolderOutputDir ?? string.Empty;
        _hotfolderIncludeSubdirectories = settings.HotfolderIncludeSubdirectories;
        _hotfolderOriginalActionIndex = (int)settings.HotfolderOriginalAction;
        _hotfolderOriginalSubfolder = string.IsNullOrWhiteSpace(settings.HotfolderOriginalSubfolder) ? "Originale" : settings.HotfolderOriginalSubfolder;

        // Daily stats
        if (settings.HotfolderStatsDate?.Date == DateTime.Today)
        {
            _hotfolderTodayProcessedCount = settings.HotfolderTodayProcessedCount;
            _hotfolderTodaySavedBytes = settings.HotfolderTodaySavedBytes;
        }
        else
        {
            settings.HotfolderStatsDate = DateTime.Today;
            settings.HotfolderTodayProcessedCount = 0;
            settings.HotfolderTodaySavedBytes = 0;
            _settingsService.Save();
        }

        // System & Autostart settings
        _isAutostartEnabled = _autostartService.IsAutostartEnabled();
        _minimizeToTrayOnClose = settings.MinimizeToTrayOnClose;
        _startMinimizedInTray = settings.StartMinimizedInTray;
        _showTrayNotifications = settings.ShowTrayNotifications;
        _checkForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;

        // Auto-heal autostart path if enabled
        if (_isAutostartEnabled)
        {
            _autostartService.SynchronizeAutostart(true);
        }

        // Load Presets
        ReloadPresets();

        if (!string.IsNullOrWhiteSpace(settings.HotfolderSelectedPresetId))
        {
            _hotfolderSelectedPreset = Presets.FirstOrDefault(p => p.Id == settings.HotfolderSelectedPresetId);
        }

        // Hotfolder events
        _hotfolderWatcher.FileProcessed += OnHotfolderFileProcessed;
        _hotfolderWatcher.StatusMessage += (s, msg) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                HotfolderStatusText = msg;
            });
        };

        // Autostart Hotfolder if configured
        if (settings.HotfolderAutoStart && !string.IsNullOrWhiteSpace(_hotfolderPath) && Directory.Exists(_hotfolderPath))
        {
            ToggleHotfolder();
        }

        // Automatischer Update-Check im Hintergrund (falls aktiviert)
        if (_checkForUpdatesOnStartup)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(2500);
                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await CheckForUpdatesInternalAsync(showFeedbackWhenNoUpdate: false);
                });
            });
        }
    }

    private void ReloadPresets()
    {
        Presets.Clear();
        foreach (var p in _presetManager.GetAllPresets())
        {
            Presets.Add(p);
        }
    }

    partial void OnSelectedPresetChanged(Preset? value)
    {
        if (value == null || _isApplyingPreset) return;

        _isApplyingPreset = true;
        try
        {
            ApplyPresetOptions(value.Options);
            _settingsService.Settings.SelectedPresetId = value.Id;
            _settingsService.Save();
        }
        finally
        {
            _isApplyingPreset = false;
        }
    }

    partial void OnSelectedFileChanged(FileItemViewModel? value)
    {
        OnPropertyChanged(nameof(CanOpenPreview));
    }

    partial void OnIsDarkModeChanged(bool value)
    {
        App.SetTheme(value);
        _settingsService.Settings.IsDarkMode = value;
        _settingsService.Save();
    }

    private void ApplyPresetOptions(ConversionOptions opts)
    {
        SelectedFormatIndex = opts.TargetFormat switch
        {
            ImageFormat.Jpeg => 1,
            ImageFormat.Png => 2,
            ImageFormat.Webp => 3,
            ImageFormat.Avif => 4,
            ImageFormat.Ico => 5,
            ImageFormat.Gif => 6,
            ImageFormat.Bmp => 7,
            _ => 0
        };

        StripMetadata = opts.StripMetadata;

        if (opts.MaxFileSizeInBytes.HasValue)
        {
            EnableSizeLimit = true;
            if (opts.MaxFileSizeInBytes.Value >= 1024 * 1024)
            {
                SelectedUnitIndex = 1;
                SizeLimitValue = opts.MaxFileSizeInBytes.Value / (1024.0 * 1024.0);
            }
            else
            {
                SelectedUnitIndex = 0;
                SizeLimitValue = opts.MaxFileSizeInBytes.Value / 1024.0;
            }
        }
        else
        {
            EnableSizeLimit = false;
        }

        SelectedFallbackStrategyIndex = opts.FallbackStrategy switch
        {
            FallbackStrategy.Quantize => 1,
            FallbackStrategy.WarnOnly => 2,
            _ => 0
        };

        SelectedResizeModeIndex = opts.ResizeMode switch
        {
            CoreResizeMode.Percentage => 1,
            CoreResizeMode.AbsolutePixels => 2,
            CoreResizeMode.Pad => 3,
            CoreResizeMode.FillCrop => 4,
            CoreResizeMode.MaxEdge => 5,
            _ => 0
        };

        ResizePercentage = opts.ResizePercentage;
        TargetWidth = opts.TargetWidth;
        TargetHeight = opts.TargetHeight;
        MaxEdgeLength = opts.MaxEdgeLength ?? 1200;
        PadColor = opts.PadColor;
        MaintainAspectRatio = opts.MaintainAspectRatio;

        if (opts.Watermark != null && opts.Watermark.Enabled)
        {
            EnableWatermark = true;
            SelectedWatermarkTypeIndex = opts.Watermark.Type == WatermarkType.Image ? 1 : 0;
            WatermarkText = opts.Watermark.Text;
            WatermarkImagePath = opts.Watermark.ImagePath ?? string.Empty;
            WatermarkOpacity = opts.Watermark.Opacity;
            WatermarkFontSize = (int)opts.Watermark.FontSize;
            WatermarkTextColor = opts.Watermark.TextColor;
            WatermarkIncludeShadow = opts.Watermark.IncludeShadow;
            SelectedWatermarkPositionIndex = opts.Watermark.Position switch
            {
                WatermarkPosition.BottomLeft => 1,
                WatermarkPosition.BottomCenter => 2,
                WatermarkPosition.Center => 3,
                WatermarkPosition.TopRight => 4,
                WatermarkPosition.TopLeft => 5,
                WatermarkPosition.TopCenter => 6,
                _ => 0
            };
        }
        else
        {
            EnableWatermark = false;
        }
    }

    [RelayCommand]
    private void SaveCurrentAsPreset()
    {
        var inputWindow = new Window
        {
            Title = "Neues Profil speichern",
            Width = 360,
            Height = 180,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Application.Current.MainWindow,
            ResizeMode = System.Windows.ResizeMode.NoResize
        };

        var sp = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
        sp.Children.Add(new System.Windows.Controls.TextBlock { Text = "Profilname:", Margin = new Thickness(0, 0, 0, 6) });
        var tb = new System.Windows.Controls.TextBox { Text = "Mein Profil", Margin = new Thickness(0, 0, 0, 12), Padding = new Thickness(6, 4, 6, 4) };
        sp.Children.Add(tb);

        var btnPanel = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var btnOk = new System.Windows.Controls.Button { Content = "Speichern", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var btnCancel = new System.Windows.Controls.Button { Content = "Abbrechen", Width = 80, IsCancel = true };
        btnOk.Click += (s, e) => inputWindow.DialogResult = true;
        btnPanel.Children.Add(btnOk);
        btnPanel.Children.Add(btnCancel);
        sp.Children.Add(btnPanel);
        inputWindow.Content = sp;

        if (inputWindow.ShowDialog() == true && !string.IsNullOrWhiteSpace(tb.Text))
        {
            var newPreset = new Preset
            {
                Name = tb.Text.Trim(),
                Description = "Benutzerdefiniertes Profil",
                IsBuiltIn = false,
                Options = BuildConversionOptions()
            };
            _presetManager.SaveUserPreset(newPreset);
            ReloadPresets();
            SelectedPreset = Presets.FirstOrDefault(p => p.Id == newPreset.Id);
            StatusSummary = $"Profil '{newPreset.Name}' gespeichert.";
        }
    }

    [RelayCommand]
    private void DeleteCurrentPreset()
    {
        if (SelectedPreset == null || SelectedPreset.IsBuiltIn)
        {
            MessageBox.Show("Standardprofile können nicht gelöscht werden.", "Profil löschen", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var result = MessageBox.Show($"Möchten Sie das Profil '{SelectedPreset.Name}' wirklich löschen?", "Profil löschen", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            _presetManager.DeleteUserPreset(SelectedPreset.Id);
            ReloadPresets();
            SelectedPreset = Presets.FirstOrDefault();
            StatusSummary = "Profil gelöscht.";
        }
    }

    [RelayCommand]
    private void ToggleExplorerIntegration()
    {
        if (IsExplorerIntegrationEnabled)
        {
            _explorerIntegration.Unregister();
            IsExplorerIntegrationEnabled = false;
            _settingsService.Settings.ExplorerIntegrationEnabled = false;
            _settingsService.Save();
            MessageBox.Show("Windows Explorer Kontextmenü erfolgreich entfernt.", "Explorer Integration", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            var exePath = Environment.ProcessPath;
            if (_explorerIntegration.Register(exePath))
            {
                IsExplorerIntegrationEnabled = true;
                _settingsService.Settings.ExplorerIntegrationEnabled = true;
                _settingsService.Save();
                MessageBox.Show("Windows Explorer Kontextmenü erfolgreich aktiviert!\nRechtsklick auf Bilder oder Ordner zeigt nun 'Mit Imaginary konvertieren...'.", "Explorer Integration", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Konnte Windows Explorer Kontextmenü nicht registrieren.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public void AttachTrayService(ITrayService trayService)
    {
        _trayService = trayService;
        _trayService.UpdateHotfolderStatus(IsHotfolderActive);
    }

    partial void OnIsAutostartEnabledChanged(bool value)
    {
        if (value)
        {
            _autostartService.EnableAutostart();
        }
        else
        {
            _autostartService.DisableAutostart();
        }
        _settingsService.Settings.IsAutostartEnabled = value;
        _settingsService.Save();
    }

    partial void OnMinimizeToTrayOnCloseChanged(bool value)
    {
        _settingsService.Settings.MinimizeToTrayOnClose = value;
        _settingsService.Save();
    }

    partial void OnStartMinimizedInTrayChanged(bool value)
    {
        _settingsService.Settings.StartMinimizedInTray = value;
        _settingsService.Save();
    }

    partial void OnShowTrayNotificationsChanged(bool value)
    {
        _settingsService.Settings.ShowTrayNotifications = value;
        _settingsService.Save();
    }

    partial void OnCheckForUpdatesOnStartupChanged(bool value)
    {
        _settingsService.Settings.CheckForUpdatesOnStartup = value;
        _settingsService.Save();
    }

    partial void OnHotfolderSelectedPresetChanged(Preset? value)
    {
        _settingsService.Settings.HotfolderSelectedPresetId = value?.Id;
        _settingsService.Save();
    }

    partial void OnHotfolderIncludeSubdirectoriesChanged(bool value)
    {
        _settingsService.Settings.HotfolderIncludeSubdirectories = value;
        _settingsService.Save();
    }

    partial void OnHotfolderOriginalActionIndexChanged(int value)
    {
        _settingsService.Settings.HotfolderOriginalAction = (HotfolderOriginalAction)value;
        _settingsService.Save();
    }

    partial void OnHotfolderOriginalSubfolderChanged(string value)
    {
        _settingsService.Settings.HotfolderOriginalSubfolder = value;
        _settingsService.Save();
    }

    [RelayCommand]
    public void SelectTab(int tabIndex)
    {
        SelectedTabIndex = tabIndex;
    }

    [RelayCommand]
    private void ClearHotfolderHistory()
    {
        HotfolderHistory.Clear();
    }

    [RelayCommand]
    private void ResetHotfolderStats()
    {
        HotfolderTodayProcessedCount = 0;
        HotfolderTodaySavedBytes = 0;
        OnPropertyChanged(nameof(HotfolderSavedBytesFormatted));
        _settingsService.Settings.HotfolderTodayProcessedCount = 0;
        _settingsService.Settings.HotfolderTodaySavedBytes = 0;
        _settingsService.Save();
    }

    [RelayCommand]
    public void ToggleHotfolder()
    {
        if (IsHotfolderActive)
        {
            _hotfolderWatcher.Stop();
            IsHotfolderActive = false;
            HotfolderStatusText = "Hotfolder gestoppt";
            _trayService?.UpdateHotfolderStatus(false);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(HotfolderPath) || !Directory.Exists(HotfolderPath))
            {
                MessageBox.Show("Bitte wählen Sie zuerst einen gültigen Überwachungsordner aus.", "Hotfolder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var outDir = string.IsNullOrWhiteSpace(HotfolderOutputPath) ? Path.Combine(HotfolderPath, "converted") : HotfolderOutputPath;
            var options = HotfolderSelectedPreset != null ? HotfolderSelectedPreset.Options : BuildConversionOptions();

            var action = (HotfolderOriginalAction)HotfolderOriginalActionIndex;
            _hotfolderWatcher.Start(HotfolderPath, outDir, options, HotfolderIncludeSubdirectories, action, HotfolderOriginalSubfolder);
            IsHotfolderActive = true;
            HotfolderStatusText = $"Überwache: {HotfolderPath}";
            _trayService?.UpdateHotfolderStatus(true);

            _settingsService.Settings.HotfolderPath = HotfolderPath;
            _settingsService.Settings.HotfolderOutputDir = HotfolderOutputPath;
            _settingsService.Settings.HotfolderIncludeSubdirectories = HotfolderIncludeSubdirectories;
            _settingsService.Settings.HotfolderOriginalAction = action;
            _settingsService.Settings.HotfolderOriginalSubfolder = HotfolderOriginalSubfolder;
            _settingsService.Settings.HotfolderSelectedPresetId = HotfolderSelectedPreset?.Id;
            _settingsService.Save();
        }
    }

    private void OnHotfolderFileProcessed(object? sender, HotfolderFileEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var item = new HotfolderHistoryItem
            {
                Timestamp = DateTime.Now,
                FileName = Path.GetFileName(e.SourceFile),
                TargetFileName = e.Result.TargetPath != null ? Path.GetFileName(e.Result.TargetPath) : string.Empty,
                OriginalSizeBytes = e.Result.OriginalSizeBytes,
                FinalSizeBytes = e.Result.FinalSizeBytes,
                SavingsPercent = e.Result.SavingsPercentage,
                Success = e.Result.Success,
                ErrorMessage = e.Result.ErrorMessage
            };

            HotfolderHistory.Insert(0, item);
            while (HotfolderHistory.Count > 100)
            {
                HotfolderHistory.RemoveAt(HotfolderHistory.Count - 1);
            }

            if (e.Result.Success)
            {
                HotfolderTodayProcessedCount++;
                var saved = Math.Max(0, e.Result.OriginalSizeBytes - e.Result.FinalSizeBytes);
                HotfolderTodaySavedBytes += saved;
                OnPropertyChanged(nameof(HotfolderSavedBytesFormatted));

                _settingsService.Settings.HotfolderTodayProcessedCount = HotfolderTodayProcessedCount;
                _settingsService.Settings.HotfolderTodaySavedBytes = HotfolderTodaySavedBytes;
                _settingsService.Save();

                if (ShowTrayNotifications && _trayService != null)
                {
                    _trayService.ShowNotification(
                        "Hotfolder: Bild optimiert",
                        $"{item.FileName} -> {item.TargetFileName} ({item.FormattedSavings})",
                        System.Windows.Forms.ToolTipIcon.Info);
                }
            }
            else if (ShowTrayNotifications && _trayService != null)
            {
                _trayService.ShowNotification(
                    "Hotfolder: Fehler",
                    $"{item.FileName}: {e.Result.ErrorMessage}",
                    System.Windows.Forms.ToolTipIcon.Warning);
            }

            StatusSummary = $"Hotfolder: {e.SourceFile} verarbeitet.";
        });
    }

    [RelayCommand]
    private void SelectHotfolder()
    {
        var dlg = new OpenFolderDialog { Title = "Überwachungsordner (Hotfolder) wählen" };
        if (dlg.ShowDialog() == true)
        {
            HotfolderPath = dlg.FolderName;
        }
    }

    [RelayCommand]
    private void SelectHotfolderOutput()
    {
        var dlg = new OpenFolderDialog { Title = "Hotfolder Ausgabeordner wählen" };
        if (dlg.ShowDialog() == true)
        {
            HotfolderOutputPath = dlg.FolderName;
        }
    }

    [RelayCommand]
    private void SelectWatermarkImage()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Wasserzeichen-Bild auswählen",
            Filter = "Bilder (*.png;*.jpg;*.webp)|*.png;*.jpg;*.webp|Alle Dateien (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            WatermarkImagePath = dlg.FileName;
        }
    }

    [RelayCommand]
    private void OpenPreview()
    {
        if (SelectedFile == null) return;

        AppLogger.Debug("Preview", $"Öffne Vorher/Nachher-Vorschau für: {SelectedFile.FileName}");
        var previewWin = new PreviewWindow
        {
            Owner = Application.Current.MainWindow
        };
        previewWin.LoadComparison(SelectedFile);
        previewWin.ShowDialog();
    }

    [RelayCommand]
    private void OpenLogViewer()
    {
        try
        {
            AppLogger.Debug("UI", "Öffne System- & Fehler-Protokoll...");
            var logWin = new LogViewerWindow
            {
                Owner = Application.Current.MainWindow
            };
            logWin.Show();
        }
        catch (Exception ex)
        {
            AppLogger.Error("UI", "Fehler beim Öffnen des Protokolls: " + ex.Message, ex);
            MessageBox.Show(Application.Current.MainWindow, "Fehler beim Öffnen des Protokolls:\n" + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void AddFiles()
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Bilder (*.jpg;*.jpeg;*.png;*.webp;*.avif;*.gif;*.bmp;*.tif;*.tiff)|*.jpg;*.jpeg;*.png;*.webp;*.avif;*.gif;*.bmp;*.tif;*.tiff|Alle Dateien (*.*)|*.*",
            Title = "Bilder auswählen"
        };

        if (dlg.ShowDialog() == true)
        {
            AddFilePaths(dlg.FileNames);
        }
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dlg = new OpenFolderDialog { Title = "Ordner mit Bildern auswählen" };
        if (dlg.ShowDialog() == true)
        {
            var files = Directory.GetFiles(dlg.FolderName, "*.*", SearchOption.AllDirectories)
                .Where(f => _formatDetector.DetectFromExtension(f) != ImageFormat.Unknown)
                .ToArray();

            AddFilePaths(files);
        }
    }

    public void AddFilePaths(IEnumerable<string> paths)
    {
        var existingPaths = new HashSet<string>(Files.Select(f => f.FilePath), StringComparer.OrdinalIgnoreCase);
        int addedCount = 0;

        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                if (!existingPaths.Contains(path))
                {
                    var fi = new FileInfo(path);
                    Files.Add(new FileItemViewModel
                    {
                        FilePath = path,
                        FileName = fi.Name,
                        OriginalSizeBytes = fi.Length
                    });
                    existingPaths.Add(path);
                    addedCount++;
                }
            }
            else if (Directory.Exists(path))
            {
                var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                    .Where(f => _formatDetector.DetectFromExtension(f) != ImageFormat.Unknown);
                foreach (var f in files)
                {
                    if (!existingPaths.Contains(f))
                    {
                        var fi = new FileInfo(f);
                        Files.Add(new FileItemViewModel
                        {
                            FilePath = f,
                            FileName = fi.Name,
                            OriginalSizeBytes = fi.Length
                        });
                        existingPaths.Add(f);
                        addedCount++;
                    }
                }
            }
        }
        StatusSummary = $"{Files.Count} Datei(en) geladen";
        AppLogger.Info("UI", $"{addedCount} Datei(en) hinzugefügt. Aktuelle Gesamtliste: {Files.Count} Datei(en).");
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedFile != null)
        {
            var name = SelectedFile.FileName;
            Files.Remove(SelectedFile);
            StatusSummary = $"{Files.Count} Datei(en) geladen";
            AppLogger.Debug("UI", $"Datei aus Liste entfernt: {name}");
        }
    }

    [RelayCommand]
    private void ClearFiles()
    {
        var count = Files.Count;
        Files.Clear();
        ProgressValue = 0;
        StatusSummary = "Bereit";
        AppLogger.Info("UI", $"Dateiliste geleert ({count} Einträge entfernt).");
    }

    [RelayCommand]
    private void SelectOutputDirectory()
    {
        var dlg = new OpenFolderDialog { Title = "Zielordner auswählen" };
        if (dlg.ShowDialog() == true)
        {
            OutputDirectory = dlg.FolderName;
            _settingsService.Settings.LastOutputDirectory = dlg.FolderName;
            _settingsService.Save();
        }
    }

    [RelayCommand]
    private async Task StartConversionAsync()
    {
        if (Files.Count == 0 || IsBusy) return;

        IsBusy = true;
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanCancel));
        ProgressValue = 0;
        StatusSummary = "Konvertierung läuft...";

        _cts = new CancellationTokenSource();

        foreach (var file in Files)
        {
            file.Status = JobStatus.Pending;
            file.StatusMessage = "Ausstehend";
        }

        var options = BuildConversionOptions();
        var fileLookup = Files.ToDictionary(f => f.FilePath, StringComparer.OrdinalIgnoreCase);
        var inputs = Files.Select(f => ImageJobInput.FromFile(f.FilePath)).ToList();

        var progress = new Progress<BatchProgress>(bp =>
        {
            ProgressValue = bp.Percentage;
            StatusSummary = $"{bp.CurrentIndex} von {bp.TotalCount} verarbeitet ({bp.Percentage:N0}%)";

            if (bp.LatestResult?.SourcePath != null &&
                fileLookup.TryGetValue(bp.LatestResult.SourcePath, out var itemVm))
            {
                itemVm.UpdateFromJobResult(bp.LatestResult);
            }
        });

        try
        {
            var batchResult = await _batchProcessor.ProcessBatchAsync(
                inputs,
                options,
                saveToDisk: true,
                progress: progress,
                cancellationToken: _cts.Token);

            var savedMb = (batchResult.TotalOriginalSizeBytes - batchResult.TotalFinalSizeBytes) / (1024.0 * 1024.0);
            StatusSummary = $"Abgeschlossen: {batchResult.SucceededCount} erfolgreich, {batchResult.FailedCount} fehlgeschlagen. " +
                            $"Ersparnis: {savedMb:N2} MB ({batchResult.OverallSavingsPercentage:N1}%) in {batchResult.Duration.TotalSeconds:N1}s";
        }
        catch (OperationCanceledException)
        {
            StatusSummary = "Konvertierung vom Benutzer abgebrochen.";
        }
        catch (Exception ex)
        {
            StatusSummary = $"Fehler beim Verarbeiten: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanCancel));
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void CancelConversion()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _cts.Cancel();
            StatusSummary = "Abbruch wird eingeleitet...";
        }
    }

    public ConversionOptions BuildConversionOptions()
    {
        var options = new ConversionOptions();

        // Format mapping
        options.TargetFormat = SelectedFormatIndex switch
        {
            1 => ImageFormat.Jpeg,
            2 => ImageFormat.Png,
            3 => ImageFormat.Webp,
            4 => ImageFormat.Avif,
            5 => ImageFormat.Ico,
            6 => ImageFormat.Gif,
            7 => ImageFormat.Bmp,
            _ => null // Keep original
        };

        options.StripMetadata = StripMetadata;

        // Size limit
        if (EnableSizeLimit && SizeLimitValue > 0)
        {
            var multiplier = SelectedUnitIndex == 1 ? (1024L * 1024L) : 1024L;
            options.MaxFileSizeInBytes = (long)(SizeLimitValue * multiplier);
        }

        // Fallback strategy
        options.FallbackStrategy = SelectedFallbackStrategyIndex switch
        {
            1 => FallbackStrategy.Quantize,
            2 => FallbackStrategy.WarnOnly,
            _ => FallbackStrategy.ResizeDown
        };

        // Resize mode
        options.ResizeMode = SelectedResizeModeIndex switch
        {
            1 => CoreResizeMode.Percentage,
            2 => CoreResizeMode.AbsolutePixels,
            3 => CoreResizeMode.Pad,
            4 => CoreResizeMode.FillCrop,
            5 => CoreResizeMode.MaxEdge,
            _ => CoreResizeMode.None
        };

        options.ResizePercentage = ResizePercentage;
        options.TargetWidth = TargetWidth;
        options.TargetHeight = TargetHeight;
        options.MaxEdgeLength = MaxEdgeLength;
        options.PadColor = PadColor;
        options.MaintainAspectRatio = MaintainAspectRatio;

        // Watermark
        if (EnableWatermark)
        {
            options.Watermark = new WatermarkOptions
            {
                Enabled = true,
                Type = SelectedWatermarkTypeIndex == 1 ? WatermarkType.Image : WatermarkType.Text,
                Text = WatermarkText,
                ImagePath = WatermarkImagePath,
                Opacity = (float)WatermarkOpacity,
                FontSize = WatermarkFontSize,
                TextColor = WatermarkTextColor,
                IncludeShadow = WatermarkIncludeShadow,
                Position = SelectedWatermarkPositionIndex switch
                {
                    1 => WatermarkPosition.BottomLeft,
                    2 => WatermarkPosition.BottomCenter,
                    3 => WatermarkPosition.Center,
                    4 => WatermarkPosition.TopRight,
                    5 => WatermarkPosition.TopLeft,
                    6 => WatermarkPosition.TopCenter,
                    _ => WatermarkPosition.BottomRight
                }
            };
        }
        else
        {
            options.Watermark = new WatermarkOptions { Enabled = false };
        }

        if (!string.IsNullOrWhiteSpace(OutputDirectory))
        {
            options.OutputDirectory = OutputDirectory;
        }

        return options;
    }

    [RelayCommand]
    public async Task CheckForUpdatesManualAsync()
    {
        await CheckForUpdatesInternalAsync(showFeedbackWhenNoUpdate: true);
    }

    [RelayCommand]
    public void ShowUpdateDialog()
    {
        if (_latestUpdateInfo != null)
        {
            var dialog = new UpdateNotificationDialog(_latestUpdateInfo, _updateService, _settingsService)
            {
                Owner = Application.Current.MainWindow
            };
            dialog.ShowDialog();
        }
    }

    private async Task CheckForUpdatesInternalAsync(bool showFeedbackWhenNoUpdate)
    {
        if (IsCheckingForUpdates) return;
        IsCheckingForUpdates = true;

        try
        {
            var owner = _settingsService.Settings.GitHubRepositoryOwner;
            var repo = _settingsService.Settings.GitHubRepositoryName;

            AppLogger.Info("Update", $"Prüfe auf Updates bei {owner}/{repo} (Installierte Version: {_updateService.CurrentVersion})...");
            var updateInfo = await _updateService.CheckForUpdateAsync(owner, repo);

            _settingsService.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
            _settingsService.Save();

            if (updateInfo.IsUpdateAvailable)
            {
                _latestUpdateInfo = updateInfo;
                IsUpdateAvailable = true;
                UpdateBadgeText = $"🚀 Update v{updateInfo.LatestVersion} verfügbar!";
                AppLogger.Info("Update", $"Neues Update gefunden: v{updateInfo.LatestVersion}");

                var skipped = _settingsService.Settings.SkippedVersion;
                var currentVerStr = updateInfo.LatestVersion?.ToString();
                if (showFeedbackWhenNoUpdate || string.IsNullOrEmpty(skipped) || !string.Equals(skipped, currentVerStr, StringComparison.OrdinalIgnoreCase))
                {
                    ShowUpdateDialog();
                }
            }
            else
            {
                IsUpdateAvailable = false;
                UpdateBadgeText = string.Empty;

                if (showFeedbackWhenNoUpdate)
                {
                    if (!string.IsNullOrEmpty(updateInfo.ErrorMessage))
                    {
                        MessageBox.Show(Application.Current.MainWindow,
                            $"Update-Prüfung:\n\n{updateInfo.ErrorMessage}",
                            "Update-Prüfung", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(Application.Current.MainWindow,
                            $"Imaginary ist auf dem neuesten Stand!\n\nInstallierte Version: v{_updateService.CurrentVersion}",
                            "Imaginary ist aktuell", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Update", "Fehler bei Update-Prüfung", ex);
            if (showFeedbackWhenNoUpdate)
            {
                MessageBox.Show(Application.Current.MainWindow,
                    $"Fehler bei der Update-Prüfung:\n{ex.Message}",
                    "Update-Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }
}
