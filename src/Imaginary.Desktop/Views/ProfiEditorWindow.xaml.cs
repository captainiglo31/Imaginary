using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Imaginary.Core.Logging;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using Imaginary.Desktop.Services;
using Microsoft.Win32;
using SkiaSharp;
using WpfColor = System.Windows.Media.Color;
using WpfPoint = System.Windows.Point;

namespace Imaginary.Desktop.Views;

public partial class ProfiEditorWindow : Window
{
    private readonly string _filePath;
    private readonly IProfiDeveloperService _developerService;

    // Bitmap pipelines
    private SKBitmap? _fullSourceBitmap;
    private SKBitmap? _previewSourceBitmap;
    private SKBitmap? _currentGradedPreview;

    // Settings & State
    private ProfiAdjustmentSettings _settings = new();
    private static ProfiAdjustmentSettings? _copiedSettings;
    private readonly Stack<ProfiAdjustmentSettings> _undoStack = new();
    private readonly Stack<ProfiAdjustmentSettings> _redoStack = new();

    private bool _isInitialized;
    private bool _isUpdatingSliders;
    private bool _isShowingOriginal;
    private bool _isSplitView;
    private double _splitPosition = 0.50; // 0.0 to 1.0

    // Zoom & Pan
    private double _zoom = 1.0;
    private bool _isPanning;
    private WpfPoint _panMouseStart;
    private WpfPoint _panScrollStart;

    // Live Histogram
    private WriteableBitmap? _histogramWb;
    private const int HistoWidth = 256;
    private const int HistoHeight = 100;

    // Toast Notification Timer
    private readonly DispatcherTimer _toastTimer;

    // Public Results
    public bool HasChanges { get; private set; }
    public string ResultFilePath { get; private set; } = string.Empty;
    public SKBitmap? ResultBitmap { get; private set; }

