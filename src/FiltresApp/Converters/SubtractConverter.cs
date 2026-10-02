using System.Globalization;
using System.Windows.Data;

namespace FiltresApp.Converters;

/// <summary>Retire la valeur du paramètre (ex. "6") d'une largeur : permet de caler un contrôle sur la
/// largeur réelle d'un autre élément moins ses marges (champ de recherche sous un en-tête de colonne).</summary>
public class SubtractConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var width = value is double d && !double.IsNaN(d) ? d : 0;
        var minus = double.TryParse(parameter as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var p) ? p : 0;
        return Math.Max(0, width - minus);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
