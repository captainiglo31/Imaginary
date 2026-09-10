using SkiaSharp;

namespace Imaginary.Core.Services;

public class OctreeQuantizer : IColorQuantizer
{
    private const int MaxDepth = 7;

    private class OctreeNode
    {
        public bool IsLeaf { get; set; }
        public long RedSum { get; set; }
        public long GreenSum { get; set; }
        public long BlueSum { get; set; }
        public int PixelCount { get; set; }
        public OctreeNode?[] Children { get; } = new OctreeNode?[8];
        public OctreeNode? NextReducible { get; set; }

        public SKColor Color => PixelCount == 0
            ? SKColors.Black
            : new SKColor(
                (byte)(RedSum / PixelCount),
                (byte)(GreenSum / PixelCount),
                (byte)(BlueSum / PixelCount));
    }

    public SKBitmap Quantize(SKBitmap source, int maxColors = 256)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (maxColors <= 0) maxColors = 256;

        var root = new OctreeNode();
        var reducibleNodes = new OctreeNode?[MaxDepth];
        var leafCount = 0;

        // Step 1: Build octree by sampling pixels
        // For performance on large images, sample every step if > 1MP
        var totalPixels = source.Width * source.Height;
        var sampleStep = totalPixels > 1_000_000 ? 2 : 1;

        for (var y = 0; y < source.Height; y += sampleStep)
        {
            for (var x = 0; x < source.Width; x += sampleStep)
            {
                var color = source.GetPixel(x, y);
                // Skip fully transparent pixels
                if (color.Alpha < 32) continue;

                AddColor(root, color.Red, color.Green, color.Blue, 0, reducibleNodes, ref leafCount);

                while (leafCount > maxColors)
                {
                    ReduceTree(reducibleNodes, ref leafCount);
                }
            }
        }

        // Step 2: Remap all pixels to quantized colors
        var result = new SKBitmap(source.Width, source.Height, source.ColorType, source.AlphaType);

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                var color = source.GetPixel(x, y);
                if (color.Alpha < 32)
                {
                    result.SetPixel(x, y, SKColors.Transparent);
                }
                else
                {
                    var quantizedColor = GetQuantizedColor(root, color.Red, color.Green, color.Blue, 0);
                    // Retain original alpha
                    result.SetPixel(x, y, quantizedColor.WithAlpha(color.Alpha));
                }
            }
        }

        return result;
    }

    private static void AddColor(OctreeNode node, byte r, byte g, byte b, int level, OctreeNode?[] reducibleNodes, ref int leafCount)
    {
        if (level == MaxDepth)
        {
            if (!node.IsLeaf)
            {
                node.IsLeaf = true;
                leafCount++;
            }
            node.RedSum += r;
            node.GreenSum += g;
            node.BlueSum += b;
            node.PixelCount++;
            return;
        }

        var shift = 7 - level;
        var index = (((r >> shift) & 1) << 2) |
                    (((g >> shift) & 1) << 1) |
                    ((b >> shift) & 1);

        if (node.Children[index] == null)
        {
            var child = new OctreeNode();
            node.Children[index] = child;

            if (level < MaxDepth - 1)
            {
                child.NextReducible = reducibleNodes[level];
                reducibleNodes[level] = child;
            }
        }

        var nextChild = node.Children[index]!;
        if (nextChild.IsLeaf)
        {
            nextChild.RedSum += r;
            nextChild.GreenSum += g;
            nextChild.BlueSum += b;
            nextChild.PixelCount++;
        }
        else
        {
            AddColor(nextChild, r, g, b, level + 1, reducibleNodes, ref leafCount);
        }
    }

    private static void ReduceTree(OctreeNode?[] reducibleNodes, ref int leafCount)
    {
        // Find deepest reducible node
        var level = MaxDepth - 1;
        while (level >= 0 && reducibleNodes[level] == null)
        {
            level--;
        }

        if (level < 0) return;

        var node = reducibleNodes[level]!;
        reducibleNodes[level] = node.NextReducible;

        long rSum = 0, gSum = 0, bSum = 0;
        var pCount = 0;
        var childrenRemoved = 0;

        for (var i = 0; i < 8; i++)
        {
            if (node.Children[i] != null)
            {
                rSum += node.Children[i]!.RedSum;
                gSum += node.Children[i]!.GreenSum;
                bSum += node.Children[i]!.BlueSum;
                pCount += node.Children[i]!.PixelCount;
                childrenRemoved++;
                node.Children[i] = null;
            }
        }

        node.IsLeaf = true;
        node.RedSum = rSum;
        node.GreenSum = gSum;
        node.BlueSum = bSum;
        node.PixelCount = pCount;

        leafCount -= (childrenRemoved - 1);
    }

    private static SKColor GetQuantizedColor(OctreeNode node, byte r, byte g, byte b, int level)
    {
        if (node.IsLeaf)
        {
            return node.Color;
        }

        var shift = 7 - level;
        var index = (((r >> shift) & 1) << 2) |
                    (((g >> shift) & 1) << 1) |
                    ((b >> shift) & 1);

        if (node.Children[index] != null)
        {
            return GetQuantizedColor(node.Children[index]!, r, g, b, level + 1);
        }

        // Fallback to average of existing children if branch missing
        foreach (var child in node.Children)
        {
            if (child != null)
            {
                return GetQuantizedColor(child, r, g, b, level + 1);
            }
        }

        return node.Color;
    }
}
