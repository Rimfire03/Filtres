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

    public void Reload() => Load();

    private void Load()
    {
        Lines = new ObservableCollection<OrderLine>(FamilyFilter.Apply(
            App.Db.OrderLines
                .Include(l => l.FilterLinks).ThenInclude(fl => fl.PeriodicFilter)
                .Include(l => l.OpacimetricLinks).ThenInclude(ol => ol.OpacimetricFilter).ThenInclude(f => f!.Family)
                .AsNoTracking()
                .Where(l => l.DocumentType == _type)
                .ToList()));
    }

    [RelayCommand]
    private void Add()
    {
        if (!App.GuardWritable()) return;
        var entity = new OrderLine
        {
            DocumentType = _type,
            Ordre = (App.Db.OrderLines.Where(l => l.DocumentType == _type).Max(l => (int?)l.Ordre) ?? 0) + 1,
            FamilyOverride = FamilyFilter.DefaultFamilyOverride
        };
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
            EditField.Multiline("Référence fournisseur", () => entity.Notes, v => entity.Notes = v)
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
        if (SelectedLine is not null) OpenLinkWindow(SelectedLine);
    }

    /// <summary>Filtre rattachable (à périodicité ou F7 à H13), vu depuis une ligne de commande.</summary>
    private sealed record LinkCandidate(FilterRef Ref, string Category, string Location, string Dimension,
        bool IsLinked, string? LinkedElsewhere, bool DimensionMatches, Func<FilterPickItem> ToPickItem);

    /// <summary>Tous les filtres rattachables pour <paramref name="line"/> (chargée avec ses rattachements),
    /// dans l'ordre catégorie puis emplacement, filtres F7 à H13 en dernier.</summary>
    private List<LinkCandidate> LoadLinkCandidates(OrderLine line)
    {
        // Filtres déjà rattachés à une AUTRE ligne : indicateur rouge / mention "déjà rattaché à".
        var periodicElsewhere = App.Db.OrderLinePeriodicFilters.AsNoTracking()
            .Where(l => l.OrderLineId != line.Id)
            .Select(l => new { l.PeriodicFilterId, l.OrderLine!.Designation })
            .ToList()
            .GroupBy(l => l.PeriodicFilterId)
            .ToDictionary(g => g.Key, g => g.First().Designation);
        var opacimetricElsewhere = App.Db.OrderLineOpacimetricFilters.AsNoTracking()
            .Where(l => l.OrderLineId != line.Id)
            .Select(l => new { l.OpacimetricFilterId, l.OrderLine!.Designation })
            .ToList()
            .GroupBy(l => l.OpacimetricFilterId)
            .ToDictionary(g => g.Key, g => g.First().Designation);

        var linkedPeriodic = line.FilterLinks.Select(l => l.PeriodicFilterId).ToHashSet();
        var linkedOpacimetric = line.OpacimetricLinks.Select(l => l.OpacimetricFilterId).ToHashSet();

        var candidates = new List<LinkCandidate>();
        foreach (var f in App.Db.PeriodicFilters.AsNoTracking().OrderBy(f => f.Category).ThenBy(f => f.Location).ToList())
        {
            // Filtres lavables ("à laver", G3) : jamais rattachables, exclus des candidats (voir
            // PeriodicFilterRowViewModel.IsWashable, qui masque aussi leur puce "Lié").
            if (f.Dimension?.Contains("laver", StringComparison.OrdinalIgnoreCase) == true) continue;

            var category = CategoryLabels.GetValueOrDefault(f.Category, f.Category.ToString());
            var isLinked = linkedPeriodic.Contains(f.Id);
            var elsewhere = periodicElsewhere.GetValueOrDefault(f.Id);
            var matches = DimensionMatchService.Matches(line, f);
            candidates.Add(new LinkCandidate(FilterRef.Periodic(f.Id), category, f.Location, f.Dimension, isLinked, elsewhere, matches,
                () => new FilterPickItem(f, category, isLinked, matches, elsewhere)));
        }
        foreach (var f in App.Db.OpacimetricFilters.AsNoTracking().OrderBy(f => f.Location).ToList())
        {
            var isLinked = linkedOpacimetric.Contains(f.Id);
            var elsewhere = opacimetricElsewhere.GetValueOrDefault(f.Id);
            var matches = DimensionMatchService.Matches(line, f.Dimension);
            candidates.Add(new LinkCandidate(FilterRef.Opacimetric(f.Id), OrderLine.OpacimetricFamilyLabel, f.Location, f.Dimension, isLinked, elsewhere, matches,
                () => new FilterPickItem(f, isLinked, matches, elsewhere)));
        }
        return candidates;
    }

    /// <summary>Remplace les rattachements de la ligne, enregistre et recharge l'écran.</summary>
    private void SaveLinks(OrderLine line, IEnumerable<FilterRef> selected)
    {
        var tracked = App.Db.OrderLines.Include(l => l.FilterLinks).Include(l => l.OpacimetricLinks).First(l => l.Id == line.Id);
        FilterLinkService.SetLinks(App.Db, tracked, selected);
        App.Db.SaveChanges();
        Load();
        SelectedLine = Lines.FirstOrDefault(l => l.Id == line.Id);
    }

    /// <summary>Fenêtre complète "Rattacher des filtres..." pour une ligne (bouton, ou menu rapide).</summary>
    public void OpenLinkWindow(OrderLine line)
    {
        if (!App.GuardWritable()) return;
        SelectedLine = line;

        var items = LoadLinkCandidates(line).Select(c => c.ToPickItem()).ToList();
        var selected = App.Dialogs.PickFilterLinks(items);
        if (selected is null) return;
        SaveLinks(line, selected);
    }

    /// <summary>Saisie directe dans la cellule "Besoin" (lignes hors familles G4 plissé, G4 plan, G3).
    /// Retourne false (saisie à annuler) en lecture seule ou si le texte n'est pas un nombre entier.</summary>
    public bool SetManualNeed(OrderLine line, string text)
    {
        if (!line.UsesManualNeed) return false;
        if (!IntInput.TryParse(text, "Besoin", out var value)) return false;
        if (value == line.ManualNeed) return true;
        if (!App.GuardWritable()) return false;

        var tracked = App.Db.OrderLines.First(l => l.Id == line.Id);
        tracked.ManualNeed = value;
        App.Db.SaveChanges();
        line.ManualNeed = value;
        return true;
    }

    public record QuickLinkOption(FilterRef Ref, string Label, bool IsLinked);

    /// <summary>Nombre maximal de filtres de dimension correspondante proposés dans le menu rapide.</summary>
    public const int QuickLinkMaxSuggestions = 20;

    /// <summary>Menu rapide (clic droit sur "Filtres liés") : filtres déjà rattachés à la ligne, puis
    /// filtres de dimension correspondante (même comparaison approximative que la fenêtre complète, F7 à
    /// H13 compris), limités à <see cref="QuickLinkMaxSuggestions"/>. Le second élément indique combien de
    /// suggestions ont été omises.</summary>
    public (List<QuickLinkOption> Options, int Omitted) GetQuickLinkOptions(OrderLine line)
    {
        var candidates = LoadLinkCandidates(line);

        static string Label(LinkCandidate c)
        {
            var label = $"{c.Category} — {c.Location} ({c.Dimension})";
            return c.LinkedElsewhere is null ? label : $"{label} — déjà rattaché à « {c.LinkedElsewhere} »";
        }

        var options = candidates.Where(c => c.IsLinked).Select(c => new QuickLinkOption(c.Ref, Label(c), true)).ToList();
        var suggestions = candidates.Where(c => !c.IsLinked && c.DimensionMatches).ToList();
        options.AddRange(suggestions.Take(QuickLinkMaxSuggestions).Select(c => new QuickLinkOption(c.Ref, Label(c), false)));
        return (options, Math.Max(0, suggestions.Count - QuickLinkMaxSuggestions));
    }

    /// <summary>Coche / décoche un filtre depuis le menu rapide. Un filtre rattaché à une autre ligne lui
    /// est retiré (un filtre = une seule ligne), comme dans la fenêtre complète.</summary>
    public void SetQuickLink(OrderLine line, FilterRef filter, bool link)
    {
        if (!App.GuardWritable()) return;
        var selected = line.FilterLinks.Select(l => FilterRef.Periodic(l.PeriodicFilterId))
            .Concat(line.OpacimetricLinks.Select(l => FilterRef.Opacimetric(l.OpacimetricFilterId)))
            .ToHashSet();
        if (link) selected.Add(filter);
        else selected.Remove(filter);
        SaveLinks(line, selected);
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

    /// <summary>Colonnes imprimées / exportées en PDF : la colonne "Filtres liés" de la grille n'y figure
    /// jamais.</summary>
    private static string[] BuildHeaders() =>
        new[] { "Dimension", "Destination", "Type", "Référence fournisseur", "Besoin", "Quantité à commander" };

    private static string[] BuildRow(OrderLine l) => new[]
    {
        l.Designation,
        l.Destination ?? "",
        l.Dimension ?? "",
        l.Notes ?? "",
        l.Need?.ToString() ?? "",
        l.InventoryQuantity?.ToString() ?? ""
    };

    [RelayCommand]
    private void Print()
    {
        var rows = OrderFamilyFilter.BuildGroupedRows(Lines, BuildRow, BuildHeaders().Length);
        App.Printer.PrintTable(Title + FamilyFilter.TitleSuffix, BuildHeaders(), rows);
    }

    [RelayCommand]
    private void ExportPdf()
    {
        var rows = OrderFamilyFilter.BuildGroupedRows(Lines, BuildRow, BuildHeaders().Length);
        var path = App.PdfExport.ExportTable(App.Settings.ResolvedPdfExportPath, Title + FamilyFilter.TitleSuffix, BuildHeaders(), rows, App.CompanyLogo);
        App.Dialogs.ShowMessage("Export PDF", $"Bon de commande généré avec succès.\n\nIl est stocké dans :\n{path}");
    }
}
