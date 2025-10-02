using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NovaGM.Converters
{
    public sealed class ZeroToBoolConverter : IValueConverter
    {
        /// <summary>
        /// When true (default) the converter returns true when the numeric input equals zero.
        /// When false it returns true for non-zero values.
        /// </summary>
        public bool WhenZero { get; set; } = true;

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isZero = value switch
            {
                null => true,
                IConvertible convertible => convertible.ToInt64(culture) == 0,
                _ => false
            };

            return WhenZero ? isZero : !isZero;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
