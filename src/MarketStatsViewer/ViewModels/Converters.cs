using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace MarketStatsViewer.ViewModels;

/// <summary>
/// Конвертер цвета для отображения изменения цены:
/// зеленый при росте (> 0), красный при падении (< 0), серый при отсутствии изменений.
/// </summary>
public class PriceChangeColorConverter : IValueConverter
{
    private static readonly SolidColorBrush GreenBrush = new(Color.FromRgb(56, 161, 105));
    private static readonly SolidColorBrush RedBrush = new(Color.FromRgb(229, 62, 62));
    private static readonly SolidColorBrush GrayBrush = new(Color.FromRgb(160, 174, 192));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            if (d > 0.0001) return GreenBrush;
            if (d < -0.0001) return RedBrush;
        }
        return GrayBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>
/// Конвертер для форматирования чисел со знаком плюс/минус (например, "+45" или "-12").
/// </summary>
public class SignedNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double d)
        {
            return d > 0 ? $"+{d:F2}" : $"{d:F2}";
        }
        if (value is int i)
        {
            return i > 0 ? $"+{i}" : $"{i}";
        }
        return value?.ToString() ?? "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}

/// <summary>
/// Конвертер строкового hex-кода цвета ("#38A169") в WPF SolidColorBrush.
/// </summary>
public class HexToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch
            {
                // Fallback
            }
        }
        return new SolidColorBrush(Color.FromRgb(113, 128, 150));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
}
