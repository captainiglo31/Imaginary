namespace Imaginary.Core.Models;

public class ProfiPreset
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "📸";
    public ProfiAdjustmentSettings Settings { get; set; } = new();
}
