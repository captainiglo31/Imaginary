using Imaginary.Core.Models;

namespace Imaginary.Core.Services;

public interface IOutputPathResolver
{
    string ResolveOutputPath(string? sourceFilePath, string originalFileName, ImageFormat targetFormat, string? designatedOutputDirectory);
}
