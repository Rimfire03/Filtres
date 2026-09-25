using System.Windows.Controls;
using System.Windows.Input;
using FiltresApp.Core.Models;
using FiltresApp.ViewModels;

namespace FiltresApp.Views;

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
}
