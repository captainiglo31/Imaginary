using Imaginary.Core.Models;

namespace Imaginary.Core.Services;

public class OutputPathResolver : IOutputPathResolver
{
    private readonly IImageFormatDetector _formatDetector;

    public OutputPathResolver(IImageFormatDetector formatDetector)
    {
        _formatDetector = formatDetector ?? throw new ArgumentNullException(nameof(formatDetector));
    }

    public string ResolveOutputPath(
        string? sourceFilePath,
        string originalFileName,
        ImageFormat targetFormat,
        string? designatedOutputDirectory)
    {
        var targetDir = designatedOutputDirectory;
        if (string.IsNullOrWhiteSpace(targetDir))
        {
            if (!string.IsNullOrWhiteSpace(sourceFilePath) && File.Exists(sourceFilePath))
            {
                var sourceDir = Path.GetDirectoryName(sourceFilePath) ?? Directory.GetCurrentDirectory();
                targetDir = Path.Combine(sourceDir, "converted");
            }
            else
            {
                targetDir = Path.Combine(Directory.GetCurrentDirectory(), "converted");
            }
        }

        Directory.CreateDirectory(targetDir);

        var baseFileName = Path.GetFileNameWithoutExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(baseFileName))
        {
            baseFileName = "image";
        }

        var newExtension = _formatDetector.GetDefaultExtension(targetFormat);
        var targetFileName = $"{baseFileName}{newExtension}";
        var fullTargetPath = Path.Combine(targetDir, targetFileName);

        // Prevent overwriting original file or collision with existing output file
        var collisionCounter = 1;
        while (File.Exists(fullTargetPath) || (sourceFilePath != null && string.Equals(Path.GetFullPath(fullTargetPath), Path.GetFullPath(sourceFilePath), StringComparison.OrdinalIgnoreCase)))
        {
            targetFileName = $"{baseFileName}_{collisionCounter}{newExtension}";
            fullTargetPath = Path.Combine(targetDir, targetFileName);
            collisionCounter++;
        }

        return fullTargetPath;
    }
}
