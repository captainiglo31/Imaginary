using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Imaginary.Desktop.Models;

namespace Imaginary.Desktop.Views;

public partial class PreviewWindow : Window
{
    private bool _isDraggingSplit;

    public PreviewWindow()
    {
        InitializeComponent();
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
}
