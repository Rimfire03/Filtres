using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace FiltresApp.Views;

/// <summary>
/// Comportement commun des cellules de grille saisies directement (style InlineEditTextBox) : un clic
/// sélectionne la ligne et le texte, Entrée / sortie de la cellule enregistre, Échap annule.
/// </summary>
public sealed class InlineIntCell
{
    private readonly Func<TextBox, bool> _save;
    private readonly DataGridColumn[] _dependentColumns;

    // Empêche une seconde validation quand le message d'erreur fait perdre le focus à la cellule.
    private bool _committing;

    /// <param name="save">Enregistre le texte de la cellule ; false si la saisie est refusée.</param>
    /// <param name="dependentColumns">Colonnes calculées à relire après enregistrement (les lignes ne sont
    /// pas notifiantes).</param>
    public InlineIntCell(Func<TextBox, bool> save, params DataGridColumn[] dependentColumns)
    {
        _save = save;
        _dependentColumns = dependentColumns;
    }

    public void GotFocus(TextBox box)
    {
        box.SelectAll();
        // La ligne saisie devient aussi la ligne sélectionnée (boutons Modifier / Supprimer).
        var row = DataGridRow.GetRowContainingElement(box);
        if (row is not null) row.IsSelected = true;
    }

    public void LostFocus(TextBox box) => Commit(box);

    public void PreviewKeyDown(TextBox box, KeyEventArgs e)
    {
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
        if (_committing || box.IsReadOnly) return;
        _committing = true;
        try
        {
            _save(box);
            // Réaffiche la valeur enregistrée (normalisée, ou l'ancienne si la saisie a été refusée).
            RestoreSavedValue(box);
            RefreshDependentCells(box);
        }
        finally
        {
            _committing = false;
        }
    }

    private void RefreshDependentCells(TextBox box)
    {
        var row = DataGridRow.GetRowContainingElement(box);
        if (row is null) return;
        foreach (var column in _dependentColumns)
            if (column.GetCellContent(row) is TextBlock cell)
                BindingOperations.GetBindingExpression(cell, TextBlock.TextProperty)?.UpdateTarget();
    }

    private static void RestoreSavedValue(TextBox box) =>
        BindingOperations.GetBindingExpression(box, TextBox.TextProperty)?.UpdateTarget();
}
