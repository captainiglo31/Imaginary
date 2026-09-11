using FluentAssertions;
using Imaginary.Core.Services;

namespace Imaginary.Core.Tests;

public class StartupHealthTrackerTests
{
    [Fact]
    public void StartupHealthTracker_ShouldDetectCrashLoop_AfterMultipleUnhealthyStarts()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"imaginary_health_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var healthFile = Path.Combine(tempDir, "startup_health.json");

        try
        {
            // First start: app starts up
            var tracker1 = new StartupHealthTracker(healthFile);
            tracker1.IsCrashLoopDetected.Should().BeFalse();
            tracker1.RecordStartup();
            tracker1.CrashCount.Should().Be(0);

            // Crash happens before RecordHealthy() is called!

            // Second start:
            var tracker2 = new StartupHealthTracker(healthFile);
            tracker2.RecordStartup();
            tracker2.CrashCount.Should().Be(1);
            tracker2.IsCrashLoopDetected.Should().BeFalse();

            // Crash happens again!

            // Third start:
            var tracker3 = new StartupHealthTracker(healthFile);
            tracker3.RecordStartup();
            tracker3.CrashCount.Should().Be(2);
            tracker3.IsCrashLoopDetected.Should().BeTrue();

            // Now healthy run:
            tracker3.RecordHealthy();
            tracker3.CrashCount.Should().Be(0);
            tracker3.IsCrashLoopDetected.Should().BeFalse();

            // Next run is healthy again:
            var tracker4 = new StartupHealthTracker(healthFile);
            tracker4.RecordStartup();
            tracker4.CrashCount.Should().Be(0);
            tracker4.IsCrashLoopDetected.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void StartupHealthTracker_Reset_ShouldClearCrashes()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"imaginary_health_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var healthFile = Path.Combine(tempDir, "startup_health.json");

        try
        {
            var tracker = new StartupHealthTracker(healthFile);
            tracker.RecordStartup();
            // simulate crash
            tracker.RecordStartup();
            // simulate crash
            tracker.RecordStartup();

            tracker.IsCrashLoopDetected.Should().BeTrue();

            tracker.Reset();

            tracker.CrashCount.Should().Be(0);
            tracker.IsCrashLoopDetected.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public void StartupHealthTracker_AfterRollback_ShouldStartHealthy()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"imaginary_health_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var healthFile = Path.Combine(tempDir, "startup_health.json");

        try
        {
            var tracker = new StartupHealthTracker(healthFile);
            tracker.RecordStartup();
            tracker.RecordStartup();
            tracker.RecordStartup();
            tracker.IsCrashLoopDetected.Should().BeTrue();

            // Simulate rollback reset:
            var rollbackTracker = new StartupHealthTracker(healthFile);
            rollbackTracker.Reset();

            // Next launch (e.g. rolled back executable):
            var freshTracker = new StartupHealthTracker(healthFile);
            freshTracker.IsCrashLoopDetected.Should().BeFalse();
            freshTracker.RecordStartup();
            freshTracker.CrashCount.Should().Be(0);
            freshTracker.IsCrashLoopDetected.Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
