using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace glFTPd_Commander.Converters
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }
        public bool CollapseInsteadOfHide { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = false;

            if (value is bool)
                flag = (bool)value;

            if (Invert)
                flag = !flag;

            return flag
                ? Visibility.Visible
                : (CollapseInsteadOfHide ? Visibility.Collapsed : Visibility.Hidden);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Visibility visibility)
            {
                bool result = visibility == Visibility.Visible;
                return Invert ? !result : result;
            }
            return false;
        }
    }
}
