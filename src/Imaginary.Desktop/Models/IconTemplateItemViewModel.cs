using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Imaginary.Core.Models;

namespace Imaginary.Desktop.Models;

public partial class IconTemplateItemViewModel : ObservableObject
{
    public IconTemplateItem Item { get; }

    [ObservableProperty]
    private bool _isSelected;

    public event Action? SelectionChanged;

    public IconTemplateItemViewModel(IconTemplateItem item)
    {
        Item = item;
        _isSelected = item.DefaultSelected;
    }

    partial void OnIsSelectedChanged(bool value)
    {
        SelectionChanged?.Invoke();
    }

    public string Id => Item.Id;
    public string FileName => Item.FileName;
    public string DisplayText => Item.DisplayText;
    public string Category => Item.CategoryName;
    public string CategoryName => Item.CategoryName;
    public IconTargetPlatform Platform => Item.Platform;
    public int Width => Item.Width;
    public int Height => Item.Height;
    public string Format => Item.Format;
    public string RelativeFolder => Item.RelativeFolder;
    public string Description => Item.Description;
    public string RelativePath => Item.RelativePath;

    public string DimensionsText => Width > 0 ? $"{Width}×{Height} px" : "Format";
    public string BadgeText => $"{Format.ToUpperInvariant()} • {Width}×{Height}";
    public string PlatformBadge => Platform switch
    {
        IconTargetPlatform.Windows => "🪟 Windows",
        IconTargetPlatform.WebFavicon => "🌐 Web",
        IconTargetPlatform.AppleIosMac => "🍎 Apple",
        IconTargetPlatform.Android => "🤖 Android",
        _ => "📁 Icon"
    };

    public string PathAndDescription => !string.IsNullOrEmpty(Item.RelativeFolder)
        ? $"{Item.RelativeFolder} — {Item.Description}"
        : Item.Description;
}
