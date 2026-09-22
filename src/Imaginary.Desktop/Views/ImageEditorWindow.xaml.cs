using System;
using System.ComponentModel;
using System.Windows;
using Imaginary.Core.Services;

namespace Imaginary.Desktop.Views;

public partial class ImageEditorWindow : Window
{
    public ImageEditorWindow(string filePath, IImageEditorService editorService, IBackgroundRemovalService bgRemovalService)
    {
        InitializeComponent();
        ResultFilePath = filePath;
        EditorControl.Initialize(filePath, editorService, bgRemovalService);
        EditorControl.FileSaved += (s, path) =>
        {
            ResultFilePath = path ?? filePath;
            DialogResult = true;
            Close();
        };
        EditorControl.Cancelled += (s, e) =>
        {
            DialogResult = false;
            Close();
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        EditorControl.Cleanup();
        base.OnClosing(e);
    }

    public string ResultFilePath { get; private set; } = string.Empty;
    public bool HasChanges => EditorControl.HasChanges;
}
