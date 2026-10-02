using System.Globalization;
using System.Windows.Data;
namespace GameLauncher.Converters;
public sealed class ArtworkHeightConverter : IValueConverter
{
    public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value is double width && double.IsFinite(width)?Math.Max(0,width)*9/16:180d;
    public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>throw new NotSupportedException();
}
