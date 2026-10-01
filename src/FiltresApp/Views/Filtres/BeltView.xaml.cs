using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FiltresApp.Services;
using FiltresApp.ViewModels.Filtres;

namespace FiltresApp.Views.Filtres;

public partial class BeltView : UserControl
{
    public BeltView()
    {
        InitializeComponent();
    }

    /// <summary>Menu contextuel de la grille sur le point d'ouvrir : reconstruit le sous-menu "Couleur" à
    /// partir de la palette réglée dans Paramètres (même pattern que les autres écrans de filtres).</summary>
    private void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = (e.OriginalSource as DependencyObject).FindAncestor<DataGridRow>();
        if (row?.ContextMenu is not { } menu || row.DataContext is not BeltRowViewModel rowVm) return;
        if (menu.Items.OfType<MenuItem>().FirstOrDefault(m => (string)m.Header == "Couleur") is not { } colorItem) return;
        RowColorMenu.Populate(colorItem, rowVm.RowColorId, colorId => rowVm.RowColorId = colorId);
    }
}
