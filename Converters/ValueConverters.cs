using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FaceSearchApp.Converters
{
    /// null / false → Collapsed,  non-null / true → Visible
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
            => v is true ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => v is Visibility.Visible;
    }

    /// true → Collapsed,  false → Visible
    [ValueConversion(typeof(bool), typeof(Visibility))]
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
            => v is true ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => v is not Visibility.Visible;
    }

    /// null → Collapsed,  non-null → Visible
    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
            => v is null ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => throw new NotSupportedException();
    }

    /// non-null → Collapsed,  null → Visible
    public class NotNullToVisibilityConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
            => v is null ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => throw new NotSupportedException();
    }

    /// float score → SolidColorBrush  (green / orange / red)
    [ValueConversion(typeof(float), typeof(SolidColorBrush))]
    public class ScoreToForegroundConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
        {
            var score = v is float f ? f : 0f;
            var color = score >= 0.80f ? Color.FromRgb(0x4C, 0xAF, 0x50)   // green
                      : score >= 0.65f ? Color.FromRgb(0xFF, 0xA7, 0x26)   // orange
                                       : Color.FromRgb(0xEF, 0x53, 0x50);  // red
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => throw new NotSupportedException();
    }

    /// float score → progress bar background brush
    [ValueConversion(typeof(float), typeof(SolidColorBrush))]
    public class ScoreToProgressBrushConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
        {
            var score = v is float f ? f : 0f;
            var color = score >= 0.80f ? Color.FromRgb(0x4C, 0xAF, 0x50)
                      : score >= 0.65f ? Color.FromRgb(0xFF, 0xA7, 0x26)
                                       : Color.FromRgb(0xEF, 0x53, 0x50);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => throw new NotSupportedException();
    }

    /// float (0-1) → int (0-100) for ProgressBar
    [ValueConversion(typeof(float), typeof(int))]
    public class ScoreToPercentConverter : IValueConverter
    {
        public object Convert(object? v, Type t, object? p, CultureInfo c)
            => (int)((v is float f ? f : 0f) * 100);

        public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
            => throw new NotSupportedException();
    }
}
