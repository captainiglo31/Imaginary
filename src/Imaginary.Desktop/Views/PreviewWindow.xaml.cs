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
        }
    }

    private void OnImageAreaMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingSplit && e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateSliderFromMouse(e.GetPosition(SplitViewGrid).X);
        }
    }

    private void OnImageAreaMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingSplit)
        {
            _isDraggingSplit = false;
            SplitViewGrid.ReleaseMouseCapture();
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

        if (RadioSplit.IsChecked == true)
        {
            SplitViewGrid.Visibility = Visibility.Visible;
            SideBySideGrid.Visibility = Visibility.Collapsed;
            UpdateSplitClip();
        }
        else
        {
            SplitViewGrid.Visibility = Visibility.Collapsed;
            SideBySideGrid.Visibility = Visibility.Visible;
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnContainerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        double factor = e.Delta > 0 ? 1.2 : (1.0 / 1.2);
        double targetScale = Math.Clamp(_syncScale.ScaleX * factor, 0.5, 8.0);
        double actualFactor = targetScale / _syncScale.ScaleX;

        Point mousePos = e.GetPosition((IInputElement)sender);

        _syncTranslate.X = mousePos.X - (mousePos.X - _syncTranslate.X) * actualFactor;
        _syncTranslate.Y = mousePos.Y - (mousePos.Y - _syncTranslate.Y) * actualFactor;

        _syncScale.ScaleX = targetScale;
        _syncScale.ScaleY = targetScale;

        UpdateZoomBadge();
        e.Handled = true;
    }

    private void OnContainerMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.RightButton == MouseButtonState.Pressed || e.MiddleButton == MouseButtonState.Pressed)
        {
            _isPanning = true;
            _panStartPoint = e.GetPosition(this);
            _startTranslate = new Point(_syncTranslate.X, _syncTranslate.Y);
            ((IInputElement)sender).CaptureMouse();
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
        }
    }

    private void OnContainerMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            ((IInputElement)sender).ReleaseMouseCapture();
        }
    }

    private void OnResetZoomClicked(object sender, MouseButtonEventArgs e)
    {
        _syncScale.ScaleX = 1.0;
        _syncScale.ScaleY = 1.0;
        _syncTranslate.X = 0.0;
        _syncTranslate.Y = 0.0;
        UpdateZoomBadge();
    }

    private void UpdateZoomBadge()
    {
        int pct = (int)Math.Round(_syncScale.ScaleX * 100.0);
        TextZoomLevel.Text = $"{pct}%";
    }
}
