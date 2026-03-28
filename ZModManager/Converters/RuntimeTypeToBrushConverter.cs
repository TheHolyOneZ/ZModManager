using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using ZModManager.Models;

namespace ZModManager.Converters;

[ValueConversion(typeof(RuntimeType), typeof(Brush))]
public class RuntimeTypeToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Mono    = new(Color.FromRgb(0x4F, 0xC3, 0xF7)); // sky blue
    private static readonly SolidColorBrush IL2CPP  = new(Color.FromRgb(0xFF, 0x8A, 0x65)); // warm orange
    private static readonly SolidColorBrush Unknown = new(Color.FromRgb(0x4A, 0x4A, 0x6A)); // dim purple-grey

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is RuntimeType r ? r switch
        {
            RuntimeType.Mono   => Mono,
            RuntimeType.IL2CPP => IL2CPP,
            _                  => Unknown
        } : Unknown;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
