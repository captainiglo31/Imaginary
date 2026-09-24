using Imaginary.Core.Models;
using SkiaSharp;

namespace Imaginary.Core.Services;

public class SocialMediaTemplateService : ISocialMediaTemplateService
{
    private static readonly List<SocialMediaTemplate> Templates = new()
    {
        // Instagram
        new SocialMediaTemplate
        {
            Id = "instagram_post_square",
            Platform = "Instagram",
            Name = "Post (Quadrat 1:1)",
            Width = 1080,
            Height = 1080,
            AspectRatioText = "1:1",
            Description = "Standard-Format für Instagram Feed-Beiträge",
            Icon = "📸"
        },
        new SocialMediaTemplate
        {
            Id = "instagram_post_portrait",
            Platform = "Instagram",
            Name = "Post Porträt (4:5)",
            Width = 1080,
            Height = 1350,
            AspectRatioText = "4:5",
            Description = "Optimal für maximale Aufmerksamkeit im Feed auf Smartphones",
            Icon = "📸"
        },
        new SocialMediaTemplate
        {
            Id = "instagram_post_landscape",
            Platform = "Instagram",
            Name = "Post Querformat (1.91:1)",
            Width = 1080,
            Height = 566,
            AspectRatioText = "1.91:1",
            Description = "Klassisches Querformat für Fotos und Landschaftsbilder",
            Icon = "📸"
        },
        new SocialMediaTemplate
        {
            Id = "instagram_story_reel",
            Platform = "Instagram",
            Name = "Story & Reel (9:16)",
            Width = 1080,
            Height = 1920,
            AspectRatioText = "9:16",
            Description = "Vollbild-Format für Instagram Stories und Reels",
            Icon = "📱"
        },
        new SocialMediaTemplate
        {
            Id = "instagram_profile",
            Platform = "Instagram",
            Name = "Profilbild (1:1)",
            Width = 320,
            Height = 320,
            AspectRatioText = "1:1",
            Description = "Rund zugeschnittenes Profilbild für Konten",
            Icon = "👤"
        },

        // YouTube
        new SocialMediaTemplate
        {
            Id = "youtube_thumbnail",
            Platform = "YouTube",
            Name = "Video Thumbnail (16:9)",
            Width = 1280,
            Height = 720,
            AspectRatioText = "16:9",
            Description = "Offizielle HD-Vorschaugrafik für YouTube Videos",
            Icon = "▶️"
        },
        new SocialMediaTemplate
        {
            Id = "youtube_banner",
            Platform = "YouTube",
            Name = "Kanal-Banner (16:9)",
            Width = 2560,
            Height = 1440,
            AspectRatioText = "16:9",
            Description = "Header-Grafik für Kanalseiten auf Desktop, Tablet und TV",
            Icon = "▶️"
        },
        new SocialMediaTemplate
        {
            Id = "youtube_shorts",
            Platform = "YouTube",
            Name = "Shorts Cover (9:16)",
            Width = 1080,
            Height = 1920,
            AspectRatioText = "9:16",
            Description = "Vollbild-Vorschau für YouTube Shorts",
            Icon = "📱"
        },
        new SocialMediaTemplate
        {
            Id = "youtube_profile",
            Platform = "YouTube",
            Name = "Kanal-Icon (1:1)",
            Width = 800,
            Height = 800,
            AspectRatioText = "1:1",
            Description = "Rundes Kanal-Logo & Avatar",
            Icon = "👤"
        },

        // TikTok
        new SocialMediaTemplate
        {
            Id = "tiktok_video",
            Platform = "TikTok",
            Name = "Video Cover & Story (9:16)",
            Width = 1080,
            Height = 1920,
            AspectRatioText = "9:16",
            Description = "Vertikales Vollbild für TikTok Feeds",
            Icon = "🎵"
        },
        new SocialMediaTemplate
        {
            Id = "tiktok_profile",
            Platform = "TikTok",
            Name = "Profilbild (1:1)",
            Width = 200,
            Height = 200,
            AspectRatioText = "1:1",
            Description = "TikTok Avatar-Grafik",
            Icon = "👤"
        },

        // LinkedIn
        new SocialMediaTemplate
        {
            Id = "linkedin_post",
            Platform = "LinkedIn",
            Name = "Feed Post (1.91:1)",
            Width = 1200,
            Height = 627,
            AspectRatioText = "1.91:1",
            Description = "Professioneller Beitrag für den LinkedIn Desktop- & Mobile-Feed",
            Icon = "💼"
        },
        new SocialMediaTemplate
        {
            Id = "linkedin_banner_company",
            Platform = "LinkedIn",
            Name = "Unternehmens-Banner (4:1)",
            Width = 1584,
            Height = 396,
            AspectRatioText = "4:1",
            Description = "Offizielles Header-Bild für Unternehmensseiten und Profile",
            Icon = "💼"
        },
        new SocialMediaTemplate
        {
            Id = "linkedin_profile",
            Platform = "LinkedIn",
            Name = "Profilbild (1:1)",
            Width = 400,
            Height = 400,
            AspectRatioText = "1:1",
            Description = "Professionelles Business-Porträt",
            Icon = "👤"
        },

        // X (Twitter)
        new SocialMediaTemplate
        {
            Id = "x_post",
            Platform = "X (Twitter)",
            Name = "Post Bild (16:9)",
            Width = 1200,
            Height = 675,
            AspectRatioText = "16:9",
            Description = "Standard-Format für Tweets & Medienanhänge",
            Icon = "🐦"
        },
        new SocialMediaTemplate
        {
            Id = "x_header",
            Platform = "X (Twitter)",
            Name = "Header Banner (3:1)",
            Width = 1500,
            Height = 500,
            AspectRatioText = "3:1",
            Description = "Kopfzeilen-Hintergrund für X-Profile",
            Icon = "🐦"
        },

        // Facebook
        new SocialMediaTemplate
        {
            Id = "facebook_post",
            Platform = "Facebook",
            Name = "Feed Post (1.91:1)",
            Width = 1200,
            Height = 630,
            AspectRatioText = "1.91:1",
            Description = "Optimale Vorschau für geteilte Links und Feed-Fotos",
            Icon = "👥"
        },
        new SocialMediaTemplate
        {
            Id = "facebook_story",
            Platform = "Facebook",
            Name = "Story (9:16)",
            Width = 1080,
            Height = 1920,
            AspectRatioText = "9:16",
            Description = "Vollbild Story-Beitrag",
            Icon = "📱"
        },
        new SocialMediaTemplate
        {
            Id = "facebook_cover",
            Platform = "Facebook",
            Name = "Titelbild / Cover",
            Width = 820,
            Height = 312,
            AspectRatioText = "2.63:1",
            Description = "Header für Profile und Gruppen",
            Icon = "👥"
        },

        // Pinterest
        new SocialMediaTemplate
        {
            Id = "pinterest_pin",
            Platform = "Pinterest",
            Name = "Standard Pin (2:3)",
            Width = 1000,
            Height = 1500,
            AspectRatioText = "2:3",
            Description = "Empfohlenes vertikales Seitenverhältnis für hohe Klickraten",
            Icon = "📌"
        }
    };

