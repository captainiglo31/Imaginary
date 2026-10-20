using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using Imaginary.Desktop.Services;
using SkiaSharp;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace Imaginary.Desktop.Views;

public partial class ImageEditorControl : UserControl
{
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    private Window ParentWindow => Window.GetWindow(this) ?? Application.Current.MainWindow;

    public event EventHandler<string?>? FileSaved;
    public event EventHandler? Cancelled;
    public event EventHandler<string?>? SwitchToProfiRequested;

    private string _filePath = string.Empty;
    private IImageEditorService? _editorService;
    private IBackgroundRemovalService? _bgRemovalService;
    private IAutoRedactionService? _autoRedactionService;
    private IUpscaleService? _upscaleService;
    private IImageConverter? _converter;
    private IScreenshotService? _screenshotService;

    private SKBitmap _currentBitmap = null!;
    private SKBitmap? _originalBitmap;
    private readonly List<SKPoint> _interactiveBrushPoints = new();
    private SKRectI _interactiveBoundingBox = SKRectI.Empty;
    private bool _isDrawingInteractiveStroke;
    private List<EditorAnnotation> _annotations = new();
    private EditorAnnotation? _selectedAnnotation;
    private bool _isMovingAnnotation;
    private bool _hasMovedSelectedAnnotation;
    private WpfPoint _moveStart;
    private bool _isPanningCanvas;
    private WpfPoint _panMouseStart;
    private WpfPoint _panScrollStart;
    private bool _isSpaceDown;
    private List<SKPoint> _activeFreehandPoints = new();
    private List<SKPoint> _activeBlurStrokePoints = new();

    private class EditorHistoryState : IDisposable
    {
        public SKBitmap? BaseBitmap { get; set; }
        public List<EditorAnnotation> Annotations { get; set; } = new();
        public int StepBadgeCounter { get; set; }

        public void Dispose()
        {
            BaseBitmap?.Dispose();
            BaseBitmap = null;
        }
    }

    private readonly Stack<EditorHistoryState> _undoStack = new();
    private readonly Stack<EditorHistoryState> _redoStack = new();
    private const int MaxUndoHistory = 20;

    private WriteableBitmap? _displayWriteableBitmap;
    private bool _isDisplayUpdatePending;

    private EditorTool _currentTool = EditorTool.Pan;
    private SKColor _selectedColor = SKColors.Red;
    private float _strokeWidth = 4f;
    private int _stepBadgeCounter = 1;

    private WpfPoint _dragStart;
    private bool _isDragging;
    private bool _isPickingColor;
    private SKColor _pickedColor = SKColors.White;
    private SKRectI? _activeCropRect;
    private bool _isCompareMode;
    private bool _isDraggingSplit;
    private bool _isHoldingOriginal;

    // Canvas resizing & layer transformation fields
    private readonly HashSet<SKBitmap> _sessionLayerBitmaps = new();
    private enum AnnotationResizeHandle { None, TopLeft, TopCenter, TopRight, MiddleRight, BottomRight, BottomCenter, BottomLeft, MiddleLeft }
    private enum CanvasResizeGrip { None, Right, Bottom, Corner }
    private bool _isResizingAnnotation;
    private AnnotationResizeHandle _activeResizeHandle = AnnotationResizeHandle.None;
    private SKRect _resizeStartRect;
    private WpfPoint _resizeStartPos;
    private bool _isResizingCanvasInteractive;
    private CanvasResizeGrip _canvasGripMode = CanvasResizeGrip.None;
    private WpfPoint _canvasResizeStartPos;
    private int _canvasResizeOriginalWidth;
    private int _canvasResizeOriginalHeight;
    private int _canvasResizeNewWidth;
    private int _canvasResizeNewHeight;

    public string ResultFilePath { get; private set; } = string.Empty;
    public bool HasChanges { get; private set; }

    public ImageEditorControl()
    {
        InitializeComponent();

        Loaded += (s, e) =>
        {
            OnZoomFitClicked(this, new RoutedEventArgs());
        };
    }

    public void ResetEditorState()
    {
        ExitCompareMode();

        _isResizingAnnotation = false;
        _isResizingCanvasInteractive = false;
        if (PreviewCanvasExpandRect != null) PreviewCanvasExpandRect.Visibility = Visibility.Collapsed;
        if (BadgeCanvasExpandDimensions != null) BadgeCanvasExpandDimensions.Visibility = Visibility.Collapsed;
        if (PanelSelectedImageLayerOptions != null) PanelSelectedImageLayerOptions.Visibility = Visibility.Collapsed;

        foreach (var bmp in _sessionLayerBitmaps)
        {
            bmp.Dispose();
        }
        _sessionLayerBitmaps.Clear();

        _annotations.Clear();
        _selectedAnnotation = null;
        _isMovingAnnotation = false;
        _hasMovedSelectedAnnotation = false;
        _isPanningCanvas = false;

        _stepBadgeCounter = 1;
        if (TextStepBadgeNumber != null)
        {
            TextStepBadgeNumber.Text = "1";
        }

        while (_undoStack.Count > 0) _undoStack.Pop().Dispose();
        while (_redoStack.Count > 0) _redoStack.Pop().Dispose();

        _activeCropRect = null;
        _activeFreehandPoints.Clear();
        _activeBlurStrokePoints.Clear();
        _interactiveBrushPoints.Clear();
        _interactiveBoundingBox = SKRectI.Empty;
        _isDrawingInteractiveStroke = false;

        if (PreviewLine != null) PreviewLine.Visibility = Visibility.Collapsed;
        if (PreviewArrowHead != null) PreviewArrowHead.Visibility = Visibility.Collapsed;
        if (PreviewPolyline != null)
        {
            PreviewPolyline.Visibility = Visibility.Collapsed;
            PreviewPolyline.Points.Clear();
        }
        if (PreviewRect != null) PreviewRect.Visibility = Visibility.Collapsed;
        if (PreviewEllipse != null) PreviewEllipse.Visibility = Visibility.Collapsed;
        if (BrushCursorCircle != null) BrushCursorCircle.Visibility = Visibility.Collapsed;
        if (InteractiveStrokeCanvas != null) InteractiveStrokeCanvas.Children.Clear();

        _currentBitmap?.Dispose();
        _currentBitmap = null!;
        _originalBitmap?.Dispose();
        _originalBitmap = null;
        _displayWriteableBitmap = null;

        HasChanges = false;
    }

    public void Initialize(
        string filePath,
        IImageEditorService editorService,
        IBackgroundRemovalService bgRemovalService,
        IAutoRedactionService? autoRedactionService = null,
        IUpscaleService? upscaleService = null,
        IImageConverter? converter = null,
        IScreenshotService? screenshotService = null)
    {
        ResetEditorState();

        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _editorService = editorService ?? throw new ArgumentNullException(nameof(editorService));
        _bgRemovalService = bgRemovalService ?? throw new ArgumentNullException(nameof(bgRemovalService));
        _autoRedactionService = autoRedactionService;
        _upscaleService = upscaleService;
        _converter = converter;
        _screenshotService = screenshotService;

        ResultFilePath = filePath;
        HasChanges = false;

        TextFileName.Text = Path.GetFileName(filePath);

        if (_converter != null)
        {
            try
            {
                var loaded = _converter.LoadBitmap(filePath);
                _currentBitmap = loaded.Bitmap;
            }
            catch
            {
                using var stream = File.OpenRead(filePath);
                _currentBitmap = SKBitmap.Decode(stream);
            }
        }
        else
        {
            using var stream = File.OpenRead(filePath);
            _currentBitmap = SKBitmap.Decode(stream);
        }

        if (_currentBitmap == null)
        {
            throw new InvalidOperationException($"Das Bild '{Path.GetFileName(filePath)}' konnte nicht geladen werden (nicht unterstütztes oder beschädigtes Format).");
        }

        _originalBitmap = _currentBitmap.Copy();

        UpdateImageDisplay();
        UpdateLayersList();
        UpdateUndoRedoButtons();
        UpdateAiModelStatus();

        ToolPan.IsChecked = true;
        if (ComboBgMode != null)
        {
            ComboBgMode.SelectedIndex = 0;
        }

        OnZoomFitClicked(this, new RoutedEventArgs());
    }

    private void UpdateImageDisplay()
    {
        if (_currentBitmap == null) return;
        int width = _currentBitmap.Width;
        int height = _currentBitmap.Height;

        TextDimensions.Text = $"{width} × {height} px";

        if (DrawingOverlayCanvas != null)
        {
            DrawingOverlayCanvas.Width = width;
            DrawingOverlayCanvas.Height = height;
        }

        if (InteractiveStrokeCanvas != null)
        {
            InteractiveStrokeCanvas.Width = width;
            InteractiveStrokeCanvas.Height = height;
        }

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var displayBitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(displayBitmap))
        {
            canvas.DrawBitmap(_currentBitmap, 0, 0);

            foreach (var ann in _annotations)
            {
                if (ann.IsVisible)
                {
                    ann.Render(canvas);
                }
            }

            if (_selectedAnnotation != null && _selectedAnnotation.IsVisible && _currentTool == EditorTool.Pan)
            {
                var bounds = _selectedAnnotation.GetBounds();
                using var selectPaint = new SKPaint
                {
                    Color = SKColor.Parse("#38BDF8"),
                    StrokeWidth = 2f,
                    Style = SKPaintStyle.Stroke,
                    PathEffect = SKPathEffect.CreateDash(new float[] { 6, 4 }, 0),
                    IsAntialias = true
                };
                canvas.DrawRoundRect(bounds.Left - 4, bounds.Top - 4, bounds.Width + 8, bounds.Height + 8, 4, 4, selectPaint);

                using var handlePaint = new SKPaint { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
                using var handleBorder = new SKPaint { Color = SKColor.Parse("#0284C7"), StrokeWidth = 1.5f, Style = SKPaintStyle.Stroke, IsAntialias = true };

                if (_selectedAnnotation.Type == AnnotationType.Image || _selectedAnnotation.Type == AnnotationType.Rectangle)
                {
                    // 8 Transform Handles for Image & Rectangle layers
                    DrawHandle(canvas, bounds.Left, bounds.Top, handlePaint, handleBorder);
                    DrawHandle(canvas, bounds.MidX, bounds.Top, handlePaint, handleBorder);
                    DrawHandle(canvas, bounds.Right, bounds.Top, handlePaint, handleBorder);
                    DrawHandle(canvas, bounds.Right, bounds.MidY, handlePaint, handleBorder);
                    DrawHandle(canvas, bounds.Right, bounds.Bottom, handlePaint, handleBorder);
                    DrawHandle(canvas, bounds.MidX, bounds.Bottom, handlePaint, handleBorder);
                    DrawHandle(canvas, bounds.Left, bounds.Bottom, handlePaint, handleBorder);
                    DrawHandle(canvas, bounds.Left, bounds.MidY, handlePaint, handleBorder);
                }
                else
                {
                    DrawHandle(canvas, bounds.Left - 4, bounds.Top - 4, handlePaint, handleBorder);
                    DrawHandle(canvas, bounds.Right + 4, bounds.Top - 4, handlePaint, handleBorder);
                    DrawHandle(canvas, bounds.Left - 4, bounds.Bottom + 4, handlePaint, handleBorder);
                    DrawHandle(canvas, bounds.Right + 4, bounds.Bottom + 4, handlePaint, handleBorder);
                }
            }

            canvas.Flush();
        }

        if (_displayWriteableBitmap == null ||
            _displayWriteableBitmap.PixelWidth != width ||
            _displayWriteableBitmap.PixelHeight != height)
        {
            _displayWriteableBitmap = new WriteableBitmap(
                width,
                height,
                96,
                96,
                PixelFormats.Pbgra32,
                null);
            BaseImage.Source = _displayWriteableBitmap;
        }

        _displayWriteableBitmap.WritePixels(
            new Int32Rect(0, 0, width, height),
            displayBitmap.GetPixels(),
            (int)displayBitmap.ByteCount,
            displayBitmap.RowBytes);

        if (PanelSelectedImageLayerOptions != null)
        {
            if (_selectedAnnotation != null && _selectedAnnotation.Type == AnnotationType.Image)
            {
                PanelSelectedImageLayerOptions.Visibility = Visibility.Visible;
                if (SliderLayerOpacity != null) SliderLayerOpacity.Value = _selectedAnnotation.Opacity;
                if (TextLayerOpacityVal != null) TextLayerOpacityVal.Text = $"{(_selectedAnnotation.Opacity * 100):0}%";
                if (ButtonToggleAspectLock != null) ButtonToggleAspectLock.Content = _selectedAnnotation.KeepAspectRatio ? "🔗 Seitenverhältnis" : "🔓 Freies Format";
            }
            else
            {
                PanelSelectedImageLayerOptions.Visibility = Visibility.Collapsed;
            }
        }

        if (_isCompareMode)
        {
            UpdateSplitGeometry();
        }
    }

