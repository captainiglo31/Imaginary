using FluentAssertions;
using Imaginary.Core.Models;
using Imaginary.Core.Services;

namespace Imaginary.Core.Tests;

public class AutostartServiceTests
{
    [Fact]
    public void EnableAutostart_WithNonExistentPath_ReturnsFalse()
    {
        var service = new AutostartService();
        var result = service.EnableAutostart(@"C:\non_existent_imaginary_exe_test_path\imaginary.exe");
        result.Should().BeFalse();
    }

    [Fact]
    public void HotfolderHistoryItem_FormattedProperties_ShouldFormatCorrectly()
    {
        var item = new HotfolderHistoryItem
        {
            Timestamp = new DateTime(2026, 9, 10, 14, 30, 0),
            FileName = "photo.png",
            TargetFileName = "photo.webp",
            OriginalSizeBytes = 1024 * 1024, // 1 MB
            FinalSizeBytes = 256 * 1024,    // 256 KB
            SavingsPercent = 75.0,
            Success = true
        };

        item.FormattedTime.Should().Be("14:30:00");
        item.FormattedOriginalSize.Should().Contain("MB");
        item.FormattedFinalSize.Should().Contain("KB");
        item.FormattedSavings.Should().Be("-75,0 %");
        item.StatusText.Should().Be("Erfolgreich");
    }
}
