using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Écran "Inventaire". Depuis la fusion Inventaire / Commande chmy (voir README), cet écran
/// n'est plus adossé à une entité <see cref="InventoryLine"/> distincte : il lit/écrit exactement les
/// mêmes lignes <see cref="OrderLine"/> (type <see cref="OrderDocumentType.CommandeChmy"/>) que
/// <see cref="OrderListViewModel"/>, avec une mise en page différente mettant en avant les colonnes de
/// stock (quantité) plutôt que le rattachement filtre / besoin calculé. Une ligne ajoutée ici
/// apparaît donc automatiquement dans "Commande" et réciproquement (au prochain changement d'écran,
/// voir <see cref="IReloadable"/>).</summary>
public partial class InventoryListViewModel : OrderLineListViewModelBase
{
    public override string Title => "Inventaire";

    public InventoryListViewModel() : base(OrderDocumentType.CommandeChmy) => Load();

    protected override IEnumerable<EditField> ExtraEditFields(OrderLine entity)
    {
        yield return EditField.NullableInt("Inventaire", () => entity.Inventaire, v => entity.Inventaire = v);
    }

    /// <summary>Saisie directe dans la cellule "Inventaire" de la grille. Retourne false (saisie à annuler)
    /// si le poste est en lecture seule ou si le texte n'est pas un nombre entier.</summary>
    public bool SetInventaire(OrderLine line, string text) =>
        SaveIntCell(line, text, "Inventaire", l => l.Inventaire, (l, v) => l.Inventaire = v);

    /// <summary>Vide la colonne "Inventaire" des lignes affichées (toutes, ou celles de la famille filtrée),
    /// après confirmation et sauvegarde de la base.</summary>
    [RelayCommand]
    private void ClearInventaire()
    {
        if (!App.GuardWritable()) return;
        var ids = Lines.Where(l => l.Inventaire.HasValue).Select(l => l.Id).ToList();
        if (ids.Count == 0)
        {
            App.Dialogs.ShowMessage("Vider la colonne Inventaire", "La colonne Inventaire est déjà vide pour les lignes affichées.");
            return;
        }

        var scope = FamilyFilter.TitleSuffix.Length == 0 ? "toutes les lignes" : $"les lignes de la famille « {FamilyFilter.Selected} »";
        if (!App.Dialogs.ShowConfirm("Vider la colonne Inventaire",
                $"Effacer la valeur d'inventaire de {ids.Count} ligne(s) ({scope}) ?\n\n" +
                "Une copie de sauvegarde de la base sera faite juste avant.")) return;

        App.DbFactory.CreateBackup("avant-vidage-inventaire");
        App.Db.OrderLines.Where(l => ids.Contains(l.Id))
            .ExecuteUpdate(s => s.SetProperty(l => l.Inventaire, (int?)null));
        // Les lignes éventuellement suivies par le contexte gardent l'ancienne valeur en mémoire.
        App.Db.ChangeTracker.Clear();
        Load();
    }

    protected override string[] PrintHeaders { get; } =
        { "Dimension", "Destination", "Type", "Référence fournisseur", "Besoin mars (calculé)", "Besoin septembre (calculé)", "Inventaire", "Quantité" };

    protected override string[] PrintRow(OrderLine l) => new[]
    {
        l.Designation, l.Destination ?? "", l.Dimension ?? "", l.Notes ?? "", l.NeedMars?.ToString() ?? "", l.NeedSeptembre?.ToString() ?? "", l.Inventaire?.ToString() ?? "", l.InventoryQuantity?.ToString() ?? ""
    };

    /// <summary>Le PDF de cet écran est aussi un bon de commande.</summary>
    protected override string PdfTitle => "Commande";
}
