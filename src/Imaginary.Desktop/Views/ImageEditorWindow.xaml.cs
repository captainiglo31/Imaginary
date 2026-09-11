using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Imaginary.Core.Logging;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using SkiaSharp;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace Imaginary.Desktop.Views;

public partial class ImageEditorWindow : Window
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private readonly string _filePath;
    private readonly IImageEditorService _editorService;
    private readonly IBackgroundRemovalService _bgRemovalService;

    private SKBitmap _currentBitmap;
    private readonly Stack<SKBitmap> _undoStack = new();
    private readonly Stack<SKBitmap> _redoStack = new();

    private EditorTool _currentTool = EditorTool.Pan;
    private SKColor _selectedColor = SKColors.Red;
    private float _strokeWidth = 4f;
    private int _stepBadgeCounter = 1;

    private WpfPoint _dragStart;
    private bool _isDragging;
    private bool _isPickingColor;
    private SKColor _pickedColor = SKColors.White;
    private SKRectI? _activeCropRect;

    public string ResultFilePath { get; private set; }
    public bool HasChanges { get; private set; }

    public ImageEditorWindow(string filePath, IImageEditorService editorService, IBackgroundRemovalService bgRemovalService)
    {
        InitializeComponent();

        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _editorService = editorService ?? throw new ArgumentNullException(nameof(editorService));
        _bgRemovalService = bgRemovalService ?? throw new ArgumentNullException(nameof(bgRemovalService));

        ResultFilePath = filePath;

        TextFileName.Text = Path.GetFileName(filePath);

        using (var stream = File.OpenRead(filePath))
        {
            _currentBitmap = SKBitmap.Decode(stream);
        }

        UpdateImageDisplay();
        UpdateUndoRedoButtons();
        UpdateAiModelStatus();

        ToolPan.IsChecked = true;
    }

    private void UpdateImageDisplay()
    {
        TextDimensions.Text = $"{_currentBitmap.Width} × {_currentBitmap.Height} px";

        using var image = SKImage.FromBitmap(_currentBitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Position = 0;

        var bitmapImage = new BitmapImage();
        bitmapImage.BeginInit();
        bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
        bitmapImage.StreamSource = stream;
        bitmapImage.EndInit();
        bitmapImage.Freeze();

        BaseImage.Source = bitmapImage;
    }

    private void PushUndoState()
    {
        _undoStack.Push(_currentBitmap.Copy());
        _redoStack.Clear();
        HasChanges = true;
        UpdateUndoRedoButtons();
    }

    private void UpdateUndoRedoButtons()
    {
        ButtonUndo.IsEnabled = _undoStack.Count > 0;
        ButtonRedo.IsEnabled = _redoStack.Count > 0;
    }

    private void OnUndoClicked(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0) return;

        _redoStack.Push(_currentBitmap);
        _currentBitmap = _undoStack.Pop();
        UpdateImageDisplay();
        UpdateUndoRedoButtons();
    }

    private void OnRedoClicked(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;

        _undoStack.Push(_currentBitmap);
        _currentBitmap = _redoStack.Pop();
        UpdateImageDisplay();
        UpdateUndoRedoButtons();
    }

    private void OnToolSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;

        string name = rb.Name;
        _isPickingColor = false;

        _currentTool = name switch
        {
            "ToolCrop" => EditorTool.Crop,
            "ToolPixelate" => EditorTool.Pixelate,
            "ToolBlur" => EditorTool.Blur,
            "ToolBlackout" => EditorTool.Blackout,
            "ToolArrow" => EditorTool.Arrow,
            "ToolRectangle" => EditorTool.Rectangle,
            "ToolOval" => EditorTool.Oval,
            "ToolStepBadge" => EditorTool.StepBadge,
            "ToolPen" => EditorTool.Pen,
            "ToolHighlighter" => EditorTool.Highlighter,
            "ToolText" => EditorTool.Text,
            "ToolBgRemove" => EditorTool.BackgroundRemoval,
            _ => EditorTool.Pan
        };

        PanelCropOptions.Visibility = _currentTool == EditorTool.Crop ? Visibility.Visible : Visibility.Collapsed;
        PanelBlurOptions.Visibility = _currentTool == EditorTool.Pixelate ? Visibility.Visible : Visibility.Collapsed;
        SidePanel.Visibility = _currentTool == EditorTool.BackgroundRemoval ? Visibility.Visible : Visibility.Collapsed;

        TextStatusHint.Text = _currentTool switch
        {
            EditorTool.Pan => "✋ Hand-Modus: Bild mit der Maus verschieben • Mausrad zum Zoomen.",
            EditorTool.Crop => "✂️ Zuschnitt: Bereich mit der Maus aufziehen • Dann 'Zuschnitt anwenden' klicken.",
            EditorTool.Pixelate => "🔲 DSGVO-Verpixelung: Bereich über sensible Daten (Passwörter, Gesichter, Kennzeichen) ziehen.",
            EditorTool.Blur => "💧 Weichzeichner: Bereich ziehen, um Inhalt sanft unkenntlich zu machen.",
            EditorTool.Blackout => "⬛ Schwärzen: Blickdichten Block über vertrauliche Daten ziehen.",
            EditorTool.Arrow => "🏹 Pfeil: Vom Startpunkt zum Zielpunkt ziehen.",
            EditorTool.Rectangle => "▭ Rechteck: Rahmen zur Hervorhebung aufziehen.",
            EditorTool.Oval => "⭕ Kreis: Detail einkreisen.",
            EditorTool.StepBadge => $"❶ Schritt-Badge: Klick ins Bild platziert Schritt-Nummer {_stepBadgeCounter}.",
            EditorTool.Pen => "✏️ Freihand: Mit der Maus frei zeichnen.",
            EditorTool.Highlighter => "🖍️ Textmarker: Halbtransparente Markierung über Text ziehen.",
            EditorTool.Text => "🔤 Text: Klick ins Bild, um Text einzugeben.",
            EditorTool.BackgroundRemoval => "🪄 Hintergrund: Rechts Farbe oder KI-Modell wählen.",
            _ => "Werkzeug aktiv."
        };
    }

    private void OnColorSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string hex)
        {
            _selectedColor = SKColor.Parse(hex);
        }
    }

    private void OnStrokeWidthChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboStrokeWidth?.SelectedItem is ComboBoxItem item && float.TryParse(item.Tag?.ToString(), out float w))
        {
            _strokeWidth = w;
        }
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool == EditorTool.Pan) return;

        var pos = e.GetPosition(BaseImage);
        _dragStart = pos;
        _isDragging = true;

        if (_isPickingColor)
        {
            int px = Math.Clamp((int)pos.X, 0, _currentBitmap.Width - 1);
            int py = Math.Clamp((int)pos.Y, 0, _currentBitmap.Height - 1);
            _pickedColor = _currentBitmap.GetPixel(px, py);
            BorderPickedColor.Background = new SolidColorBrush(WpfColor.FromRgb(_pickedColor.Red, _pickedColor.Green, _pickedColor.Blue));
            _isPickingColor = false;
            TextStatusHint.Text = $"Farbe ausgewählt: #{_pickedColor.Red:X2}{_pickedColor.Green:X2}{_pickedColor.Blue:X2}";
            return;
        }

        if (_currentTool == EditorTool.StepBadge)
        {
            PushUndoState();
            var center = new SKPoint((float)pos.X, (float)pos.Y);
            var updated = _editorService.DrawStepBadge(_currentBitmap, center, _stepBadgeCounter++, _selectedColor);
            _currentBitmap.Dispose();
            _currentBitmap = updated;
            UpdateImageDisplay();
            TextStatusHint.Text = $"Schritt-Badge {_stepBadgeCounter - 1} platziert. Nächster: {_stepBadgeCounter}.";
            _isDragging = false;
            return;
        }

        if (_currentTool == EditorTool.Text)
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox("Geben Sie den gewünschten Text ein:", "Text einfügen", "Hinweis");
            if (!string.IsNullOrWhiteSpace(input))
            {
                PushUndoState();
                var textPos = new SKPoint((float)pos.X, (float)pos.Y);
                var updated = _editorService.DrawText(_currentBitmap, input, textPos, _selectedColor, fontSize: 24f, backgroundColor: new SKColor(15, 23, 42, 210));
                _currentBitmap.Dispose();
                _currentBitmap = updated;
                UpdateImageDisplay();
            }
            _isDragging = false;
            return;
        }

        CanvasContainer.CaptureMouse();
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;

        var current = e.GetPosition(BaseImage);
        double left = Math.Min(_dragStart.X, current.X);
        double top = Math.Min(_dragStart.Y, current.Y);
        double width = Math.Abs(_dragStart.X - current.X);
        double height = Math.Abs(_dragStart.Y - current.Y);

        switch (_currentTool)
        {
            case EditorTool.Crop:
            case EditorTool.Pixelate:
            case EditorTool.Blur:
            case EditorTool.Blackout:
            case EditorTool.Rectangle:
                PreviewRect.Visibility = Visibility.Visible;
                Canvas.SetLeft(PreviewRect, left);
                Canvas.SetTop(PreviewRect, top);
                PreviewRect.Width = width;
                PreviewRect.Height = height;
                PreviewRect.Stroke = new SolidColorBrush(WpfColor.FromRgb(_selectedColor.Red, _selectedColor.Green, _selectedColor.Blue));
                break;

            case EditorTool.Oval:
                PreviewEllipse.Visibility = Visibility.Visible;
                Canvas.SetLeft(PreviewEllipse, left);
                Canvas.SetTop(PreviewEllipse, top);
                PreviewEllipse.Width = width;
                PreviewEllipse.Height = height;
                PreviewEllipse.Stroke = new SolidColorBrush(WpfColor.FromRgb(_selectedColor.Red, _selectedColor.Green, _selectedColor.Blue));
                break;

            case EditorTool.Arrow:
            case EditorTool.Pen:
            case EditorTool.Highlighter:
                PreviewLine.Visibility = Visibility.Visible;
                PreviewLine.X1 = _dragStart.X;
                PreviewLine.Y1 = _dragStart.Y;
                PreviewLine.X2 = current.X;
                PreviewLine.Y2 = current.Y;
                PreviewLine.Stroke = new SolidColorBrush(WpfColor.FromRgb(_selectedColor.Red, _selectedColor.Green, _selectedColor.Blue));
                PreviewLine.StrokeThickness = _strokeWidth;
                break;
        }
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;

        _isDragging = false;
        CanvasContainer.ReleaseMouseCapture();

        PreviewRect.Visibility = Visibility.Collapsed;
        PreviewEllipse.Visibility = Visibility.Collapsed;
        PreviewLine.Visibility = Visibility.Collapsed;

        var end = e.GetPosition(BaseImage);
        int x1 = (int)Math.Min(_dragStart.X, end.X);
        int y1 = (int)Math.Min(_dragStart.Y, end.Y);
        int x2 = (int)Math.Max(_dragStart.X, end.X);
        int y2 = (int)Math.Max(_dragStart.Y, end.Y);

        int w = x2 - x1;
        int h = y2 - y1;

        if (_currentTool == EditorTool.Crop)
        {
            if (w >= 10 && h >= 10)
            {
                _activeCropRect = new SKRectI(x1, y1, x2, y2);
                PreviewRect.Visibility = Visibility.Visible;
                Canvas.SetLeft(PreviewRect, x1);
                Canvas.SetTop(PreviewRect, y1);
                PreviewRect.Width = w;
                PreviewRect.Height = h;
                TextStatusHint.Text = $"Zuschnitt-Bereich ({w} × {h} px) gewählt. Klicke oben auf '✂️ Zuschnitt anwenden'.";
            }
            return;
        }

        if (w < 4 && h < 4 && _currentTool != EditorTool.Pen) return;

        PushUndoState();

        SKBitmap updated = _currentBitmap;

        switch (_currentTool)
        {
            case EditorTool.Pixelate:
                int pixelSize = (int)SliderPixelSize.Value;
                updated = _editorService.ApplyPixelate(_currentBitmap, new SKRectI(x1, y1, x2, y2), pixelSize);
                break;

            case EditorTool.Blur:
                updated = _editorService.ApplyGaussianBlur(_currentBitmap, new SKRectI(x1, y1, x2, y2), sigma: 14f);
                break;

            case EditorTool.Blackout:
                updated = _editorService.ApplyBlackout(_currentBitmap, new SKRectI(x1, y1, x2, y2), _selectedColor);
                break;

            case EditorTool.Arrow:
                var startPt = new SKPoint((float)_dragStart.X, (float)_dragStart.Y);
                var endPt = new SKPoint((float)end.X, (float)end.Y);
                updated = _editorService.DrawArrow(_currentBitmap, startPt, endPt, _selectedColor, _strokeWidth);
                break;

            case EditorTool.Rectangle:
                updated = _editorService.DrawRectangle(_currentBitmap, new SKRect(x1, y1, x2, y2), _selectedColor, _strokeWidth);
                break;

            case EditorTool.Oval:
                updated = _editorService.DrawOval(_currentBitmap, new SKRect(x1, y1, x2, y2), _selectedColor, _strokeWidth);
                break;

            case EditorTool.Pen:
                var p1 = new SKPoint((float)_dragStart.X, (float)_dragStart.Y);
                var p2 = new SKPoint((float)end.X, (float)end.Y);
                updated = _editorService.DrawArrow(_currentBitmap, p1, p2, _selectedColor, _strokeWidth); // simple line
                break;

            case EditorTool.Highlighter:
                var hl1 = new SKPoint((float)_dragStart.X, (float)_dragStart.Y);
                var hl2 = new SKPoint((float)end.X, (float)end.Y);
                updated = _editorService.DrawHighlighter(_currentBitmap, hl1, hl2, _selectedColor, strokeWidth: 26f);
                break;
        }

        if (updated != _currentBitmap)
        {
            _currentBitmap.Dispose();
            _currentBitmap = updated;
            UpdateImageDisplay();
        }
    }

    private void OnApplyCropClicked(object sender, RoutedEventArgs e)
    {
        if (_activeCropRect.HasValue)
        {
            PushUndoState();
            var cropped = _editorService.Crop(_currentBitmap, _activeCropRect.Value);
            _currentBitmap.Dispose();
            _currentBitmap = cropped;
            _activeCropRect = null;
            PreviewRect.Visibility = Visibility.Collapsed;
            UpdateImageDisplay();
            TextStatusHint.Text = "Zuschnitt erfolgreich angewendet.";
        }
    }

    private void OnCropAspectChanged(object sender, SelectionChangedEventArgs e)
    {
        // Aspect ratio handling for crop
    }

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control || _currentTool == EditorTool.Pan)
        {
            double factor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
            SetZoom(CanvasScaleTransform.ScaleX * factor);
            e.Handled = true;
        }
    }

    private void OnZoomInClicked(object sender, RoutedEventArgs e) => SetZoom(CanvasScaleTransform.ScaleX * 1.25);
    private void OnZoomOutClicked(object sender, RoutedEventArgs e) => SetZoom(CanvasScaleTransform.ScaleX * 0.8);
    private void OnZoomResetClicked(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void OnZoomFitClicked(object sender, RoutedEventArgs e)
    {
        double viewportW = CanvasScrollViewer.ActualWidth - 40;
        double viewportH = CanvasScrollViewer.ActualHeight - 40;
        if (viewportW <= 0 || viewportH <= 0) return;

        double scaleX = viewportW / _currentBitmap.Width;
        double scaleY = viewportH / _currentBitmap.Height;
        SetZoom(Math.Min(scaleX, scaleY));
    }

    private void SetZoom(double scale)
    {
        double clamped = Math.Clamp(scale, 0.1, 8.0);
        CanvasScaleTransform.ScaleX = clamped;
        CanvasScaleTransform.ScaleY = clamped;
        TextZoomLevel.Text = $"{(int)(clamped * 100)}%";
    }

    // Background Removal
    private void OnBgModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboBgMode == null) return;
        bool isAi = ComboBgMode.SelectedIndex == 1;
        PanelColorKeyOptions.Visibility = isAi ? Visibility.Collapsed : Visibility.Visible;
        PanelAiOptions.Visibility = isAi ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnPickColorClicked(object sender, RoutedEventArgs e)
    {
        _isPickingColor = true;
        TextStatusHint.Text = "🎯 Klicke auf die Hintergrundfarbe im Bild, die transparent werden soll.";
    }

    private void OnExecuteColorRemovalClicked(object sender, RoutedEventArgs e)
    {
        PushUndoState();
        float tolerance = (float)(SliderTolerance.Value / 100.0);
        var transparent = _editorService.RemoveBackgroundByColor(_currentBitmap, _pickedColor, tolerance);
        _currentBitmap.Dispose();
        _currentBitmap = transparent;
        UpdateImageDisplay();
        TextStatusHint.Text = "Hintergrund erfolgreich transparent gemacht!";
    }

    private void UpdateAiModelStatus()
    {
        bool downloaded = _bgRemovalService.IsAiModelDownloaded();
        if (downloaded)
        {
            long size = _bgRemovalService.GetModelSizeBytes();
            TextAiStatus.Text = $"🟢 Modell bereit ({size / (1024.0 * 1024.0):F1} MB)";
            ButtonDownloadAiModel.Visibility = Visibility.Collapsed;
            ButtonExecuteAiRemoval.IsEnabled = true;
        }
        else
        {
            TextAiStatus.Text = "⚪ Modell noch nicht heruntergeladen";
            ButtonDownloadAiModel.Visibility = Visibility.Visible;
            ButtonExecuteAiRemoval.IsEnabled = false;
        }
    }

    private async void OnDownloadAiModelClicked(object sender, RoutedEventArgs e)
    {
        ButtonDownloadAiModel.IsEnabled = false;
        ProgressAiDownload.Visibility = Visibility.Visible;
        ProgressAiDownload.Value = 0;

        var progress = new Progress<double>(pct => ProgressAiDownload.Value = pct);
        bool ok = await _bgRemovalService.DownloadModelAsync(progress);

        ProgressAiDownload.Visibility = Visibility.Collapsed;
        ButtonDownloadAiModel.IsEnabled = true;
        UpdateAiModelStatus();

        if (ok)
        {
            MessageBox.Show(this, "Das KI-Modell (U-2-Net) wurde erfolgreich heruntergeladen und ist nun 100% offline einsatzbereit!", "KI Modell", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(this, "Fehler beim Herunterladen des KI-Modells. Bitte Internetverbindung prüfen.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnExecuteAiRemovalClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            ButtonExecuteAiRemoval.IsEnabled = false;
            TextStatusHint.Text = "🧠 KI analysiert das Bild und segmentiert den Hintergrund...";

            PushUndoState();
            var result = await _bgRemovalService.RemoveBackgroundAsync(_currentBitmap, BackgroundRemovalMode.AiOnnx);

            _currentBitmap.Dispose();
            _currentBitmap = result;
            UpdateImageDisplay();
            TextStatusHint.Text = "Hintergrund mit Deep-Learning KI erfolgreich entfernt!";
        }
        catch (Exception ex)
        {
            AppLogger.Error("AI", "Fehler bei KI-Hintergrundentfernung", ex);
            MessageBox.Show(this, "Fehler bei der KI-Hintergrundentfernung: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            ButtonExecuteAiRemoval.IsEnabled = true;
        }
    }

    // Saving & Actions
    private void OnSaveAndReplaceClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveBitmapToFile(_currentBitmap, _filePath);
            ResultFilePath = _filePath;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Editor", "Fehler beim Speichern", ex);
            MessageBox.Show(this, "Fehler beim Speichern: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSaveAsCopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = Path.GetDirectoryName(_filePath) ?? "";
            string name = Path.GetFileNameWithoutExtension(_filePath);
            string copyPath = Path.Combine(dir, $"{name}_bearbeitet.png");

            SaveBitmapToFile(_currentBitmap, copyPath);
            ResultFilePath = copyPath;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Editor", "Fehler beim Speichern als Kopie", ex);
            MessageBox.Show(this, "Fehler beim Speichern: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCopyToClipboardClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            using var image = SKImage.FromBitmap(_currentBitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream();
            data.SaveTo(stream);
            stream.Position = 0;

            var wpfBmp = new BitmapImage();
            wpfBmp.BeginInit();
            wpfBmp.CacheOption = BitmapCacheOption.OnLoad;
            wpfBmp.StreamSource = stream;
            wpfBmp.EndInit();
            wpfBmp.Freeze();

            Clipboard.SetImage(wpfBmp);
            TextStatusHint.Text = "📋 Bearbeitetes Bild erfolgreich in die Zwischenablage kopiert!";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Editor", "Fehler beim Kopieren in Zwischenablage", ex);
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OnUndoClicked(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OnRedoClicked(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_activeCropRect.HasValue)
            {
                _activeCropRect = null;
                PreviewRect.Visibility = Visibility.Collapsed;
                TextStatusHint.Text = "Zuschnitt abgebrochen.";
            }
        }
    }

    private static void SaveBitmapToFile(SKBitmap bitmap, string path)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    protected override void OnClosed(EventArgs e)
    {
        _currentBitmap?.Dispose();
        while (_undoStack.Count > 0) _undoStack.Pop().Dispose();
        while (_redoStack.Count > 0) _redoStack.Pop().Dispose();
        base.OnClosed(e);
    }
}

public enum EditorTool
{
    Pan,
    Crop,
    Pixelate,
    Blur,
    Blackout,
    Arrow,
    Rectangle,
    Oval,
    StepBadge,
    Pen,
    Highlighter,
    Text,
    BackgroundRemoval
}
