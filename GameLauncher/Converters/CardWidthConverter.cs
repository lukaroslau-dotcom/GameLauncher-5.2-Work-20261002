using System.Globalization;
using System.Windows.Data;
using GameLauncher.Constants;

namespace GameLauncher.Converters;

public sealed class CardWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double width = value is double available && available > 0 ? available : 700;
        return Math.Max(1, Math.Floor(width / Math.Clamp(Math.Floor(width / 300), 1, 5)) - 16);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
public sealed class StatusLabelConverter : IValueConverter
{
    public static string Label(string value) => value switch
    {
        GameStatus.Installed => "Installiert",
        GameStatus.WaitingForUpdates => "Wartend",
        GameStatus.Finished => "Abgeschlossen",
        GameStatus.Deleted => "Gelöscht",
        GameStatus.Wishlist => "Wunschliste",
        _ => value
    };
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Label(value as string ?? "");
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
