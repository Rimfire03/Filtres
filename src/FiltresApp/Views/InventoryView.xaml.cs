using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using FiltresApp.Core.Models;
using FiltresApp.ViewModels;

namespace FiltresApp.Views;

public partial class InventoryView : UserControl
{
    // Empêche une seconde validation quand le message d'erreur fait perdre le focus à la cellule.
    private bool _committing;

    public InventoryView()
    {
        InitializeComponent();
    }

    private void InventaireBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var box = (TextBox)sender;
        box.SelectAll();
        // La ligne saisie devient aussi la ligne sélectionnée (boutons Modifier / Supprimer).
        var row = DataGridRow.GetRowContainingElement(box);
        if (row is not null) row.IsSelected = true;
    }

    private void InventaireBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit((TextBox)sender);

    private void InventaireBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;
        if (e.Key == Key.Enter)
        {
            Commit(box);
            box.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            RestoreSavedValue(box);
            box.SelectAll();
            e.Handled = true;
        }
    }

    private void Commit(TextBox box)
    {
        if (_committing || box.IsReadOnly || box.DataContext is not OrderLine line || DataContext is not InventoryListViewModel vm) return;
        _committing = true;
        try
        {
            vm.SetInventaire(line, box.Text);
            // Réaffiche la valeur enregistrée (normalisée, ou l'ancienne si la saisie a été refusée).
            RestoreSavedValue(box);
        }
        finally
        {
            _committing = false;
        }
    }

    private static void RestoreSavedValue(TextBox box) =>
        BindingOperations.GetBindingExpression(box, TextBox.TextProperty)?.UpdateTarget();
}
