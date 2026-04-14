using System;
using System.Globalization;
using Microsoft.UI.Xaml.Data;

namespace Vidvix.Views.Converters;

public sealed class TimelineMillisecondsToTimeConverter : IValueConverter
{
    public Func<TimeSpan, string>? Formatter { get; set; }

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var milliseconds = TryConvertToDouble(value);
        if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds))
        {
            milliseconds = 0d;
        }

        var duration = TimeSpan.FromMilliseconds(Math.Max(0d, milliseconds));
        return Formatter?.Invoke(duration) ?? FormatFullTime(duration);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();

    public static string FormatFullTime(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        return $"{(int)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}.{duration.Milliseconds:000}";
    }

    private static double TryConvertToDouble(object value)
    {
        if (value is null)
        {
            return 0d;
        }

        return value switch
        {
            double doubleValue => doubleValue,
            float floatValue => floatValue,
            decimal decimalValue => (double)decimalValue,
            byte byteValue => byteValue,
            sbyte sbyteValue => sbyteValue,
            short shortValue => shortValue,
            ushort ushortValue => ushortValue,
            int intValue => intValue,
            uint uintValue => uintValue,
            long longValue => longValue,
            ulong ulongValue => ulongValue,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => System.Convert.ToDouble(value, CultureInfo.InvariantCulture)
        };
    }
}
