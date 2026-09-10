using System;
using System.IO;
using FluentAssertions;
using Imaginary.Core.Logging;
using Xunit;

namespace Imaginary.Core.Tests;

public class LogServiceTests
{
    [Fact]
    public void LogService_ShouldStoreEntries_AndNotifySubscribers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ImaginaryLogTest_" + Guid.NewGuid());
        var logger = new LogService(maxEntries: 10, customLogDir: tempDir);

        LogEntry? receivedEntry = null;
        logger.EntryLogged += entry => receivedEntry = entry;

        logger.Info("TestModule", "Hello world");

        receivedEntry.Should().NotBeNull();
        receivedEntry!.Level.Should().Be(LogLevel.Information);
        receivedEntry.Source.Should().Be("TestModule");
        receivedEntry.Message.Should().Be("Hello world");
        receivedEntry.ExceptionDetails.Should().BeNull();

        var entries = logger.GetRecentEntries();
        entries.Should().HaveCount(1);
        entries[0].Message.Should().Be("Hello world");

        // Verify file was created
        logger.LogFilePath.Should().NotBeNull();
        File.Exists(logger.LogFilePath!).Should().BeTrue();
        var content = File.ReadAllText(logger.LogFilePath!);
        content.Should().Contain("[INFO ] [TestModule] Hello world");

        // Cleanup
        try { Directory.Delete(tempDir, true); } catch { }
    }

    [Fact]
    public void LogService_ShouldRespectMaxEntries()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ImaginaryLogTest_" + Guid.NewGuid());
        var logger = new LogService(maxEntries: 100, customLogDir: tempDir);

        for (int i = 0; i < 150; i++)
        {
            logger.Debug("Source", $"Message {i}");
        }

        var entries = logger.GetRecentEntries();
        entries.Should().HaveCount(100);
        entries[0].Message.Should().Be("Message 50");
        entries[99].Message.Should().Be("Message 149");

        try { Directory.Delete(tempDir, true); } catch { }
    }

    [Fact]
    public void LogService_ShouldFormatExceptionsProperly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ImaginaryLogTest_" + Guid.NewGuid());
        var logger = new LogService(maxEntries: 10, customLogDir: tempDir);

        try
        {
            throw new InvalidOperationException("Something went wrong!");
        }
        catch (Exception ex)
        {
            logger.Error("Processor", "Processing failed", ex);
        }

        var entries = logger.GetRecentEntries();
        entries.Should().HaveCount(1);
        entries[0].Level.Should().Be(LogLevel.Error);
        entries[0].ExceptionDetails.Should().Contain("InvalidOperationException: Something went wrong!");

        try { Directory.Delete(tempDir, true); } catch { }
    }
}
