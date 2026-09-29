using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using FiltresApp.Services;
using FiltresApp.ViewModels.Filtres;

namespace FiltresApp.Views.Filtres;

public partial class DynamicFilterView : UserControl
{
    public DynamicFilterView()
    {
        InitializeComponent();
    }

    /// <summary>Clic droit sur une cellule "Type" : menu rapide listant les types déjà utilisés (toutes
    /// familles confondues de cette variété), générée dynamiquement depuis les données en base (pas de
    /// liste figée en dur, ce champ restant un texte libre). Même pattern que OrderView.xaml.cs
    /// (rattachement de filtres).</summary>
    private void Grid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var cell = (e.OriginalSource as DependencyObject).FindAncestor<DataGridCell>();
        if (cell?.Column != TypeColumn || cell.DataContext is not DynamicFilterRowViewModel row || DataContext is not DynamicFilterListViewModel vm) return;

        vm.SelectedFilter = row;
        var menu = new ContextMenu { PlacementTarget = cell, Placement = PlacementMode.MousePoint };
        var types = vm.GetDistinctFilterTypes();

        if (types.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "Aucun type existant pour l'instant", IsEnabled = false });
        }
        else
        {
            menu.Items.Add(new MenuItem { Header = "Choisir un type existant :", IsEnabled = false });
            foreach (var type in types)
            {
                var item = new MenuItem { Header = new TextBlock { Text = type }, IsCheckable = true, IsChecked = type == row.FilterType, IsEnabled = App.IsWritable };
                item.Click += (_, _) => vm.SetFilterType(row, type);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Effacer le type", IsEnabled = App.IsWritable && !string.IsNullOrEmpty(row.FilterType) };
        clear.Click += (_, _) => vm.SetFilterType(row, null);
        menu.Items.Add(clear);

        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Menu contextuel de la grille sur le point d'ouvrir : reconstruit le sous-menu "Couleur" à
    /// partir de la palette réglée dans Paramètres (voir RowColorMenu et PeriodicFilterView.xaml.cs pour
    /// l'explication de cette approche plutôt qu'un événement XAML direct sur le MenuItem).</summary>
    private void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = (e.OriginalSource as DependencyObject).FindAncestor<DataGridRow>();
        if (row?.ContextMenu is not { } menu || row.DataContext is not DynamicFilterRowViewModel rowVm) return;
        if (menu.Items.OfType<MenuItem>().FirstOrDefault(m => (string)m.Header == "Couleur") is not { } colorItem) return;
        RowColorMenu.Populate(colorItem, rowVm.RowColorId, colorId => rowVm.RowColorId = colorId);
    }
}
