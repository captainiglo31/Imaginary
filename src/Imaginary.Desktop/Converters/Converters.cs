using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Imaginary.Desktop.Models;

namespace Imaginary.Desktop.Converters;

public class StatusToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(46, 125, 50));
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(198, 40, 40));
    private static readonly SolidColorBrush OrangeBrush = new(Color.FromRgb(239, 108, 0));
    private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(117, 117, 117));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is JobStatus status)
        {
            return status switch
            {
                JobStatus.Done => GreenBrush,
                JobStatus.Failed => RedBrush,
                JobStatus.Processing => OrangeBrush,
                _ => GrayBrush
            };
        }
        return GrayBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b) return !b;
        return false;
    }
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            return b ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter != null)
        {
            if (int.TryParse(parameter.ToString(), out int targetVal))
            {
                return intValue == targetVal ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

public class IntToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter != null)
        {
            if (int.TryParse(parameter.ToString(), out int targetVal))
            {
                return intValue == targetVal;
            }
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool b && b && parameter != null)
        {
            if (int.TryParse(parameter.ToString(), out int targetVal))
            {
                return targetVal;
            }
        }
        return Binding.DoNothing;
    }
}

