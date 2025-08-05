using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace glFTPd_Commander.Converters
{
    public class IconPathToImageSourceConverter : IValueConverter
    {
        public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string path && !string.IsNullOrWhiteSpace(path))
                return new BitmapImage(new Uri(path));
            // Return a fallback image, or DependencyProperty.UnsetValue to not show anything
            return null; // or return a default icon path here
        }

        public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
