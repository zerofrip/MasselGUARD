using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MasselGUARD
{
    /// <summary>Converts a <c>double</c> pixel value to a pixel-unit <see cref="GridLength"/>.</summary>
    [ValueConversion(typeof(double), typeof(GridLength))]
    public class PixelGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is double d ? new GridLength(d) : GridLength.Auto;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is GridLength gl ? gl.Value : 0.0;
    }
}
