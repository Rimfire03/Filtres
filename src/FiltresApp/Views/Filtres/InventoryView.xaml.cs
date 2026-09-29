using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using FiltresApp.ViewModels.Filtres;

namespace FiltresApp.Views.Filtres;

public partial class InventoryView : UserControl
{
    private readonly InlineIntCell _inventaireCell;

    public InventoryView()
    {
        InitializeComponent();
        _inventaireCell = new InlineIntCell(
            box => box.DataContext is OrderLine line && DataContext is InventoryListViewModel vm && vm.SetInventaire(line, box.Text),
            QuantityColumn);
    }

    private void InventaireBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _inventaireCell.GotFocus((TextBox)sender);
    private void InventaireBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _inventaireCell.LostFocus((TextBox)sender);
    private void InventaireBox_PreviewKeyDown(object sender, KeyEventArgs e) => _inventaireCell.PreviewKeyDown((TextBox)sender, e);

    /// <summary>Menu contextuel de la grille sur le point d'ouvrir : reconstruit le sous-menu "Couleur" à
    /// partir de la palette réglée dans Paramètres (voir RowColorMenu et PeriodicFilterView.xaml.cs pour
    /// l'explication de cette approche plutôt qu'un événement XAML direct sur le MenuItem).</summary>
    private void Grid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = (e.OriginalSource as DependencyObject).FindAncestor<DataGridRow>();
        if (row?.ContextMenu is not { } menu || row.DataContext is not OrderLine line || DataContext is not InventoryListViewModel vm) return;
        if (menu.Items.OfType<MenuItem>().FirstOrDefault(m => (string)m.Header == "Couleur") is not { } colorItem) return;
        RowColorMenu.Populate(colorItem, line.RowColorId, colorId => vm.SetRowColor(line, colorId));
    }
}