    public ProfiEditorWindow(string filePath, IProfiDeveloperService developerService)
    {
        _isUpdatingSliders = true;
        InitializeComponent();
        _isUpdatingSliders = false;
        _isInitialized = true;

        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _developerService = developerService ?? throw new ArgumentNullException(nameof(developerService));

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.8) };
        _toastTimer.Tick += (s, e) =>
        {
            _toastTimer.Stop();
            ToastNotification.Visibility = Visibility.Collapsed;
        };

        Loaded += OnWindowLoaded;
        Closing += OnWindowClosing;
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            TextFileName.Text = Path.GetFileName(_filePath);
            TextDimensions.Text = "Lade und decodiere RAW/Bilddaten...";

            await Task.Run(() =>
            {
                // 1. Load full resolution bitmap (RAW or standard image)
                _fullSourceBitmap = _developerService.LoadRawOrStandardBitmap(_filePath);

                // 2. Generate fast preview bitmap (e.g. max edge 1920 px) for instantaneous 60 FPS slider response
                int maxPreviewEdge = 1920;
                if (_fullSourceBitmap.Width > maxPreviewEdge || _fullSourceBitmap.Height > maxPreviewEdge)
                {
                    float scale = Math.Min((float)maxPreviewEdge / _fullSourceBitmap.Width, (float)maxPreviewEdge / _fullSourceBitmap.Height);
                    int prevW = Math.Max(1, (int)(_fullSourceBitmap.Width * scale));
                    int prevH = Math.Max(1, (int)(_fullSourceBitmap.Height * scale));

                    var info = new SKImageInfo(prevW, prevH, SKColorType.Bgra8888, SKAlphaType.Premul);
                    _previewSourceBitmap = new SKBitmap(info);
                    _fullSourceBitmap.ScalePixels(_previewSourceBitmap, SKFilterQuality.Medium);
                }
                else
                {
                    _previewSourceBitmap = _fullSourceBitmap.Copy();
                }
            });

            if (_fullSourceBitmap == null || _previewSourceBitmap == null)
            {
                MessageBox.Show(this, $"Die Datei '{Path.GetFileName(_filePath)}' konnte nicht im Profi-Modus geöffnet werden.", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
                return;
            }

            // Update format & dimensions badge
            string ext = Path.GetExtension(_filePath).TrimStart('.').ToUpperInvariant();
            bool isRaw = ext is "CR2" or "CR3" or "NEF" or "ARW" or "DNG" or "ORF" or "RW2" or "PEF" or "RAF";
            TextFormatBadge.Text = isRaw ? $"RAW ({ext}) / 14-Bit Pipeline" : $"{ext} / 32-Bit Pipeline";
            TextDimensions.Text = $"{_fullSourceBitmap.Width} × {_fullSourceBitmap.Height} px  •  Vorschau: {_previewSourceBitmap.Width} × {_previewSourceBitmap.Height} px";

            // Initialize Histogram WriteableBitmap
            _histogramWb = new WriteableBitmap(HistoWidth, HistoHeight, 96, 96, PixelFormats.Bgra32, null);
            ImageHistogram.Source = _histogramWb;

            // Initial render
            UpdateSlidersFromSettings();
            RenderCurrentPreview();

            // Auto fit image in viewport
            _ = Dispatcher.InvokeAsync(() => OnZoomFitClicked(this, new RoutedEventArgs()), DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            AppLogger.Error("ProfiEditor", "Fehler beim Initialisieren des Profi-Modus", ex);
            MessageBox.Show(this, "Fehler beim Laden des Bildes: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _toastTimer.Stop();
        _currentGradedPreview?.Dispose();
        _previewSourceBitmap?.Dispose();
        _fullSourceBitmap?.Dispose();
    }

    // ==========================================
    // Rendering & Pipeline
    // ==========================================

    private void RenderCurrentPreview()
    {
        if (_previewSourceBitmap == null) return;

        var sw = Stopwatch.StartNew();

        // 1. Check if user toggled Vorher (Original) view
        if (_isShowingOriginal)
        {
            DisplayBitmap(_previewSourceBitmap);
            UpdateHistogram(_previewSourceBitmap);
            TextStatusPerformance.Text = $"Anzeige: Originalbild (Vorher) | {_previewSourceBitmap.Width} × {_previewSourceBitmap.Height} px";
            return;
        }

        // 2. Apply adjustments to preview
        var graded = _developerService.ApplyAdjustments(_previewSourceBitmap, _settings);
        _currentGradedPreview?.Dispose();
        _currentGradedPreview = graded;

        // 3. Composite Split View if enabled
        if (_isSplitView)
        {
            var splitComposite = CompositeSplitView(_previewSourceBitmap, graded, _splitPosition);
            DisplayBitmap(splitComposite);
            splitComposite.Dispose();
        }
        else
        {
            DisplayBitmap(graded);
        }

        // 4. Compute and draw live histogram
        UpdateHistogram(graded);

        sw.Stop();
        TextStatusPerformance.Text = $"Renderzeit: {sw.ElapsedMilliseconds} ms (ca. {Math.Min(60, (int)(1000.0 / Math.Max(1, sw.ElapsedMilliseconds)))} FPS) | SkiaSharp Multithreaded Pipeline | Farbraum: sRGB";
    }

    private void DisplayBitmap(SKBitmap bitmap)
    {
        // Convert SKBitmap to fast WPF BitmapSource without PNG encoding overhead
        var info = bitmap.Info;
        int stride = info.RowBytes;
        IntPtr pixels = bitmap.GetPixels();

        var bs = BitmapSource.Create(
            info.Width,
            info.Height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride * info.Height,
            stride);

        bs.Freeze();
        MainDisplayImage.Source = bs;
    }

    private SKBitmap CompositeSplitView(SKBitmap original, SKBitmap graded, double splitPos)
    {
        int w = original.Width;
        int h = original.Height;
        int splitX = Math.Clamp((int)(w * splitPos), 0, w);

        var composite = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(composite);

        // Draw left side: Original
        var srcOriginal = new SKRect(0, 0, splitX, h);
        var dstOriginal = new SKRect(0, 0, splitX, h);
        canvas.DrawBitmap(original, srcOriginal, dstOriginal);

        // Draw right side: Graded
        var srcGraded = new SKRect(splitX, 0, w, h);
        var dstGraded = new SKRect(splitX, 0, w, h);
        canvas.DrawBitmap(graded, srcGraded, dstGraded);

        // Draw Split separator line
        using var linePaint = new SKPaint
        {
            Color = SKColor.Parse("#00D2FF"),
            StrokeWidth = 2.5f,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke
        };
        canvas.DrawLine(splitX, 0, splitX, h, linePaint);

        return composite;
    }

    // ==========================================
    // Live Histogram Drawing (Additive Blending)
    // ==========================================

    private void UpdateHistogram(SKBitmap bitmap)
    {
        if (_histogramWb == null) return;

        var data = _developerService.ComputeHistogram(bitmap);

        // Update clipping indicators
        bool shadowClip = data.ShadowClippingPercentage > 0.04;
        bool highlightClip = data.HighlightClippingPercentage > 0.04;

        DotShadowClipping.Fill = shadowClip ? new SolidColorBrush(WpfColor.FromRgb(56, 189, 248)) : new SolidColorBrush(WpfColor.FromRgb(51, 65, 85));
        ToggleShadowClipping.ToolTip = $"Schatten-Clipping: {data.ShadowClippingPercentage:F2}% ({data.ShadowClippingCount:N0} Pixel)";

        DotHighlightClipping.Fill = highlightClip ? new SolidColorBrush(WpfColor.FromRgb(239, 68, 68)) : new SolidColorBrush(WpfColor.FromRgb(51, 65, 85));
        ToggleHighlightClipping.ToolTip = $"Lichter-Clipping: {data.HighlightClippingPercentage:F2}% ({data.HighlightClippingCount:N0} Pixel)";

        // Draw RGB additive histogram onto WriteableBitmap
        int maxVal = Math.Max(1, data.MaxValue);
        byte[] buffer = new byte[HistoWidth * HistoHeight * 4];

        // Background color: #070A14
        byte bgB = 0x14, bgG = 0x0A, bgR = 0x07, bgA = 0xFF;
        for (int i = 0; i < buffer.Length; i += 4)
        {
            buffer[i] = bgB;
            buffer[i + 1] = bgG;
            buffer[i + 2] = bgR;
            buffer[i + 3] = bgA;
        }

        // Draw overlapping channels with additive blending
        for (int x = 0; x < HistoWidth; x++)
        {
            int rH = Math.Min(HistoHeight - 1, (int)((long)data.Red[x] * (HistoHeight - 4) / maxVal));
            int gH = Math.Min(HistoHeight - 1, (int)((long)data.Green[x] * (HistoHeight - 4) / maxVal));
            int bH = Math.Min(HistoHeight - 1, (int)((long)data.Blue[x] * (HistoHeight - 4) / maxVal));
            int lH = Math.Min(HistoHeight - 1, (int)((long)data.Luminance[x] * (HistoHeight - 4) / maxVal));

            for (int yFromBottom = 0; yFromBottom < HistoHeight; yFromBottom++)
            {
                int y = HistoHeight - 1 - yFromBottom;
                int offset = (y * HistoWidth + x) * 4;

                bool hasR = yFromBottom <= rH;
                bool hasG = yFromBottom <= gH;
                bool hasB = yFromBottom <= bH;

                if (hasR || hasG || hasB)
                {
                    // Additive mix
                    int curB = buffer[offset];
                    int curG = buffer[offset + 1];
                    int curR = buffer[offset + 2];

                    if (hasB) curB = Math.Min(255, curB + 160);
                    if (hasG) curG = Math.Min(255, curG + 160);
                    if (hasR) curR = Math.Min(255, curR + 160);

                    buffer[offset] = (byte)curB;
                    buffer[offset + 1] = (byte)curG;
                    buffer[offset + 2] = (byte)curR;
                }

                // Luminance line on top
                if (yFromBottom == lH)
                {
                    buffer[offset] = 240;
                    buffer[offset + 1] = 240;
                    buffer[offset + 2] = 240;
                }
            }
        }

        _histogramWb.WritePixels(new Int32Rect(0, 0, HistoWidth, HistoHeight), buffer, HistoWidth * 4, 0);
    }

    // ==========================================
    // UI Sliders & Sync
    // ==========================================

    private void UpdateSlidersFromSettings()
    {
        _isUpdatingSliders = true;
        try
        {
            // Light
            SliderExposure.Value = _settings.Exposure;
            ValExposure.Text = $"{_settings.Exposure:+0.00;-0.00;0.00} EV";

            SliderContrast.Value = _settings.Contrast;
            ValContrast.Text = $"{_settings.Contrast:+0;-0;0}";

            SliderHighlights.Value = _settings.Highlights;
            ValHighlights.Text = $"{_settings.Highlights:+0;-0;0}";

            SliderShadows.Value = _settings.Shadows;
            ValShadows.Text = $"{_settings.Shadows:+0;-0;0}";

            SliderWhites.Value = _settings.Whites;
            ValWhites.Text = $"{_settings.Whites:+0;-0;0}";

            SliderBlacks.Value = _settings.Blacks;
            ValBlacks.Text = $"{_settings.Blacks:+0;-0;0}";

            // Color
            SliderTemperature.Value = _settings.Temperature;
            ValTemperature.Text = $"{_settings.Temperature:+0;-0;0}";

            SliderTint.Value = _settings.Tint;
            ValTint.Text = $"{_settings.Tint:+0;-0;0}";

            SliderVibrance.Value = _settings.Vibrance;
            ValVibrance.Text = $"{_settings.Vibrance:+0;-0;0}";

            SliderSaturation.Value = _settings.Saturation;
            ValSaturation.Text = $"{_settings.Saturation:+0;-0;0}";

            SliderHueShift.Value = _settings.HueShift;
            ValHueShift.Text = $"{_settings.HueShift:+0;-0;0}°";

            // Detail
            SliderClarity.Value = _settings.Clarity;
            ValClarity.Text = $"{_settings.Clarity:+0;-0;0}";

            SliderDehaze.Value = _settings.Dehaze;
            ValDehaze.Text = $"{_settings.Dehaze:+0;-0;0}";

            SliderSharpen.Value = _settings.Sharpen;
            ValSharpen.Text = $"{_settings.Sharpen:0}";

            SliderNoiseReduction.Value = _settings.NoiseReduction;
            ValNoiseReduction.Text = $"{_settings.NoiseReduction:0}";

            // Grading
            SliderShadowHue.Value = _settings.ShadowHue;
            ValShadowHue.Text = $"{_settings.ShadowHue:0}°";
            SliderShadowSat.Value = _settings.ShadowSaturation;
            ValShadowSat.Text = $"{_settings.ShadowSaturation:0}";

            SliderHighlightHue.Value = _settings.HighlightHue;
            ValHighlightHue.Text = $"{_settings.HighlightHue:0}°";
            SliderHighlightSat.Value = _settings.HighlightSaturation;
            ValHighlightSat.Text = $"{_settings.HighlightSaturation:0}";

            SliderGradingBalance.Value = _settings.Balance;
            ValGradingBalance.Text = $"{_settings.Balance:+0;-0;0}";

            UpdateGradingSwatches();

            // Effects
            SliderVignette.Value = _settings.Vignette;
            ValVignette.Text = $"{_settings.Vignette:+0;-0;0}";

            SliderGrain.Value = _settings.Grain;
            ValGrain.Text = $"{_settings.Grain:0}";

            // Curves
            SliderCurveHighlights.Value = _settings.CurveHighlights;
            ValCurveHighlights.Text = $"{_settings.CurveHighlights:+0;-0;0}";

            SliderCurveLights.Value = _settings.CurveLights;
            ValCurveLights.Text = $"{_settings.CurveLights:+0;-0;0}";

            SliderCurveDarks.Value = _settings.CurveDarks;
            ValCurveDarks.Text = $"{_settings.CurveDarks:+0;-0;0}";

            SliderCurveShadows.Value = _settings.CurveShadows;
            ValCurveShadows.Text = $"{_settings.CurveShadows:+0;-0;0}";
        }
        finally
        {
            _isUpdatingSliders = false;
        }
    }

    private void OnSliderAdjustmentChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized || _isUpdatingSliders) return;

        // Read all values from sliders into _settings
        _settings.Exposure = (float)SliderExposure.Value;
        ValExposure.Text = $"{_settings.Exposure:+0.00;-0.00;0.00} EV";

        _settings.Contrast = (float)SliderContrast.Value;
        ValContrast.Text = $"{_settings.Contrast:+0;-0;0}";

        _settings.Highlights = (float)SliderHighlights.Value;
        ValHighlights.Text = $"{_settings.Highlights:+0;-0;0}";

        _settings.Shadows = (float)SliderShadows.Value;
        ValShadows.Text = $"{_settings.Shadows:+0;-0;0}";

        _settings.Whites = (float)SliderWhites.Value;
        ValWhites.Text = $"{_settings.Whites:+0;-0;0}";

        _settings.Blacks = (float)SliderBlacks.Value;
        ValBlacks.Text = $"{_settings.Blacks:+0;-0;0}";

        _settings.Temperature = (float)SliderTemperature.Value;
        ValTemperature.Text = $"{_settings.Temperature:+0;-0;0}";

        _settings.Tint = (float)SliderTint.Value;
        ValTint.Text = $"{_settings.Tint:+0;-0;0}";

        _settings.Vibrance = (float)SliderVibrance.Value;
        ValVibrance.Text = $"{_settings.Vibrance:+0;-0;0}";

        _settings.Saturation = (float)SliderSaturation.Value;
        ValSaturation.Text = $"{_settings.Saturation:+0;-0;0}";

        _settings.HueShift = (float)SliderHueShift.Value;
        ValHueShift.Text = $"{_settings.HueShift:+0;-0;0}°";

        _settings.Clarity = (float)SliderClarity.Value;
        ValClarity.Text = $"{_settings.Clarity:+0;-0;0}";

        _settings.Dehaze = (float)SliderDehaze.Value;
        ValDehaze.Text = $"{_settings.Dehaze:+0;-0;0}";

        _settings.Sharpen = (float)SliderSharpen.Value;
        ValSharpen.Text = $"{_settings.Sharpen:0}";

        _settings.NoiseReduction = (float)SliderNoiseReduction.Value;
        ValNoiseReduction.Text = $"{_settings.NoiseReduction:0}";

        _settings.ShadowHue = (float)SliderShadowHue.Value;
        ValShadowHue.Text = $"{_settings.ShadowHue:0}°";

        _settings.ShadowSaturation = (float)SliderShadowSat.Value;
        ValShadowSat.Text = $"{_settings.ShadowSaturation:0}";

        _settings.HighlightHue = (float)SliderHighlightHue.Value;
        ValHighlightHue.Text = $"{_settings.HighlightHue:0}°";

        _settings.HighlightSaturation = (float)SliderHighlightSat.Value;
        ValHighlightSat.Text = $"{_settings.HighlightSaturation:0}";

        _settings.Balance = (float)SliderGradingBalance.Value;
        ValGradingBalance.Text = $"{_settings.Balance:+0;-0;0}";

        _settings.Vignette = (float)SliderVignette.Value;
        ValVignette.Text = $"{_settings.Vignette:+0;-0;0}";

        _settings.Grain = (float)SliderGrain.Value;
        ValGrain.Text = $"{_settings.Grain:0}";

        _settings.CurveHighlights = (float)SliderCurveHighlights.Value;
        ValCurveHighlights.Text = $"{_settings.CurveHighlights:+0;-0;0}";

        _settings.CurveLights = (float)SliderCurveLights.Value;
        ValCurveLights.Text = $"{_settings.CurveLights:+0;-0;0}";

        _settings.CurveDarks = (float)SliderCurveDarks.Value;
        ValCurveDarks.Text = $"{_settings.CurveDarks:+0;-0;0}";

        _settings.CurveShadows = (float)SliderCurveShadows.Value;
        ValCurveShadows.Text = $"{_settings.CurveShadows:+0;-0;0}";

        UpdateGradingSwatches();

        HasChanges = true;
        RenderCurrentPreview();
    }

    private void UpdateGradingSwatches()
    {
        var sColor = HsvToRgb(_settings.ShadowHue, _settings.ShadowSaturation / 100f, 0.85f);
        SwatchShadowColor.Background = new SolidColorBrush(sColor);

        var hColor = HsvToRgb(_settings.HighlightHue, _settings.HighlightSaturation / 100f, 0.85f);
        SwatchHighlightColor.Background = new SolidColorBrush(hColor);
    }

    private static WpfColor HsvToRgb(float h, float s, float v)
    {
        if (s <= 0.001f)
        {
            byte val = (byte)(v * 255);
            return WpfColor.FromRgb(val, val, val);
        }

        h = (h % 360f + 360f) % 360f;
        float c = v * s;
        float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
        float m = v - c;

        float r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else { r = c; b = x; }

        return WpfColor.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private void PushUndoState()
    {
        _undoStack.Push(_settings.Clone());
        _redoStack.Clear();
    }

    // ==========================================
    // Presets & Quick Looks
    // ==========================================

    private void OnPresetButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string presetName)
        {
            var presets = _developerService.GetBuiltInPresets();
            var matched = presets.FirstOrDefault(p =>
                string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase) ||
                p.Name.StartsWith(presetName, StringComparison.OrdinalIgnoreCase));
            if (matched != null)
            {
                PushUndoState();
                _settings = matched.Settings.Clone();
                UpdateSlidersFromSettings();
                HasChanges = true;
                RenderCurrentPreview();
                ShowToast($"Preset angewendet: {matched.Name}");
            }
        }
    }

    // ==========================================
    // Reset Handlers
    // ==========================================

    private void OnResetAllClicked(object sender, RoutedEventArgs e)
    {
        PushUndoState();
        _settings.ResetAll();
        UpdateSlidersFromSettings();
        HasChanges = true;
        RenderCurrentPreview();
        ShowToast("Alle Regler auf neutral zurückgesetzt");
    }

    private void OnResetLightClicked(object sender, RoutedEventArgs e)
    {
        PushUndoState();
        _settings.ResetLight();
        UpdateSlidersFromSettings();
        RenderCurrentPreview();
    }

    private void OnResetColorClicked(object sender, RoutedEventArgs e)
    {
        PushUndoState();
        _settings.ResetColor();
        UpdateSlidersFromSettings();
        RenderCurrentPreview();
    }

    private void OnResetDetailClicked(object sender, RoutedEventArgs e)
    {
        PushUndoState();
        _settings.ResetDetails();
        UpdateSlidersFromSettings();
        RenderCurrentPreview();
    }

    private void OnResetGradingClicked(object sender, RoutedEventArgs e)
    {
        PushUndoState();
        _settings.ResetGrading();
        UpdateSlidersFromSettings();
        RenderCurrentPreview();
    }

    private void OnResetEffectsClicked(object sender, RoutedEventArgs e)
    {
        PushUndoState();
        _settings.ResetEffects();
        UpdateSlidersFromSettings();
        RenderCurrentPreview();
    }

    private void OnResetCurveClicked(object sender, RoutedEventArgs e)
    {
        PushUndoState();
        _settings.ResetCurves();
        UpdateSlidersFromSettings();
        RenderCurrentPreview();
    }

    // Individual Slider Resets (Double-click or label click)
    private void OnResetExposureClicked(object sender, MouseButtonEventArgs e) { SliderExposure.Value = 0; }
    private void OnResetContrastClicked(object sender, MouseButtonEventArgs e) { SliderContrast.Value = 0; }
    private void OnResetHighlightsClicked(object sender, MouseButtonEventArgs e) { SliderHighlights.Value = 0; }
    private void OnResetShadowsClicked(object sender, MouseButtonEventArgs e) { SliderShadows.Value = 0; }
    private void OnResetWhitesClicked(object sender, MouseButtonEventArgs e) { SliderWhites.Value = 0; }
    private void OnResetBlacksClicked(object sender, MouseButtonEventArgs e) { SliderBlacks.Value = 0; }
    private void OnResetTempClicked(object sender, MouseButtonEventArgs e) { SliderTemperature.Value = 0; }
    private void OnResetTintClicked(object sender, MouseButtonEventArgs e) { SliderTint.Value = 0; }
    private void OnResetVibranceClicked(object sender, MouseButtonEventArgs e) { SliderVibrance.Value = 0; }
    private void OnResetSaturationClicked(object sender, MouseButtonEventArgs e) { SliderSaturation.Value = 0; }
    private void OnResetHueClicked(object sender, MouseButtonEventArgs e) { SliderHueShift.Value = 0; }
    private void OnResetClarityClicked(object sender, MouseButtonEventArgs e) { SliderClarity.Value = 0; }
    private void OnResetDehazeClicked(object sender, MouseButtonEventArgs e) { SliderDehaze.Value = 0; }
    private void OnResetSharpenClicked(object sender, MouseButtonEventArgs e) { SliderSharpen.Value = 0; }
    private void OnResetNoiseClicked(object sender, MouseButtonEventArgs e) { SliderNoiseReduction.Value = 0; }
    private void OnResetShadowSatClicked(object sender, MouseButtonEventArgs e) { SliderShadowSat.Value = 0; }
    private void OnResetHighlightSatClicked(object sender, MouseButtonEventArgs e) { SliderHighlightSat.Value = 0; }
    private void OnResetGradingBalanceClicked(object sender, MouseButtonEventArgs e) { SliderGradingBalance.Value = 0; }
    private void OnResetVignetteClicked(object sender, MouseButtonEventArgs e) { SliderVignette.Value = 0; }
    private void OnResetGrainClicked(object sender, MouseButtonEventArgs e) { SliderGrain.Value = 0; }
    private void OnResetCurveHighlightsClicked(object sender, MouseButtonEventArgs e) { SliderCurveHighlights.Value = 0; }
    private void OnResetCurveLightsClicked(object sender, MouseButtonEventArgs e) { SliderCurveLights.Value = 0; }
    private void OnResetCurveDarksClicked(object sender, MouseButtonEventArgs e) { SliderCurveDarks.Value = 0; }
    private void OnResetCurveShadowsClicked(object sender, MouseButtonEventArgs e) { SliderCurveShadows.Value = 0; }

    // ==========================================
    // Compare & Split View
    // ==========================================

    private void OnToggleBeforeAfterClicked(object sender, RoutedEventArgs e)
    {
        _isShowingOriginal = ToggleBeforeAfter.IsChecked == true;
        if (_isShowingOriginal)
        {
            ShowToast("Original (Vorher) anzeigen");
        }
        RenderCurrentPreview();
    }

    private void OnToggleSplitViewClicked(object sender, RoutedEventArgs e)
    {
        _isSplitView = ToggleSplitView.IsChecked == true;
        PanelSplitController.Visibility = _isSplitView ? Visibility.Visible : Visibility.Collapsed;
        RenderCurrentPreview();
    }

    private void OnSplitPositionChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isInitialized || SliderSplitPosition == null) return;
        _splitPosition = SliderSplitPosition.Value / 100.0;
        if (_isSplitView)
        {
            RenderCurrentPreview();
        }
    }

    private void OnToggleClippingClicked(object sender, RoutedEventArgs e)
    {
        // Highlight clipping regions if desired
        RenderCurrentPreview();
    }

    // ==========================================
    // Copy & Paste Settings
    // ==========================================

    private void OnCopySettingsClicked(object sender, RoutedEventArgs e)
    {
        _copiedSettings = _settings.Clone();
        ShowToast("📋 Entwicklungseinstellungen kopiert!");
    }

    private void OnPasteSettingsClicked(object sender, RoutedEventArgs e)
    {
        if (_copiedSettings != null)
        {
            PushUndoState();
            _settings = _copiedSettings.Clone();
            UpdateSlidersFromSettings();
            HasChanges = true;
            RenderCurrentPreview();
            ShowToast("📥 Entwicklungseinstellungen eingefügt!");
        }
        else
        {
            ShowToast("⚠️ Keine Einstellungen im Zwischenspeicher");
        }
    }

    private void ShowToast(string message)
    {
        TextToastNotification.Text = message;
        ToastNotification.Visibility = Visibility.Visible;
        _toastTimer.Stop();
        _toastTimer.Start();
    }

    // ==========================================
    // Zoom & Pan
    // ==========================================

    private void ApplyZoom(double newZoom)
    {
        _zoom = Math.Clamp(newZoom, 0.1, 8.0);
        ViewportScaleTransform.ScaleX = _zoom;
        ViewportScaleTransform.ScaleY = _zoom;
        TextZoomLevel.Text = $"{_zoom * 100:0}%";
    }

    private void OnZoomInClicked(object sender, RoutedEventArgs e) => ApplyZoom(_zoom * 1.25);
    private void OnZoomOutClicked(object sender, RoutedEventArgs e) => ApplyZoom(_zoom / 1.25);
    private void OnZoomResetClicked(object sender, RoutedEventArgs e) => ApplyZoom(1.0);

    private void OnZoomFitClicked(object sender, RoutedEventArgs e)
    {
        if (_previewSourceBitmap == null) return;

        double viewportW = ViewportScrollViewer.ActualWidth - 30;
        double viewportH = ViewportScrollViewer.ActualHeight - 30;
        if (viewportW <= 0 || viewportH <= 0) return;

        double fitX = viewportW / _previewSourceBitmap.Width;
        double fitY = viewportH / _previewSourceBitmap.Height;
        double fitZoom = Math.Min(fitX, fitY);

        ApplyZoom(Math.Max(0.1, fitZoom));
    }

    private void OnViewportMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            double factor = e.Delta > 0 ? 1.15 : 0.85;
            ApplyZoom(_zoom * factor);
            e.Handled = true;
        }
    }

    private void OnViewportMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton == MouseButtonState.Pressed || (e.LeftButton == MouseButtonState.Pressed && Keyboard.IsKeyDown(Key.Space)))
        {
            _isPanning = true;
            _panMouseStart = e.GetPosition(ViewportScrollViewer);
            _panScrollStart = new WpfPoint(ViewportScrollViewer.HorizontalOffset, ViewportScrollViewer.VerticalOffset);
            ViewportScrollViewer.CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    private void OnViewportMouseMove(object sender, MouseEventArgs e)
    {
        // 1. Handle Panning
        if (_isPanning)
        {
            var cur = e.GetPosition(ViewportScrollViewer);
            double dx = cur.X - _panMouseStart.X;
            double dy = cur.Y - _panMouseStart.Y;
            ViewportScrollViewer.ScrollToHorizontalOffset(_panScrollStart.X - dx);
            ViewportScrollViewer.ScrollToVerticalOffset(_panScrollStart.Y - dy);
            return;
        }

        // 2. Pixel Inspector / Loupe
        if (_currentGradedPreview != null && MainDisplayImage.IsMouseOver)
        {
            var pos = e.GetPosition(MainDisplayImage);
            int x = (int)pos.X;
            int y = (int)pos.Y;

            if (x >= 0 && x < _currentGradedPreview.Width && y >= 0 && y < _currentGradedPreview.Height)
            {
                var color = _currentGradedPreview.GetPixel(x, y);
                TextPixelInspector.Text = $"X: {x} | Y: {y} | RGB: {color.Red}, {color.Green}, {color.Blue} | #{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
                BorderPixelColorPreview.Background = new SolidColorBrush(WpfColor.FromRgb(color.Red, color.Green, color.Blue));
            }
        }
    }

    private void OnViewportMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            ViewportScrollViewer.ReleaseMouseCapture();
            Cursor = Cursors.Arrow;
        }
    }

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Toggle Vorher/Nachher with backslash key '\'
        if (e.Key is Key.Oem5 or Key.OemBackslash or Key.OemPipe)
        {
            ToggleBeforeAfter.IsChecked = !ToggleBeforeAfter.IsChecked;
            OnToggleBeforeAfterClicked(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_undoStack.Count > 0)
            {
                _redoStack.Push(_settings.Clone());
                _settings = _undoStack.Pop();
                UpdateSlidersFromSettings();
                RenderCurrentPreview();
                ShowToast("↩️ Rückgängig");
            }
            e.Handled = true;
        }
        else if (e.Key == Key.Y && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_redoStack.Count > 0)
            {
                _undoStack.Push(_settings.Clone());
                _settings = _redoStack.Pop();
                UpdateSlidersFromSettings();
                RenderCurrentPreview();
                ShowToast("↪️ Wiederholen");
            }
            e.Handled = true;
        }
        else if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OnZoomFitClicked(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.D1 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OnZoomResetClicked(sender, e);
            e.Handled = true;
        }
    }

    // ==========================================
    // Save, Export & Actions
    // ==========================================

    private SKBitmap RenderFullResolutionResult()
    {
        if (_fullSourceBitmap == null) throw new InvalidOperationException("Source Bitmap nicht verfügbar.");
        return _developerService.ApplyAdjustments(_fullSourceBitmap, _settings);
    }

    private void OnCopyToClipboardClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            using var final = RenderFullResolutionResult();
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
            ShowToast("📋 Bild in voller Auflösung in die Zwischenablage kopiert!");
        }
        catch (Exception ex)
        {
            AppLogger.Error("ProfiEditor", "Fehler beim Kopieren in Zwischenablage", ex);
            MessageBox.Show(this, "Fehler beim Kopieren in Zwischenablage: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnExportAsCopyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = Path.GetDirectoryName(_filePath) ?? "";
            string name = Path.GetFileNameWithoutExtension(_filePath);

            var sfd = new SaveFileDialog
            {
                InitialDirectory = dir,
                FileName = $"{name}_profi.jpg",
                Filter = "JPEG-Bild (*.jpg;*.jpeg)|*.jpg;*.jpeg|PNG-Bild (*.png)|*.png|TIFF-Bild (*.tif;*.tiff)|*.tif;*.tiff|WebP-Bild (*.webp)|*.webp|AVIF-Bild (*.avif)|*.avif|Alle Dateien (*.*)|*.*",
                Title = "Entwickeltes Bild als neue Datei exportieren"
            };

            if (sfd.ShowDialog(this) != true) return;

            string targetPath = sfd.FileName;
            using var final = RenderFullResolutionResult();

            string ext = Path.GetExtension(targetPath).ToLowerInvariant();
            SKEncodedImageFormat format = ext switch
            {
                ".png" => SKEncodedImageFormat.Png,
                ".webp" => SKEncodedImageFormat.Webp,
                ".avif" => SKEncodedImageFormat.Avif,
                _ => SKEncodedImageFormat.Jpeg
            };

            using var image = SKImage.FromBitmap(final);
            using var data = image.Encode(format, 95);
            using (var fs = File.Create(targetPath))
            {
                data.SaveTo(fs);
            }

            ResultFilePath = targetPath;
            HasChanges = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            AppLogger.Error("ProfiEditor", "Fehler beim Exportieren", ex);
            MessageBox.Show(this, "Fehler beim Exportieren: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnSaveAndApplyClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            string ext = Path.GetExtension(_filePath).ToLowerInvariant();
            bool isRaw = ext is ".cr2" or ".cr3" or ".nef" or ".arw" or ".dng" or ".orf" or ".rw2" or ".pef" or ".raf";

            // If original is camera RAW, we must export as a standard photo file (JPEG/PNG) to preserve the RAW negative
            if (isRaw)
            {
                OnExportAsCopyClicked(sender, e);
                return;
            }

            using var final = RenderFullResolutionResult();
            SKEncodedImageFormat format = ext switch
            {
                ".png" => SKEncodedImageFormat.Png,
                ".webp" => SKEncodedImageFormat.Webp,
                ".avif" => SKEncodedImageFormat.Avif,
                _ => SKEncodedImageFormat.Jpeg
            };

            using var image = SKImage.FromBitmap(final);
            using var data = image.Encode(format, 95);

            string tempFile = _filePath + ".tmp";
            using (var fs = File.Create(tempFile))
            {
                data.SaveTo(fs);
            }

            File.Move(tempFile, _filePath, overwrite: true);

            ResultFilePath = _filePath;
            HasChanges = true;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            AppLogger.Error("ProfiEditor", "Fehler beim Speichern", ex);
            MessageBox.Show(this, "Fehler beim Speichern: " + ex.Message, "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
