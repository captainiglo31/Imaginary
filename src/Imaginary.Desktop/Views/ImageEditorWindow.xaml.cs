using System;
using System.ComponentModel;
using System.Windows;
using Imaginary.Core.Services;
using Imaginary.Desktop.Services;

namespace Imaginary.Desktop.Views;

public partial class ImageEditorWindow : Window
{
    public ImageEditorWindow(
        string filePath,
        IImageEditorService editorService,
        IBackgroundRemovalService bgRemovalService,
        IAutoRedactionService? autoRedactionService = null,
        IUpscaleService? upscaleService = null,
        IImageConverter? converter = null,
        IScreenshotService? screenshotService = null)
    {
        InitializeComponent();
        ResultFilePath = filePath;
        EditorControl.Initialize(filePath, editorService, bgRemovalService, autoRedactionService, upscaleService, converter, screenshotService);
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
