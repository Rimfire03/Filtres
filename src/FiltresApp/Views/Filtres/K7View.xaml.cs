using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using FiltresApp.ViewModels.Filtres;

namespace FiltresApp.Views.Filtres;

public partial class K7View : UserControl
{
    public K7View()
    {
        InitializeComponent();
    }

    /// <summary>Menu contextuel de la grille sur le point d'ouvrir : reconstruit le sous-menu "Couleur" à
    /// partir de la palette réglée dans Paramètres (voir RowColorMenu et PeriodicFilterView.xaml.cs pour
    /// l'explication de cette approche plutôt qu'un événement XAML direct sur le MenuItem).</summary>
    private void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = (e.OriginalSource as DependencyObject).FindAncestor<DataGridRow>();
        if (row?.ContextMenu is not { } menu || row.DataContext is not K7Location location || DataContext is not K7ListViewModel vm) return;
        if (menu.Items.OfType<MenuItem>().FirstOrDefault(m => (string)m.Header == "Couleur") is not { } colorItem) return;
        RowColorMenu.Populate(colorItem, location.RowColorId, colorId => vm.SetRowColor(location, colorId));
    }
}
