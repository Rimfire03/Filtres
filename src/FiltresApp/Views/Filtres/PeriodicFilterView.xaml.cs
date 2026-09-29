using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FiltresApp.Services;
using FiltresApp.ViewModels.Filtres;

namespace FiltresApp.Views.Filtres;

public partial class PeriodicFilterView : UserControl
{
    public PeriodicFilterView()
    {
        InitializeComponent();
    }

    /// <summary>Menu contextuel de la grille sur le point d'ouvrir : reconstruit le sous-menu "Couleur" à
    /// partir de la palette réglée dans Paramètres (voir RowColorMenu). Un MenuItem déclaré en XAML à
    /// l'intérieur d'un Setter.Value (Style DataGridRow) ne peut pas se voir attacher un gestionnaire
    /// d'événement nommé directement en XAML (limitation du compilateur XAML pour ce contenu différé) -
    /// on le fait donc ici, sur l'événement ContextMenuOpening de la grille elle-même.</summary>
    private void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = (e.OriginalSource as DependencyObject).FindAncestor<DataGridRow>();
        if (row?.ContextMenu is not { } menu || row.DataContext is not PeriodicFilterRowViewModel rowVm) return;
        if (menu.Items.OfType<MenuItem>().FirstOrDefault(m => (string)m.Header == "Couleur") is not { } colorItem) return;
        RowColorMenu.Populate(colorItem, rowVm.RowColorId, colorId => rowVm.RowColorId = colorId);
    }
}
