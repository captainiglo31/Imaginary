namespace Imaginary.Core.Services;

public interface IExplorerIntegration
{
    bool IsRegistered();
    bool Register(string? executablePath = null);
    bool Unregister();
}
