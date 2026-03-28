using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ZModManager.Models;

namespace ZModManager.Converters;

[ValueConversion(typeof(LogLevel), typeof(Brush))]
public class LogLevelToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush Info    = new(Color.FromRgb(0xCC, 0xCC, 0xCC));
    private static readonly SolidColorBrush Success = new(Color.FromRgb(0x6A, 0x99, 0x55));
    private static readonly SolidColorBrush Warning = new(Color.FromRgb(0xDC, 0xDC, 0xAA));
    private static readonly SolidColorBrush Error   = new(Color.FromRgb(0xF4, 0x47, 0x47));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is LogLevel l ? l switch
        {
            LogLevel.Success => Success,
            LogLevel.Warning => Warning,
            LogLevel.Error   => Error,
            _                => Info
        } : Info;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
