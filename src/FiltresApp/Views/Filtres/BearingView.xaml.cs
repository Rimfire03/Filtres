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

    /// <summary>Colonne "Famille" : le choix dans la liste est enregistré tout de suite.</summary>
    private void FamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { IsLoaded: true, SelectedItem: string choice } combo
            || combo.DataContext is not BearingRowViewModel row || DataContext is not BearingListViewModel vm) return;
        vm.SetFamilyChoice(row.Entity, choice);
    }

    private void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = (e.OriginalSource as DependencyObject).FindAncestor<DataGridRow>();
        if (row?.ContextMenu is not { } menu || row.DataContext is not BearingRowViewModel rowVm) return;
        if (menu.Items.OfType<MenuItem>().FirstOrDefault(m => (string)m.Header == "Couleur") is not { } colorItem) return;
        RowColorMenu.Populate(colorItem, rowVm.RowColorId, colorId => rowVm.RowColorId = colorId);
    }
}
