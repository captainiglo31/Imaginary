namespace Imaginary.Core.Services;

public interface IStartMenuIntegration
{
    bool IsRegistered();
    bool Register(string? executablePath = null);
    bool Unregister();
    void Synchronize(bool shouldBeEnabled, string? executablePath = null);
    string GetShortcutPath();
}
