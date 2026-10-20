using System;
using System.Windows;
using System.Windows.Controls;
using Imaginary.Core.Services;
using SkiaSharp;

namespace Imaginary.Desktop.Views;

public partial class CanvasResizeDialog : Window
{
    private readonly int _currentWidth;
    private readonly int _currentHeight;
    private readonly double _aspectRatio;
    private bool _isUpdatingText;

    public int NewWidth { get; private set; }
    public int NewHeight { get; private set; }
    public CanvasAnchor Anchor { get; private set; } = CanvasAnchor.TopLeft;
    public SKColor BackgroundColor { get; private set; } = SKColors.Transparent;
    public int OffsetX { get; private set; }
    public int OffsetY { get; private set; }

    public CanvasResizeDialog(int currentWidth, int currentHeight)
    {
        InitializeComponent();

        _currentWidth = Math.Max(1, currentWidth);
        _currentHeight = Math.Max(1, currentHeight);
        _aspectRatio = (double)_currentWidth / _currentHeight;

        TextCurrentDimensions.Text = $"Aktuelle Größe: {_currentWidth} × {_currentHeight} px";

        _isUpdatingText = true;
        TextNewWidth.Text = _currentWidth.ToString();
        TextNewHeight.Text = _currentHeight.ToString();
        UpdatePaddingResultInfo();
        _isUpdatingText = false;
    }

    private void OnDimensionsChanged(object sender, TextChangedEventArgs e)
    {
        if (_isUpdatingText) return;

        if (CheckKeepAspect?.IsChecked == true)
        {
            _isUpdatingText = true;
            if (sender == TextNewWidth && int.TryParse(TextNewWidth.Text, out int parsedW) && parsedW > 0)
            {
                int h = Math.Max(1, (int)Math.Round(parsedW / _aspectRatio));
                TextNewHeight.Text = h.ToString();
            }
            else if (sender == TextNewHeight && int.TryParse(TextNewHeight.Text, out int parsedH) && parsedH > 0)
            {
                int w = Math.Max(1, (int)Math.Round(parsedH * _aspectRatio));
                TextNewWidth.Text = w.ToString();
            }
            _isUpdatingText = false;
        }
    }

    private void OnKeepAspectToggled(object sender, RoutedEventArgs e)
    {
        if (CheckKeepAspect.IsChecked == true && int.TryParse(TextNewWidth.Text, out int w) && w > 0)
        {
            _isUpdatingText = true;
            int h = Math.Max(1, (int)Math.Round(w / _aspectRatio));
            TextNewHeight.Text = h.ToString();
            _isUpdatingText = false;
        }
    }

    private void OnPresetDoubleWidthClicked(object sender, RoutedEventArgs e)
    {
        _isUpdatingText = true;
        TextNewWidth.Text = (_currentWidth * 2).ToString();
        TextNewHeight.Text = _currentHeight.ToString();
        AnchorMiddleLeft.IsChecked = true;
        _isUpdatingText = false;
    }

    private void OnPresetDoubleHeightClicked(object sender, RoutedEventArgs e)
    {
        _isUpdatingText = true;
        TextNewWidth.Text = _currentWidth.ToString();
        TextNewHeight.Text = (_currentHeight * 2).ToString();
        AnchorTopCenter.IsChecked = true;
        _isUpdatingText = false;
    }

    private void OnPresetPadding200Clicked(object sender, RoutedEventArgs e)
    {
        _isUpdatingText = true;
        TextNewWidth.Text = (_currentWidth + 400).ToString();
        TextNewHeight.Text = (_currentHeight + 400).ToString();
        AnchorCenter.IsChecked = true;
        _isUpdatingText = false;
    }

