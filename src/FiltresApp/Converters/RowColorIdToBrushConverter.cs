using System.Globalization;
using System.Windows.Data;
using FiltresApp.Services;

namespace FiltresApp.Converters;

/// <summary>Fond de ligne (K7View.xaml / OrderView.xaml / InventoryView.xaml) : K7Location et OrderLine ne
/// sont pas des ObservableObject (voir OrderListViewModel.SetFamilyChoice), donc pas de propriété
/// RowColorBrush calculée comme sur les Row ViewModels de filtres (PeriodicFilterRowViewModel) - un
/// convertisseur suffit, la ligne est de toute façon réinsérée dans la collection après un changement de
/// couleur (voir OrderLineListViewModelBase.SetRowColor / K7ListViewModel.SetRowColor).</summary>
public class RowColorIdToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        RowColorPalette.BrushFor(value as int?);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
