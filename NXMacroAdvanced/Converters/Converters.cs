using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace NXMacroAdvanced.Converters
{
    public class BoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            bool b = v is bool val && val;
            bool inv = p?.ToString() == "inverse";
            return (b ^ inv) ? Visibility.Visible : Visibility.Collapsed;
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => v is Visibility vis && vis == Visibility.Visible;
    }

    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c) => v is bool b && !b;
        public object ConvertBack(object v, Type t, object p, CultureInfo c) => v is bool b && !b;
    }

    public class ConnectionStatusToColorConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v is bool b && b
                ? new SolidColorBrush(Color.FromRgb(105, 240, 174))
                : new SolidColorBrush(Color.FromRgb(255, 82,  82));
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class EnumEqualityConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
            => v?.ToString() == p?.ToString();
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
        {
            if (v is bool b && b && p != null)
                return Enum.Parse(t, p.ToString()!);
            return Binding.DoNothing;
        }
    }

    public class StringToVisibilityConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
            => string.IsNullOrEmpty(v?.ToString()) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class BoolToBrushConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            bool b = v is bool val && val;
            var parts = p?.ToString()?.Split('|') ?? new[] { "#00000000", "#00000000" };
            string hex = b ? (parts.Length > 0 ? parts[0] : "#00000000")
                           : (parts.Length > 1 ? parts[1] : "#00000000");
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return Brushes.Transparent; }
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class BoolToStringConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            bool b = v is bool val && val;
            var parts = p?.ToString()?.Split('|') ?? new[] { "True", "False" };
            return b ? (parts.Length > 0 ? parts[0] : "True")
                     : (parts.Length > 1 ? parts[1] : "False");
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class BoolToIconConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            bool b = v is bool val && val;
            var parts = p?.ToString()?.Split('|') ?? new[] { "Check", "Close" };
            string kind = b ? parts[0] : (parts.Length > 1 ? parts[1] : "Close");
            return Enum.Parse(typeof(MaterialDesignThemes.Wpf.PackIconKind), kind);
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }

    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object v, Type t, object p, CultureInfo c)
        {
            bool b = v is bool val && val;
            var parts = p?.ToString()?.Split('|') ?? new[] { "#FFFFFF", "#444444" };
            string hex = b ? parts[0] : (parts.Length > 1 ? parts[1] : "#444444");
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { return Brushes.Gray; }
        }
        public object ConvertBack(object v, Type t, object p, CultureInfo c)
            => throw new NotImplementedException();
    }
}