    private void OnPaddingChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePaddingResultInfo();
    }

    private void UpdatePaddingResultInfo()
    {
        if (TextPaddingResultInfo == null) return;

        int padT = int.TryParse(TextPadTop?.Text, out int pt) ? Math.Max(0, pt) : 0;
        int padB = int.TryParse(TextPadBottom?.Text, out int pb) ? Math.Max(0, pb) : 0;
        int padL = int.TryParse(TextPadLeft?.Text, out int pl) ? Math.Max(0, pl) : 0;
        int padR = int.TryParse(TextPadRight?.Text, out int pr) ? Math.Max(0, pr) : 0;

        int totalW = _currentWidth + padL + padR;
        int totalH = _currentHeight + padT + padB;

        TextPaddingResultInfo.Text = $"Neue Größe: {totalW} × {totalH} px (+{padL + padR} B, +{padT + padB} H)";
    }

    private void OnPresetPad50Clicked(object sender, RoutedEventArgs e)
    {
        SetPadding(50, 50, 50, 50);
    }

    private void OnPresetPad100Clicked(object sender, RoutedEventArgs e)
    {
        SetPadding(100, 100, 100, 100);
    }

    private void OnPresetPad200RightClicked(object sender, RoutedEventArgs e)
    {
        SetPadding(0, 0, 0, 200);
    }

    private void OnPresetPad200BottomClicked(object sender, RoutedEventArgs e)
    {
        SetPadding(0, 200, 0, 0);
    }

    private void SetPadding(int top, int bottom, int left, int right)
    {
        TextPadTop.Text = top.ToString();
        TextPadBottom.Text = bottom.ToString();
        TextPadLeft.Text = left.ToString();
        TextPadRight.Text = right.ToString();
    }

    private void OnApplyClicked(object sender, RoutedEventArgs e)
    {
        if (TabMode.SelectedIndex == 1) // Padding Tab
        {
            int padT = int.TryParse(TextPadTop.Text, out int pt) ? Math.Max(0, pt) : 0;
            int padB = int.TryParse(TextPadBottom.Text, out int pb) ? Math.Max(0, pb) : 0;
            int padL = int.TryParse(TextPadLeft.Text, out int pl) ? Math.Max(0, pl) : 0;
            int padR = int.TryParse(TextPadRight.Text, out int pr) ? Math.Max(0, pr) : 0;

            NewWidth = _currentWidth + padL + padR;
            NewHeight = _currentHeight + padT + padB;
            Anchor = CanvasAnchor.TopLeft;
            OffsetX = padL;
            OffsetY = padT;
        }
        else // Dimensions Tab
        {
            if (!int.TryParse(TextNewWidth.Text, out int w) || w <= 0)
            {
                MessageBox.Show(this, "Bitte geben Sie eine gültige positive Breite in Pixeln ein.", "Ungültige Eingabe", MessageBoxButton.OK, MessageBoxImage.Warning);
                TextNewWidth.Focus();
                return;
            }

            if (!int.TryParse(TextNewHeight.Text, out int h) || h <= 0)
            {
                MessageBox.Show(this, "Bitte geben Sie eine gültige positive Höhe in Pixeln ein.", "Ungültige Eingabe", MessageBoxButton.OK, MessageBoxImage.Warning);
                TextNewHeight.Focus();
                return;
            }

            NewWidth = Math.Clamp(w, 16, 16384);
            NewHeight = Math.Clamp(h, 16, 16384);

            Anchor = GetSelectedAnchor();
            (OffsetX, OffsetY) = CalculateOffsets(Anchor, _currentWidth, _currentHeight, NewWidth, NewHeight);
        }

        BackgroundColor = ComboBackground.SelectedIndex switch
        {
            1 => SKColors.White,
            2 => SKColors.Black,
            3 => SKColor.Parse("#111827"),
            _ => SKColors.Transparent
        };

        DialogResult = true;
        Close();
    }

    private CanvasAnchor GetSelectedAnchor()
    {
        if (AnchorTopLeft.IsChecked == true) return CanvasAnchor.TopLeft;
        if (AnchorTopCenter.IsChecked == true) return CanvasAnchor.TopCenter;
        if (AnchorTopRight.IsChecked == true) return CanvasAnchor.TopRight;
        if (AnchorMiddleLeft.IsChecked == true) return CanvasAnchor.MiddleLeft;
        if (AnchorCenter.IsChecked == true) return CanvasAnchor.Center;
        if (AnchorMiddleRight.IsChecked == true) return CanvasAnchor.MiddleRight;
        if (AnchorBottomLeft.IsChecked == true) return CanvasAnchor.BottomLeft;
        if (AnchorBottomCenter.IsChecked == true) return CanvasAnchor.BottomCenter;
        if (AnchorBottomRight.IsChecked == true) return CanvasAnchor.BottomRight;
        return CanvasAnchor.TopLeft;
    }

    public static (int offsetX, int offsetY) CalculateOffsets(CanvasAnchor anchor, int oldW, int oldH, int newW, int newH)
    {
        int x = anchor switch
        {
            CanvasAnchor.TopLeft or CanvasAnchor.MiddleLeft or CanvasAnchor.BottomLeft => 0,
            CanvasAnchor.TopCenter or CanvasAnchor.Center or CanvasAnchor.BottomCenter => (newW - oldW) / 2,
            CanvasAnchor.TopRight or CanvasAnchor.MiddleRight or CanvasAnchor.BottomRight => newW - oldW,
            _ => 0
        };

        int y = anchor switch
        {
            CanvasAnchor.TopLeft or CanvasAnchor.TopCenter or CanvasAnchor.TopRight => 0,
            CanvasAnchor.MiddleLeft or CanvasAnchor.Center or CanvasAnchor.MiddleRight => (newH - oldH) / 2,
            CanvasAnchor.BottomLeft or CanvasAnchor.BottomCenter or CanvasAnchor.BottomRight => newH - oldH,
            _ => 0
        };

        return (x, y);
    }
}
