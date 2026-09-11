using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Imaginary.Core;
using Imaginary.Core.Models;
using Imaginary.Desktop.Models;
using Imaginary.Desktop.Services;
using Imaginary.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Imaginary.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ITrayService _trayService;
    private Point _dragStartPoint;
    private bool _isDraggingOut;

    public MainWindow()
    {
        InitializeComponent();

        var services = new ServiceCollection();
        services.AddImaginaryCore();
        services.AddSingleton<ITrayService, TrayService>();
        services.AddSingleton<IScreenshotService, ScreenshotService>();
        services.AddSingleton<MainViewModel>();
        var serviceProvider = services.BuildServiceProvider();

        _viewModel = serviceProvider.GetRequiredService<MainViewModel>();
        _trayService = serviceProvider.GetRequiredService<ITrayService>();

        _trayService.Initialize(
            mainWindow: this,
            toggleHotfolder: () => _viewModel.ToggleHotfolderCommand.Execute(null),
            isHotfolderRunning: () => _viewModel.IsHotfolderActive,
            openSettings: () => _viewModel.SelectTabCommand.Execute(2),
            captureScreenshot: () => _viewModel.CaptureScreenshotCommand.Execute(null));

        _viewModel.AttachTrayService(_trayService);
        DataContext = _viewModel;

        if (App.StartupArgs.Length > 0)
        {
            _viewModel.AddFilePaths(App.StartupArgs);
        }

        App.FilesReceivedViaIpc += files =>
        {
            _viewModel.AddFilePaths(files);
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
            _viewModel.OpenEditorCommand.Execute(null);
            e.Handled = true;
        }
    }
}