    private void RequestDisplayUpdate()
    {
        if (_isDisplayUpdatePending) return;
        _isDisplayUpdatePending = true;

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            _isDisplayUpdatePending = false;
            UpdateImageDisplay();
        });
    }

    private static void DrawHandle(SKCanvas canvas, float x, float y, SKPaint fill, SKPaint border)
    {
        canvas.DrawCircle(x, y, 4.5f, fill);
        canvas.DrawCircle(x, y, 4.5f, border);
    }

    public SKBitmap GetFinalBitmap()
    {
        var finalBitmap = _currentBitmap.Copy();
        using var canvas = new SKCanvas(finalBitmap);
        foreach (var ann in _annotations)
        {
            ann.Render(canvas);
        }
        return finalBitmap;
    }

    private void PushUndoState(bool bitmapModified = false)
    {
        _undoStack.Push(new EditorHistoryState
        {
            BaseBitmap = bitmapModified ? _currentBitmap.Copy() : null,
            Annotations = _annotations.Select(a => a.Clone()).ToList(),
            StepBadgeCounter = _stepBadgeCounter
        });

        if (_undoStack.Count > MaxUndoHistory)
        {
            var states = _undoStack.Reverse().ToList();
            while (states.Count > MaxUndoHistory)
            {
                var oldest = states[0];
                states.RemoveAt(0);
                oldest.Dispose();
            }
            _undoStack.Clear();
            foreach (var s in states)
            {
                _undoStack.Push(s);
            }
        }

        while (_redoStack.Count > 0)
        {
            var r = _redoStack.Pop();
            r.Dispose();
        }
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

        var state = _undoStack.Pop();

        _redoStack.Push(new EditorHistoryState
        {
            BaseBitmap = state.BaseBitmap != null ? _currentBitmap.Copy() : null,
            Annotations = _annotations.Select(a => a.Clone()).ToList(),
            StepBadgeCounter = _stepBadgeCounter
        });

        if (state.BaseBitmap != null)
        {
            _currentBitmap.Dispose();
            _currentBitmap = state.BaseBitmap;
        }
        _annotations = state.Annotations;
        _stepBadgeCounter = state.StepBadgeCounter;
        if (TextStepBadgeNumber != null) TextStepBadgeNumber.Text = _stepBadgeCounter.ToString();
        _selectedAnnotation = null;

        UpdateImageDisplay();
        UpdateLayersList();
        UpdateUndoRedoButtons();
        _interactiveBrushPoints.Clear();
        _interactiveBoundingBox = SKRectI.Empty;
        InteractiveStrokeCanvas?.Children.Clear();
    }

    private void OnRedoClicked(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;

        var state = _redoStack.Pop();

        _undoStack.Push(new EditorHistoryState
        {
            BaseBitmap = state.BaseBitmap != null ? _currentBitmap.Copy() : null,
            Annotations = _annotations.Select(a => a.Clone()).ToList(),
            StepBadgeCounter = _stepBadgeCounter
        });

        if (state.BaseBitmap != null)
        {
            _currentBitmap.Dispose();
            _currentBitmap = state.BaseBitmap;
        }
        _annotations = state.Annotations;
        _stepBadgeCounter = state.StepBadgeCounter;
        if (TextStepBadgeNumber != null) TextStepBadgeNumber.Text = _stepBadgeCounter.ToString();
        _selectedAnnotation = null;

        UpdateImageDisplay();
        UpdateLayersList();
        UpdateUndoRedoButtons();
        _interactiveBrushPoints.Clear();
        _interactiveBoundingBox = SKRectI.Empty;
        InteractiveStrokeCanvas?.Children.Clear();
    }

    private void OnToolSelected(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;

        string name = rb.Name;
        _isPickingColor = false;

        if (_isCompareMode && name != "ToolPan")
        {
            ExitCompareMode();
        }

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

        if (_currentTool != EditorTool.Pan && _selectedAnnotation != null)
        {
            _selectedAnnotation = null;
            UpdateImageDisplay();
        }

        if (PanelCropOptions != null) PanelCropOptions.Visibility = _currentTool == EditorTool.Crop ? Visibility.Visible : Visibility.Collapsed;
        if (PanelBlurOptions != null) PanelBlurOptions.Visibility = _currentTool == EditorTool.Pixelate ? Visibility.Visible : Visibility.Collapsed;
        if (PanelBlurBrushOptions != null) PanelBlurBrushOptions.Visibility = _currentTool == EditorTool.Blur ? Visibility.Visible : Visibility.Collapsed;
        if (PanelStepBadgeOptions != null) PanelStepBadgeOptions.Visibility = _currentTool == EditorTool.StepBadge ? Visibility.Visible : Visibility.Collapsed;
        if (_currentTool == EditorTool.BackgroundRemoval)
        {
            if (SidePanel != null) SidePanel.Visibility = Visibility.Visible;
            if (TabSideBgRemove != null) TabSideBgRemove.IsChecked = true;
            OnSideTabChanged(null, null);
        }

        UpdateAiBrushButtonState();
        if (_currentTool != EditorTool.BackgroundRemoval)
        {
            HideBrushCursor();
        }

        if (TextStatusHint != null)
        {
            TextStatusHint.Text = _currentTool switch
            {
                EditorTool.Pan => "✋ Verschieben: Platziertes Element anklicken & verschieben (Entf = Löschen) • Freie Fläche ziehen zum Pannen • Mausrad = Zoom • Shift+Mausrad = Horizontal.",
                EditorTool.Crop => "✂️ Zuschnitt: Bereich mit der Maus aufziehen • Dann 'Zuschnitt anwenden' klicken.",
                EditorTool.Pixelate => "🔲 DSGVO-Verpixelung: Bereich über sensible Daten ziehen (Shift = Quadrat).",
                EditorTool.Blur => "💧 Weichzeichner-Pinsel: Mit gedrückter Maustaste malen, um Bereiche sanft unkenntlich zu machen.",
                EditorTool.Blackout => "⬛ Schwärzen: Blickdichten Block über vertrauliche Daten ziehen (Shift = Quadrat).",
                EditorTool.Arrow => "🏹 Pfeil: Vom Startpunkt zum Zielpunkt ziehen (Shift = 0°/45°/90° einrasten).",
                EditorTool.Rectangle => "▭ Rechteck: Rahmen zur Hervorhebung aufziehen (Shift = Quadrat).",
                EditorTool.Oval => "⭕ Kreis: Detail einkreisen (Shift = Kreis).",
                EditorTool.StepBadge => $"❶ Schritt-Badge: Klick ins Bild platziert Schritt-Nummer {_stepBadgeCounter}.",
                EditorTool.Pen => "✏️ Freihand: Mit der Maus frei zeichnen (Shift = gerade Linie).",
                EditorTool.Highlighter => "🖍️ Textmarker: Halbtransparente Markierung über Text ziehen (Shift = waagerecht).",
                EditorTool.Text => "🔤 Text: Klick ins Bild, um Text einzugeben.",
                EditorTool.BackgroundRemoval => ComboBgMode?.SelectedIndex == 2
                    ? "🖌️ KI-Pinsel aktiv: Male über das Objekt im Bild, um es auszuwählen (oder korrigiere Kanten)."
                    : "🪄 Hintergrund: Rechts Methode (Farbe, KI-Vollbild oder KI-Pinsel) wählen • Mausrad zum Zoomen.",
                _ => "Werkzeug aktiv."
            };
        }
    }

    private void OnColorSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string hex)
        {
            _selectedColor = SKColor.Parse(hex);
        }
    }

    private void OnStrokeSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _strokeWidth = (float)e.NewValue;
        if (TextStrokeWidthVal != null)
        {
            TextStrokeWidthVal.Text = $"{(int)_strokeWidth} px";
        }
        if (_selectedAnnotation != null && _currentTool == EditorTool.Pan)
        {
            _selectedAnnotation.StrokeWidth = _strokeWidth;
            UpdateImageDisplay();
        }
    }

    private void OnStepBadgeNumberChanged(object sender, TextChangedEventArgs e)
    {
        if (TextStepBadgeNumber != null && int.TryParse(TextStepBadgeNumber.Text, out int num) && num > 0)
        {
            _stepBadgeCounter = num;
        }
    }

    private void OnResetStepBadgeClicked(object sender, RoutedEventArgs e)
    {
        _stepBadgeCounter = 1;
        if (TextStepBadgeNumber != null)
        {
            TextStepBadgeNumber.Text = "1";
        }
        if (TextStatusHint != null)
        {
            TextStatusHint.Text = "Schritt-Badge Zähler auf 1 zurückgesetzt.";
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && !e.IsRepeat && Keyboard.FocusedElement is not TextBox)
        {
            _isSpaceDown = true;
            Mouse.OverrideCursor = Cursors.Hand;
            e.Handled = true;
            return;
        }

        if ((e.Key == Key.OemBackslash || e.Key == Key.Oem5 || e.Key == Key.OemPipe) && Keyboard.FocusedElement is not TextBox)
        {
            if (_isCompareMode && !_isHoldingOriginal)
            {
                SetHoldOriginal(true);
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Delete && Keyboard.FocusedElement is not TextBox)
        {
            if (_selectedAnnotation != null)
            {
                PushUndoState();
                _annotations.Remove(_selectedAnnotation);
                _selectedAnnotation = null;
                UpdateImageDisplay();
                UpdateLayersList();
                if (TextStatusHint != null) TextStatusHint.Text = "Ausgewählte Ebene gelöscht.";
                e.Handled = true;
                return;
            }
        }

        if (e.Key == Key.Z && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            OnUndoClicked(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Y && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            OnRedoClicked(sender, e);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && Keyboard.FocusedElement is not TextBox)
        {
            InsertFromClipboard();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.D && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control && Keyboard.FocusedElement is not TextBox && _selectedAnnotation != null)
        {
            PushUndoState();
            var clone = _selectedAnnotation.Clone();
            clone.Id = Guid.NewGuid();
            clone.MoveBy(24f, 24f);
            if (clone.Type == AnnotationType.Image)
            {
                clone.ImageName = $"{clone.ImageName} (Kopie)";
            }
            _annotations.Add(clone);
            _selectedAnnotation = clone;
            UpdateImageDisplay();
            UpdateLayersList();
            if (TextStatusHint != null) TextStatusHint.Text = "Ebene dupliziert.";
            e.Handled = true;
            return;
        }

        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            if (e.Key == Key.OemPlus || e.Key == Key.Add)
            {
                OnZoomInClicked(sender, e);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.OemMinus || e.Key == Key.Subtract)
            {
                OnZoomOutClicked(sender, e);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D0 || e.Key == Key.NumPad0)
            {
                OnZoomFitClicked(sender, e);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.D1 || e.Key == Key.NumPad1)
            {
                OnZoomResetClicked(sender, e);
                e.Handled = true;
                return;
            }
        }
    }

    private void OnWindowPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            _isSpaceDown = false;
            if (!_isPanningCanvas)
            {
                Mouse.OverrideCursor = null;
            }
            e.Handled = true;
        }
        else if (e.Key == Key.OemBackslash || e.Key == Key.Oem5 || e.Key == Key.OemPipe)
        {
            if (_isCompareMode && _isHoldingOriginal)
            {
                SetHoldOriginal(false);
                e.Handled = true;
            }
        }
    }

    private static WpfPoint SnapPoint(WpfPoint start, WpfPoint current)
    {
        double dx = current.X - start.X;
        double dy = current.Y - start.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 2.0) return current;

        double angle = Math.Atan2(dy, dx);
        double step = Math.PI / 4.0; // 45 degrees
        double snappedAngle = Math.Round(angle / step) * step;

        return new WpfPoint(start.X + dist * Math.Cos(snappedAngle), start.Y + dist * Math.Sin(snappedAngle));
    }

    private void UpdateLayersList()
    {
        if (ItemsControlLayers == null) return;

        foreach (var ann in _annotations)
        {
            ann.IsSelected = (ann == _selectedAnnotation);
        }

        var reversedList = _annotations.AsEnumerable().Reverse().ToList();
        ItemsControlLayers.ItemsSource = null;
        ItemsControlLayers.ItemsSource = reversedList;

        if (TextLayerCount != null)
        {
            TextLayerCount.Text = $"{_annotations.Count} {(_annotations.Count == 1 ? "Ebene" : "Ebenen")}";
        }

        if (TextNoLayers != null)
        {
            TextNoLayers.Visibility = _annotations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void OnToggleLayersClicked(object sender, RoutedEventArgs e)
    {
        if (SidePanel == null) return;

        if (SidePanel.Visibility == Visibility.Visible && TabSideLayers?.IsChecked == true)
        {
            SidePanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            SidePanel.Visibility = Visibility.Visible;
            if (TabSideLayers != null) TabSideLayers.IsChecked = true;
            OnSideTabChanged(null, null);
            UpdateLayersList();
        }
    }

    private void OnCloseSidePanelClicked(object sender, RoutedEventArgs e)
    {
        if (SidePanel != null)
        {
            SidePanel.Visibility = Visibility.Collapsed;
        }
        if (_currentTool == EditorTool.BackgroundRemoval && ToolPan != null)
        {
            ToolPan.IsChecked = true;
        }
    }

    private void OnToggleBgRemoveClicked(object sender, RoutedEventArgs e)
    {
        if (SidePanel == null) return;
        if (SidePanel.Visibility == Visibility.Visible && TabSideBgRemove?.IsChecked == true)
        {
            SidePanel.Visibility = Visibility.Collapsed;
            if (_currentTool == EditorTool.BackgroundRemoval && ToolPan != null)
            {
                ToolPan.IsChecked = true;
            }
        }
        else
        {
            SidePanel.Visibility = Visibility.Visible;
            if (TabSideBgRemove != null) TabSideBgRemove.IsChecked = true;
            OnSideTabChanged(null, null);
            if (ToolBgRemove != null) ToolBgRemove.IsChecked = true;
        }
    }

    private void OnToolBgRemovePreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (ToolBgRemove.IsChecked == true)
        {
            if (SidePanel != null)
            {
                if (SidePanel.Visibility == Visibility.Collapsed || TabSideBgRemove?.IsChecked != true)
                {
                    SidePanel.Visibility = Visibility.Visible;
                    if (TabSideBgRemove != null) TabSideBgRemove.IsChecked = true;
                    OnSideTabChanged(null, null);
                }
                else
                {
                    SidePanel.Visibility = Visibility.Collapsed;
                    if (ToolPan != null) ToolPan.IsChecked = true;
                }
            }
            e.Handled = true;
        }
    }

    private void OnSideTabChanged(object? sender, RoutedEventArgs? e)
    {
        if (TabSideLayers == null || PanelLayersView == null || PanelBgRemovalView == null) return;
        bool isLayers = TabSideLayers.IsChecked == true;
        PanelLayersView.Visibility = isLayers ? Visibility.Visible : Visibility.Collapsed;
        PanelBgRemovalView.Visibility = isLayers ? Visibility.Collapsed : Visibility.Visible;
        if (isLayers)
        {
            UpdateLayersList();
            UpdateAiBrushButtonState();
        }
        else if (ComboBgMode?.SelectedIndex == 2 && _currentTool != EditorTool.BackgroundRemoval)
        {
            ActivateInteractiveBrushTool();
        }
        else
        {
            UpdateAiBrushButtonState();
        }
    }

    private void OnSelectLayerClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Guid id)
        {
            var target = _annotations.FirstOrDefault(a => a.Id == id);
            if (target != null)
            {
                _selectedAnnotation = target;
                if (SliderStrokeWidth != null && target.StrokeWidth >= SliderStrokeWidth.Minimum && target.StrokeWidth <= SliderStrokeWidth.Maximum)
                {
                    SliderStrokeWidth.Value = target.StrokeWidth;
                }
                UpdateImageDisplay();
                UpdateLayersList();
            }
        }
    }

    private void OnToggleLayerVisibilityClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Guid id)
        {
            var target = _annotations.FirstOrDefault(a => a.Id == id);
            if (target != null)
            {
                PushUndoState();
                target.IsVisible = !target.IsVisible;
                UpdateImageDisplay();
                UpdateLayersList();
            }
        }
    }

    private void OnMoveLayerUpClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Guid id)
        {
            int idx = _annotations.FindIndex(a => a.Id == id);
            if (idx >= 0 && idx < _annotations.Count - 1)
            {
                PushUndoState();
                var item = _annotations[idx];
                _annotations.RemoveAt(idx);
                _annotations.Insert(idx + 1, item);
                UpdateImageDisplay();
                UpdateLayersList();
            }
        }
    }

    private void OnMoveLayerDownClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Guid id)
        {
            int idx = _annotations.FindIndex(a => a.Id == id);
            if (idx > 0)
            {
                PushUndoState();
                var item = _annotations[idx];
                _annotations.RemoveAt(idx);
                _annotations.Insert(idx - 1, item);
                UpdateImageDisplay();
                UpdateLayersList();
            }
        }
    }

    private void OnDeleteLayerItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is Guid id)
        {
            var target = _annotations.FirstOrDefault(a => a.Id == id);
            if (target != null)
            {
                PushUndoState();
                if (_selectedAnnotation == target) _selectedAnnotation = null;
                _annotations.Remove(target);
                UpdateImageDisplay();
                UpdateLayersList();
            }
        }
    }

    private void OnDeleteSelectedLayerClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedAnnotation != null)
        {
            PushUndoState();
            _annotations.Remove(_selectedAnnotation);
            _selectedAnnotation = null;
            UpdateImageDisplay();
            UpdateLayersList();
        }
    }

    private void OnClearAllLayersClicked(object sender, RoutedEventArgs e)
    {
        if (_annotations.Count == 0) return;
        var result = MessageBox.Show("Möchtest du wirklich alle platzierten Annotationen entfernen?",
            "Alle Ebenen leeren", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            PushUndoState();
            _annotations.Clear();
            _selectedAnnotation = null;
            UpdateImageDisplay();
            UpdateLayersList();
        }
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Middle mouse button or Right mouse button or Spacebar + Left button always pans the canvas regardless of active tool
        if (e.ChangedButton == MouseButton.Middle || e.ChangedButton == MouseButton.Right || (e.ChangedButton == MouseButton.Left && _isSpaceDown))
        {
            _isPanningCanvas = true;
            _panMouseStart = e.GetPosition(CanvasScrollViewer);
            _panScrollStart = new WpfPoint(CanvasScrollViewer.HorizontalOffset, CanvasScrollViewer.VerticalOffset);
            CanvasContainer.CaptureMouse();
            Mouse.OverrideCursor = Cursors.SizeAll;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;

        if (_isCompareMode)
        {
            _isDraggingSplit = true;
            CanvasContainer.CaptureMouse();
            var posSplit = e.GetPosition(CanvasContainer);
            UpdateSplitFromMouse(posSplit.X);
            e.Handled = true;
            return;
        }

        var pos = e.GetPosition(BaseImage);
        var pt = new SKPoint((float)pos.X, (float)pos.Y);

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

        if (_currentTool == EditorTool.Pan)
        {
            if (_selectedAnnotation != null && _selectedAnnotation.IsVisible)
            {
                var handle = GetAnnotationResizeHandle(pt, _selectedAnnotation);
                if (handle != AnnotationResizeHandle.None)
                {
                    _activeResizeHandle = handle;
                    _isResizingAnnotation = true;
                    _resizeStartRect = _selectedAnnotation.Rect;
                    _resizeStartPos = pos;
                    _hasMovedSelectedAnnotation = false;
                    CanvasContainer.CaptureMouse();
                    return;
                }
            }

            // Check if user clicked an existing visible annotation to move it
            EditorAnnotation? hit = null;
            for (int i = _annotations.Count - 1; i >= 0; i--)
            {
                if (_annotations[i].IsVisible && _annotations[i].HitTest(pt))
                {
                    hit = _annotations[i];
                    break;
                }
            }

            if (hit != null)
            {
                _selectedAnnotation = hit;
                _isMovingAnnotation = true;
                _hasMovedSelectedAnnotation = false;
                _moveStart = pos;
                CanvasContainer.CaptureMouse();
                if (SliderStrokeWidth != null && hit.StrokeWidth >= SliderStrokeWidth.Minimum && hit.StrokeWidth <= SliderStrokeWidth.Maximum)
                {
                    SliderStrokeWidth.Value = hit.StrokeWidth;
                }
                UpdateImageDisplay();
                UpdateLayersList();
                TextStatusHint.Text = "Element ausgewählt. Ziehen zum Verschieben • Entf zum Löschen.";
            }
            else
            {
                if (_selectedAnnotation != null)
                {
                    _selectedAnnotation = null;
                    UpdateImageDisplay();
                    UpdateLayersList();
                }
                _isPanningCanvas = true;
                _panMouseStart = e.GetPosition(CanvasScrollViewer);
                _panScrollStart = new WpfPoint(CanvasScrollViewer.HorizontalOffset, CanvasScrollViewer.VerticalOffset);
                CanvasContainer.CaptureMouse();
            }
            return;
        }

        if (_currentTool == EditorTool.BackgroundRemoval)
        {
            if (ComboBgMode?.SelectedIndex == 2)
            {
                if (RadioBrushSelectObject?.IsChecked == true)
                {
                    _isDrawingInteractiveStroke = true;
                    AddInteractiveBrushPoint(pt);
                    CanvasContainer.CaptureMouse();
                    return;
                }
                if (RadioBrushRestore?.IsChecked == true || RadioBrushErase?.IsChecked == true)
                {
                    PushUndoState(bitmapModified: true);
                    _isDrawingInteractiveStroke = true;
                    ApplyMaskBrushAt(pt);
                    CanvasContainer.CaptureMouse();
                    return;
                }
            }
            return;
        }

        _dragStart = pos;
        _isDragging = true;

        if (_currentTool == EditorTool.Blur)
        {
            _activeBlurStrokePoints.Clear();
            _activeBlurStrokePoints.Add(pt);
            PreviewPolyline.Points.Clear();
            PreviewPolyline.Points.Add(new WpfPoint(pos.X, pos.Y));
            PreviewPolyline.Stroke = new SolidColorBrush(WpfColor.FromArgb(130, 56, 189, 248));
            float r = SliderBlurRadius != null ? (float)SliderBlurRadius.Value : 24f;
            PreviewPolyline.StrokeThickness = r * 2f;
            PreviewPolyline.Visibility = Visibility.Visible;
            CanvasContainer.CaptureMouse();
            return;
        }

        if (_currentTool == EditorTool.StepBadge)
        {
            PushUndoState();
            var badge = new EditorAnnotation
            {
                Type = AnnotationType.StepBadge,
                StartPoint = pt,
                Color = _selectedColor,
                StrokeWidth = _strokeWidth,
                BadgeNumber = _stepBadgeCounter++
            };
            if (TextStepBadgeNumber != null)
            {
                TextStepBadgeNumber.Text = _stepBadgeCounter.ToString();
            }
            _annotations.Add(badge);
            UpdateImageDisplay();
            UpdateLayersList();
            TextStatusHint.Text = $"Schritt-Badge {badge.BadgeNumber} platziert. Nächster: {_stepBadgeCounter}.";
            _isDragging = false;
            return;
        }

        if (_currentTool == EditorTool.Text)
        {
            string input = Microsoft.VisualBasic.Interaction.InputBox("Geben Sie den gewünschten Text ein:", "Text einfügen", "Hinweis");
            if (!string.IsNullOrWhiteSpace(input))
            {
                PushUndoState();
                var textAnn = new EditorAnnotation
                {
                    Type = AnnotationType.Text,
                    StartPoint = pt,
                    Text = input,
                    Color = _selectedColor,
                    FontSize = 24f,
                    BackgroundColor = new SKColor(15, 23, 42, 210)
                };
                _annotations.Add(textAnn);
                UpdateImageDisplay();
                UpdateLayersList();
            }
            _isDragging = false;
            return;
        }

        if (_currentTool == EditorTool.Pen)
        {
            _activeFreehandPoints.Clear();
            _activeFreehandPoints.Add(pt);
            PreviewPolyline.Points.Clear();
            PreviewPolyline.Points.Add(new WpfPoint(pos.X, pos.Y));
            PreviewPolyline.Stroke = new SolidColorBrush(WpfColor.FromRgb(_selectedColor.Red, _selectedColor.Green, _selectedColor.Blue));
            PreviewPolyline.StrokeThickness = _strokeWidth;
            PreviewPolyline.Visibility = Visibility.Visible;
        }

        CanvasContainer.CaptureMouse();
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanningCanvas)
        {
            var currentScrollPos = e.GetPosition(CanvasScrollViewer);
            double deltaX = currentScrollPos.X - _panMouseStart.X;
            double deltaY = currentScrollPos.Y - _panMouseStart.Y;
            CanvasScrollViewer.ScrollToHorizontalOffset(_panScrollStart.X - deltaX);
            CanvasScrollViewer.ScrollToVerticalOffset(_panScrollStart.Y - deltaY);
            return;
        }

        if (_isCompareMode)
        {
            if (_isDraggingSplit && e.LeftButton == MouseButtonState.Pressed)
            {
                var posSplit = e.GetPosition(CanvasContainer);
                UpdateSplitFromMouse(posSplit.X);
                e.Handled = true;
                return;
            }
            CanvasContainer.Cursor = Cursors.SizeWE;
            return;
        }

        var current = e.GetPosition(BaseImage);
        var currentPt = new SKPoint((float)current.X, (float)current.Y);

        if (_isResizingCanvasInteractive)
        {
            var curPos = e.GetPosition(CanvasScrollViewer);
            double zoom = CanvasScaleTransform?.ScaleX ?? 1.0;
            if (zoom <= 0.01) zoom = 1.0;

            double deltaX = (curPos.X - _canvasResizeStartPos.X) / zoom;
            double deltaY = (curPos.Y - _canvasResizeStartPos.Y) / zoom;

            int newW = _canvasResizeOriginalWidth;
            int newH = _canvasResizeOriginalHeight;

            if (_canvasGripMode == CanvasResizeGrip.Right || _canvasGripMode == CanvasResizeGrip.Corner)
            {
                newW = Math.Max(16, (int)Math.Round(_canvasResizeOriginalWidth + deltaX));
            }
            if (_canvasGripMode == CanvasResizeGrip.Bottom || _canvasGripMode == CanvasResizeGrip.Corner)
            {
                newH = Math.Max(16, (int)Math.Round(_canvasResizeOriginalHeight + deltaY));
            }

            _canvasResizeNewWidth = newW;
            _canvasResizeNewHeight = newH;

            if (PreviewCanvasExpandRect != null)
            {
                PreviewCanvasExpandRect.Width = newW;
                PreviewCanvasExpandRect.Height = newH;
                Canvas.SetLeft(PreviewCanvasExpandRect, 0);
                Canvas.SetTop(PreviewCanvasExpandRect, 0);
                PreviewCanvasExpandRect.Visibility = Visibility.Visible;
            }

            if (BadgeCanvasExpandDimensions != null && TextCanvasExpandDimensions != null)
            {
                int diffW = newW - _canvasResizeOriginalWidth;
                int diffH = newH - _canvasResizeOriginalHeight;
                string diffStr = $"({(diffW >= 0 ? "+" : "")}{diffW}, {(diffH >= 0 ? "+" : "")}{diffH})";
                TextCanvasExpandDimensions.Text = $"{newW} × {newH} px {diffStr}";

                Canvas.SetLeft(BadgeCanvasExpandDimensions, Math.Max(10, newW - 180));
                Canvas.SetTop(BadgeCanvasExpandDimensions, Math.Max(10, newH + 10));
                BadgeCanvasExpandDimensions.Visibility = Visibility.Visible;
            }
            return;
        }

        if (_isResizingAnnotation && _selectedAnnotation != null)
        {
            float dx = (float)(current.X - _resizeStartPos.X);
            float dy = (float)(current.Y - _resizeStartPos.Y);

            if (!_hasMovedSelectedAnnotation && (Math.Abs(dx) > 1f || Math.Abs(dy) > 1f))
            {
                PushUndoState();
                _hasMovedSelectedAnnotation = true;
            }

            var newRect = CalculateResizedRect(_resizeStartRect, _activeResizeHandle, dx, dy, _selectedAnnotation.KeepAspectRatio);
            _selectedAnnotation.Rect = newRect;
            RequestDisplayUpdate();
            return;
        }

        if (_currentTool == EditorTool.Pan)
        {
            if (_isMovingAnnotation && _selectedAnnotation != null)
            {
                float dx = (float)(current.X - _moveStart.X);
                float dy = (float)(current.Y - _moveStart.Y);
                if (Math.Abs(dx) > 0.5f || Math.Abs(dy) > 0.5f)
                {
                    if (!_hasMovedSelectedAnnotation)
                    {
                        PushUndoState();
                        _hasMovedSelectedAnnotation = true;
                    }
                    _selectedAnnotation.MoveBy(dx, dy);
                    _moveStart = current;
                    RequestDisplayUpdate();
                }
                return;
            }

            // Hover cursor indication in Pan mode
            if (!_isDragging && !_isMovingAnnotation && !_isPanningCanvas && !_isResizingAnnotation && !_isResizingCanvasInteractive)
            {
                if (_selectedAnnotation != null)
                {
                    var handle = GetAnnotationResizeHandle(currentPt, _selectedAnnotation);
                    if (handle != AnnotationResizeHandle.None)
                    {
                        CanvasContainer.Cursor = GetCursorForHandle(handle);
                        return;
                    }
                }

                bool overElement = false;
                for (int i = _annotations.Count - 1; i >= 0; i--)
                {
                    if (_annotations[i].IsVisible && _annotations[i].HitTest(currentPt))
                    {
                        overElement = true;
                        break;
                    }
                }
                CanvasContainer.Cursor = _isSpaceDown ? Cursors.Hand : (overElement ? Cursors.SizeAll : Cursors.Hand);
            }
            return;
        }

        if (_currentTool == EditorTool.BackgroundRemoval)
        {
            if (ComboBgMode?.SelectedIndex == 2)
            {
                CanvasContainer.Cursor = Cursors.Cross;
                UpdateBrushCursorPosition(currentPt);

                if (_isDrawingInteractiveStroke)
                {
                    if (RadioBrushSelectObject?.IsChecked == true)
                    {
                        AddInteractiveBrushPoint(currentPt);
                        return;
                    }
                    if (RadioBrushRestore?.IsChecked == true || RadioBrushErase?.IsChecked == true)
                    {
                        ApplyMaskBrushAt(currentPt);
                        return;
                    }
                }
            }
            else
            {
                HideBrushCursor();
            }
            return;
        }
        else
        {
            HideBrushCursor();
        }

        if (!_isDragging) return;

        if (_currentTool == EditorTool.Blur)
        {
            if (_activeBlurStrokePoints.Count > 0)
            {
                var last = _activeBlurStrokePoints[^1];
                float bdx = currentPt.X - last.X;
                float bdy = currentPt.Y - last.Y;
                if (bdx * bdx + bdy * bdy < 2.25f) return;
            }
            _activeBlurStrokePoints.Add(currentPt);
            PreviewPolyline.Points.Add(new WpfPoint(current.X, current.Y));
            return;
        }

        bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        if (isShift && (_currentTool == EditorTool.Arrow || _currentTool == EditorTool.Highlighter || _currentTool == EditorTool.Pen))
        {
            current = SnapPoint(_dragStart, current);
            currentPt = new SKPoint((float)current.X, (float)current.Y);
        }

        double left = Math.Min(_dragStart.X, current.X);
        double top = Math.Min(_dragStart.Y, current.Y);
        double width = Math.Abs(_dragStart.X - current.X);
        double height = Math.Abs(_dragStart.Y - current.Y);

        if (isShift && (_currentTool == EditorTool.Rectangle || _currentTool == EditorTool.Oval || _currentTool == EditorTool.Blackout || _currentTool == EditorTool.Pixelate))
        {
            double side = Math.Max(width, height);
            width = side;
            height = side;
            if (current.X < _dragStart.X) left = _dragStart.X - side;
            if (current.Y < _dragStart.Y) top = _dragStart.Y - side;
        }

        switch (_currentTool)
        {
            case EditorTool.Crop:
            case EditorTool.Pixelate:
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
                double adx = current.X - _dragStart.X;
                double ady = current.Y - _dragStart.Y;
                double alen = Math.Sqrt(adx * adx + ady * ady);
                if (alen > 4)
                {
                    double dirX = adx / alen;
                    double dirY = ady / alen;
                    double arrowLen = Math.Clamp(_strokeWidth * 4.2, 16.0, 60.0);
                    double baseOffset = Math.Min(arrowLen * 0.85, alen * 0.8);
                    var basePt = new WpfPoint(current.X - baseOffset * dirX, current.Y - baseOffset * dirY);

                    PreviewLine.Visibility = Visibility.Visible;
                    PreviewLine.X1 = _dragStart.X;
                    PreviewLine.Y1 = _dragStart.Y;
                    PreviewLine.X2 = basePt.X;
                    PreviewLine.Y2 = basePt.Y;
                    PreviewLine.Stroke = new SolidColorBrush(WpfColor.FromRgb(_selectedColor.Red, _selectedColor.Green, _selectedColor.Blue));
                    PreviewLine.StrokeThickness = _strokeWidth;

                    double wingAngle = 28.0 * (Math.PI / 180.0);
                    double aAngle = Math.Atan2(ady, adx);
                    var w1 = new WpfPoint(
                        current.X - arrowLen * Math.Cos(aAngle - wingAngle),
                        current.Y - arrowLen * Math.Sin(aAngle - wingAngle));
                    var w2 = new WpfPoint(
                        current.X - arrowLen * Math.Cos(aAngle + wingAngle),
                        current.Y - arrowLen * Math.Sin(aAngle + wingAngle));

                    PreviewArrowHead.Points = new PointCollection { current, w1, basePt, w2 };
                    PreviewArrowHead.Fill = new SolidColorBrush(WpfColor.FromRgb(_selectedColor.Red, _selectedColor.Green, _selectedColor.Blue));
                    PreviewArrowHead.Stroke = new SolidColorBrush(WpfColor.FromRgb(_selectedColor.Red, _selectedColor.Green, _selectedColor.Blue));
                    PreviewArrowHead.Visibility = Visibility.Visible;
                }
                break;

            case EditorTool.Highlighter:
                PreviewLine.Visibility = Visibility.Visible;
                PreviewLine.X1 = _dragStart.X;
                PreviewLine.Y1 = _dragStart.Y;
                PreviewLine.X2 = current.X;
                PreviewLine.Y2 = current.Y;
                PreviewLine.Stroke = new SolidColorBrush(WpfColor.FromArgb(120, _selectedColor.Red, _selectedColor.Green, _selectedColor.Blue));
                PreviewLine.StrokeThickness = 26f;
                break;

            case EditorTool.Pen:
                if (_activeFreehandPoints.Count > 0)
                {
                    var last = _activeFreehandPoints[^1];
                    float pdx = currentPt.X - last.X;
                    float pdy = currentPt.Y - last.Y;
                    if (pdx * pdx + pdy * pdy < 2.25f) break;
                }
                _activeFreehandPoints.Add(currentPt);
                PreviewPolyline.Points.Add(new WpfPoint(current.X, current.Y));
                break;
        }
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanningCanvas)
        {
            _isPanningCanvas = false;
            CanvasContainer.ReleaseMouseCapture();
            if (!_isSpaceDown) Mouse.OverrideCursor = null;
            return;
        }

        if (_isResizingCanvasInteractive)
        {
            _isResizingCanvasInteractive = false;
            CanvasContainer.ReleaseMouseCapture();
            if (PreviewCanvasExpandRect != null) PreviewCanvasExpandRect.Visibility = Visibility.Collapsed;
            if (BadgeCanvasExpandDimensions != null) BadgeCanvasExpandDimensions.Visibility = Visibility.Collapsed;

            if (_canvasResizeNewWidth > 0 && _canvasResizeNewHeight > 0 &&
                (_canvasResizeNewWidth != _canvasResizeOriginalWidth || _canvasResizeNewHeight != _canvasResizeOriginalHeight) &&
                _editorService != null)
            {
                PushUndoState(bitmapModified: true);
                var oldBmp = _currentBitmap;
                _currentBitmap = _editorService.ResizeCanvas(oldBmp, _canvasResizeNewWidth, _canvasResizeNewHeight, CanvasAnchor.TopLeft, SKColors.Transparent);
                oldBmp.Dispose();
                UpdateImageDisplay();
                UpdateLayersList();
                if (TextStatusHint != null) TextStatusHint.Text = $"Leinwand vergrößert auf {_canvasResizeNewWidth} × {_canvasResizeNewHeight} px.";
            }
            e.Handled = true;
            return;
        }

        if (_isResizingAnnotation)
        {
            _isResizingAnnotation = false;
            _activeResizeHandle = AnnotationResizeHandle.None;
            CanvasContainer.ReleaseMouseCapture();
            UpdateImageDisplay();
            e.Handled = true;
            return;
        }

        if (_isCompareMode)
        {
            if (_isDraggingSplit)
            {
                _isDraggingSplit = false;
                CanvasContainer.ReleaseMouseCapture();
                e.Handled = true;
            }
            return;
        }

        if (_currentTool == EditorTool.Pan)
        {
            if (_isMovingAnnotation)
            {
                _isMovingAnnotation = false;
                CanvasContainer.ReleaseMouseCapture();
                UpdateImageDisplay();
            }
            return;
        }

        if (_currentTool == EditorTool.BackgroundRemoval)
        {
            if (_isDrawingInteractiveStroke)
            {
                _isDrawingInteractiveStroke = false;
                CanvasContainer.ReleaseMouseCapture();
                UpdateImageDisplay();
            }
            return;
        }

        if (!_isDragging) return;

        _isDragging = false;
        CanvasContainer.ReleaseMouseCapture();

        PreviewRect.Visibility = Visibility.Collapsed;
        PreviewEllipse.Visibility = Visibility.Collapsed;
        PreviewLine.Visibility = Visibility.Collapsed;
        PreviewArrowHead.Visibility = Visibility.Collapsed;
        PreviewPolyline.Visibility = Visibility.Collapsed;
        PreviewPolyline.Points.Clear();

        if (_currentTool == EditorTool.Blur)
        {
            if (_activeBlurStrokePoints.Count >= 1 && _editorService != null)
            {
                PushUndoState(bitmapModified: true);
                float r = SliderBlurRadius != null ? (float)SliderBlurRadius.Value : 24f;
                float s = SliderBlurSigma != null ? (float)SliderBlurSigma.Value : 10f;
                var blurUpdated = _editorService.ApplyBlurBrush(_currentBitmap, _activeBlurStrokePoints, r, s);
                _currentBitmap.Dispose();
                _currentBitmap = blurUpdated;
                _activeBlurStrokePoints.Clear();
                UpdateImageDisplay();
            }
            return;
        }

        var end = e.GetPosition(BaseImage);
        bool isShift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        if (isShift && (_currentTool == EditorTool.Arrow || _currentTool == EditorTool.Highlighter || _currentTool == EditorTool.Pen))
        {
            end = SnapPoint(_dragStart, end);
        }

        int x1 = (int)Math.Min(_dragStart.X, end.X);
        int y1 = (int)Math.Min(_dragStart.Y, end.Y);
        int x2 = (int)Math.Max(_dragStart.X, end.X);
        int y2 = (int)Math.Max(_dragStart.Y, end.Y);

        int w = x2 - x1;
        int h = y2 - y1;

        if (isShift && (_currentTool == EditorTool.Rectangle || _currentTool == EditorTool.Oval || _currentTool == EditorTool.Blackout || _currentTool == EditorTool.Pixelate))
        {
            int side = Math.Max(w, h);
            w = side;
            h = side;
            if (end.X < _dragStart.X) x1 = (int)_dragStart.X - side;
            if (end.Y < _dragStart.Y) y1 = (int)_dragStart.Y - side;
            x2 = x1 + side;
            y2 = y1 + side;
        }

        if (_currentTool == EditorTool.Crop)
        {
            double? aspect = ComboCropAspect?.SelectedIndex switch
            {
                1 => 1.0,           // 1:1
                2 => 16.0 / 9.0,    // 16:9
                3 => 4.0 / 3.0,     // 4:3
                4 => 9.0 / 16.0,    // 9:16
                5 => 4.0 / 5.0,     // 4:5
                6 => 1.91,          // 1.91:1
                7 => 3.0,           // 3:1
                _ => null           // Free
            };

            if (aspect.HasValue && w > 0)
            {
                int sideH = (int)(w / aspect.Value);
                if (y1 + sideH <= _currentBitmap.Height)
                {
                    h = sideH;
                    y2 = y1 + h;
                }
            }

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

        if (_currentTool == EditorTool.Pen)
        {
            if (_activeFreehandPoints.Count >= 2)
            {
                PushUndoState();
                _annotations.Add(new EditorAnnotation
                {
                    Type = AnnotationType.Freehand,
                    Points = new List<SKPoint>(_activeFreehandPoints),
                    Color = _selectedColor,
                    StrokeWidth = _strokeWidth
                });
                _activeFreehandPoints.Clear();
                UpdateImageDisplay();
                UpdateLayersList();
            }
            return;
        }

        if (w < 4 && h < 4 && _currentTool != EditorTool.Arrow && _currentTool != EditorTool.Highlighter) return;

        bool modifiesBitmap = _currentTool == EditorTool.Pixelate || _currentTool == EditorTool.Blackout;
        PushUndoState(bitmapModified: modifiesBitmap);

        switch (_currentTool)
        {
            case EditorTool.Pixelate:
                if (_editorService == null) break;
                int pixelSize = (int)SliderPixelSize.Value;
                var pixUpdated = _editorService.ApplyPixelate(_currentBitmap, new SKRectI(x1, y1, x2, y2), pixelSize);
                _currentBitmap.Dispose();
                _currentBitmap = pixUpdated;
                UpdateImageDisplay();
                break;

            case EditorTool.Blackout:
                if (_editorService == null) break;
                var blkUpdated = _editorService.ApplyBlackout(_currentBitmap, new SKRectI(x1, y1, x2, y2), _selectedColor);
                _currentBitmap.Dispose();
                _currentBitmap = blkUpdated;
                UpdateImageDisplay();
                break;

            case EditorTool.Arrow:
                _annotations.Add(new EditorAnnotation
                {
                    Type = AnnotationType.Arrow,
                    StartPoint = new SKPoint((float)_dragStart.X, (float)_dragStart.Y),
                    EndPoint = new SKPoint((float)end.X, (float)end.Y),
                    Color = _selectedColor,
                    StrokeWidth = _strokeWidth
                });
                UpdateImageDisplay();
                UpdateLayersList();
                break;

            case EditorTool.Rectangle:
                _annotations.Add(new EditorAnnotation
                {
                    Type = AnnotationType.Rectangle,
                    Rect = new SKRect(x1, y1, x2, y2),
                    Color = _selectedColor,
                    StrokeWidth = _strokeWidth
                });
                UpdateImageDisplay();
                UpdateLayersList();
                break;

            case EditorTool.Oval:
                _annotations.Add(new EditorAnnotation
                {
                    Type = AnnotationType.Oval,
                    Rect = new SKRect(x1, y1, x2, y2),
                    Color = _selectedColor,
                    StrokeWidth = _strokeWidth
                });
                UpdateImageDisplay();
                UpdateLayersList();
                break;

            case EditorTool.Highlighter:
                _annotations.Add(new EditorAnnotation
                {
                    Type = AnnotationType.Highlighter,
                    StartPoint = new SKPoint((float)_dragStart.X, (float)_dragStart.Y),
                    EndPoint = new SKPoint((float)end.X, (float)end.Y),
                    Color = _selectedColor,
                    StrokeWidth = 26f
                });
                UpdateImageDisplay();
                UpdateLayersList();
                break;
        }
    }

    private void OnApplyCropClicked(object sender, RoutedEventArgs e)
    {
        if (_activeCropRect.HasValue && _editorService != null)
        {
            PushUndoState(bitmapModified: true);
            var crop = _activeCropRect.Value;
            var cropped = _editorService.Crop(_currentBitmap, crop);
            _currentBitmap.Dispose();
            _currentBitmap = cropped;

            foreach (var ann in _annotations)
            {
                ann.MoveBy(-crop.Left, -crop.Top);
            }

            _activeCropRect = null;
            PreviewRect.Visibility = Visibility.Collapsed;
            UpdateImageDisplay();
            TextStatusHint.Text = "Zuschnitt erfolgreich angewendet.";
        }
    }

    private void OnCropAspectChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_currentBitmap == null || ComboCropAspect == null) return;
        double? aspect = ComboCropAspect.SelectedIndex switch
        {
            1 => 1.0,           // 1:1
            2 => 16.0 / 9.0,    // 16:9
            3 => 4.0 / 3.0,     // 4:3
            4 => 9.0 / 16.0,    // 9:16
            5 => 4.0 / 5.0,     // 4:5
            6 => 1.91,          // 1.91:1
            7 => 3.0,           // 3:1
            _ => null           // Free
        };

        if (aspect.HasValue)
        {
            int imgW = _currentBitmap.Width;
            int imgH = _currentBitmap.Height;
            int cropW, cropH;

            if (imgW / (double)imgH > aspect.Value)
            {
                cropH = (int)(imgH * 0.9);
                cropW = (int)(cropH * aspect.Value);
            }
            else
            {
                cropW = (int)(imgW * 0.9);
                cropH = (int)(cropW / aspect.Value);
            }

            int x = (imgW - cropW) / 2;
            int y = (imgH - cropH) / 2;

            _activeCropRect = new SKRectI(x, y, x + cropW, y + cropH);
            if (PreviewRect != null)
            {
                PreviewRect.Visibility = Visibility.Visible;
                Canvas.SetLeft(PreviewRect, x);
                Canvas.SetTop(PreviewRect, y);
                PreviewRect.Width = cropW;
                PreviewRect.Height = cropH;
            }
            TextStatusHint.Text = $"Seitenverhältnis gewählt: {cropW} × {cropH} px. Klicke auf '✂️ Zuschnitt anwenden'.";
        }
    }

    private void OnAutoRedactClicked(object sender, RoutedEventArgs e)
    {
        if (_currentBitmap == null) return;
        if (_autoRedactionService == null)
        {
            MessageBox.Show("DSGVO-Automatikdienst ist nicht verfügbar.", "Auto-DSGVO", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var menu = new ContextMenu();
        var itemPixelateBalanced = new MenuItem { Header = "▦ Auto-Verpixeln (Ausgewogen – Standard)" };
        itemPixelateBalanced.Click += (s, ev) => ApplyAutoRedaction(RedactionStyle.Pixelate, 16, 0.45f);

        var itemPixelatePrecise = new MenuItem { Header = "🎯 Hohe Präzision (Nur eindeutige Gesichter & Kennzeichen)" };
        itemPixelatePrecise.Click += (s, ev) => ApplyAutoRedaction(RedactionStyle.Pixelate, 16, 0.60f);

        var itemPixelateSensitive = new MenuItem { Header = "🔍 Hohe Empfindlichkeit (Erkennt auch entfernte Objekte)" };
        itemPixelateSensitive.Click += (s, ev) => ApplyAutoRedaction(RedactionStyle.Pixelate, 16, 0.35f);

        var itemBlur = new MenuItem { Header = "💧 Auto-Weichzeichnen (Blur)" };
        itemBlur.Click += (s, ev) => ApplyAutoRedaction(RedactionStyle.Blur, 16, 0.45f);

        var itemBlackout = new MenuItem { Header = "⬛ Auto-Schwärzen (DSGVO Blackout)" };
        itemBlackout.Click += (s, ev) => ApplyAutoRedaction(RedactionStyle.Blackout, 16, 0.45f);

        menu.Items.Add(itemPixelateBalanced);
        menu.Items.Add(itemPixelatePrecise);
        menu.Items.Add(itemPixelateSensitive);
        menu.Items.Add(new Separator());
        menu.Items.Add(itemBlur);
        menu.Items.Add(itemBlackout);

        if (sender is Button btn)
        {
            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void ApplyAutoRedaction(RedactionStyle style, int intensity, float minConfidence = 0.45f)
    {
        if (_currentBitmap == null || _autoRedactionService == null) return;

        var detected = _autoRedactionService.DetectRegions(_currentBitmap, detectFaces: true, detectPlates: true, minConfidence: minConfidence);
        if (detected.Count == 0)
        {
            TextStatusHint.Text = "🤖 Auto-DSGVO: Keine Gesichter oder Kennzeichen im Bild erkannt.";
            return;
        }

        int faces = detected.Count(d => d.Type == SensitiveRegionType.Face);
        int plates = detected.Count(d => d.Type == SensitiveRegionType.LicensePlate);

        var parts = new List<string>();
        if (plates > 0) parts.Add($"{plates} Kennzeichen");
        if (faces > 0) parts.Add($"{faces} Gesicht(er)");
        string summary = string.Join(" und ", parts);

        PushUndoState(bitmapModified: true);
        var redacted = _autoRedactionService.AutoRedact(_currentBitmap, detectFaces: true, detectPlates: true, style: style, intensity: intensity, minConfidence: minConfidence);
        _currentBitmap.Dispose();
        _currentBitmap = redacted;
        HasChanges = true;
        UpdateImageDisplay();
        TextStatusHint.Text = $"🤖 Auto-DSGVO: {summary} erfolgreich anonymisiert.";
    }

    private void OnUpscaleClicked(object sender, RoutedEventArgs e)
    {
        if (_currentBitmap == null) return;
        if (_upscaleService == null)
        {
            MessageBox.Show("KI-Upscaling-Dienst ist nicht verfügbar.", "KI-Upscaling", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var menu = new ContextMenu();
        bool is2xAi = _upscaleService.IsAiModelDownloaded(2);
        bool is4xAi = _upscaleService.IsAiModelDownloaded(4);

        var item2x = new MenuItem
        {
            Header = is2xAi
                ? "✨ 2x KI-Super-Resolution (ONNX Neural Net – Höchste Qualität)"
                : "✨ 2x Super-Resolution (Offline Lanczos3 + Detail-Recovery)"
        };
        item2x.Click += async (s, ev) => await ExecuteUpscaleAsync(2);

        var item4x = new MenuItem
        {
            Header = is4xAi
                ? "✨ 4x KI-Super-Resolution (ONNX Neural Net – Höchste Qualität)"
                : "✨ 4x Super-Resolution (Offline Lanczos3 + Detail-Recovery)"
        };
        item4x.Click += async (s, ev) => await ExecuteUpscaleAsync(4);

        menu.Items.Add(item2x);
        menu.Items.Add(item4x);

        if (!is2xAi || !is4xAi)
        {
            menu.Items.Add(new Separator());
            var itemDownload = new MenuItem
            {
                Header = "⬇️ Echtes ONNX-KI-Modell herunterladen (einmalig ca. 67 MB)..."
            };
            itemDownload.Click += async (s, ev) =>
            {
                int targetFactor = !is2xAi ? 2 : 4;
                TextStatusHint.Text = $"⬇️ Lade {targetFactor}x ONNX-Modell von Hugging Face herunter...";
                var progress = new Progress<double>(pct =>
                {
                    TextStatusHint.Text = $"⬇️ Lade {targetFactor}x ONNX-Modell herunter: {pct:N0}%...";
                });
                bool ok = await _upscaleService.DownloadModelAsync(targetFactor, progress);
                if (ok)
                {
                    TextStatusHint.Text = $"✔ {targetFactor}x ONNX-Modell erfolgreich heruntergeladen und einsatzbereit!";
                }
                else
                {
                    TextStatusHint.Text = $"Fehler beim Herunterladen des Modells. Der Offline-Algorithmus steht weiterhin bereit.";
                }
            };
            menu.Items.Add(itemDownload);
        }

        if (sender is Button btn)
        {
            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private async Task ExecuteUpscaleAsync(int factor)
    {
        if (_currentBitmap == null || _upscaleService == null) return;

        try
        {
            bool hasAi = _upscaleService.IsAiModelDownloaded(factor);
            string engineName = hasAi ? "ONNX Neural Net" : "Lanczos3 + Detail-Recovery";
            TextStatusHint.Text = $"✨ Berechne {factor}x Super Resolution ({engineName})... Bitte warten.";
            PushUndoState(bitmapModified: true);
            var upscaled = await _upscaleService.UpscaleAsync(_currentBitmap, factor);
            _currentBitmap.Dispose();
            _currentBitmap = upscaled;
            HasChanges = true;
            UpdateImageDisplay();
            OnZoomFitClicked(this, new RoutedEventArgs());
            TextStatusHint.Text = $"✨ {factor}x Super Resolution ({engineName}) erfolgreich: Neue Auflösung {_currentBitmap.Width} × {_currentBitmap.Height} px.";
        }
        catch (Exception ex)
        {
            TextStatusHint.Text = $"Fehler beim KI-Upscaling: {ex.Message}";
        }
    }

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
        {
            CanvasScrollViewer.ScrollToHorizontalOffset(CanvasScrollViewer.HorizontalOffset - e.Delta);
            e.Handled = true;
            return;
        }

        double factor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
        double oldScale = CanvasScaleTransform.ScaleX;
        double newScale = Math.Clamp(oldScale * factor, 0.1, 8.0);

        if (Math.Abs(newScale - oldScale) > 0.001)
        {
            var mousePos = e.GetPosition(CanvasScrollViewer);

            double contentLeft = Math.Max(0, (CanvasScrollViewer.ViewportWidth - CanvasContainer.ActualWidth) / 2.0);
            double contentTop = Math.Max(0, (CanvasScrollViewer.ViewportHeight - CanvasContainer.ActualHeight) / 2.0);

            double contentX = (CanvasScrollViewer.ScrollableWidth > 0)
                ? (mousePos.X + CanvasScrollViewer.HorizontalOffset)
                : Math.Max(0, mousePos.X - contentLeft);

            double contentY = (CanvasScrollViewer.ScrollableHeight > 0)
                ? (mousePos.Y + CanvasScrollViewer.VerticalOffset)
                : Math.Max(0, mousePos.Y - contentTop);

            double relX = contentX / oldScale;
            double relY = contentY / oldScale;

            SetZoom(newScale);
            CanvasScrollViewer.UpdateLayout();

            double targetOffsetX = relX * newScale - mousePos.X;
            double targetOffsetY = relY * newScale - mousePos.Y;

            CanvasScrollViewer.ScrollToHorizontalOffset(Math.Max(0, targetOffsetX));
            CanvasScrollViewer.ScrollToVerticalOffset(Math.Max(0, targetOffsetY));
        }
        e.Handled = true;
    }

    private void OnZoomInClicked(object sender, RoutedEventArgs e) => SetZoom(Math.Clamp(CanvasScaleTransform.ScaleX * 1.25, 0.1, 8.0));
    private void OnZoomOutClicked(object sender, RoutedEventArgs e) => SetZoom(Math.Clamp(CanvasScaleTransform.ScaleX * 0.8, 0.1, 8.0));
    private void OnZoomResetClicked(object sender, RoutedEventArgs e)
    {
        SetZoom(1.0);
        CanvasScrollViewer.ScrollToHorizontalOffset(0);
        CanvasScrollViewer.ScrollToVerticalOffset(0);
    }

    private void OnZoomFitClicked(object sender, RoutedEventArgs e)
    {
        if (_currentBitmap == null) return;
        double viewportW = CanvasScrollViewer.ActualWidth - 40;
        double viewportH = CanvasScrollViewer.ActualHeight - 40;
        if (viewportW <= 0 || viewportH <= 0) return;

        double scaleX = viewportW / _currentBitmap.Width;
        double scaleY = viewportH / _currentBitmap.Height;
        double fitScale = Math.Min(scaleX, scaleY);
        if (fitScale > 1.0) fitScale = 1.0;

        SetZoom(fitScale);
        CanvasScrollViewer.ScrollToHorizontalOffset(0);
        CanvasScrollViewer.ScrollToVerticalOffset(0);
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
        if (ComboBgMode == null || PanelColorKeyOptions == null || PanelAiOptions == null) return;
        int idx = ComboBgMode.SelectedIndex;
        PanelColorKeyOptions.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelAiOptions.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (PanelInteractiveAiOptions != null)
            PanelInteractiveAiOptions.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;

        if (idx == 2)
        {
            ActivateInteractiveBrushTool();
        }
        else
        {
            UpdateAiBrushButtonState();
            HideBrushCursor();
        }

        if (TextStatusHint != null)
        {
            TextStatusHint.Text = idx switch
            {
                0 => "Farbe: Wähle mit der Pipette eine Hintergrundfarbe zum Entfernen.",
                1 => "KI Vollbild: Segmentiert Personen, Tiere und Hauptobjekte vollautomatisch.",
                2 => "🎯 KI-Pinsel: Male über ein Objekt, um es gezielt durch KI auszuschneiden, oder nutze Korrektur-Pinsel.",
                _ => "Hintergrundentfernung aktiv."
            };
        }
    }

    private void OnPickColorClicked(object sender, RoutedEventArgs e)
    {
        _isPickingColor = true;
        if (TextStatusHint != null)
        {
            TextStatusHint.Text = "🎯 Klicke auf die Hintergrundfarbe im Bild, die transparent werden soll.";
        }
    }

    private void OnExecuteColorRemovalClicked(object sender, RoutedEventArgs e)
    {
        if (_editorService == null) return;
        PushUndoState(bitmapModified: true);
        float tolerance = SliderTolerance != null ? (float)(SliderTolerance.Value / 100.0) : 0.15f;
        var transparent = _editorService.RemoveBackgroundByColor(_currentBitmap, _pickedColor, tolerance);
        _currentBitmap.Dispose();
        _currentBitmap = transparent;
        UpdateImageDisplay();
        if (TextStatusHint != null)
        {
            TextStatusHint.Text = "Hintergrund erfolgreich transparent gemacht!";
        }
    }

    private void UpdateAiModelStatus()
    {
        if (_bgRemovalService == null || TextAiStatus == null || ButtonDownloadAiModel == null || ButtonExecuteAiRemoval == null) return;

        bool downloaded = _bgRemovalService.IsAiModelDownloaded();
        if (downloaded)
        {
            long size = _bgRemovalService.GetModelSizeBytes();
            string sizeStr = $"{size / (1024.0 * 1024.0):F1} MB";
            TextAiStatus.Text = $"🟢 Modell bereit ({sizeStr})";
            ButtonDownloadAiModel.Visibility = Visibility.Collapsed;
            ButtonExecuteAiRemoval.IsEnabled = true;

            if (TextInteractiveAiStatus != null) TextInteractiveAiStatus.Text = $"🟢 Modell bereit ({sizeStr})";
            if (ButtonDownloadInteractiveAi != null) ButtonDownloadInteractiveAi.Visibility = Visibility.Collapsed;
            if (ButtonExecuteInteractiveAiCutout != null) ButtonExecuteInteractiveAiCutout.IsEnabled = true;
        }
        else
        {
            TextAiStatus.Text = "⚪ Modell noch nicht heruntergeladen";
            ButtonDownloadAiModel.Visibility = Visibility.Visible;
            ButtonExecuteAiRemoval.IsEnabled = false;

            if (TextInteractiveAiStatus != null) TextInteractiveAiStatus.Text = "⚪ Modell noch nicht heruntergeladen";
            if (ButtonDownloadInteractiveAi != null) ButtonDownloadInteractiveAi.Visibility = Visibility.Visible;
            if (ButtonExecuteInteractiveAiCutout != null) ButtonExecuteInteractiveAiCutout.IsEnabled = false;
        }
    }

    private async void OnDownloadAiModelClicked(object sender, RoutedEventArgs e)
    {
        if (_bgRemovalService == null) return;
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
            MessageBox.Show(ParentWindow, "Das KI-Modell (U-2-Net) wurde erfolgreich heruntergeladen und ist nun 100% offline einsatzbereit!", "KI Modell", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(ParentWindow, "Fehler beim Herunterladen des KI-Modells. Bitte Internetverbindung prüfen.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnExecuteAiRemovalClicked(object sender, RoutedEventArgs e)
    {
        if (_bgRemovalService == null) return;
        try
        {
            ButtonExecuteAiRemoval.IsEnabled = false;
            ButtonExecuteAiRemoval.Content = "⏳ Freistellung läuft...";
            if (ProgressAiFullRemoval != null) ProgressAiFullRemoval.Visibility = Visibility.Visible;
            if (TextAiFullRemovalLoading != null) TextAiFullRemovalLoading.Visibility = Visibility.Visible;
            TextStatusHint.Text = "🧠 KI analysiert das Bild und segmentiert den Hintergrund... Bitte warten.";
            CanvasContainer.Cursor = Cursors.Wait;

            PushUndoState(bitmapModified: true);
            var result = await _bgRemovalService.RemoveBackgroundAsync(_currentBitmap, BackgroundRemovalMode.AiOnnx);

            _currentBitmap.Dispose();
            _currentBitmap = result;
            UpdateImageDisplay();
            TextStatusHint.Text = "Hintergrund mit Deep-Learning KI erfolgreich entfernt!";
        }
        catch (Exception ex)
        {
            AppLogger.Error("AI", "Fehler bei KI-Hintergrundentfernung", ex);
            MessageBox.Show(ParentWindow, "Fehler bei der KI-Hintergrundentfernung: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (ProgressAiFullRemoval != null) ProgressAiFullRemoval.Visibility = Visibility.Collapsed;
            if (TextAiFullRemovalLoading != null) TextAiFullRemovalLoading.Visibility = Visibility.Collapsed;
            ButtonExecuteAiRemoval.Content = "✨ Mit KI freistellen";
            ButtonExecuteAiRemoval.IsEnabled = true;
            CanvasContainer.Cursor = Cursors.Arrow;
        }
    }

    private void OnInteractiveBrushModeChanged(object sender, RoutedEventArgs e)
    {
        if (PanelCutoutButtons == null) return;
        bool isSelect = RadioBrushSelectObject?.IsChecked == true;
        PanelCutoutButtons.Visibility = isSelect ? Visibility.Visible : Visibility.Collapsed;

        if (_currentTool != EditorTool.BackgroundRemoval)
        {
            ActivateInteractiveBrushTool();
        }
        else
        {
            UpdateBrushCursorColor();
        }

        if (TextStatusHint != null)
        {
            if (isSelect)
                TextStatusHint.Text = "🎯 Objekt markieren: Male mit dem Pinsel über das gewünschte Objekt • Klicke dann auf 'Markiertes Objekt freistellen'.";
            else if (RadioBrushRestore?.IsChecked == true)
                TextStatusHint.Text = "🟢 Kanten wiederherstellen: Ziehe mit gedrückter Maustaste über das Bild, um Original-Pixel zurückzuholen.";
            else if (RadioBrushErase?.IsChecked == true)
                TextStatusHint.Text = "🔴 Kanten radieren: Ziehe mit gedrückter Maustaste über das Bild, um Pixel transparent wegzuradieren.";
        }
    }

    private void OnInteractiveBrushSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_currentTool != EditorTool.BackgroundRemoval && ComboBgMode?.SelectedIndex == 2)
        {
            ActivateInteractiveBrushTool();
        }
        else
        {
            UpdateBrushCursorSize();
        }
    }

    private void AddInteractiveBrushPoint(SKPoint pt)
    {
        float radius = SliderInteractiveBrushSize != null ? (float)SliderInteractiveBrushSize.Value : 32f;

        if (_interactiveBrushPoints.Count > 0)
        {
            var last = _interactiveBrushPoints[^1];
            float dX = pt.X - last.X;
            float dY = pt.Y - last.Y;
            if (dX * dX + dY * dY < (radius * 0.35f) * (radius * 0.35f))
            {
                return;
            }
        }

        _interactiveBrushPoints.Add(pt);

        int minX = (int)Math.Floor(pt.X - radius);
        int maxX = (int)Math.Ceiling(pt.X + radius);
        int minY = (int)Math.Floor(pt.Y - radius);
        int maxY = (int)Math.Ceiling(pt.Y + radius);

        var ptBox = new SKRectI(minX, minY, maxX, maxY);
        _interactiveBoundingBox = _interactiveBoundingBox.IsEmpty
            ? ptBox
            : SKRectI.Union(_interactiveBoundingBox, ptBox);

        if (InteractiveStrokeCanvas != null)
        {
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = radius * 2,
                Height = radius * 2,
                Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(115, 56, 189, 248)), // #7338BDF8 vibrant cyan
                IsHitTestVisible = false
            };
            Canvas.SetLeft(dot, pt.X - radius);
            Canvas.SetTop(dot, pt.Y - radius);
            InteractiveStrokeCanvas.Children.Add(dot);
        }
    }

    private void ApplyMaskBrushAt(SKPoint pt)
    {
        if (_originalBitmap == null || _currentBitmap == null || _bgRemovalService == null) return;
        float radius = SliderInteractiveBrushSize != null ? (float)SliderInteractiveBrushSize.Value : 32f;
        bool restore = RadioBrushRestore?.IsChecked == true;

        _bgRemovalService.ApplyMaskBrush(_currentBitmap, _originalBitmap, pt, radius, restore);
        RequestDisplayUpdate();
    }

    private void OnResetInteractiveStrokeClicked(object sender, RoutedEventArgs e)
    {
        _interactiveBrushPoints.Clear();
        _interactiveBoundingBox = SKRectI.Empty;
        InteractiveStrokeCanvas?.Children.Clear();
        if (TextStatusHint != null)
        {
            TextStatusHint.Text = "Pinselauswahl zurückgesetzt.";
        }
    }

    private async void OnExecuteInteractiveAiCutoutClicked(object sender, RoutedEventArgs e)
    {
        if (_bgRemovalService == null || !_bgRemovalService.IsAiModelDownloaded())
        {
            MessageBox.Show(ParentWindow, "Das KI-Modell wurde noch nicht heruntergeladen. Bitte zuerst auf 'KI-Modell herunterladen' klicken.", "Modell erforderlich", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_interactiveBrushPoints.Count == 0 || _interactiveBoundingBox.IsEmpty)
        {
            MessageBox.Show(ParentWindow, "Bitte malen Sie zuerst mit dem Pinsel über das gewünschte Objekt.", "Keine Objektauswahl", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (ButtonExecuteInteractiveAiCutout != null)
            {
                ButtonExecuteInteractiveAiCutout.IsEnabled = false;
                ButtonExecuteInteractiveAiCutout.Content = "⏳ Objekt wird isoliert...";
            }
            if (ButtonResetInteractiveStroke != null) ButtonResetInteractiveStroke.IsEnabled = false;
            if (ProgressAiCutout != null) ProgressAiCutout.Visibility = Visibility.Visible;
            if (TextAiCutoutLoading != null) TextAiCutoutLoading.Visibility = Visibility.Visible;
            if (TextStatusHint != null) TextStatusHint.Text = "🧠 KI isoliert das markierte Objekt und schneidet es aus... Bitte warten.";
            CanvasContainer.Cursor = Cursors.Wait;

            PushUndoState(bitmapModified: true);
            var cutout = await _bgRemovalService.SegmentObjectRegionAsync(
                _currentBitmap,
                _interactiveBoundingBox,
                _interactiveBrushPoints);

            _currentBitmap.Dispose();
            _currentBitmap = cutout;

            // Clear visual brush selection
            _interactiveBrushPoints.Clear();
            _interactiveBoundingBox = SKRectI.Empty;
            InteractiveStrokeCanvas?.Children.Clear();

            UpdateImageDisplay();
            if (TextStatusHint != null)
            {
                TextStatusHint.Text = "Objekt erfolgreich freigestellt! Kanten können nun mit 🟢/🔴 korrigiert werden.";
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("AI", "Fehler bei KI-Objektauswahl", ex);
            MessageBox.Show(ParentWindow, "Fehler beim Freistellen des Objekts: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (ProgressAiCutout != null) ProgressAiCutout.Visibility = Visibility.Collapsed;
            if (TextAiCutoutLoading != null) TextAiCutoutLoading.Visibility = Visibility.Collapsed;
            if (ButtonExecuteInteractiveAiCutout != null)
            {
                ButtonExecuteInteractiveAiCutout.Content = "✂️ Markiertes Objekt freistellen";
                ButtonExecuteInteractiveAiCutout.IsEnabled = true;
            }
            if (ButtonResetInteractiveStroke != null) ButtonResetInteractiveStroke.IsEnabled = true;
            CanvasContainer.Cursor = Cursors.Cross;
        }
    }

    private void OnActivateAiBrushClicked(object sender, RoutedEventArgs e)
    {
        ActivateInteractiveBrushTool();
    }

    private void ActivateInteractiveBrushTool()
    {
        if (ToolBgRemove != null && ToolBgRemove.IsChecked != true)
        {
            ToolBgRemove.IsChecked = true;
        }
        _currentTool = EditorTool.BackgroundRemoval;

        // Deselect any active annotation
        if (_selectedAnnotation != null)
        {
            _selectedAnnotation = null;
            UpdateImageDisplay();
        }

        // Hide other tool options
        if (PanelCropOptions != null) PanelCropOptions.Visibility = Visibility.Collapsed;
        if (PanelBlurOptions != null) PanelBlurOptions.Visibility = Visibility.Collapsed;
        if (PanelBlurBrushOptions != null) PanelBlurBrushOptions.Visibility = Visibility.Collapsed;
        if (PanelStepBadgeOptions != null) PanelStepBadgeOptions.Visibility = Visibility.Collapsed;

        // Ensure side panel is visible on background removal tab
        if (SidePanel != null) SidePanel.Visibility = Visibility.Visible;
        if (TabSideBgRemove != null && TabSideBgRemove.IsChecked != true)
        {
            TabSideBgRemove.IsChecked = true;
            OnSideTabChanged(null, null);
        }

        // Ensure mode is on KI-Pinsel (Index 2)
        if (ComboBgMode != null && ComboBgMode.SelectedIndex != 2)
        {
            ComboBgMode.SelectedIndex = 2;
        }

        UpdateAiBrushButtonState();

        if (TextStatusHint != null)
        {
            if (RadioBrushRestore?.IsChecked == true)
                TextStatusHint.Text = "🟢 KI-Kanten wiederherstellen: Ziehe mit gedrückter Maustaste über das Bild, um Original-Pixel zurückzuholen.";
            else if (RadioBrushErase?.IsChecked == true)
                TextStatusHint.Text = "🔴 KI-Kanten radieren: Ziehe mit gedrückter Maustaste über das Bild, um Pixel transparent wegzuradieren.";
            else
                TextStatusHint.Text = "🎯 KI-Pinsel aktiv: Male über das Objekt im Bild • Klicke anschließend auf 'Markiertes Objekt freistellen'.";
        }
    }

    private void UpdateAiBrushButtonState()
    {
        if (ButtonActivateAiBrush == null || TextActivateAiBrush == null || IconActivateAiBrush == null) return;

        bool isBrushActive = _currentTool == EditorTool.BackgroundRemoval && ComboBgMode?.SelectedIndex == 2;
        if (isBrushActive)
        {
            ButtonActivateAiBrush.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)); // #10B981 emerald green
            ButtonActivateAiBrush.Foreground = System.Windows.Media.Brushes.White;
            ButtonActivateAiBrush.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(5, 150, 105));
            IconActivateAiBrush.Text = "✓";
            TextActivateAiBrush.Text = "KI-Freistellpinsel ist aktiv";
            if (TextBrushActiveIndicator != null)
            {
                TextBrushActiveIndicator.Text = "● Pinsel aktiv – jetzt im Bild über das Objekt malen";
                TextBrushActiveIndicator.Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129));
            }
            if (BorderBrushActivation != null)
            {
                BorderBrushActivation.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 16, 185, 129));
            }
        }
        else
        {
            ButtonActivateAiBrush.ClearValue(Control.BackgroundProperty);
            ButtonActivateAiBrush.ClearValue(Control.ForegroundProperty);
            ButtonActivateAiBrush.ClearValue(Control.BorderBrushProperty);
            IconActivateAiBrush.Text = "🖌️";
            TextActivateAiBrush.Text = "KI-Freistellpinsel aktivieren";
            if (TextBrushActiveIndicator != null)
            {
                TextBrushActiveIndicator.Text = "(Klicken, um den Pinsel aufzunehmen)";
                TextBrushActiveIndicator.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondaryBrush");
            }
            if (BorderBrushActivation != null)
            {
                BorderBrushActivation.ClearValue(Border.BorderBrushProperty);
            }
        }
    }

    private void UpdateBrushCursorPosition(SKPoint pt)
    {
        if (BrushCursorCircle == null) return;
        float radius = SliderInteractiveBrushSize != null ? (float)SliderInteractiveBrushSize.Value : 32f;
        double diameter = radius * 2.0;

        BrushCursorCircle.Width = diameter;
        BrushCursorCircle.Height = diameter;
        Canvas.SetLeft(BrushCursorCircle, pt.X - radius);
        Canvas.SetTop(BrushCursorCircle, pt.Y - radius);

        UpdateBrushCursorColor();
        BrushCursorCircle.Visibility = Visibility.Visible;
    }

    private void UpdateBrushCursorColor()
    {
        if (BrushCursorCircle == null) return;
        if (RadioBrushRestore?.IsChecked == true)
        {
            BrushCursorCircle.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)); // #10B981 green
        }
        else if (RadioBrushErase?.IsChecked == true)
        {
            BrushCursorCircle.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(239, 68, 68)); // #EF4444 red
        }
        else
        {
            BrushCursorCircle.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromRgb(56, 189, 248)); // #38BDF8 cyan
        }
    }

    private void UpdateBrushCursorSize()
    {
        if (BrushCursorCircle == null || BrushCursorCircle.Visibility != Visibility.Visible) return;
        float radius = SliderInteractiveBrushSize != null ? (float)SliderInteractiveBrushSize.Value : 32f;
        BrushCursorCircle.Width = radius * 2.0;
        BrushCursorCircle.Height = radius * 2.0;
    }

    private void HideBrushCursor()
    {
        if (BrushCursorCircle != null)
        {
            BrushCursorCircle.Visibility = Visibility.Collapsed;
        }
    }

    private void OnCanvasMouseEnter(object sender, MouseEventArgs e)
    {
        if (_currentTool == EditorTool.BackgroundRemoval && ComboBgMode?.SelectedIndex == 2)
        {
            var pos = e.GetPosition(BaseImage);
            UpdateBrushCursorPosition(new SKPoint((float)pos.X, (float)pos.Y));
        }
    }

    private void OnCanvasMouseLeave(object sender, MouseEventArgs e)
    {
        HideBrushCursor();
    }

    // Saving & Actions
    private void OnSaveAndReplaceClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            using var final = GetFinalBitmap();
            SaveBitmapToFile(final, _filePath);
            ResultFilePath = _filePath;
            HasChanges = true;
            FileSaved?.Invoke(this, ResultFilePath);
            var parentWin = Window.GetWindow(this);
            if (parentWin is ImageEditorWindow) parentWin.Close();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Editor", "Fehler beim Speichern", ex);
            MessageBox.Show(ParentWindow, "Fehler beim Speichern: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSaveAsCopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = Path.GetDirectoryName(_filePath) ?? "";
            string name = Path.GetFileNameWithoutExtension(_filePath);
            string copyPath = Path.Combine(dir, $"{name}_bearbeitet.png");

            using var final = GetFinalBitmap();
            SaveBitmapToFile(final, copyPath);
            ResultFilePath = copyPath;
            HasChanges = true;
            FileSaved?.Invoke(this, ResultFilePath);
            var parentWin = Window.GetWindow(this);
            if (parentWin is ImageEditorWindow) parentWin.Close();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Editor", "Fehler beim Speichern als Kopie", ex);
            MessageBox.Show(ParentWindow, "Fehler beim Speichern: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCopyToClipboardClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            using var final = GetFinalBitmap();
            using var image = SKImage.FromBitmap(final);
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

            ClipboardMonitorService.Current?.SuppressNextUpdate();
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
        Cancelled?.Invoke(this, EventArgs.Empty);
        var parentWin = Window.GetWindow(this);
        if (parentWin is ImageEditorWindow) parentWin.Close();
    }

    private void OnSwitchToProfiModeClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string pathForProfi = _filePath;
            if (HasChanges)
            {
                var ask = MessageBox.Show(ParentWindow,
                    "Möchtest du die aktuellen Änderungen vor dem Wechsel in den Profi-Modus übernehmen?",
                    "In Profi-Modus wechseln",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (ask == MessageBoxResult.Cancel) return;
                if (ask == MessageBoxResult.Yes)
                {
                    using var final = GetFinalBitmap();
                    string tempPath = Path.Combine(Path.GetTempPath(), $"imaginary_profi_prep_{Guid.NewGuid():N}.png");
                    SaveBitmapToFile(final, tempPath);
                    pathForProfi = tempPath;
                }
            }

            if (SwitchToProfiRequested != null)
            {
                SwitchToProfiRequested.Invoke(this, pathForProfi);
                return;
            }

            var profiService = new ProfiDeveloperService();
            var profiWin = new ProfiEditorWindow(pathForProfi, profiService)
            {
                Owner = ParentWindow
            };

            if (profiWin.ShowDialog() == true && profiWin.HasChanges)
            {
                ResultFilePath = profiWin.ResultFilePath;
                HasChanges = true;
                FileSaved?.Invoke(this, ResultFilePath);
                var parentWin = Window.GetWindow(this);
                if (parentWin is ImageEditorWindow) parentWin.Close();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Editor", "Fehler beim Wechsel in den Profi-Modus", ex);
            MessageBox.Show(ParentWindow, "Fehler beim Wechsel in den Profi-Modus: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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
        else if (e.Key == Key.Delete && _selectedAnnotation != null && _currentTool == EditorTool.Pan)
        {
            PushUndoState();
            _annotations.Remove(_selectedAnnotation);
            _selectedAnnotation = null;
            UpdateImageDisplay();
            TextStatusHint.Text = "Element gelöscht.";
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_isCompareMode)
            {
                ExitCompareMode();
                e.Handled = true;
                return;
            }

            if (_activeCropRect.HasValue)
            {
                _activeCropRect = null;
                PreviewRect.Visibility = Visibility.Collapsed;
                TextStatusHint.Text = "Zuschnitt abgebrochen.";
            }
            else if (_selectedAnnotation != null)
            {
                _selectedAnnotation = null;
                UpdateImageDisplay();
            }
        }
    }

    private static void SaveBitmapToFile(SKBitmap bitmap, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var format = ext switch
        {
            ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
            ".webp" => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png
        };
        int quality = format == SKEncodedImageFormat.Jpeg ? 92 : 100;
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, quality);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    public void Cleanup()
    {
        ResetEditorState();
    }

    private void OnCompareClicked(object sender, RoutedEventArgs e)
    {
        if (_isCompareMode)
        {
            ExitCompareMode();
        }
        else
        {
            EnterCompareMode();
        }
    }

    private void EnterCompareMode()
    {
        if (_currentBitmap == null || _originalBitmap == null)
        {
            if (TextStatusHint != null) TextStatusHint.Text = "Kein Bild zum Vergleichen vorhanden.";
            return;
        }

        _isCompareMode = true;
        _isDraggingSplit = false;
        _isHoldingOriginal = false;

        ToolPan.IsChecked = true;
        _currentTool = EditorTool.Pan;

        ButtonCompare.Background = (Brush)FindResource("AccentBrush");
        ButtonCompare.Foreground = Brushes.White;

        CompareOverlayGrid.Visibility = Visibility.Visible;
        PanelCompareControls.Visibility = Visibility.Visible;

        ImageOriginalCompare.Source = CreateBitmapSourceFromSKBitmap(_originalBitmap);

        UpdateSplitGeometry();
        if (TextStatusHint != null) TextStatusHint.Text = "⚖️ Vorher/Nachher-Vergleich aktiv: Ziehe die Trennlinie oder den Schieberegler.";
    }

    private void ExitCompareMode()
    {
        if (!_isCompareMode) return;

        _isCompareMode = false;
        _isDraggingSplit = false;
        _isHoldingOriginal = false;

        ButtonCompare?.ClearValue(Button.BackgroundProperty);
        ButtonCompare?.ClearValue(Button.ForegroundProperty);

        if (CompareOverlayGrid != null) CompareOverlayGrid.Visibility = Visibility.Collapsed;
        if (PanelCompareControls != null) PanelCompareControls.Visibility = Visibility.Collapsed;
        if (ImageOriginalCompare != null) ImageOriginalCompare.Source = null;

        Mouse.OverrideCursor = null;
        if (TextStatusHint != null) TextStatusHint.Text = "Vergleichsmodus beendet.";
    }

    private void UpdateSplitGeometry()
    {
        if (!_isCompareMode || _currentBitmap == null || CompareOverlayGrid == null) return;
        int width = _currentBitmap.Width;
        int height = _currentBitmap.Height;
        if (width <= 0 || height <= 0) return;

        CompareOverlayGrid.Width = width;
        CompareOverlayGrid.Height = height;
        ImageOriginalCompare.Width = width;
        ImageOriginalCompare.Height = height;

        if (_isHoldingOriginal)
        {
            OriginalClipGeometry.Rect = new Rect(0, 0, width, height);
            SplitLine.Visibility = Visibility.Collapsed;
            SplitHandle.Visibility = Visibility.Collapsed;
            BadgeProcessed.Visibility = Visibility.Collapsed;
            BadgeOriginal.Visibility = Visibility.Visible;
            TextBadgeOriginal.Text = $"👁️ 100% ORIGINAL (VORHER: {_originalBitmap?.Width} × {_originalBitmap?.Height} px)";
            return;
        }

        SplitLine.Visibility = Visibility.Visible;
        SplitHandle.Visibility = Visibility.Visible;
        BadgeProcessed.Visibility = Visibility.Visible;
        BadgeOriginal.Visibility = Visibility.Visible;
        TextBadgeOriginal.Text = $"📷 Vorher (Original: {_originalBitmap?.Width} × {_originalBitmap?.Height} px)";
        TextBadgeProcessed.Text = $"✨ Nachher (Bearbeitet: {_currentBitmap.Width} × {_currentBitmap.Height} px)";

        double splitX = (SliderCompareSplit.Value / 100.0) * width;
        splitX = Math.Clamp(splitX, 0, width);

        OriginalClipGeometry.Rect = new Rect(0, 0, splitX, height);

        SplitLine.X1 = splitX;
        SplitLine.X2 = splitX;
        SplitLine.Y1 = 0;
        SplitLine.Y2 = height;

        Canvas.SetLeft(SplitHandle, splitX - 18);
        Canvas.SetTop(SplitHandle, Math.Max(0, (height / 2.0) - 18));
    }

    private void OnCompareSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSplitGeometry();
    }

    private void UpdateSplitFromMouse(double mouseX)
    {
        if (_currentBitmap == null || _currentBitmap.Width <= 0 || SliderCompareSplit == null) return;
        double pct = Math.Clamp((mouseX / _currentBitmap.Width) * 100.0, 0.0, 100.0);
        SliderCompareSplit.Value = pct;
    }

    private void SetHoldOriginal(bool hold)
    {
        if (!_isCompareMode) return;
        _isHoldingOriginal = hold;
        UpdateSplitGeometry();
    }

    private void OnHoldOriginalMouseDown(object sender, MouseButtonEventArgs e)
    {
        SetHoldOriginal(true);
        if (sender is UIElement el) el.CaptureMouse();
    }

    private void OnHoldOriginalMouseUp(object sender, MouseButtonEventArgs e)
    {
        SetHoldOriginal(false);
        if (sender is UIElement el) el.ReleaseMouseCapture();
    }

    private void OnCloseCompareClicked(object sender, RoutedEventArgs e)
    {
        ExitCompareMode();
    }

    private static BitmapSource? CreateBitmapSourceFromSKBitmap(SKBitmap? bitmap)
    {
        if (bitmap == null) return null;
        var info = bitmap.Info;
        SKBitmap displayBmp;
        bool needDispose = false;

        if (bitmap.ColorType != SKColorType.Bgra8888 || bitmap.AlphaType != SKAlphaType.Premul)
        {
            displayBmp = new SKBitmap(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var canvas = new SKCanvas(displayBmp);
            canvas.DrawBitmap(bitmap, 0, 0);
            canvas.Flush();
            needDispose = true;
        }
        else
        {
            displayBmp = bitmap;
        }

        try
        {
            var bs = BitmapSource.Create(
                displayBmp.Width,
                displayBmp.Height,
                96,
                96,
                PixelFormats.Pbgra32,
                null,
                displayBmp.GetPixels(),
                displayBmp.RowBytes * displayBmp.Height,
                displayBmp.RowBytes);
            bs.Freeze();
            return bs;
        }
        finally
        {
            if (needDispose)
            {
                displayBmp.Dispose();
            }
        }
    }

    // ==========================================
    // Paint-Style Canvas Resizing (Ziehpunkte)
    // ==========================================

    private void OnCanvasGripMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || _currentBitmap == null) return;

        CanvasResizeGrip grip = CanvasResizeGrip.None;
        if (sender == CanvasGripRight) grip = CanvasResizeGrip.Right;
        else if (sender == CanvasGripBottom) grip = CanvasResizeGrip.Bottom;
        else if (sender == CanvasGripCorner) grip = CanvasResizeGrip.Corner;

        if (grip == CanvasResizeGrip.None) return;

        _isResizingCanvasInteractive = true;
        _canvasGripMode = grip;
        _canvasResizeStartPos = e.GetPosition(CanvasScrollViewer);
        _canvasResizeOriginalWidth = _currentBitmap.Width;
        _canvasResizeOriginalHeight = _currentBitmap.Height;
        _canvasResizeNewWidth = _currentBitmap.Width;
        _canvasResizeNewHeight = _currentBitmap.Height;

        if (PreviewCanvasExpandRect != null)
        {
            PreviewCanvasExpandRect.Width = _currentBitmap.Width;
            PreviewCanvasExpandRect.Height = _currentBitmap.Height;
            Canvas.SetLeft(PreviewCanvasExpandRect, 0);
            Canvas.SetTop(PreviewCanvasExpandRect, 0);
            PreviewCanvasExpandRect.Visibility = Visibility.Visible;
        }

        CanvasContainer.CaptureMouse();
        e.Handled = true;
    }

    private void OnResizeCanvasClicked(object sender, RoutedEventArgs e)
    {
        if (_currentBitmap == null || _editorService == null) return;

        var dlg = new CanvasResizeDialog(_currentBitmap.Width, _currentBitmap.Height)
        {
            Owner = ParentWindow
        };

        if (dlg.ShowDialog() == true)
        {
            PushUndoState(bitmapModified: true);
            var oldBmp = _currentBitmap;
            _currentBitmap = _editorService.ResizeCanvas(oldBmp, dlg.NewWidth, dlg.NewHeight, dlg.Anchor, dlg.BackgroundColor);
            oldBmp.Dispose();

            if (dlg.OffsetX != 0 || dlg.OffsetY != 0)
            {
                foreach (var ann in _annotations)
                {
                    ann.MoveBy(dlg.OffsetX, dlg.OffsetY);
                }
            }

            UpdateImageDisplay();
            UpdateLayersList();
            if (TextStatusHint != null) TextStatusHint.Text = $"Leinwandgröße geändert auf {dlg.NewWidth} × {dlg.NewHeight} px.";
        }
    }

    // ==========================================
    // 8 Transformations-Anfasser für Ebenen
    // ==========================================

    private AnnotationResizeHandle GetAnnotationResizeHandle(SKPoint pt, EditorAnnotation? ann)
    {
        if (ann == null || !ann.IsVisible) return AnnotationResizeHandle.None;
        if (ann.Type != AnnotationType.Image && ann.Type != AnnotationType.Rectangle) return AnnotationResizeHandle.None;

        var bounds = ann.GetBounds();
        float hitTol = 10f;

        bool Near(float x, float y) => Math.Abs(pt.X - x) <= hitTol && Math.Abs(pt.Y - y) <= hitTol;

        if (Near(bounds.Left, bounds.Top)) return AnnotationResizeHandle.TopLeft;
        if (Near(bounds.MidX, bounds.Top)) return AnnotationResizeHandle.TopCenter;
        if (Near(bounds.Right, bounds.Top)) return AnnotationResizeHandle.TopRight;
        if (Near(bounds.Right, bounds.MidY)) return AnnotationResizeHandle.MiddleRight;
        if (Near(bounds.Right, bounds.Bottom)) return AnnotationResizeHandle.BottomRight;
        if (Near(bounds.MidX, bounds.Bottom)) return AnnotationResizeHandle.BottomCenter;
        if (Near(bounds.Left, bounds.Bottom)) return AnnotationResizeHandle.BottomLeft;
        if (Near(bounds.Left, bounds.MidY)) return AnnotationResizeHandle.MiddleLeft;

        return AnnotationResizeHandle.None;
    }

    private static Cursor GetCursorForHandle(AnnotationResizeHandle handle) => handle switch
    {
        AnnotationResizeHandle.TopLeft or AnnotationResizeHandle.BottomRight => Cursors.SizeNWSE,
        AnnotationResizeHandle.TopRight or AnnotationResizeHandle.BottomLeft => Cursors.SizeNESW,
        AnnotationResizeHandle.TopCenter or AnnotationResizeHandle.BottomCenter => Cursors.SizeNS,
        AnnotationResizeHandle.MiddleLeft or AnnotationResizeHandle.MiddleRight => Cursors.SizeWE,
        _ => Cursors.Arrow
    };

    private static SKRect CalculateResizedRect(SKRect orig, AnnotationResizeHandle handle, float dx, float dy, bool keepAspect)
    {
        float left = orig.Left;
        float top = orig.Top;
        float right = orig.Right;
        float bottom = orig.Bottom;

        float aspect = orig.Width / Math.Max(1f, orig.Height);

        switch (handle)
        {
            case AnnotationResizeHandle.MiddleRight:
                right = Math.Max(left + 16f, orig.Right + dx);
                if (keepAspect) bottom = top + (right - left) / aspect;
                break;
            case AnnotationResizeHandle.BottomCenter:
                bottom = Math.Max(top + 16f, orig.Bottom + dy);
                if (keepAspect) right = left + (bottom - top) * aspect;
                break;
            case AnnotationResizeHandle.MiddleLeft:
                left = Math.Min(right - 16f, orig.Left + dx);
                if (keepAspect) bottom = top + (right - left) / aspect;
                break;
            case AnnotationResizeHandle.TopCenter:
                top = Math.Min(bottom - 16f, orig.Top + dy);
                if (keepAspect) right = left + (bottom - top) * aspect;
                break;
            case AnnotationResizeHandle.BottomRight:
                if (keepAspect)
                {
                    float d = Math.Max(dx, dy * aspect);
                    right = Math.Max(left + 16f, orig.Right + d);
                    bottom = top + (right - left) / aspect;
                }
                else
                {
                    right = Math.Max(left + 16f, orig.Right + dx);
                    bottom = Math.Max(top + 16f, orig.Bottom + dy);
                }
                break;
            case AnnotationResizeHandle.BottomLeft:
                if (keepAspect)
                {
                    float d = Math.Max(-dx, dy * aspect);
                    left = Math.Min(right - 16f, orig.Right - (orig.Width + d));
                    bottom = top + (right - left) / aspect;
                }
                else
                {
                    left = Math.Min(right - 16f, orig.Left + dx);
                    bottom = Math.Max(top + 16f, orig.Bottom + dy);
                }
                break;
            case AnnotationResizeHandle.TopRight:
                if (keepAspect)
                {
                    float d = Math.Max(dx, -dy * aspect);
                    right = Math.Max(left + 16f, orig.Left + (orig.Width + d));
                    top = bottom - (right - left) / aspect;
                }
                else
                {
                    right = Math.Max(left + 16f, orig.Right + dx);
                    top = Math.Min(bottom - 16f, orig.Top + dy);
                }
                break;
            case AnnotationResizeHandle.TopLeft:
                if (keepAspect)
                {
                    float d = Math.Max(-dx, -dy * aspect);
                    left = Math.Min(right - 16f, orig.Right - (orig.Width + d));
                    top = bottom - (right - left) / aspect;
                }
                else
                {
                    left = Math.Min(right - 16f, orig.Left + dx);
                    top = Math.Min(bottom - 16f, orig.Top + dy);
                }
                break;
        }

        return new SKRect(left, top, right, bottom);
    }

    // ==========================================
    // Multi-Image & Screenshot Ebenen-Import
    // ==========================================

    public void InsertImageFromBitmap(SKBitmap bmp, string name, float? posX = null, float? posY = null)
    {
        ArgumentNullException.ThrowIfNull(bmp);

        _sessionLayerBitmaps.Add(bmp);

        int canvasW = _currentBitmap?.Width ?? 1920;
        int canvasH = _currentBitmap?.Height ?? 1080;

        float imgW = bmp.Width;
        float imgH = bmp.Height;

        // Auto-scale down if image is larger than 85% of canvas
        float maxAllowedW = canvasW * 0.85f;
        float maxAllowedH = canvasH * 0.85f;
        if (imgW > maxAllowedW || imgH > maxAllowedH)
        {
            float scale = Math.Min(maxAllowedW / imgW, maxAllowedH / imgH);
            imgW = Math.Max(32f, imgW * scale);
            imgH = Math.Max(32f, imgH * scale);
        }

        float x = posX ?? Math.Max(20f, (canvasW - imgW) / 2f);
        float y = posY ?? Math.Max(20f, (canvasH - imgH) / 2f);

        // Deselect previous
        foreach (var a in _annotations) a.IsSelected = false;

        var ann = new EditorAnnotation
        {
            Type = AnnotationType.Image,
            ImageBitmap = bmp,
            ImageName = name,
            Rect = new SKRect(x, y, x + imgW, y + imgH),
            IsSelected = true,
            KeepAspectRatio = true,
            Opacity = 1.0f
        };

        PushUndoState();
        _annotations.Add(ann);
        _selectedAnnotation = ann;

        ToolPan.IsChecked = true;
        _currentTool = EditorTool.Pan;

        UpdateImageDisplay();
        UpdateLayersList();

        if (TextStatusHint != null)
        {
            TextStatusHint.Text = $"🖼️ '{name}' als neue Ebene eingefügt. Ziehe zum Verschieben oder an den Ecken zum Skalieren.";
        }
    }

    public void InsertFromClipboard()
    {
        try
        {
            if (Clipboard.ContainsImage())
            {
                var wpfBmp = Clipboard.GetImage();
                if (wpfBmp != null)
                {
                    var skBmp = ConvertBitmapSourceToSKBitmap(wpfBmp);
                    if (skBmp != null)
                    {
                        InsertImageFromBitmap(skBmp, $"Screenshot {DateTime.Now:HH:mm:ss}");
                        return;
                    }
                }
            }

            if (Clipboard.ContainsFileDropList())
            {
                var dropList = Clipboard.GetFileDropList();
                foreach (var file in dropList)
                {
                    if (File.Exists(file) && IsSupportedImageFile(file))
                    {
                        InsertImageFromFile(file);
                        return;
                    }
                }
            }

            if (TextStatusHint != null)
            {
                TextStatusHint.Text = "Kein Bild in der Zwischenablage. Drücke Win+Shift+S für einen Screenshot.";
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Editor", "Fehler beim Einfügen aus Zwischenablage", ex);
            if (TextStatusHint != null) TextStatusHint.Text = "Fehler beim Einfügen aus der Zwischenablage.";
        }
    }

    private static SKBitmap? ConvertBitmapSourceToSKBitmap(BitmapSource source)
    {
        using var memStream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(memStream);
        memStream.Position = 0;
        return SKBitmap.Decode(memStream);
    }

    private static bool IsSupportedImageFile(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".png" or ".jpg" or ".jpeg" or ".webp" or ".avif" or ".bmp" or ".gif" or ".tif" or ".tiff" or ".ico";
    }

    public void InsertImageFromFile(string filePath, float? posX = null, float? posY = null)
    {
        try
        {
            SKBitmap? loaded = null;
            if (_converter != null)
            {
                try
                {
                    loaded = _converter.LoadBitmap(filePath).Bitmap;
                }
                catch { }
            }

            if (loaded == null)
            {
                using var stream = File.OpenRead(filePath);
                loaded = SKBitmap.Decode(stream);
            }

            if (loaded != null)
            {
                InsertImageFromBitmap(loaded, Path.GetFileName(filePath), posX, posY);
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Editor", $"Fehler beim Laden von {filePath}", ex);
            MessageBox.Show(ParentWindow, $"Konnte Bild nicht laden: {ex.Message}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public async Task CaptureScreenshotAndInsertAsync()
    {
        try
        {
            if (TextStatusHint != null) TextStatusHint.Text = "📸 Windows Snipping Tool gestartet (Bereich auswählen)...";

            if (_screenshotService != null)
            {
                _screenshotService.TriggerNativeSnipping();
            }
            else
            {
                Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
            }

            // Poll clipboard for up to 15 seconds for a new screenshot
            DateTime start = DateTime.Now;

            while ((DateTime.Now - start).TotalSeconds < 15)
            {
                await Task.Delay(500);

                if (Clipboard.ContainsImage())
                {
                    var wpfBmp = Clipboard.GetImage();
                    if (wpfBmp != null)
                    {
                        var skBmp = ConvertBitmapSourceToSKBitmap(wpfBmp);
                        if (skBmp != null)
                        {
                            InsertImageFromBitmap(skBmp, $"Screenshot {DateTime.Now:HH:mm:ss}");
                            if (TextStatusHint != null) TextStatusHint.Text = "📸 Screenshot erfolgreich als neue Ebene eingefügt!";
                            return;
                        }
                    }
                }
            }

            if (TextStatusHint != null) TextStatusHint.Text = "Zeitüberschreitung beim Warten auf Screenshot.";
        }
        catch (Exception ex)
        {
            AppLogger.Error("Editor", "Fehler bei Screenshot-Aufnahme", ex);
        }
    }

    // ==========================================
    // Drag & Drop auf Arbeitsfläche
    // ==========================================

    private void OnCanvasDragOver(object sender, DragEventArgs e)
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

    private void OnCanvasDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var mousePos = e.GetPosition(BaseImage);
            float curX = (float)mousePos.X;
            float curY = (float)mousePos.Y;

            foreach (var file in files)
            {
                if (File.Exists(file) && IsSupportedImageFile(file))
                {
                    InsertImageFromFile(file, curX, curY);
                    curX += 30;
                    curY += 30;
                }
            }
            e.Handled = true;
        }
    }

    // ==========================================
    // Menü- & Toolbar-Aktionen für Bild-Import
    // ==========================================

    private void OnInsertImageButtonClicked(object sender, RoutedEventArgs e)
    {
        if (ButtonInsertImage?.ContextMenu != null)
        {
            ButtonInsertImage.ContextMenu.PlacementTarget = ButtonInsertImage;
            ButtonInsertImage.ContextMenu.IsOpen = true;
        }
    }

    private void OnInsertFromClipboardClicked(object sender, RoutedEventArgs e)
    {
        InsertFromClipboard();
    }

    private async void OnCaptureScreenshotAndInsertClicked(object sender, RoutedEventArgs e)
    {
        await CaptureScreenshotAndInsertAsync();
    }

    private void OnInsertFromFileClicked(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Bilddatei als Ebene einfügen",
            Filter = "Alle unterstützten Bildformate|*.png;*.jpg;*.jpeg;*.webp;*.avif;*.bmp;*.gif;*.tif;*.tiff;*.ico|PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|WebP (*.webp)|*.webp|Alle Dateien (*.*)|*.*",
            Multiselect = true
        };

        if (dlg.ShowDialog(ParentWindow) == true)
        {
            float? posX = null, posY = null;
            foreach (var filename in dlg.FileNames)
            {
                InsertImageFromFile(filename, posX, posY);
                posX = (posX ?? 40f) + 30f;
                posY = (posY ?? 40f) + 30f;
            }
        }
    }

    // ==========================================
    // Aktionen für ausgewählte Bild-Ebene
    // ==========================================

    private void OnLayerOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_selectedAnnotation != null && _selectedAnnotation.Type == AnnotationType.Image)
        {
            _selectedAnnotation.Opacity = (float)SliderLayerOpacity.Value;
            if (TextLayerOpacityVal != null)
            {
                TextLayerOpacityVal.Text = $"{(_selectedAnnotation.Opacity * 100):0}%";
            }
            RequestDisplayUpdate();
        }
    }

    private void OnResetSelectedImageLayerSizeClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedAnnotation != null && _selectedAnnotation.Type == AnnotationType.Image && _selectedAnnotation.ImageBitmap != null)
        {
            PushUndoState();
            var origW = _selectedAnnotation.ImageBitmap.Width;
            var origH = _selectedAnnotation.ImageBitmap.Height;
            _selectedAnnotation.Rect = new SKRect(_selectedAnnotation.Rect.Left, _selectedAnnotation.Rect.Top, _selectedAnnotation.Rect.Left + origW, _selectedAnnotation.Rect.Top + origH);
            UpdateImageDisplay();
            if (TextStatusHint != null) TextStatusHint.Text = $"Ebene auf Originalgröße ({origW} × {origH} px) zurückgesetzt.";
        }
    }

    private void OnToggleAspectLockClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedAnnotation != null && _selectedAnnotation.Type == AnnotationType.Image)
        {
            _selectedAnnotation.KeepAspectRatio = !_selectedAnnotation.KeepAspectRatio;
            ButtonToggleAspectLock.Content = _selectedAnnotation.KeepAspectRatio ? "🔗 Seitenverhältnis" : "🔓 Freies Format";
            if (TextStatusHint != null) TextStatusHint.Text = _selectedAnnotation.KeepAspectRatio ? "Seitenverhältnis gesperrt." : "Freies Skalieren aktiv.";
        }
    }

    private void OnFlattenSelectedLayerClicked(object sender, RoutedEventArgs e)
    {
        if (_selectedAnnotation != null && _selectedAnnotation.Type == AnnotationType.Image && _selectedAnnotation.ImageBitmap != null)
        {
            PushUndoState(bitmapModified: true);
            using var canvas = new SKCanvas(_currentBitmap);
            _selectedAnnotation.Render(canvas);
            canvas.Flush();

            _annotations.Remove(_selectedAnnotation);
            _selectedAnnotation = null;

            UpdateImageDisplay();
            UpdateLayersList();
            if (TextStatusHint != null) TextStatusHint.Text = "Ebene fest in das Hauptbild eingebettet.";
        }
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

public enum AnnotationType
{
    Arrow,
    Rectangle,
    Oval,
    StepBadge,
    Freehand,
    Highlighter,
    Text,
    Image
}

public class EditorAnnotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool IsVisible { get; set; } = true;
    public bool IsSelected { get; set; }
    public AnnotationType Type { get; set; }
    public SKPoint StartPoint { get; set; }
    public SKPoint EndPoint { get; set; }
    public SKRect Rect { get; set; }
    public List<SKPoint> Points { get; set; } = new();
    public SKColor Color { get; set; }
    public float StrokeWidth { get; set; } = 4f;
    public int BadgeNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public float FontSize { get; set; } = 24f;
    public SKColor BackgroundColor { get; set; } = new SKColor(15, 23, 42, 210);

    // Image layer properties
    public SKBitmap? ImageBitmap { get; set; }
    public string ImageName { get; set; } = "Eingefügtes Bild";
    public float Opacity { get; set; } = 1.0f;
    public bool KeepAspectRatio { get; set; } = true;

    public string LayerDisplayName => Type switch
    {
        AnnotationType.Arrow => "Pfeil",
        AnnotationType.Rectangle => "Rechteck",
        AnnotationType.Oval => "Kreis",
        AnnotationType.StepBadge => $"Schritt {BadgeNumber}",
        AnnotationType.Freehand => "Freihand-Linie",
        AnnotationType.Highlighter => "Textmarker",
        AnnotationType.Text => string.IsNullOrWhiteSpace(Text) ? "Text" : $"Text: \"{(Text.Length > 16 ? Text.Substring(0, 14) + "…" : Text)}\"",
        AnnotationType.Image => string.IsNullOrWhiteSpace(ImageName) ? "Bild-Ebene" : $"🖼️ {ImageName}",
        _ => "Ebene"
    };

    public string VisibilityIcon => IsVisible ? "👁️" : "🚫";

    public Brush LayerColorBrush => Type == AnnotationType.Image
        ? new SolidColorBrush(WpfColor.FromRgb(56, 189, 248))
        : new SolidColorBrush(WpfColor.FromArgb(Color.Alpha, Color.Red, Color.Green, Color.Blue));

    public Brush LayerBgBrush => IsSelected
        ? new SolidColorBrush(WpfColor.FromArgb(40, 56, 189, 248))
        : new SolidColorBrush(WpfColor.FromArgb(0, 0, 0, 0));

    public Brush LayerBorderBrush => IsSelected
        ? new SolidColorBrush(WpfColor.FromArgb(200, 56, 189, 248))
        : new SolidColorBrush(WpfColor.FromArgb(35, 148, 163, 184));

    public EditorAnnotation Clone()
    {
        return new EditorAnnotation
        {
            Id = this.Id,
            IsVisible = this.IsVisible,
            IsSelected = this.IsSelected,
            Type = this.Type,
            StartPoint = this.StartPoint,
            EndPoint = this.EndPoint,
            Rect = this.Rect,
            Points = new List<SKPoint>(this.Points),
            Color = this.Color,
            StrokeWidth = this.StrokeWidth,
            BadgeNumber = this.BadgeNumber,
            Text = this.Text,
            FontSize = this.FontSize,
            BackgroundColor = this.BackgroundColor,
            ImageBitmap = this.ImageBitmap,
            ImageName = this.ImageName,
            Opacity = this.Opacity,
            KeepAspectRatio = this.KeepAspectRatio
        };
    }

    public void MoveBy(float dx, float dy)
    {
        StartPoint = new SKPoint(StartPoint.X + dx, StartPoint.Y + dy);
        EndPoint = new SKPoint(EndPoint.X + dx, EndPoint.Y + dy);
        Rect = new SKRect(Rect.Left + dx, Rect.Top + dy, Rect.Right + dx, Rect.Bottom + dy);
        for (int i = 0; i < Points.Count; i++)
        {
            Points[i] = new SKPoint(Points[i].X + dx, Points[i].Y + dy);
        }
    }

    public static float GetStepBadgeRadius(float strokeWidth)
    {
        return Math.Max(12f, 10f + strokeWidth * 2f);
    }

    public SKRect GetBounds()
    {
        switch (Type)
        {
            case AnnotationType.Arrow:
            case AnnotationType.Highlighter:
                float minX = Math.Min(StartPoint.X, EndPoint.X);
                float minY = Math.Min(StartPoint.Y, EndPoint.Y);
                float maxX = Math.Max(StartPoint.X, EndPoint.X);
                float maxY = Math.Max(StartPoint.Y, EndPoint.Y);
                float pad = Math.Max(StrokeWidth * 2, 10f);
                return new SKRect(minX - pad, minY - pad, maxX + pad, maxY + pad);

            case AnnotationType.Rectangle:
            case AnnotationType.Oval:
                float rPad = StrokeWidth / 2f + 4f;
                return new SKRect(Math.Min(Rect.Left, Rect.Right) - rPad,
                                  Math.Min(Rect.Top, Rect.Bottom) - rPad,
                                  Math.Max(Rect.Left, Rect.Right) + rPad,
                                  Math.Max(Rect.Top, Rect.Bottom) + rPad);

            case AnnotationType.StepBadge:
                float radius = GetStepBadgeRadius(StrokeWidth);
                return new SKRect(StartPoint.X - radius - 4, StartPoint.Y - radius - 4, StartPoint.X + radius + 4, StartPoint.Y + radius + 4);

            case AnnotationType.Text:
                if (string.IsNullOrEmpty(Text)) return new SKRect(StartPoint.X, StartPoint.Y, StartPoint.X, StartPoint.Y);
                using (var paint = new SKPaint
                {
                    TextSize = FontSize,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
                })
                {
                    var textBounds = new SKRect();
                    float advanceWidth = paint.MeasureText(Text, ref textBounds);
                    float textWidth = Math.Max(textBounds.Width, advanceWidth);
                    float padH = 8f;
                    float padV = 5f;
                    return new SKRect(
                        StartPoint.X - padH,
                        StartPoint.Y + textBounds.Top - padV,
                        StartPoint.X + textWidth + padH,
                        StartPoint.Y + Math.Max(textBounds.Bottom, 0) + padV);
                }

            case AnnotationType.Freehand:
                if (Points.Count == 0) return new SKRect(StartPoint.X, StartPoint.Y, StartPoint.X, StartPoint.Y);
                float fMinX = float.MaxValue, fMinY = float.MaxValue, fMaxX = float.MinValue, fMaxY = float.MinValue;
                foreach (var p in Points)
                {
                    if (p.X < fMinX) fMinX = p.X;
                    if (p.Y < fMinY) fMinY = p.Y;
                    if (p.X > fMaxX) fMaxX = p.X;
                    if (p.Y > fMaxY) fMaxY = p.Y;
                }
                float fPad = StrokeWidth + 6;
                return new SKRect(fMinX - fPad, fMinY - fPad, fMaxX + fPad, fMaxY + fPad);

            case AnnotationType.Image:
                return Rect;

            default:
                return SKRect.Empty;
        }
    }

    public bool HitTest(SKPoint p)
    {
        if (!IsVisible) return false;
        var bounds = GetBounds();
        if (!bounds.Contains(p.X, p.Y)) return false;

        switch (Type)
        {
            case AnnotationType.Arrow:
            case AnnotationType.Highlighter:
                return DistanceToSegment(p, StartPoint, EndPoint) <= Math.Max(StrokeWidth + 8, 14f);

            case AnnotationType.Rectangle:
                return bounds.Contains(p.X, p.Y);

            case AnnotationType.Oval:
                return bounds.Contains(p.X, p.Y);

            case AnnotationType.StepBadge:
                float radius = GetStepBadgeRadius(StrokeWidth);
                float dx = p.X - StartPoint.X;
                float dy = p.Y - StartPoint.Y;
                float hitR = radius + 4f;
                return (dx * dx + dy * dy) <= (hitR * hitR);

            case AnnotationType.Text:
                return bounds.Contains(p.X, p.Y);

            case AnnotationType.Freehand:
                for (int i = 0; i < Points.Count - 1; i++)
                {
                    if (DistanceToSegment(p, Points[i], Points[i + 1]) <= Math.Max(StrokeWidth + 8, 12f))
                        return true;
                }
                return bounds.Contains(p.X, p.Y);

            default:
                return bounds.Contains(p.X, p.Y);
        }
    }

    private static float DistanceToSegment(SKPoint p, SKPoint v, SKPoint w)
    {
        float l2 = (w.X - v.X) * (w.X - v.X) + (w.Y - v.Y) * (w.Y - v.Y);
        if (l2 == 0) return (float)Math.Sqrt((p.X - v.X) * (p.X - v.X) + (p.Y - v.Y) * (p.Y - v.Y));
        float t = Math.Max(0, Math.Min(1, ((p.X - v.X) * (w.X - v.X) + (p.Y - v.Y) * (w.Y - v.Y)) / l2));
        SKPoint projection = new SKPoint(v.X + t * (w.X - v.X), v.Y + t * (w.Y - v.Y));
        return (float)Math.Sqrt((p.X - projection.X) * (p.X - projection.X) + (p.Y - projection.Y) * (p.Y - projection.Y));
    }

    public void Render(SKCanvas canvas)
    {
        if (!IsVisible) return;
        switch (Type)
        {
            case AnnotationType.Arrow:
                float dx = EndPoint.X - StartPoint.X;
                float dy = EndPoint.Y - StartPoint.Y;
                float len = MathF.Sqrt(dx * dx + dy * dy);
                if (len < 1f) break;

                float dirX = dx / len;
                float dirY = dy / len;
                float arrowLen = Math.Clamp(StrokeWidth * 4.2f, 16f, 60f);
                float baseOffset = Math.Min(arrowLen * 0.85f, len * 0.8f);
                var baseCenter = new SKPoint(EndPoint.X - baseOffset * dirX, EndPoint.Y - baseOffset * dirY);

                using (var shaftPaint = new SKPaint
                {
                    Color = Color,
                    StrokeWidth = StrokeWidth,
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    IsAntialias = true
                })
                {
                    canvas.DrawLine(StartPoint, baseCenter, shaftPaint);
                }

                float wingAngle = 28f * (MathF.PI / 180f);
                float angle = MathF.Atan2(dy, dx);

                var wing1 = new SKPoint(
                    EndPoint.X - arrowLen * MathF.Cos(angle - wingAngle),
                    EndPoint.Y - arrowLen * MathF.Sin(angle - wingAngle));

                var wing2 = new SKPoint(
                    EndPoint.X - arrowLen * MathF.Cos(angle + wingAngle),
                    EndPoint.Y - arrowLen * MathF.Sin(angle + wingAngle));

                using (var headPaint = new SKPaint
                {
                    Color = Color,
                    Style = SKPaintStyle.Fill,
                    IsAntialias = true
                })
                using (var strokePaint = new SKPaint
                {
                    Color = Color,
                    StrokeWidth = Math.Max(1.5f, StrokeWidth * 0.35f),
                    Style = SKPaintStyle.Stroke,
                    StrokeJoin = SKStrokeJoin.Miter,
                    StrokeMiter = 4f,
                    IsAntialias = true
                })
                using (var path = new SKPath())
                {
                    path.MoveTo(EndPoint);
                    path.LineTo(wing1);
                    path.LineTo(baseCenter);
                    path.LineTo(wing2);
                    path.Close();

                    canvas.DrawPath(path, headPaint);
                    canvas.DrawPath(path, strokePaint);
                }
                break;

            case AnnotationType.Rectangle:
                using (var paint = new SKPaint
                {
                    Color = Color,
                    StrokeWidth = StrokeWidth,
                    Style = SKPaintStyle.Stroke,
                    IsAntialias = true
                })
                {
                    canvas.DrawRoundRect(Rect, 4f, 4f, paint);
                }
                break;

            case AnnotationType.Oval:
                {
                    using var ovalPaint = new SKPaint
                    {
                        Color = Color,
                        StrokeWidth = StrokeWidth,
                        Style = SKPaintStyle.Stroke,
                        IsAntialias = true
                    };
                    canvas.DrawOval(Rect, ovalPaint);
                    break;
                }

            case AnnotationType.StepBadge:
                {
                    float radius = GetStepBadgeRadius(StrokeWidth);
                    using (var shadowPaint = new SKPaint
                    {
                        Color = new SKColor(0, 0, 0, 100),
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true
                    })
                    {
                        canvas.DrawCircle(StartPoint.X, StartPoint.Y + 1.5f, radius + 1f, shadowPaint);
                    }

                    using (var circlePaint = new SKPaint
                    {
                        Color = Color,
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true
                    })
                    {
                        canvas.DrawCircle(StartPoint, radius, circlePaint);
                    }

                    float lum = (0.2126f * Color.Red + 0.7152f * Color.Green + 0.0722f * Color.Blue) / 255f;
                    bool isLight = lum > 0.55f;
                    SKColor textColor = isLight ? new SKColor(15, 23, 42) : SKColors.White;
                    SKColor borderColor = isLight ? new SKColor(15, 23, 42, 160) : SKColors.White;

                    float borderW = Math.Max(1.5f, radius * 0.08f);
                    using (var borderPaint = new SKPaint
                    {
                        Color = borderColor,
                        StrokeWidth = borderW,
                        Style = SKPaintStyle.Stroke,
                        IsAntialias = true
                    })
                    {
                        canvas.DrawCircle(StartPoint, radius, borderPaint);
                    }

                    using (var textPaint = new SKPaint
                    {
                        Color = textColor,
                        TextSize = radius * 1.15f,
                        IsAntialias = true,
                        TextAlign = SKTextAlign.Center,
                        Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
                    })
                    {
                        var textBounds = new SKRect();
                        string numStr = BadgeNumber.ToString();
                        textPaint.MeasureText(numStr, ref textBounds);
                        float textY = StartPoint.Y - textBounds.MidY;
                        canvas.DrawText(numStr, StartPoint.X, textY, textPaint);
                    }
                    break;
                }

            case AnnotationType.Highlighter:
                {
                    var highlightColor = new SKColor(Color.Red, Color.Green, Color.Blue, 115);
                    using (var hlPaint = new SKPaint
                    {
                        Color = highlightColor,
                        StrokeWidth = StrokeWidth > 0 ? StrokeWidth : 24f,
                        Style = SKPaintStyle.Stroke,
                        StrokeCap = SKStrokeCap.Round,
                        BlendMode = SKBlendMode.SrcOver,
                        IsAntialias = true
                    })
                    {
                        canvas.DrawLine(StartPoint, EndPoint, hlPaint);
                    }
                    break;
                }

            case AnnotationType.Text:
                if (string.IsNullOrEmpty(Text)) return;
                using (var textPaint = new SKPaint
                {
                    Color = Color,
                    TextSize = FontSize,
                    IsAntialias = true,
                    Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
                })
                {
                    var bounds = new SKRect();
                    float advanceWidth = textPaint.MeasureText(Text, ref bounds);
                    float textWidth = Math.Max(bounds.Width, advanceWidth);

                    using var bgPaint = new SKPaint
                    {
                        Color = BackgroundColor,
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true
                    };
                    float padH = 8f;
                    float padV = 5f;
                    var bgRect = new SKRect(
                        StartPoint.X - padH,
                        StartPoint.Y + bounds.Top - padV,
                        StartPoint.X + textWidth + padH,
                        StartPoint.Y + Math.Max(bounds.Bottom, 0) + padV);

                    canvas.DrawRoundRect(bgRect, 4f, 4f, bgPaint);
                    canvas.DrawText(Text, StartPoint.X, StartPoint.Y, textPaint);
                }
                break;

            case AnnotationType.Freehand:
                if (Points == null || Points.Count < 2) return;
                using (var paint = new SKPaint
                {
                    Color = Color,
                    StrokeWidth = StrokeWidth,
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round,
                    IsAntialias = true
                })
                {
                    using var path = new SKPath();
                    path.MoveTo(Points[0]);
                    for (int i = 1; i < Points.Count; i++)
                    {
                        path.LineTo(Points[i]);
                    }
                    canvas.DrawPath(path, paint);
                }
                break;

            case AnnotationType.Image:
                if (ImageBitmap != null && Rect.Width > 0 && Rect.Height > 0)
                {
                    using var imgPaint = new SKPaint
                    {
                        IsAntialias = true,
                        FilterQuality = SKFilterQuality.High,
                        Color = new SKColor(255, 255, 255, (byte)(Math.Clamp(Opacity, 0f, 1f) * 255))
                    };
                    canvas.DrawBitmap(ImageBitmap, Rect, imgPaint);
                }
                break;
        }
    }
}
