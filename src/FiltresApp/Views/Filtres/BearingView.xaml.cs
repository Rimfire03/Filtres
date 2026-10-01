using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FiltresApp.Services;
using FiltresApp.ViewModels.Filtres;

namespace FiltresApp.Views.Filtres;

public partial class BearingView : UserControl
{
    public BearingView()
    {
        InitializeComponent();
    }

    private void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = (e.OriginalSource as DependencyObject).FindAncestor<DataGridRow>();
        if (row?.ContextMenu is not { } menu || row.DataContext is not BearingRowViewModel rowVm) return;
        if (menu.Items.OfType<MenuItem>().FirstOrDefault(m => (string)m.Header == "Couleur") is not { } colorItem) return;
        RowColorMenu.Populate(colorItem, rowVm.RowColorId, colorId => rowVm.RowColorId = colorId);
    }
}
