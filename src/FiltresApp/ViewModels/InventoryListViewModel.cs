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

    public InventoryListViewModel()
    {
        Load();
    }

    public void Reload() => Load();

    private void Load()
    {
        Lines = new ObservableCollection<OrderLine>(
            App.Db.OrderLines
                .AsNoTracking()
                .Where(l => l.DocumentType == OrderDocumentType.CommandeChmy)
                .OrderBy(l => l.Ordre)
                .ToList());
    }

    [RelayCommand]
    private void Add()
    {
        if (!App.GuardWritable()) return;
        var entity = new OrderLine
        {
            DocumentType = OrderDocumentType.CommandeChmy,
            Ordre = (Lines.Count == 0 ? 0 : Lines.Max(l => l.Ordre)) + 1
        };
        if (!EditEntity(entity, true)) return;
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
        if (!EditEntity(tracked, false)) return;
        App.Db.SaveChanges();
        Load();
    }

    private bool EditEntity(OrderLine entity, bool isNew)
    {
        var fields = new List<EditField>
        {
            EditField.Text("Dimension", () => entity.Designation, v => entity.Designation = v, required: true),
            EditField.NullableText("Destination", () => entity.Destination, v => entity.Destination = v),
            EditField.NullableText("Type", () => entity.Dimension, v => entity.Dimension = v),
            EditField.Multiline("Référence fournisseur", () => entity.Notes, v => entity.Notes = v),
            EditField.NullableInt("Inventaire", () => entity.Inventaire, v => entity.Inventaire = v),
            EditField.NullableInt("Quantité", () => entity.Quantite, v => entity.Quantite = v)
        };
        return App.Dialogs.EditFields(isNew ? "Ajouter une ligne" : "Modifier la ligne", fields);
    }

    [RelayCommand]
    private void Delete()
    {
        if (!App.GuardWritable()) return;
        if (SelectedLine is null) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer '{SelectedLine.Designation}' ?")) return;
        var tracked = App.Db.OrderLines.First(l => l.Id == SelectedLine.Id);
        App.Db.OrderLines.Remove(tracked);
        App.Db.SaveChanges();
        Load();
    }

    /// <summary>Saisie directe dans la cellule "Inventaire" de la grille. Retourne false (saisie à annuler)
    /// si le poste est en lecture seule ou si le texte n'est pas un nombre entier.</summary>
    public bool SetInventaire(OrderLine line, string text)
    {
        int? value = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            if (!int.TryParse(text.Trim(), out var parsed))
            {
                App.Dialogs.ShowMessage("Inventaire", $"« {text.Trim()} » n'est pas un nombre entier : la valeur n'a pas été enregistrée.");
                return false;
            }
            value = parsed;
        }

        if (value == line.Inventaire) return true;
        if (!App.GuardWritable()) return false;

        var tracked = App.Db.OrderLines.First(l => l.Id == line.Id);
        tracked.Inventaire = value;
        App.Db.SaveChanges();
        line.Inventaire = value;
        return true;
    }

    private static string[] BuildHeaders() => new[] { "Dimension", "Destination", "Type", "Référence fournisseur", "Inventaire", "Quantité" };

    private static string[] BuildRow(OrderLine l) => new[]
    {
        l.Designation, l.Destination ?? "", l.Dimension ?? "", l.Notes ?? "", l.Inventaire?.ToString() ?? "", l.Quantite?.ToString() ?? ""
    };

    [RelayCommand]
    private void Print()
    {
        App.Printer.PrintTable(Title, BuildHeaders(), Lines.Select(BuildRow).ToList());
    }

    [RelayCommand]
    private void ExportPdf()
    {
        var path = App.PdfExport.ExportTable(App.Settings.ResolvedPdfExportPath, "Commande", BuildHeaders(), Lines.Select(BuildRow).ToList());
        App.Dialogs.ShowMessage("Export PDF", $"Bon de commande généré avec succès.\n\nIl est stocké dans :\n{path}");
    }
}
