using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public interface ISocialMediaTemplateService
{
    IReadOnlyList<SocialMediaTemplate> GetAllTemplates();
    IReadOnlyList<string> GetPlatforms();
    IReadOnlyList<SocialMediaTemplate> GetTemplatesByPlatform(string platform);
    SocialMediaTemplate? GetTemplateById(string id);

    SKBitmap ApplyTemplate(
        SKBitmap source,
        SocialMediaTemplate template,
        SocialMediaFitMode fitMode = SocialMediaFitMode.FillCrop,
        SKColor? padColor = null);
}
