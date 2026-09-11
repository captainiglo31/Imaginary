using System;

namespace Imaginary.Core.Models;

public class ProfiAdjustmentSettings
{
    // Belichtung & Licht (Exposure & Light)
    public float Exposure { get; set; } = 0.0f; // -3.0 to +3.0 EV
    public float Contrast { get; set; } = 0.0f; // -100 to +100
    public float Highlights { get; set; } = 0.0f; // -100 to +100
    public float Shadows { get; set; } = 0.0f; // -100 to +100
    public float Whites { get; set; } = 0.0f; // -100 to +100
    public float Blacks { get; set; } = 0.0f; // -100 to +100

    // Farbe & Weißabgleich (Color & White Balance)
    public float Temperature { get; set; } = 0.0f; // -100 (Kalt/Blau) to +100 (Warm/Gelb)
    public float Tint { get; set; } = 0.0f; // -100 (Grün) to +100 (Magenta)
    public float Vibrance { get; set; } = 0.0f; // -100 to +100 (intelligente Sättigung)
    public float Saturation { get; set; } = 0.0f; // -100 (Monochrom) to +100
    public float HueShift { get; set; } = 0.0f; // -180° to +180°

    // Präsenz & Details (Clarity & Details)
    public float Clarity { get; set; } = 0.0f; // -100 to +100 (Mikrokontrast)
    public float Dehaze { get; set; } = 0.0f; // -100 to +100 (Dunst entfernen)
    public float Sharpen { get; set; } = 0.0f; // 0 to 100 (Unsharp Mask)
    public float NoiseReduction { get; set; } = 0.0f; // 0 to 100 (Luminanz-Rauschunterdrückung)

    // Effekte & Vignette
    public float Vignette { get; set; } = 0.0f; // -100 (Dunkle Ecken) to +100 (Helle Ecken)
    public float Grain { get; set; } = 0.0f; // 0 to 100 (Analoges Filmkorn)

    // Split Toning / Color Grading
    public float ShadowHue { get; set; } = 210.0f; // 0 to 360 (Default: Teal)
    public float ShadowSaturation { get; set; } = 0.0f; // 0 to 100%
    public float HighlightHue { get; set; } = 35.0f; // 0 to 360 (Default: Warm Orange)
    public float HighlightSaturation { get; set; } = 0.0f; // 0 to 100%
    public float Balance { get; set; } = 0.0f; // -100 to +100

    // Tonwertkurve (Curves: 4 Zonen - Schatten, Dunkle Mitten, Helle Mitten, Lichter)
    public float CurveShadows { get; set; } = 0.0f; // -100 to +100
    public float CurveDarks { get; set; } = 0.0f; // -100 to +100
    public float CurveLights { get; set; } = 0.0f; // -100 to +100
    public float CurveHighlights { get; set; } = 0.0f; // -100 to +100

    public bool IsDefault =>
        Exposure == 0 && Contrast == 0 && Highlights == 0 && Shadows == 0 && Whites == 0 && Blacks == 0 &&
        Temperature == 0 && Tint == 0 && Vibrance == 0 && Saturation == 0 && HueShift == 0 &&
        Clarity == 0 && Dehaze == 0 && Sharpen == 0 && NoiseReduction == 0 &&
        Vignette == 0 && Grain == 0 && ShadowSaturation == 0 && HighlightSaturation == 0 &&
        CurveShadows == 0 && CurveDarks == 0 && CurveLights == 0 && CurveHighlights == 0;

    public void ResetLight()
    {
        Exposure = 0;
        Contrast = 0;
        Highlights = 0;
        Shadows = 0;
        Whites = 0;
        Blacks = 0;
    }

    public void ResetColor()
    {
        Temperature = 0;
        Tint = 0;
        Vibrance = 0;
        Saturation = 0;
        HueShift = 0;
    }

    public void ResetDetails()
    {
        Clarity = 0;
        Dehaze = 0;
        Sharpen = 0;
        NoiseReduction = 0;
    }

    public void ResetGrading()
    {
        ShadowHue = 210.0f;
        ShadowSaturation = 0;
        HighlightHue = 35.0f;
        HighlightSaturation = 0;
        Balance = 0;
    }

    public void ResetEffects()
    {
        Vignette = 0;
        Grain = 0;
    }

    public void ResetCurves()
    {
        CurveShadows = 0;
        CurveDarks = 0;
        CurveLights = 0;
        CurveHighlights = 0;
    }

    public void ResetAll()
    {
        ResetLight();
        ResetColor();
        ResetDetails();
        ResetGrading();
        ResetEffects();
        ResetCurves();
    }

    public ProfiAdjustmentSettings Clone()
    {
        return (ProfiAdjustmentSettings)MemberwiseClone();
    }
}
