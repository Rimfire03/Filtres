using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

/// <summary>Écran "Inventaire". Depuis la fusion Inventaire / Commande chmy (voir README), cet écran
/// n'est plus adossé à une entité <see cref="InventoryLine"/> distincte : il lit/écrit exactement les
/// mêmes lignes <see cref="OrderLine"/> (type <see cref="OrderDocumentType.CommandeChmy"/>) que
/// <see cref="OrderListViewModel"/>, avec une mise en page différente mettant en avant les colonnes de
/// stock (quantité) plutôt que le rattachement filtre / besoin calculé. Une ligne ajoutée ici
/// apparaît donc automatiquement dans "Commande chmy" et réciproquement (et vice versa au prochain
/// changement d'écran, voir <see cref="IReloadable"/>).</summary>
public partial class InventoryListViewModel : ObservableObject, IReloadable
{
    public string Title => "Inventaire";

    [ObservableProperty] private ObservableCollection<OrderLine> _lines = new();
    [ObservableProperty] private OrderLine? _selectedLine;

    public OrderFamilyFilter FamilyFilter { get; }

    public InventoryListViewModel()
    {
        FamilyFilter = new OrderFamilyFilter(Load);
        Load();
    }

    public void Reload() => Load();

    private void Load()
    {
        Lines = new ObservableCollection<OrderLine>(FamilyFilter.Apply(
            App.Db.OrderLines
                .Include(l => l.FilterLinks).ThenInclude(fl => fl.PeriodicFilter)
                .Include(l => l.DynamicLinks).ThenInclude(dl => dl.DynamicFilter)
                .AsNoTracking()
                .Where(l => l.DocumentType == OrderDocumentType.CommandeChmy)
                .ToList()));
    }

    [RelayCommand]
    private void Add()
    {
        if (!App.GuardWritable()) return;
        var entity = new OrderLine
        {
            DocumentType = OrderDocumentType.CommandeChmy,
            Ordre = (App.Db.OrderLines.Where(l => l.DocumentType == OrderDocumentType.CommandeChmy).Max(l => (int?)l.Ordre) ?? 0) + 1
        };
        FamilyFilter.ApplyDefaultFamily(entity);
        if (!EditEntity(entity, true, OrderLine.NoFamilyLabel)) return;
        App.Db.OrderLines.Add(entity);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void Edit()
    {
        if (!App.GuardWritable()) return;
        if (SelectedLine is null) return;
        var tracked = App.Db.OrderLines.First(l => l.Id == SelectedLine.Id);
        if (!EditEntity(tracked, false, SelectedLine.AutomaticFamilyLabel)) return;
        App.Db.SaveChanges();
        Load();
    }

    private bool EditEntity(OrderLine entity, bool isNew, string automaticFamilyLabel)
    {
        var fields = new List<EditField>
        {
            OrderFamilyFilter.CreateEditField(entity, automaticFamilyLabel),
            EditField.Text("Dimension", () => entity.Designation, v => entity.Designation = v, required: true),
            EditField.NullableText("Destination", () => entity.Destination, v => entity.Destination = v),
            EditField.NullableText("Type", () => entity.Dimension, v => entity.Dimension = v),
            EditField.Multiline("Référence fournisseur", () => entity.Notes, v => entity.Notes = v),
            EditField.NullableInt("Inventaire", () => entity.Inventaire, v => entity.Inventaire = v)
        };
        return App.Dialogs.EditFields(isNew ? "Ajouter une ligne" : "Modifier la ligne", fields);
    }

    /// <summary>Ne recharge pas toute la liste (même raisonnement que
    /// OrderListViewModel.SetFamilyChoice/Delete) : la ligne est simplement retirée de
    /// <see cref="Lines"/>, et la sélection se fixe sur la ligne suivante (précédente si c'était la
    /// dernière), pour que la vue ne saute pas tout en haut.</summary>
    [RelayCommand]
    private void Delete()
    {
        if (!App.GuardWritable()) return;
        if (SelectedLine is null) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer '{SelectedLine.Designation}' ?")) return;
        var tracked = App.Db.OrderLines.First(l => l.Id == SelectedLine.Id);
        App.Db.OrderLines.Remove(tracked);
        App.Db.SaveChanges();

        var index = Lines.IndexOf(SelectedLine);
        var next = index >= 0 && index + 1 < Lines.Count ? Lines[index + 1] : index > 0 ? Lines[index - 1] : null;
        if (index >= 0) Lines.RemoveAt(index);
        SelectedLine = next;
    }

    /// <summary>Saisie directe dans la cellule "Inventaire" de la grille. Retourne false (saisie à annuler)
    /// si le poste est en lecture seule ou si le texte n'est pas un nombre entier.</summary>
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

    public bool SetInventaire(OrderLine line, string text)
    {
        if (!IntInput.TryParse(text, "Inventaire", out var value)) return false;
        if (value == line.Inventaire) return true;
        if (!App.GuardWritable()) return false;

        var tracked = App.Db.OrderLines.First(l => l.Id == line.Id);
        tracked.Inventaire = value;
        App.Db.SaveChanges();
        line.Inventaire = value;
        return true;
    }

    private static string[] BuildHeaders() => new[] { "Dimension", "Destination", "Type", "Référence fournisseur", "Besoin mars (calculé)", "Besoin septembre (calculé)", "Inventaire", "Quantité" };

    private static string[] BuildRow(OrderLine l) => new[]
    {
        l.Designation, l.Destination ?? "", l.Dimension ?? "", l.Notes ?? "", l.NeedMars?.ToString() ?? "", l.NeedSeptembre?.ToString() ?? "", l.Inventaire?.ToString() ?? "", l.InventoryQuantity?.ToString() ?? ""
    };

    private List<string[]> BuildPrintRows() =>
        PrintService.BuildGroupedRows(Lines, l => l.FamilyGroupLabel, BuildRow, BuildHeaders().Length);

    [RelayCommand]
    private void Print()
    {
        App.Printer.PrintTable(Title + FamilyFilter.TitleSuffix, BuildHeaders(), BuildPrintRows());
    }

    [RelayCommand]
    private void ExportPdf()
    {
        var path = App.PdfExport.ExportTable(App.Settings.ResolvedPdfExportPath, "Commande" + FamilyFilter.TitleSuffix, BuildHeaders(), BuildPrintRows(), App.CompanyLogo);
        App.Dialogs.ShowMessage("Export PDF", $"Bon de commande généré avec succès.\n\nIl est stocké dans :\n{path}");
    }
}
