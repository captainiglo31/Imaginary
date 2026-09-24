using System.Collections.Generic;
using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public interface IIconGeneratorService
{
    IReadOnlyList<IconTemplateItem> GetAvailableTemplates();
    IReadOnlyList<IconTemplateItem> GetTemplatesForPlatform(IconTargetPlatform platform);

    IconGenerationResult GenerateIconPackage(
        SKBitmap sourceBitmap,
        IconGenerationOptions options);

    SKBitmap RenderSingleIcon(
        SKBitmap sourceBitmap,
        int width,
        int height,
        IconShape shape = IconShape.Square,
        float paddingPercent = 0f,
        SKColor? backgroundColor = null);
}
