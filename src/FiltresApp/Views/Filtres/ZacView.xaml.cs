using System.Linq;
using System.Windows;
using System.Windows.Controls;
using FiltresApp.Services;
using FiltresApp.ViewModels.Filtres;

namespace FiltresApp.Views.Filtres;

public partial class ZacView : UserControl
{
    public ZacView()
    {
        InitializeComponent();
    }

    /// <summary>Colonne "Type de ZAC" : le choix dans la liste est enregistré tout de suite.</summary>
    private void FamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { IsLoaded: true, SelectedItem: string choice } combo
            || combo.DataContext is not ZacRowViewModel row || DataContext is not ZacListViewModel vm) return;
        vm.SetFamilyChoice(row.Entity, choice);
    }

    private void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = (e.OriginalSource as DependencyObject).FindAncestor<DataGridRow>();
        if (row?.ContextMenu is not { } menu || row.DataContext is not ZacRowViewModel rowVm) return;
        if (menu.Items.OfType<MenuItem>().FirstOrDefault(m => (string)m.Header == "Couleur") is not { } colorItem) return;
        RowColorMenu.Populate(colorItem, rowVm.RowColorId, colorId => rowVm.RowColorId = colorId);
    }
}
