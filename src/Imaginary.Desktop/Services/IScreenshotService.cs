using System.Threading.Tasks;

namespace Imaginary.Desktop.Services;

public interface IScreenshotService
{
    /// <summary>
    /// Captures a selected screen region via an interactive snipping overlay.
    /// Saves the result as PNG and places it onto the clipboard.
    /// Returns the absolute path to the saved screenshot file, or null if cancelled.
    /// </summary>
    Task<string?> CaptureRegionAsync();
}
