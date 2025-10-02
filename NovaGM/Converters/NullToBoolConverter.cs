using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace NovaGM.Converters
{
    public sealed class NullToBoolConverter : IValueConverter
    {
        /// <summary>
        /// When set to true the converter returns true when the input is null.
        /// Defaults to false so null values map to false.
        /// </summary>
        public bool WhenNull { get; set; }

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var isNull = value is null;
            return WhenNull ? isNull : !isNull;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
