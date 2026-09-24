using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Imaginary.Core;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using Imaginary.Desktop.Models;
using Imaginary.Desktop.Services;
using Imaginary.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Imaginary.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ITrayService _trayService;
    private readonly IClipboardMonitorService _clipboardMonitor;
    private readonly IImageEditorService _editorService;
    private readonly IBackgroundRemovalService _bgRemovalService;
    private readonly IProfiDeveloperService _profiDeveloperService;
    private readonly IAutoRedactionService _autoRedactionService;
    private readonly IUpscaleService _upscaleService;
    private readonly IImageConverter _converter;
    private Point _dragStartPoint;
    private bool _isDraggingOut;

    public MainWindow()
    {
        InitializeComponent();

        var services = new ServiceCollection();
        services.AddImaginaryCore();
        services.AddSingleton<ITrayService, TrayService>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<IClipboardMonitorService, ClipboardMonitorService>();
        services.AddSingleton<IStatsReportingService, StatsReportingService>();
        services.AddSingleton<MainViewModel>();
        var serviceProvider = services.BuildServiceProvider();

        _viewModel = serviceProvider.GetRequiredService<MainViewModel>();
        _trayService = serviceProvider.GetRequiredService<ITrayService>();
        _clipboardMonitor = serviceProvider.GetRequiredService<IClipboardMonitorService>();
        _editorService = serviceProvider.GetRequiredService<IImageEditorService>();
        _bgRemovalService = serviceProvider.GetRequiredService<IBackgroundRemovalService>();
        _profiDeveloperService = serviceProvider.GetRequiredService<IProfiDeveloperService>();
        _autoRedactionService = serviceProvider.GetRequiredService<IAutoRedactionService>();
        _upscaleService = serviceProvider.GetRequiredService<IUpscaleService>();
        _converter = serviceProvider.GetRequiredService<IImageConverter>();

        _clipboardMonitor.StartMonitoring(this);

        _trayService.Initialize(
            mainWindow: this,
            toggleHotfolder: () => _viewModel.ToggleHotfolderCommand.Execute(null),
            isHotfolderRunning: () => _viewModel.IsHotfolderActive,
            openSettings: () => _viewModel.SelectTabCommand.Execute(4),
            captureScreenshot: () => _viewModel.TriggerNativeSnippingCommand.Execute(null));

        _viewModel.AttachTrayService(_trayService);

        // Wire up Studio Workspace events
        _viewModel.StudioImageLoadRequested += (filePath, subMode) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (subMode == 1)
                {
                    StudioProfiEditorControl.Initialize(filePath, _profiDeveloperService);
                }
                else
                {
                    StudioImageEditorControl.Initialize(filePath, _editorService, _bgRemovalService, _autoRedactionService, _upscaleService, _converter);
                }
            });
        };

        StudioImageEditorControl.FileSaved += (s, path) =>
        {
            _viewModel.ApplyStudioChangesToBatch(path);
        };

        StudioImageEditorControl.Cancelled += (s, e) =>
        {
            _viewModel.CloseStudioImageCommand.Execute(null);
        };

        StudioImageEditorControl.SwitchToProfiRequested += (s, path) =>
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                _viewModel.LoadFileInStudio(path, subMode: 1, _viewModel.StudioSourceBatchItem);
            }
        };

        StudioProfiEditorControl.FileSaved += (s, path) =>
        {
            _viewModel.ApplyStudioChangesToBatch(path);
        };

        StudioProfiEditorControl.Cancelled += (s, e) =>
        {
            _viewModel.CloseStudioImageCommand.Execute(null);
        };

        DataContext = _viewModel;

        Loaded += (s, e) =>
        {
            if (App.StartupArgs.Length > 0)
            {
                _viewModel.HandleIncomingArguments(App.StartupArgs);
            }
        };

        App.FilesReceivedViaIpc += files =>
        {
            _viewModel.HandleIncomingArguments(files);
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();

            if (!_viewModel.HasShownTrayIntroBalloon)
            {
                _trayService.ShowNotification(
                    "Imaginary läuft im Hintergrund",
                    "Imaginary überwacht weiterhin deine Ordner im Hintergrund. Klicke auf das Symbol in der Taskleiste, um das Fenster zu öffnen.",
                    System.Windows.Forms.ToolTipIcon.Info);
                _viewModel.MarkTrayIntroBalloonShown();
            }
            return;
        }

        _clipboardMonitor.Dispose();
        _trayService.Dispose();
        base.OnClosing(e);
    }

    private void OnGridDragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnGridDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0)
            {
                _viewModel.AddFilePaths(files);
            }
        }
    }

    private void OnStudioDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files != null && files.Length > 0 && File.Exists(files[0]))
            {
                _viewModel.LoadFileInStudio(files[0], _viewModel.StudioSubMode);
            }
        }
    }

    private void OnDataGridPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep && FindVisualParent<DataGridRow>(dep) != null)
        {
            _dragStartPoint = e.GetPosition(null);
            _isDraggingOut = false;
        }
    }

    private void OnDataGridPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDraggingOut)
            return;

        var currentPoint = e.GetPosition(null);
        var diff = _dragStartPoint - currentPoint;

        if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
        {
            if (sender is DataGrid dg)
            {
                var filesToDrag = new List<string>();

                foreach (var item in dg.SelectedItems)
                {
                    if (item is FileItemViewModel fileVm)
                    {
                        var path = (!string.IsNullOrWhiteSpace(fileVm.TargetPath) && File.Exists(fileVm.TargetPath))
                            ? fileVm.TargetPath
                            : fileVm.FilePath;

                        if (File.Exists(path))
                        {
                            filesToDrag.Add(path);
                        }
                    }
                    else if (item is HotfolderHistoryItem historyItem)
                    {
                        var path = historyItem.TargetFilePath;
                        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                        {
                            filesToDrag.Add(path);
                        }
                    }
                }

                if (filesToDrag.Count > 0)
                {
                    _isDraggingOut = true;
                    try
                    {
                        var dataObject = new DataObject(DataFormats.FileDrop, filesToDrag.ToArray());
                        DragDrop.DoDragDrop(dg, dataObject, DragDropEffects.Copy);
                    }
                    finally
                    {
                        _isDraggingOut = false;
                    }
                }
            }
        }
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;
        if (parentObject is T parent) return parent;
        return FindVisualParent<T>(parentObject);
    }

    private void OnDataGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid dg && dg.SelectedItem is FileItemViewModel)
        {
            _viewModel.OpenDefaultActionForSelected();
            e.Handled = true;
        }
    }

    private void OnDataGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject dep)
        {
            var row = FindVisualParent<DataGridRow>(dep);
            if (row != null && sender is DataGrid dg && row.Item is FileItemViewModel item)
            {
                dg.SelectedItem = item;
                _viewModel.SelectedFile = item;
                row.IsSelected = true;
                row.Focus();
            }
        }
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slideIn = new DoubleAnimation(14.0, 0.0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            RootWindowGrid?.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            WindowTranslateTransform?.BeginAnimation(TranslateTransform.YProperty, slideIn);
        }
        catch { }
    }

    private void OnMainTabControlSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source is TabControl tc && ReferenceEquals(tc, MainTabControl))
        {
            var anim = new DoubleAnimation(0.35, 1.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            MainTabControl.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }
}