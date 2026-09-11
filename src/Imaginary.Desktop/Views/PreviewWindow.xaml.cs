using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Imaginary.Desktop.Models;

namespace Imaginary.Desktop.Views;

public partial class PreviewWindow : Window
{
    private readonly ScaleTransform _syncScale = new(1.0, 1.0);
    private readonly TranslateTransform _syncTranslate = new(0.0, 0.0);
    private Point _panStartPoint;
    private Point _startTranslate;
    private bool _isPanning;
    private bool _isDraggingSplit;

    public PreviewWindow()
    {
        InitializeComponent();

        var group = new TransformGroup();
        group.Children.Add(_syncScale);
        group.Children.Add(_syncTranslate);

        ImageOriginalSplit.RenderTransform = group;
        ImageProcessedSplit.RenderTransform = group;
        ImageOriginalSide.RenderTransform = group;
        ImageProcessedSide.RenderTransform = group;

        UpdateCursor();
    }

    public void LoadComparison(FileItemViewModel item)
    {
        TextFileName.Text = item.FileName;
        TextDetails.Text = $"Original: {item.OriginalSizeFormatted} ({item.OriginalDimensionsFormatted})";

        if (File.Exists(item.FilePath))
        {
            try
            {
                var origBmp = LoadBitmapSafe(item.FilePath);
                ImageOriginalSplit.Source = origBmp;
                ImageOriginalSide.Source = origBmp;
            }
            catch
            {
                // Fallback
            }
        }

        if (!string.IsNullOrEmpty(item.TargetPath) && File.Exists(item.TargetPath))
        {
            try
            {
                var procBmp = LoadBitmapSafe(item.TargetPath);
                ImageProcessedSplit.Source = procBmp;
                ImageProcessedSide.Source = procBmp;

                TextDetails.Text = $"Original: {item.OriginalSizeFormatted} ({item.OriginalDimensionsFormatted})  ➔  Konvertiert: {item.FinalSizeFormatted} ({item.FinalDimensionsFormatted})";
                TextSavings.Text = $"Ersparnis: {item.SavingsFormatted}";
            }
            catch
            {
                // Fallback
            }
        }
        else
        {
            ImageProcessedSplit.Source = ImageOriginalSplit.Source;
            ImageProcessedSide.Source = ImageOriginalSide.Source;
            TextSavings.Text = "Noch nicht konvertiert";
        }

        UpdateSplitClip();
    }

