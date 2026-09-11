using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GdiBitmap = System.Drawing.Bitmap;
using GdiRectangle = System.Drawing.Rectangle;

namespace Imaginary.Desktop.Views;

public partial class ScreenCaptureOverlayWindow : Window
{
    private readonly GdiBitmap _fullBitmap;
    private Point _startPoint;
    private bool _isSelecting;

    public GdiBitmap? ResultBitmap { get; private set; }

    public ScreenCaptureOverlayWindow(GdiBitmap fullBitmap, BitmapSource wpfBitmapSource)
    {
        InitializeComponent();

        _fullBitmap = fullBitmap ?? throw new ArgumentNullException(nameof(fullBitmap));

        // Position across entire virtual screen (all monitors)
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        BackgroundImage.Source = wpfBitmapSource;

        Loaded += (s, e) =>
        {
            FullShadingGeometry.Rect = new Rect(0, 0, ActualWidth, ActualHeight);
            SelectionExcludeGeometry.Rect = Rect.Empty;
            Focus();
        };
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            ResultBitmap = null;
            DialogResult = false;
            Close();
        }
        else if (e.Key == Key.Enter)
        {
            // Full screen capture
            ResultBitmap = (GdiBitmap)_fullBitmap.Clone();
            DialogResult = true;
            Close();
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _startPoint = e.GetPosition(this);
        _isSelecting = true;
        CaptureMouse();

        UpdateSelection(new Rect(_startPoint, _startPoint));
        SelectionBorder.Visibility = Visibility.Visible;
        DimensionBadge.Visibility = Visibility.Visible;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isSelecting) return;

        var current = e.GetPosition(this);
        double x = Math.Min(_startPoint.X, current.X);
        double y = Math.Min(_startPoint.Y, current.Y);
        double w = Math.Abs(_startPoint.X - current.X);
        double h = Math.Abs(_startPoint.Y - current.Y);

        UpdateSelection(new Rect(x, y, w, h));
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isSelecting) return;

        _isSelecting = false;
        ReleaseMouseCapture();

        var current = e.GetPosition(this);
        double x = Math.Min(_startPoint.X, current.X);
        double y = Math.Min(_startPoint.Y, current.Y);
        double w = Math.Abs(_startPoint.X - current.X);
        double h = Math.Abs(_startPoint.Y - current.Y);

        if (w >= 10 && h >= 10)
        {
            // Convert DIPs to physical pixels considering display scaling
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            int pixelX = (int)Math.Round(x * dpiX);
            int pixelY = (int)Math.Round(y * dpiY);
            int pixelW = (int)Math.Round(w * dpiX);
            int pixelH = (int)Math.Round(h * dpiY);

            // Clamp bounds safely inside full screenshot
            pixelX = Math.Clamp(pixelX, 0, _fullBitmap.Width - 1);
            pixelY = Math.Clamp(pixelY, 0, _fullBitmap.Height - 1);
            pixelW = Math.Clamp(pixelW, 1, _fullBitmap.Width - pixelX);
            pixelH = Math.Clamp(pixelH, 1, _fullBitmap.Height - pixelY);

            var cropRect = new GdiRectangle(pixelX, pixelY, pixelW, pixelH);
            ResultBitmap = _fullBitmap.Clone(cropRect, _fullBitmap.PixelFormat);
            DialogResult = true;
            Close();
        }
        else
        {
            // Too small: reset selection
            SelectionBorder.Visibility = Visibility.Collapsed;
            DimensionBadge.Visibility = Visibility.Collapsed;
            SelectionExcludeGeometry.Rect = Rect.Empty;
        }
    }

    private void UpdateSelection(Rect rect)
    {
        SelectionExcludeGeometry.Rect = rect;

        System.Windows.Controls.Canvas.SetLeft(SelectionBorder, rect.X);
        System.Windows.Controls.Canvas.SetTop(SelectionBorder, rect.Y);
        SelectionBorder.Width = rect.Width;
        SelectionBorder.Height = rect.Height;

        // Calculate physical pixel dimension text
        var source = PresentationSource.FromVisual(this);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        int pxW = (int)Math.Round(rect.Width * dpiX);
        int pxH = (int)Math.Round(rect.Height * dpiY);
        DimensionText.Text = $"{pxW} × {pxH} px";

        // Position badge just below selection rectangle (or above if near screen bottom)
        double badgeTop = rect.Bottom + 8;
        if (badgeTop + 30 > ActualHeight)
        {
            badgeTop = Math.Max(0, rect.Top - 34);
        }

        double badgeLeft = rect.Left;
        if (badgeLeft + 100 > ActualWidth)
        {
            badgeLeft = Math.Max(0, ActualWidth - 110);
        }

        System.Windows.Controls.Canvas.SetLeft(DimensionBadge, badgeLeft);
        System.Windows.Controls.Canvas.SetTop(DimensionBadge, badgeTop);
    }
}
