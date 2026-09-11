using System;
using System.Collections.Generic;
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
    private List<SKPoint> _activeFreehandPoints = new();

    private class EditorHistoryState : IDisposable
    {
        public SKBitmap BaseBitmap { get; set; } = null!;
        public List<EditorAnnotation> Annotations { get; set; } = new();
        public int StepBadgeCounter { get; set; }

        public void Dispose()
        {
            BaseBitmap?.Dispose();
        }
    }

    private readonly Stack<EditorHistoryState> _undoStack = new();
    private readonly Stack<EditorHistoryState> _redoStack = new();

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

        if (_currentBitmap == null)
        {
            throw new InvalidOperationException($"Das Bild '{Path.GetFileName(filePath)}' konnte nicht geladen werden (nicht unterstütztes oder beschädigtes Format).");
        }

        _originalBitmap = _currentBitmap.Copy();

        UpdateImageDisplay();
        UpdateUndoRedoButtons();
        UpdateAiModelStatus();

        ToolPan.IsChecked = true;
        if (ComboBgMode != null)
        {
            ComboBgMode.SelectedIndex = 0;
        }
    }

    private void UpdateImageDisplay()
    {
        if (_currentBitmap == null) return;
        TextDimensions.Text = $"{_currentBitmap.Width} × {_currentBitmap.Height} px";

        using var displayBitmap = _currentBitmap.Copy();
        using (var canvas = new SKCanvas(displayBitmap))
        {
            foreach (var ann in _annotations)
            {
                ann.Render(canvas);
            }

            if (_selectedAnnotation != null && _currentTool == EditorTool.Pan)
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
                DrawHandle(canvas, bounds.Left - 4, bounds.Top - 4, handlePaint, handleBorder);
                DrawHandle(canvas, bounds.Right + 4, bounds.Top - 4, handlePaint, handleBorder);
                DrawHandle(canvas, bounds.Left - 4, bounds.Bottom + 4, handlePaint, handleBorder);
                DrawHandle(canvas, bounds.Right + 4, bounds.Bottom + 4, handlePaint, handleBorder);
            }
        }

        using var image = SKImage.FromBitmap(displayBitmap);
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

    private void PushUndoState()
    {
        _undoStack.Push(new EditorHistoryState
        {
            BaseBitmap = _currentBitmap.Copy(),
            Annotations = _annotations.Select(a => a.Clone()).ToList(),
            StepBadgeCounter = _stepBadgeCounter
        });
        while (_redoStack.Count > 0)
        {
            var r = _redoStack.Pop();
            r.BaseBitmap.Dispose();
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

        _redoStack.Push(new EditorHistoryState
        {
            BaseBitmap = _currentBitmap.Copy(),
            Annotations = _annotations.Select(a => a.Clone()).ToList(),
            StepBadgeCounter = _stepBadgeCounter
        });

        var state = _undoStack.Pop();
        _currentBitmap.Dispose();
        _currentBitmap = state.BaseBitmap;
        _annotations = state.Annotations;
        _stepBadgeCounter = state.StepBadgeCounter;
        _selectedAnnotation = null;

        UpdateImageDisplay();
        UpdateUndoRedoButtons();
        _interactiveBrushPoints.Clear();
        _interactiveBoundingBox = SKRectI.Empty;
        InteractiveStrokeCanvas?.Children.Clear();
    }

    private void OnRedoClicked(object sender, RoutedEventArgs e)
    {
        if (_redoStack.Count == 0) return;

        _undoStack.Push(new EditorHistoryState
        {
            BaseBitmap = _currentBitmap.Copy(),
            Annotations = _annotations.Select(a => a.Clone()).ToList(),
            StepBadgeCounter = _stepBadgeCounter
        });

        var state = _redoStack.Pop();
        _currentBitmap.Dispose();
        _currentBitmap = state.BaseBitmap;
        _annotations = state.Annotations;
        _stepBadgeCounter = state.StepBadgeCounter;
        _selectedAnnotation = null;

        UpdateImageDisplay();
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
        if (SidePanel != null) SidePanel.Visibility = _currentTool == EditorTool.BackgroundRemoval ? Visibility.Visible : Visibility.Collapsed;

        if (TextStatusHint != null)
        {
            TextStatusHint.Text = _currentTool switch
            {
                EditorTool.Pan => "✋ Verschieben: Platziertes Element anklicken & verschieben (Entf = Löschen) • Freie Fläche ziehen zum Verschieben • Mausrad zum Zoomen.",
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
                EditorTool.BackgroundRemoval => "🪄 Hintergrund: Rechts Methode (Farbe, KI-Vollbild oder KI-Pinsel) wählen • Mausrad zum Zoomen.",
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

    private void OnStrokeWidthChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ComboStrokeWidth?.SelectedItem is ComboBoxItem item && float.TryParse(item.Tag?.ToString(), out float w))
        {
            _strokeWidth = w;
        }
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
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
            // Check if user clicked an existing annotation to move it
            EditorAnnotation? hit = null;
            for (int i = _annotations.Count - 1; i >= 0; i--)
            {
                if (_annotations[i].HitTest(pt))
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
                UpdateImageDisplay();
                TextStatusHint.Text = "Element ausgewählt. Ziehen zum Verschieben • Entf zum Löschen.";
            }
            else
            {
                if (_selectedAnnotation != null)
                {
                    _selectedAnnotation = null;
                    UpdateImageDisplay();
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
                    PushUndoState();
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

        if (_currentTool == EditorTool.StepBadge)
        {
            PushUndoState();
            var badge = new EditorAnnotation
            {
                Type = AnnotationType.StepBadge,
                StartPoint = pt,
                Color = _selectedColor,
                BadgeNumber = _stepBadgeCounter++
            };
            _annotations.Add(badge);
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
        var current = e.GetPosition(BaseImage);
        var currentPt = new SKPoint((float)current.X, (float)current.Y);

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
                    UpdateImageDisplay();
                }
                return;
            }

            if (_isPanningCanvas)
            {
                var currentScrollPos = e.GetPosition(CanvasScrollViewer);
                double deltaX = currentScrollPos.X - _panMouseStart.X;
                double deltaY = currentScrollPos.Y - _panMouseStart.Y;
                CanvasScrollViewer.ScrollToHorizontalOffset(_panScrollStart.X - deltaX);
                CanvasScrollViewer.ScrollToVerticalOffset(_panScrollStart.Y - deltaY);
                return;
            }

            // Hover cursor indication in Pan mode
            if (!_isDragging && !_isMovingAnnotation && !_isPanningCanvas)
            {
                bool overElement = false;
                for (int i = _annotations.Count - 1; i >= 0; i--)
                {
                    if (_annotations[i].HitTest(currentPt))
                    {
                        overElement = true;
                        break;
                    }
                }
                CanvasContainer.Cursor = overElement ? Cursors.SizeAll : Cursors.Hand;
            }
            return;
        }

        if (_currentTool == EditorTool.BackgroundRemoval)
        {
            if (ComboBgMode?.SelectedIndex == 2)
            {
                CanvasContainer.Cursor = Cursors.Pen;
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
            return;
        }

        if (!_isDragging) return;

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
            case EditorTool.Highlighter:
                PreviewLine.Visibility = Visibility.Visible;
                PreviewLine.X1 = _dragStart.X;
                PreviewLine.Y1 = _dragStart.Y;
                PreviewLine.X2 = current.X;
                PreviewLine.Y2 = current.Y;
                PreviewLine.Stroke = new SolidColorBrush(WpfColor.FromRgb(_selectedColor.Red, _selectedColor.Green, _selectedColor.Blue));
                PreviewLine.StrokeThickness = _currentTool == EditorTool.Highlighter ? 24 : _strokeWidth;
                break;

            case EditorTool.Pen:
                _activeFreehandPoints.Add(currentPt);
                PreviewPolyline.Points.Add(new WpfPoint(current.X, current.Y));
                break;
        }
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_currentTool == EditorTool.Pan)
        {
            if (_isMovingAnnotation)
            {
                _isMovingAnnotation = false;
                CanvasContainer.ReleaseMouseCapture();
            }
            if (_isPanningCanvas)
            {
                _isPanningCanvas = false;
                CanvasContainer.ReleaseMouseCapture();
            }
            return;
        }

        if (_currentTool == EditorTool.BackgroundRemoval)
        {
            if (_isDrawingInteractiveStroke)
            {
                _isDrawingInteractiveStroke = false;
                CanvasContainer.ReleaseMouseCapture();
            }
            return;
        }

        if (!_isDragging) return;

        _isDragging = false;
        CanvasContainer.ReleaseMouseCapture();

        PreviewRect.Visibility = Visibility.Collapsed;
        PreviewEllipse.Visibility = Visibility.Collapsed;
        PreviewLine.Visibility = Visibility.Collapsed;
        PreviewPolyline.Visibility = Visibility.Collapsed;
        PreviewPolyline.Points.Clear();

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
            }
            return;
        }

        if (w < 4 && h < 4) return;

        PushUndoState();

        switch (_currentTool)
        {
            case EditorTool.Pixelate:
                int pixelSize = (int)SliderPixelSize.Value;
                var pixUpdated = _editorService.ApplyPixelate(_currentBitmap, new SKRectI(x1, y1, x2, y2), pixelSize);
                _currentBitmap.Dispose();
                _currentBitmap = pixUpdated;
                UpdateImageDisplay();
                break;

            case EditorTool.Blur:
                var blurUpdated = _editorService.ApplyGaussianBlur(_currentBitmap, new SKRectI(x1, y1, x2, y2), sigma: 14f);
                _currentBitmap.Dispose();
                _currentBitmap = blurUpdated;
                UpdateImageDisplay();
                break;

            case EditorTool.Blackout:
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
                break;
        }
    }

    private void OnApplyCropClicked(object sender, RoutedEventArgs e)
    {
        if (_activeCropRect.HasValue)
        {
            PushUndoState();
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
        // Aspect ratio handling for crop
    }

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
        var mousePos = e.GetPosition(CanvasScrollViewer);
        double oldScale = CanvasScaleTransform.ScaleX;
        double newScale = Math.Clamp(oldScale * factor, 0.1, 8.0);

        if (Math.Abs(newScale - oldScale) > 0.001)
        {
            double relX = (mousePos.X + CanvasScrollViewer.HorizontalOffset) / oldScale;
            double relY = (mousePos.Y + CanvasScrollViewer.VerticalOffset) / oldScale;

            SetZoom(newScale);

            CanvasScrollViewer.ScrollToHorizontalOffset(relX * newScale - mousePos.X);
            CanvasScrollViewer.ScrollToVerticalOffset(relY * newScale - mousePos.Y);
        }
        e.Handled = true;
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
        if (ComboBgMode == null || PanelColorKeyOptions == null || PanelAiOptions == null) return;
        int idx = ComboBgMode.SelectedIndex;
        PanelColorKeyOptions.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
        PanelAiOptions.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (PanelInteractiveAiOptions != null)
            PanelInteractiveAiOptions.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;

        if (TextStatusHint != null)
        {
            TextStatusHint.Text = idx switch
            {
                0 => "Farbe: Wähle mit der Pipette eine Hintergrundfarbe zum Entfernen.",
                1 => "KI Vollbild: Segmentiert Personen, Tiere und Hauptobjekte vollautomatisch.",
                2 => "KI Pinsel: Male über ein Objekt, um es gezielt durch KI auszuschneiden, oder nutze Korrektur-Pinsel.",
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
        PushUndoState();
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

    private void OnInteractiveBrushModeChanged(object sender, RoutedEventArgs e)
    {
        if (PanelCutoutButtons == null) return;
        bool isSelect = RadioBrushSelectObject?.IsChecked == true;
        PanelCutoutButtons.Visibility = isSelect ? Visibility.Visible : Visibility.Collapsed;

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

    private void AddInteractiveBrushPoint(SKPoint pt)
    {
        _interactiveBrushPoints.Add(pt);
        float radius = SliderInteractiveBrushSize != null ? (float)SliderInteractiveBrushSize.Value : 32f;

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
        if (_originalBitmap == null || _currentBitmap == null) return;
        float radius = SliderInteractiveBrushSize != null ? (float)SliderInteractiveBrushSize.Value : 32f;
        bool restore = RadioBrushRestore?.IsChecked == true;

        _bgRemovalService.ApplyMaskBrush(_currentBitmap, _originalBitmap, pt, radius, restore);
        UpdateImageDisplay();
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
        if (!_bgRemovalService.IsAiModelDownloaded())
        {
            MessageBox.Show(this, "Das KI-Modell wurde noch nicht heruntergeladen. Bitte zuerst auf 'KI-Modell herunterladen' klicken.", "Modell erforderlich", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_interactiveBrushPoints.Count == 0 || _interactiveBoundingBox.IsEmpty)
        {
            MessageBox.Show(this, "Bitte malen Sie zuerst mit dem Pinsel über das gewünschte Objekt.", "Keine Objektauswahl", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            if (ButtonExecuteInteractiveAiCutout != null) ButtonExecuteInteractiveAiCutout.IsEnabled = false;
            if (TextStatusHint != null) TextStatusHint.Text = "🧠 KI isoliert das markierte Objekt und schneidet es aus...";

            PushUndoState();
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
            MessageBox.Show(this, "Fehler beim Freistellen des Objekts: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (ButtonExecuteInteractiveAiCutout != null) ButtonExecuteInteractiveAiCutout.IsEnabled = true;
        }
    }

    // Saving & Actions
    private void OnSaveAndReplaceClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            using var final = GetFinalBitmap();
            SaveBitmapToFile(final, _filePath);
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

            using var final = GetFinalBitmap();
            SaveBitmapToFile(final, copyPath);
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
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    protected override void OnClosed(EventArgs e)
    {
        _currentBitmap?.Dispose();
        _originalBitmap?.Dispose();
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

public enum AnnotationType
{
    Arrow,
    Rectangle,
    Oval,
    StepBadge,
    Freehand,
    Highlighter,
    Text
}

public class EditorAnnotation
{
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

    public EditorAnnotation Clone()
    {
        return new EditorAnnotation
        {
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
            BackgroundColor = this.BackgroundColor
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
                float radius = 18f;
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

            default:
                return SKRect.Empty;
        }
    }

    public bool HitTest(SKPoint p)
    {
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
                float dx = p.X - StartPoint.X;
                float dy = p.Y - StartPoint.Y;
                return (dx * dx + dy * dy) <= (22f * 22f);

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
        switch (Type)
        {
            case AnnotationType.Arrow:
                using (var paint = new SKPaint
                {
                    Color = Color,
                    StrokeWidth = StrokeWidth,
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    IsAntialias = true
                })
                {
                    canvas.DrawLine(StartPoint, EndPoint, paint);
                    float dx = EndPoint.X - StartPoint.X;
                    float dy = EndPoint.Y - StartPoint.Y;
                    float angle = MathF.Atan2(dy, dx);
                    float arrowLen = Math.Max(16f, StrokeWidth * 4f);
                    float wingAngle = 28f * (MathF.PI / 180f);

                    var wing1 = new SKPoint(
                        EndPoint.X - arrowLen * MathF.Cos(angle - wingAngle),
                        EndPoint.Y - arrowLen * MathF.Sin(angle - wingAngle));

                    var wing2 = new SKPoint(
                        EndPoint.X - arrowLen * MathF.Cos(angle + wingAngle),
                        EndPoint.Y - arrowLen * MathF.Sin(angle + wingAngle));

                    using var headPaint = new SKPaint
                    {
                        Color = Color,
                        Style = SKPaintStyle.Fill,
                        IsAntialias = true
                    };

                    using var path = new SKPath();
                    path.MoveTo(EndPoint);
                    path.LineTo(wing1);
                    path.LineTo(wing2);
                    path.Close();

                    canvas.DrawPath(path, headPaint);
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
                    float radius = 18f;
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

                    using (var borderPaint = new SKPaint
                    {
                        Color = borderColor,
                        StrokeWidth = 2f,
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
        }
    }
}
