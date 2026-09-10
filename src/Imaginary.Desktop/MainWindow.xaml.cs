using System.IO;
using System.Windows;
using Imaginary.Core;
using Imaginary.Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Imaginary.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        var services = new ServiceCollection();
        services.AddImaginaryCore();
        services.AddSingleton<MainViewModel>();
        var serviceProvider = services.BuildServiceProvider();

        _viewModel = serviceProvider.GetRequiredService<MainViewModel>();
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