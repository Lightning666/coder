using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SerialWorkbench.Models;

namespace SerialWorkbench.Converters;

public sealed class DataPointsToPointCollectionConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not IEnumerable<DataPointModel> points)
        {
            return new PointCollection();
        }

        var array = points.ToArray();
        if (array.Length == 0)
        {
            return new PointCollection();
        }

        var width = 520d;
        var height = 180d;
        var max = array.Max(item => item.Value);
        var min = array.Min(item => item.Value);
        var span = Math.Max(1d, max - min);

        var pointCollection = new PointCollection();
        for (var index = 0; index < array.Length; index++)
        {
            var x = array.Length == 1 ? width / 2d : width * index / (array.Length - 1d);
            var normalized = (array[index].Value - min) / span;
            var y = height - normalized * height;
            pointCollection.Add(new Point(x, y));
        }

        return pointCollection;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