    public IReadOnlyList<SocialMediaTemplate> GetAllTemplates() => Templates;

    public IReadOnlyList<string> GetPlatforms() => Templates.Select(t => t.Platform).Distinct().ToList();

    public IReadOnlyList<SocialMediaTemplate> GetTemplatesByPlatform(string platform) =>
        Templates.Where(t => t.Platform.Equals(platform, StringComparison.OrdinalIgnoreCase)).ToList();

    public SocialMediaTemplate? GetTemplateById(string id) =>
        Templates.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public SKBitmap ApplyTemplate(
        SKBitmap source,
        SocialMediaTemplate template,
        SocialMediaFitMode fitMode = SocialMediaFitMode.FillCrop,
        SKColor? padColor = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(template);

        int targetWidth = template.Width;
        int targetHeight = template.Height;

        var result = new SKBitmap(targetWidth, targetHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(result);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            FilterQuality = SKFilterQuality.High
        };

        if (fitMode == SocialMediaFitMode.FillCrop)
        {
            // Scale up to completely cover the canvas, crop overflow centered
            float scale = Math.Max((float)targetWidth / source.Width, (float)targetHeight / source.Height);
            float scaledW = source.Width * scale;
            float scaledH = source.Height * scale;
            float destX = (targetWidth - scaledW) / 2f;
            float destY = (targetHeight - scaledH) / 2f;

            canvas.Clear(SKColors.Transparent);
            canvas.DrawBitmap(source, new SKRect(destX, destY, destX + scaledW, destY + scaledH), paint);
        }
        else if (fitMode == SocialMediaFitMode.PadBlur)
        {
            // 1. Draw blurred & darkened background covering full target
            float bgScale = Math.Max((float)targetWidth / source.Width, (float)targetHeight / source.Height) * 1.1f;
            float bgW = source.Width * bgScale;
            float bgH = source.Height * bgScale;
            float bgX = (targetWidth - bgW) / 2f;
            float bgY = (targetHeight - bgH) / 2f;

            using var blurPaint = new SKPaint
            {
                IsAntialias = true,
                FilterQuality = SKFilterQuality.Medium,
                ImageFilter = SKImageFilter.CreateBlur(35, 35)
            };

            canvas.DrawBitmap(source, new SKRect(bgX, bgY, bgX + bgW, bgY + bgH), blurPaint);

            // Subtle dark overlay for contrast
            using var overlayPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 80)
            };
            canvas.DrawRect(0, 0, targetWidth, targetHeight, overlayPaint);

            // 2. Draw sharp source image fit into target
            float fitScale = Math.Min((float)targetWidth / source.Width, (float)targetHeight / source.Height);
            float fitW = source.Width * fitScale;
            float fitH = source.Height * fitScale;
            float fitX = (targetWidth - fitW) / 2f;
            float fitY = (targetHeight - fitH) / 2f;

            // Subtle drop shadow behind the sharp image
            using var shadowPaint = new SKPaint
            {
                Color = new SKColor(0, 0, 0, 110),
                ImageFilter = SKImageFilter.CreateDropShadow(0, 6, 12, 12, new SKColor(0, 0, 0, 120))
            };
            canvas.DrawRect(fitX, fitY, fitW, fitH, shadowPaint);

            canvas.DrawBitmap(source, new SKRect(fitX, fitY, fitX + fitW, fitY + fitH), paint);
        }
        else // Pad with solid color
        {
            var bg = padColor ?? SKColors.White;
            canvas.Clear(bg);

            float scale = Math.Min((float)targetWidth / source.Width, (float)targetHeight / source.Height);
            float scaledW = source.Width * scale;
            float scaledH = source.Height * scale;
            float destX = (targetWidth - scaledW) / 2f;
            float destY = (targetHeight - scaledH) / 2f;

            canvas.DrawBitmap(source, new SKRect(destX, destY, destX + scaledW, destY + scaledH), paint);
        }

        canvas.Flush();
        return result;
    }
}
