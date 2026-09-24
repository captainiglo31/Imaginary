using CommunityToolkit.Mvvm.ComponentModel;
using Imaginary.Core.Models;

namespace Imaginary.Desktop.Models;

public partial class IconTemplateItemViewModel : ObservableObject
{
    public IconTemplateItem Item { get; }

    [ObservableProperty]
    private bool _isSelected;

    public IconTemplateItemViewModel(IconTemplateItem item)
    {
        Item = item;
        _isSelected = item.DefaultSelected;
    }

    public string Id => Item.Id;
    public string FileName => Item.FileName;
    public string DisplayText => Item.DisplayText;
    public string CategoryName => Item.CategoryName;
    public IconTargetPlatform Platform => Item.Platform;
    public int Width => Item.Width;
    public int Height => Item.Height;
    public string Format => Item.Format;
}
