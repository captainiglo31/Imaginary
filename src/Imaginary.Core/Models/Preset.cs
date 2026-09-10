namespace Imaginary.Core.Models;

public class Preset
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public ConversionOptions Options { get; set; } = new();

    public override string ToString() => Name;
}
