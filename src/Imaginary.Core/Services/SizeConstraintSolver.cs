using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public class SizeConstraintSolver : ISizeConstraintSolver
{
    private readonly IImageConverter _converter;
    private readonly IImageResizer _resizer;
    private readonly IColorQuantizer _quantizer;
    private readonly IImageFormatDetector _formatDetector;

    public SizeConstraintSolver(
        IImageConverter converter,
        IImageResizer resizer,
        IColorQuantizer quantizer,
        IImageFormatDetector formatDetector)
    {
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _resizer = resizer ?? throw new ArgumentNullException(nameof(resizer));
        _quantizer = quantizer ?? throw new ArgumentNullException(nameof(quantizer));
        _formatDetector = formatDetector ?? throw new ArgumentNullException(nameof(formatDetector));
    }

    public SizeConstraintResult Solve(SKBitmap bitmap, ImageFormat targetFormat, ConversionOptions options)
    {
        var initialDims = new ImageDimensions(bitmap.Width, bitmap.Height);
        var warnings = new List<string>();

        // Check if format is GIF and warn if it's animated or just single frame
        if (targetFormat == ImageFormat.Gif)
        {
            warnings.Add("GIF encoding saves a single static frame (animation not preserved).");
        }

        // Unconstrained case
        if (!options.MaxFileSizeInBytes.HasValue || options.MaxFileSizeInBytes.Value <= 0)
        {
            var data = _converter.Encode(bitmap, targetFormat, options.DefaultQuality);
            return new SizeConstraintResult(data, initialDims, "Standard encoding", warnings);
        }

        var maxBytes = options.MaxFileSizeInBytes.Value;
        var isLossy = _formatDetector.IsLossy(targetFormat);

        if (isLossy)
        {
            return SolveLossy(bitmap, targetFormat, options, maxBytes, initialDims, warnings);
        }
        else
        {
            return SolveLossless(bitmap, targetFormat, options, maxBytes, initialDims, warnings);
        }
    }

    private SizeConstraintResult SolveLossy(
        SKBitmap bitmap,
        ImageFormat targetFormat,
        ConversionOptions options,
        long maxBytes,
        ImageDimensions initialDims,
        List<string> warnings)
    {
        var low = 1;
        var high = Math.Clamp(options.DefaultQuality, 1, 100);
        byte[]? bestBytes = null;
        var bestQuality = -1;

        // Binary search for highest quality <= maxBytes
        while (low <= high)
        {
            var mid = (low + high) / 2;
            var bytes = _converter.Encode(bitmap, targetFormat, mid);

            if (bytes.Length <= maxBytes)
            {
                bestBytes = bytes;
                bestQuality = mid;
                low = mid + 1; // Try higher quality
            }
            else
            {
                high = mid - 1; // Need lower quality
            }
        }

        if (bestBytes != null)
        {
            return new SizeConstraintResult(
                bestBytes,
                initialDims,
                $"Lossy quality adjusted to {bestQuality}% ({bestBytes.Length:N0} B <= {maxBytes:N0} B)",
                warnings);
        }

        // Even quality 1 is larger than maxBytes
        if (options.FallbackStrategy == FallbackStrategy.ResizeDown)
        {
            return SolveByIterativeScale(bitmap, targetFormat, maxBytes, 40, warnings);
        }

        // Fallback: WarnOnly or Quantize
        var minQualityBytes = _converter.Encode(bitmap, targetFormat, 1);
        warnings.Add($"Target size limit ({maxBytes:N0} bytes) exceeded: minimum quality (1%) resulted in {minQualityBytes.Length:N0} bytes.");
        return new SizeConstraintResult(
            minQualityBytes,
            initialDims,
            $"Minimum quality (1%) limit exceeded ({minQualityBytes.Length:N0} B > {maxBytes:N0} B)",
            warnings);
    }

    private SizeConstraintResult SolveLossless(
        SKBitmap bitmap,
        ImageFormat targetFormat,
        ConversionOptions options,
        long maxBytes,
        ImageDimensions initialDims,
        List<string> warnings)
    {
        var initialBytes = _converter.Encode(bitmap, targetFormat, 100);
        if (initialBytes.Length <= maxBytes)
        {
            return new SizeConstraintResult(initialBytes, initialDims, "Lossless encoding", warnings);
        }

        switch (options.FallbackStrategy)
        {
            case FallbackStrategy.ResizeDown:
                return SolveByIterativeScale(bitmap, targetFormat, maxBytes, 100, warnings);

            case FallbackStrategy.Quantize:
            {
                var colorLevels = new[] { 256, 128, 64, 32, 16 };
                byte[]? smallestData = null;
                var smallestSize = long.MaxValue;

                foreach (var colors in colorLevels)
                {
                    using var quantized = _quantizer.Quantize(bitmap, colors);
                    var qBytes = _converter.Encode(quantized, targetFormat, 100);

                    if (qBytes.Length < smallestSize)
                    {
                        smallestSize = qBytes.Length;
                        smallestData = qBytes;
                    }

                    if (qBytes.Length <= maxBytes)
                    {
                        return new SizeConstraintResult(
                            qBytes,
                            initialDims,
                            $"Color quantization ({colors} colors: {qBytes.Length:N0} B <= {maxBytes:N0} B)",
                            warnings);
                    }
                }

                // If none met the limit, return smallest attempt with warning
                warnings.Add($"Target size limit ({maxBytes:N0} bytes) could not be met by color quantization alone (smallest: {smallestSize:N0} bytes).");
                return new SizeConstraintResult(
                    smallestData ?? initialBytes,
                    initialDims,
                    $"Quantization limit exceeded (smallest: {smallestSize:N0} B > {maxBytes:N0} B)",
                    warnings);
            }

            case FallbackStrategy.WarnOnly:
            default:
            {
                warnings.Add($"File size {initialBytes.Length:N0} bytes exceeds target limit of {maxBytes:N0} bytes; no size reduction fallback applied.");
                return new SizeConstraintResult(
                    initialBytes,
                    initialDims,
                    $"Lossless encoding (size limit exceeded: {initialBytes.Length:N0} B > {maxBytes:N0} B)",
                    warnings);
            }
        }
    }

    private SizeConstraintResult SolveByIterativeScale(
        SKBitmap bitmap,
        ImageFormat targetFormat,
        long maxBytes,
        int quality,
        List<string> warnings)
    {
        var lowScale = 0.05;
        var highScale = 0.95;
        byte[]? bestBytes = null;
        var bestDims = new ImageDimensions(bitmap.Width, bitmap.Height);
        var bestScale = 0.0;

        for (var step = 0; step < 10; step++)
        {
            var midScale = (lowScale + highScale) / 2.0;
            using var scaled = _resizer.ResizeByScale(bitmap, midScale);
            var scaledBytes = _converter.Encode(scaled, targetFormat, quality);

            if (scaledBytes.Length <= maxBytes)
            {
                bestBytes = scaledBytes;
                bestDims = new ImageDimensions(scaled.Width, scaled.Height);
                bestScale = midScale;
                lowScale = midScale + 0.02; // Can we preserve a bit more resolution?
            }
            else
            {
                highScale = midScale - 0.02; // Need to shrink more
            }

            if (lowScale > highScale) break;
        }

        if (bestBytes != null)
        {
            return new SizeConstraintResult(
                bestBytes,
                bestDims,
                $"Scaled resolution to {bestDims} (approx. {bestScale * 100:N0}%, {bestBytes.Length:N0} B <= {maxBytes:N0} B)",
                warnings);
        }

        // Even smallest scale didn't meet constraint
        using var minScaled = _resizer.ResizeByScale(bitmap, 0.05);
        var fallbackBytes = _converter.Encode(minScaled, targetFormat, quality);
        warnings.Add($"Even downscaling to {minScaled.Width}x{minScaled.Height} exceeded {maxBytes:N0} bytes ({fallbackBytes.Length:N0} bytes).");
        return new SizeConstraintResult(
            fallbackBytes,
            new ImageDimensions(minScaled.Width, minScaled.Height),
            "Downscaling limit reached",
            warnings);
    }
}
