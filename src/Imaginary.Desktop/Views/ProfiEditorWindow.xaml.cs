using System;
using System.ComponentModel;
using System.Windows;
using Imaginary.Core.Services;

namespace Imaginary.Desktop.Views;

public partial class ProfiEditorWindow : Window
{
    public ProfiEditorWindow(string filePath, IProfiDeveloperService developerService)
    {
        InitializeComponent();
        ResultFilePath = filePath;
        ProfiControl.Initialize(filePath, developerService);
        ProfiControl.FileSaved += (s, path) =>
        {
            ResultFilePath = path ?? filePath;
            DialogResult = true;
            Close();
        };
        ProfiControl.Cancelled += (s, e) =>
        {
            DialogResult = false;
            Close();
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        ProfiControl.Cleanup();
        base.OnClosing(e);
    }

    public string ResultFilePath { get; private set; } = string.Empty;
    public bool HasChanges => ProfiControl.HasChanges;
}
