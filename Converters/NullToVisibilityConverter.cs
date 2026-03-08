using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Snappr.Converters
{
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool invert = parameter?.ToString() == "Inverted";
            bool isEmpty = value == null || 
                           (value is int i && i == 0) || 
                           (value is string s && string.IsNullOrEmpty(s));


            if (invert)
            {
                return isEmpty ? Visibility.Visible : Visibility.Collapsed;
            }
            return isEmpty ? Visibility.Collapsed : Visibility.Visible;

        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
