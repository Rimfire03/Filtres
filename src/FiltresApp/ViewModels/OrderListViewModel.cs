using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

/// <summary>Écran "Commande chmy" (et, historiquement, "pour devis" - voir README, section "Écrans
/// retirés de la navigation"). Depuis la fusion Inventaire / Commande chmy (voir README), les lignes de
/// type <see cref="OrderDocumentType.CommandeChmy"/> sont exactement les mêmes que celles affichées par
/// <see cref="InventoryListViewModel"/> ("Inventaire") : les deux écrans partagent la même table
/// <see cref="OrderLine"/>, seule la mise en page de la grille diffère.</summary>
public partial class OrderListViewModel : ObservableObject, IReloadable
{
    private readonly OrderDocumentType _type;
    public string Title { get; }
    public bool AllowPdfExport => _type == OrderDocumentType.CommandeChmy;

    [ObservableProperty] private ObservableCollection<OrderLine> _lines = new();
    [ObservableProperty] private OrderLine? _selectedLine;

    public OrderFamilyFilter FamilyFilter { get; }

    public OrderListViewModel(OrderDocumentType type, string title)
    {
        _type = type;
        Title = title;
        FamilyFilter = new OrderFamilyFilter(Load);
        Load();
    }

    private static readonly Dictionary<FilterCategory, string> CategoryLabels = new()
    {
        [FilterCategory.G4Plisse] = "Filtres G4 plissés",
        [FilterCategory.G4Plan] = "Filtres G4 plan",
        [FilterCategory.G3] = "Filtres G3",
        [FilterCategory.Charbon] = "Charbon"
    };

    public void Reload()
    {
        FamilyFilter.Refresh();
        Load();
    }

    private void Load()
    {
        Lines = new ObservableCollection<OrderLine>(
            FamilyFilter.Apply(App.Db.OrderLines)
                .Include(l => l.Family)
                .Include(l => l.FilterLinks).ThenInclude(fl => fl.PeriodicFilter)
                .AsNoTracking()
                .Where(l => l.DocumentType == _type)
                .OrderBy(l => l.Ordre)
                .ToList());
    }

    [RelayCommand]
    private void Add()
    {
        if (!App.GuardWritable()) return;
        var entity = new OrderLine
        {
            DocumentType = _type,
            Ordre = (App.Db.OrderLines.Where(l => l.DocumentType == _type).Max(l => (int?)l.Ordre) ?? 0) + 1,
            OrderFamilyId = FamilyFilter.DefaultFamilyId
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
            FamilyFilter.CreateEditField(entity),
            EditField.Text("Dimension", () => entity.Designation, v => entity.Designation = v, required: true),
            EditField.NullableText("Destination", () => entity.Destination, v => entity.Destination = v),
            EditField.NullableText("Type", () => entity.Dimension, v => entity.Dimension = v),
            EditField.Multiline("Référence fournisseur", () => entity.Notes, v => entity.Notes = v),
            EditField.NullableInt("Quantité à commander", () => entity.Quantite, v => entity.Quantite = v)
        };
        return App.Dialogs.EditFields(isNew ? "Ajouter une ligne" : "Modifier la ligne", fields);
    }

    /// <summary>Ouvre le sélecteur de rattachement manuel filtre <-> ligne de commande : liste, par
    /// défaut, uniquement les filtres à périodicité (G4 plissé, G4 plan, G3, Charbon) dont la dimension
    /// correspond à celle de la ligne de commande (comparaison tolérante, voir
    /// <see cref="DimensionMatchService"/>), avec catégorie/emplacement/dimension pour identification, et
    /// coche ceux déjà rattachés. Signale en rouge les filtres déjà rattachés à une AUTRE ligne. Le besoin
    /// (mars/septembre) est recalculé automatiquement à l'affichage à partir du rattachement enregistré.
    /// L'enregistrement applique la règle d'exclusivité (un filtre = une seule ligne à la fois), voir
    /// <see cref="FilterLinkService"/>.</summary>
    [RelayCommand]
    private void LinkFilters()
    {
        if (!App.GuardWritable()) return;
        if (SelectedLine is null) return;

        var allFilters = App.Db.PeriodicFilters.AsNoTracking().OrderBy(f => f.Category).ThenBy(f => f.Location).ToList();
        var linkedIds = SelectedLine.FilterLinks.Select(l => l.PeriodicFilterId).ToHashSet();

        // Filtres déjà rattachés à une AUTRE ligne de commande (n'importe laquelle) : sert à l'indicateur
        // rouge du sélecteur, pour prévenir l'utilisateur avant qu'il ne déplace un rattachement existant.
        var linkedElsewhereByFilterId = App.Db.OrderLinePeriodicFilters
            .AsNoTracking()
            .Include(l => l.OrderLine)
            .Where(l => l.OrderLineId != SelectedLine.Id)
            .GroupBy(l => l.PeriodicFilterId)
            .ToDictionary(g => g.Key, g => g.First().OrderLine?.Designation ?? $"ligne #{g.First().OrderLineId}");

        var items = allFilters
            .Select(f => new FilterPickItem(
                f,
                CategoryLabels.GetValueOrDefault(f.Category, f.Category.ToString()),
                isSelected: linkedIds.Contains(f.Id),
                dimensionMatches: DimensionMatchService.Matches(SelectedLine, f),
                linkedElsewhereLabel: linkedElsewhereByFilterId.GetValueOrDefault(f.Id)))
            .ToList();

        var selectedIds = App.Dialogs.PickFilterLinks(items);
        if (selectedIds is null) return;

        var tracked = App.Db.OrderLines.Include(l => l.FilterLinks).First(l => l.Id == SelectedLine.Id);
        FilterLinkService.SetLinks(App.Db, tracked, selectedIds);

        App.Db.SaveChanges();
        Load();
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

    private static string[] BuildHeaders() =>
        new[] { "Famille", "Dimension", "Destination", "Type", "Référence fournisseur", "Filtres liés", "Besoin mars (calculé)", "Besoin septembre (calculé)", "Quantité à commander" };

    private static string[] BuildRow(OrderLine l) => new[]
    {
        l.FamilyName,
        l.Designation,
        l.Destination ?? "",
        l.Dimension ?? "",
        l.Notes ?? "",
        l.LinkedFilterCount > 0 ? l.LinkedFilterCount.ToString() : "",
        l.NeedMars?.ToString() ?? "",
        l.NeedSeptembre?.ToString() ?? "",
        l.Quantite?.ToString() ?? ""
    };

    [RelayCommand]
    private void Print()
    {
        var rows = Lines.Select(BuildRow).ToList();
        App.Printer.PrintTable(Title + FamilyFilter.TitleSuffix, BuildHeaders(), rows);
    }

    [RelayCommand]
    private void ExportPdf()
    {
        var rows = Lines.Select(BuildRow).ToList();
        var path = App.PdfExport.ExportTable(App.Settings.ResolvedPdfExportPath, Title + FamilyFilter.TitleSuffix, BuildHeaders(), rows);
        App.Dialogs.ShowMessage("Export PDF", $"Bon de commande généré avec succès.\n\nIl est stocké dans :\n{path}");
    }
}