    private static BitmapImage LoadBitmapSafe(string path)
    {
        var bi = new BitmapImage();
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.StreamSource = stream;
            bi.EndInit();
        }
        bi.Freeze();
        return bi;
    }

    private void OnSplitSliderValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSplitClip();
    }

    private void OnContainerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSplitClip();
    }

    private void UpdateSplitClip()
    {
        if (SplitViewGrid == null || SplitClipGeometry == null || SplitLine == null) return;

        double width = SplitViewGrid.ActualWidth;
        double height = SplitViewGrid.ActualHeight;
        if (width <= 0 || height <= 0) return;

        double splitX = (SplitSlider.Value / 100.0) * width;

        // Clip OriginalContainer on the left of the split line (0 to splitX)
        if (OriginalClipGeometry != null)
        {
            OriginalClipGeometry.Rect = new Rect(0, 0, Math.Max(0, splitX), height);
        }

        // Clip ProcessedContainer: shows processed image on the right of the split line
        SplitClipGeometry.Rect = new Rect(splitX, 0, Math.Max(0, width - splitX), height);

        // Position vertical line exactly at splitX
        SplitLine.X1 = splitX;
        SplitLine.X2 = splitX;
        SplitLine.Y1 = 0;
        SplitLine.Y2 = height;

        // Position circular handle centered on the line
        if (SplitHandle != null)
        {
            Canvas.SetLeft(SplitHandle, splitX - (SplitHandle.ActualWidth > 0 ? SplitHandle.ActualWidth / 2 : 18));
            Canvas.SetTop(SplitHandle, (height / 2) - (SplitHandle.ActualHeight > 0 ? SplitHandle.ActualHeight / 2 : 18));
        }
    }

    private void OnImageAreaMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            _isDraggingSplit = true;
            SplitViewGrid.CaptureMouse();
            UpdateSliderFromMouse(e.GetPosition(SplitViewGrid).X);
            e.Handled = true;
        }
    }

    private void OnImageAreaMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingSplit && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateSliderFromMouse(e.GetPosition(SplitViewGrid).X);
            e.Handled = true;
        }
    }

    private void OnImageAreaMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingSplit)
        {
            _isDraggingSplit = false;
            SplitViewGrid.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    private void UpdateSliderFromMouse(double mouseX)
    {
        double width = SplitViewGrid.ActualWidth;
        if (width <= 0) return;

        double clampedX = Math.Clamp(mouseX, 0, width);
        double percent = (clampedX / width) * 100.0;
        SplitSlider.Value = percent;
    }

    private void OnViewModeChanged(object sender, RoutedEventArgs e)
    {
        if (SplitViewGrid == null || SideBySideGrid == null) return;

        ResetZoom();

        if (RadioSplit.IsChecked == true)
        {
            SplitViewGrid.Visibility = Visibility.Visible;
            SideBySideGrid.Visibility = Visibility.Collapsed;
            UpdateSplitClip();
            if (TextHint != null)
            {
                TextHint.Text = "Ziehen Sie den Slider oder klicken Sie ins Bild, um Details zu vergleichen. Mausrad: Zoom, Rechtsklick: Pan.";
            }
        }
        else
        {
            SplitViewGrid.Visibility = Visibility.Collapsed;
            SideBySideGrid.Visibility = Visibility.Visible;
            if (ColOriginal != null && ColProcessed != null)
            {
                ColOriginal.Width = new GridLength(1, GridUnitType.Star);
                ColProcessed.Width = new GridLength(1, GridUnitType.Star);
            }
            if (TextHint != null)
            {
                TextHint.Text = "Mausrad: Paralleler Zoom beider Bilder • Linksklick/Rechtsklick: Pan • Doppelklick: Reset";
            }
        }
        UpdateCursor();
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnContainerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.2 : (1.0 / 1.2);
        double targetScale = Math.Clamp(_syncScale.ScaleX * factor, 1.0, 10.0);

        if (Math.Abs(targetScale - 1.0) < 0.001)
        {
            ResetZoom();
            e.Handled = true;
            return;
        }

        double actualFactor = targetScale / _syncScale.ScaleX;

        Point mousePos;

        if (RadioSideBySide?.IsChecked == true && BorderOriginalSide != null && BorderProcessedSide != null)
        {
            Point posOriginal = e.GetPosition(BorderOriginalSide);
            Point posProcessed = e.GetPosition(BorderProcessedSide);

            double origW = BorderOriginalSide.ActualWidth > 0 ? BorderOriginalSide.ActualWidth : 500;
            double origH = BorderOriginalSide.ActualHeight > 0 ? BorderOriginalSide.ActualHeight : 400;
            double procW = BorderProcessedSide.ActualWidth > 0 ? BorderProcessedSide.ActualWidth : 500;
            double procH = BorderProcessedSide.ActualHeight > 0 ? BorderProcessedSide.ActualHeight : 400;

            bool overOriginal = posOriginal.X >= 0 && posOriginal.X <= origW &&
                                 posOriginal.Y >= 0 && posOriginal.Y <= origH;
            bool overProcessed = posProcessed.X >= 0 && posProcessed.X <= procW &&
                                  posProcessed.Y >= 0 && posProcessed.Y <= procH;

            if (overOriginal)
            {
                mousePos = posOriginal;
            }
            else if (overProcessed)
            {
                mousePos = posProcessed;
            }
            else
            {
                mousePos = new Point(origW / 2.0, origH / 2.0);
            }
        }
        else
        {
            mousePos = e.GetPosition(SplitViewGrid ?? (IInputElement)sender);
        }

        _syncTranslate.X = mousePos.X - (mousePos.X - _syncTranslate.X) * actualFactor;
        _syncTranslate.Y = mousePos.Y - (mousePos.Y - _syncTranslate.Y) * actualFactor;

        _syncScale.ScaleX = targetScale;
        _syncScale.ScaleY = targetScale;

        UpdateZoomBadge();
        UpdateCursor();
        e.Handled = true;
    }

    private void OnContainerMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ResetZoom();
            e.Handled = true;
            return;
        }

        bool isSideBySide = RadioSideBySide?.IsChecked == true;
        bool isPanButton = e.RightButton == MouseButtonState.Pressed ||
                           e.MiddleButton == MouseButtonState.Pressed ||
                           (isSideBySide && e.LeftButton == MouseButtonState.Pressed);

        if (isPanButton && _syncScale.ScaleX > 1.001)
        {
            _isPanning = true;
            _panStartPoint = e.GetPosition(this);
            _startTranslate = new Point(_syncTranslate.X, _syncTranslate.Y);
            ((IInputElement)sender).CaptureMouse();
            Mouse.OverrideCursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    private void OnContainerMouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            var current = e.GetPosition(this);
            var delta = current - _panStartPoint;
            _syncTranslate.X = _startTranslate.X + delta.X;
            _syncTranslate.Y = _startTranslate.Y + delta.Y;
            e.Handled = true;
        }
    }

    private void OnContainerMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            ((IInputElement)sender).ReleaseMouseCapture();
            Mouse.OverrideCursor = null;
            UpdateCursor();
            e.Handled = true;
        }
    }

    private void OnContainerLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            Mouse.OverrideCursor = null;
            UpdateCursor();
        }
    }

    private void OnResetZoomClicked(object sender, MouseButtonEventArgs e)
    {
        ResetZoom();
    }

    private void ResetZoom()
    {
        _syncScale.ScaleX = 1.0;
        _syncScale.ScaleY = 1.0;
        _syncTranslate.X = 0.0;
        _syncTranslate.Y = 0.0;
        UpdateZoomBadge();
        UpdateCursor();
    }

    private void UpdateZoomBadge()
    {
        int pct = (int)Math.Round(_syncScale.ScaleX * 100.0);
        TextZoomLevel.Text = $"{pct}%";
    }

    private void UpdateCursor()
    {
        if (RadioSideBySide?.IsChecked == true)
        {
            if (SideBySideGrid != null)
            {
                SideBySideGrid.Cursor = _syncScale.ScaleX > 1.001 ? Cursors.SizeAll : Cursors.Arrow;
            }
        }
        else
        {
            if (SplitViewGrid != null)
            {
                SplitViewGrid.Cursor = Cursors.SizeWE;
            }
        }
    }
}
