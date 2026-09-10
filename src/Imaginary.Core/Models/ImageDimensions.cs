namespace Imaginary.Core.Models;

public readonly record struct ImageDimensions(int Width, int Height)
{
    public override string ToString() => $"{Width} x {Height}";
}
