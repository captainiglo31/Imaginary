using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;
using SkiaSharp;
using Xunit;

namespace Imaginary.Core.Tests;

public class SocialMediaTemplateServiceTests
{
    private readonly SocialMediaTemplateService _service = new();

    [Fact]
    public void GetAllTemplates_ShouldContainStandardPlatforms()
    {
        var templates = _service.GetAllTemplates();
        templates.Should().NotBeEmpty();

        var platforms = _service.GetPlatforms();
        platforms.Should().Contain(new[] { "Instagram", "YouTube", "TikTok", "LinkedIn", "X (Twitter)", "Facebook" });
    }

    [Theory]
    [InlineData("instagram_post_square", 1080, 1080)]
    [InlineData("instagram_story_reel", 1080, 1920)]
    [InlineData("youtube_thumbnail", 1280, 720)]
    [InlineData("linkedin_banner_company", 1584, 396)]
    public void GetTemplateById_ShouldReturnCorrectDimensions(string id, int expectedW, int expectedH)
    {
        var template = _service.GetTemplateById(id);
        template.Should().NotBeNull();
        template!.Width.Should().Be(expectedW);
        template.Height.Should().Be(expectedH);
    }

    [Fact]
    public void ApplyTemplate_FillCrop_ShouldProduceExactDimensions()
    {
        using var source = new SKBitmap(800, 600);
        var template = _service.GetTemplateById("instagram_post_square")!;

        using var result = _service.ApplyTemplate(source, template, SocialMediaFitMode.FillCrop);

        result.Width.Should().Be(1080);
        result.Height.Should().Be(1080);
    }

    [Fact]
    public void ApplyTemplate_PadBlur_ShouldProduceExactDimensions()
    {
        using var source = new SKBitmap(1920, 1080);
        var template = _service.GetTemplateById("instagram_story_reel")!;

        using var result = _service.ApplyTemplate(source, template, SocialMediaFitMode.PadBlur);

        result.Width.Should().Be(1080);
        result.Height.Should().Be(1920);
    }
}
