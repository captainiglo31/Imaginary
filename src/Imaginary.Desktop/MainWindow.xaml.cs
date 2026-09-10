using System.ComponentModel;
using System.IO;
using System.Windows;
using Imaginary.Core;
using Imaginary.Desktop.Services;
using Imaginary.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Imaginary.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ITrayService _trayService;

    public MainWindow()
    {
        InitializeComponent();

        var services = new ServiceCollection();
        services.AddImaginaryCore();
        services.AddSingleton<ITrayService, TrayService>();
        services.AddSingleton<MainViewModel>();
        var serviceProvider = services.BuildServiceProvider();

        _viewModel = serviceProvider.GetRequiredService<MainViewModel>();
        _trayService = serviceProvider.GetRequiredService<ITrayService>();

        _trayService.Initialize(
            mainWindow: this,
            toggleHotfolder: () => _viewModel.ToggleHotfolderCommand.Execute(null),
            isHotfolderRunning: () => _viewModel.IsHotfolderActive,
            openSettings: () => _viewModel.SelectTabCommand.Execute(2));

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
}