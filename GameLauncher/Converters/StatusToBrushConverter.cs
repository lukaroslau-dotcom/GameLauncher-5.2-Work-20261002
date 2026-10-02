using GameLauncher.Constants;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace GameLauncher.Converters
{
    public class StatusToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string status = value as string ?? "";

            return status switch
            {
                GameStatus.Installed => new SolidColorBrush(Color.FromRgb(77, 183, 131)),
                GameStatus.WaitingForUpdates => new SolidColorBrush(Color.FromRgb(215, 168, 75)),
                GameStatus.Finished => new SolidColorBrush(Color.FromRgb(124, 111, 255)),
                GameStatus.Deleted => new SolidColorBrush(Color.FromRgb(218, 105, 117)),
                _ => new SolidColorBrush(Color.FromRgb(156, 162, 174))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool flag = value is bool b && b;
            return flag
                ? System.Windows.Visibility.Collapsed
                : System.Windows.Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
