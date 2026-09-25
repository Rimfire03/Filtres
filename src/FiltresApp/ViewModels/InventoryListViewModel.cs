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
/// stock (quantité, unité) plutôt que le rattachement filtre / besoin calculé. Une ligne ajoutée ici
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
            EditField.Text("Désignation", () => entity.Designation, v => entity.Designation = v, required: true),
            EditField.NullableText("Dimension", () => entity.Dimension, v => entity.Dimension = v),
            EditField.NullableInt("Quantité", () => entity.Quantite, v => entity.Quantite = v),
            EditField.NullableText("Unité", () => entity.Unite, v => entity.Unite = v),
            EditField.Multiline("Notes", () => entity.Notes, v => entity.Notes = v)
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

    private static string[] BuildHeaders() => new[] { "Désignation", "Dimension", "Quantité", "Unité", "Notes" };

    private static string[] BuildRow(OrderLine l) => new[]
    {
        l.Designation, l.Dimension ?? "", l.Quantite?.ToString() ?? "", l.Unite ?? "", l.Notes ?? ""
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
