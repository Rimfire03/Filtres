using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Écran d'une variété créée librement sous le menu dépliant "Filtres F7 à H14" (voir
/// <see cref="FilterVariety"/>) : même disposition que l'ancien écran unique "Filtres F7 à H13"
/// (familles créées à la main, historique de remplacements ponctuels sans périodicité mensuelle fixe,
/// rattachement à Commande / Inventaire), généralisée à une variété quelconque. Partie commune avec
/// "Courroies" et "Roulements" : voir <see cref="TrackedItemListViewModel{TEntity, TFamily, TRow}"/> ;
/// familles propres à la variété.</summary>
public partial class DynamicFilterListViewModel : TrackedItemListViewModel<DynamicFilter, DynamicFilterFamily, DynamicFilterRowViewModel>
{
    private readonly MainViewModel _main;

    public FilterVariety Variety { get; private set; }
    public override string Title => Variety.Nom;

    /// <summary>Bouton "Gérer les variétés" (distinct du mode édition de cette page) : ouvre la page
    /// d'accueil "Filtres F7 à H14", seul endroit où créer/modifier/supprimer des variétés (voir
    /// <see cref="MainViewModel.NavigateToVarietyManagement"/>).</summary>
    public string VarietyManagementButtonLabel => "Gérer les variétés";

    [RelayCommand]
    private void ToggleVarietyManagement() => _main.NavigateToVarietyManagement();

    public DynamicFilterListViewModel(FilterVariety variety, MainViewModel main)
    {
        Variety = variety;
        _main = main;
        HeaderFilter = new NameDimensionFilter(ApplyFilters);
        App.YearContext.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Services.YearContext.Year)) return;
            Load();
        };
        Initialize();
    }

    /// <summary>Enregistre un remplacement (quantité en place) daté de <paramref name="date"/>.</summary>
    public override bool AddReplacement(DynamicFilter filter, DateOnly date)
    {
        if (!App.GuardWritable()) return false;
        filter.Replacements = ReplacementTrackingService.AddDynamicReplacement(App.Db, filter.Id, date);
        App.YearContext.EnsureYear(date.Year);
        return true;
    }

    /// <summary>Rechargé à chaque ouverture de l'écran : familles et rattachements à Commande / Inventaire
    /// (puce "Lié") peuvent avoir changé ailleurs.</summary>
    public override void Reload()
    {
        // La variété elle-même peut avoir été renommée depuis l'écran de gestion des variétés.
        var fresh = App.Db.FilterVarieties.AsNoTracking().FirstOrDefault(v => v.Id == Variety.Id);
        if (fresh is not null)
        {
            Variety = fresh;
            OnPropertyChanged(nameof(Title));
        }
        base.Reload();
    }

    protected override List<DynamicFilter> LoadEntities(FamilyOption family)
    {
        var query = App.Db.DynamicFilters
            .Include(f => f.Replacements)
            .Include(f => f.Family)
            .AsNoTracking()
            .Where(f => f.VarietyId == Variety.Id);
        if (family.IsNoFamily) query = query.Where(f => f.DynamicFilterFamilyId == null);
        else if (family.FamilyId is int familyId) query = query.Where(f => f.DynamicFilterFamilyId == familyId);
        return query.OrderBy(f => f.DynamicFilterFamilyId == null).ThenBy(f => f.Family!.Nom).ThenBy(f => f.Location).ToList();
    }

    protected override DynamicFilterRowViewModel CreateRow(DynamicFilter filter) =>
        new(filter, YearContext.Year, this, _linkedLines.GetValueOrDefault(filter.Id));

    protected override void Load()
    {
        // Ligne de Commande / Inventaire à laquelle chaque filtre est rattaché (un filtre = une ligne au plus).
        _linkedLines = FilterLinkService.DynamicLinkedLines(App.Db, Variety.Id);
        _loadedFilters = LoadEntities(SelectedFamily ?? AllFamilies);
        HeaderFilter.RefreshDimensions(_loadedFilters.Select(f => f.Dimension));
        ApplyFilters();
    }

    // ---- Filtres placés sous les titres de colonnes (même fonctionnement que G4 / G3 / Charbon) ----

    /// <summary>Colonnes "Nom de la centrale d'air" (nom contient) et "Dimension".</summary>
    public NameDimensionFilter HeaderFilter { get; }

    private List<DynamicFilter> _loadedFilters = new();
    private Dictionary<int, string> _linkedLines = new();

    /// <summary>Applique les filtres nom / dimension sans relire la base.</summary>
    private void ApplyFilters() =>
        Rows = new ObservableCollection<DynamicFilterRowViewModel>(
            _loadedFilters.Where(f => HeaderFilter.Matches(f.Location, f.Dimension)).Select(CreateRow));

    [RelayCommand]
    private void ResetFilters() => HeaderFilter.Reset();

    protected override DynamicFilter CreateEntity(int? familyId) => new() { VarietyId = Variety.Id, DynamicFilterFamilyId = familyId };

    protected override DynamicFilter CopyEntity(DynamicFilter source) => new()
    {
        VarietyId = source.VarietyId,
        Location = source.Location,
        Dimension = source.Dimension,
        FilterType = source.FilterType,
        QuantityInPlace = source.QuantityInPlace,
        Commentaire = source.Commentaire,
        DynamicFilterFamilyId = source.DynamicFilterFamilyId,
        RowColorId = source.RowColorId
    };

    protected override bool EditEntity(DynamicFilter entity, bool isNew)
    {
        var fields = new List<EditField>
        {
            FamilyField(entity.DynamicFilterFamilyId, id => entity.DynamicFilterFamilyId = id),
            EditField.Multiline("Nom de la centrale d'air", () => entity.Location, v => entity.Location = v, required: true),
            EditField.Multiline("Dimension", () => entity.Dimension, v => entity.Dimension = v ?? ""),
            EditField.NullableText("Type", () => entity.FilterType, v => entity.FilterType = v, uppercase: true),
            EditField.IntField("Quantité en place", () => entity.QuantityInPlace, v => entity.QuantityInPlace = v),
            EditField.Multiline("Commentaire", () => entity.Commentaire, v => entity.Commentaire = v)
        };
        var ok = App.Dialogs.EditFields(isNew ? $"Ajouter un filtre « {Variety.Nom} »" : "Modifier le filtre", fields);
        if (ok) DimensionCorrectionPrompt.Propose(() => entity.Dimension, v => entity.Dimension = v);
        return ok;
    }

    [RelayCommand]
    private void Print()
    {
        // Les en-têtes affichés incluent l'année consultée (varie chaque année) : les clés de colonne
        // utilisées pour le filtrage "Colonnes imprimées" (Paramètres, réglage générique partagé par
        // toutes les listes de filtres) doivent rester stables, voir PrintableColumnsRegistry.
        var headers = new[] { "Nom de la centrale d'air", "Dimension", "Type", "Qté en place", $"Dernier changement ({YearContext.Year})", $"Nb remplacements ({YearContext.Year})", "Commentaire" };
        var columnKeys = new[] { "Nom", "Dimension", "Type", "Qté en place", "Dernier changement", "Nb remplacements", "Commentaire" };
        var rows = PrintService.BuildGroupedRows(Rows, f => f.FamilyGroupLabel, f => new[]
        {
            f.Location, f.Dimension, f.FilterType ?? "", f.QuantityInPlace.ToString(),
            f.LastChangedDateInYear?.ToString("dd/MM/yyyy") ?? "-", f.ReplacementCountInYear.ToString(), f.Commentaire ?? ""
        }, headers.Length);
        var rowColors = PrintService.BuildGroupedRowColors(Rows, f => f.FamilyGroupLabel, f => RowColorPalette.ColorFor(f.RowColorId));
        var (filteredHeaders, filteredRows) = PrintService.FilterByPrintKeys(PrintableColumnsRegistry.FilterListsKey, columnKeys, headers, rows);
        App.Printer.PrintTable(PrintTitle + HeaderFilter.TitleSuffix, filteredHeaders, filteredRows, rowColors: rowColors);
    }

    /// <summary>Valeurs déjà utilisées pour "Type" (toutes familles confondues de cette variété), pour le
    /// menu rapide au clic droit sur la colonne (voir DynamicFilterView.xaml.cs) : liste dynamique, pas de
    /// valeurs figées en dur, puisque cette colonne reste un champ texte libre.</summary>
    public List<string> GetDistinctFilterTypes() =>
        App.Db.DynamicFilters.AsNoTracking()
            .Where(f => f.VarietyId == Variety.Id)
            .Select(f => f.FilterType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .OrderBy(t => t)
            .Select(t => t!)
            .ToList();

    /// <summary>Applique le type choisi dans le menu rapide, sauvegarde immédiatement, et met à jour la
    /// ligne affichée sans recharger toute la grille (pas de <see cref="Load"/> : perdrait la sélection).</summary>
    /// <param name="newType">Nouveau type, ou null pour effacer la cellule.</param>
    public void SetFilterType(DynamicFilterRowViewModel row, string? newType)
    {
        if (!App.GuardWritable()) return;
        var tracked = App.Db.DynamicFilters.First(f => f.Id == row.Id);
        tracked.FilterType = newType;
        App.Db.SaveChanges();
        row.ApplyFilterType(newType);
    }

    /// <summary>Remplacements de ce filtre précis (qté changée + date), par année : cette feuille n'a pas de
    /// mois fixe.</summary>
    protected override void OpenHistory(DynamicFilter filter)
    {
        var replacements = App.Db.DynamicFilterReplacements
            .AsNoTracking()
            .Where(r => r.DynamicFilterId == filter.Id)
            .ToList();
        var dimensionText = string.IsNullOrWhiteSpace(filter.Dimension) ? "-" : filter.Dimension;
        App.Dialogs.ShowReplacementHistory(new ReplacementHistory<DynamicFilterReplacement>(
            $"Nom de la centrale d'air : {filter.Location}    —    Dimension : {dimensionText}",
            "Aucun historique disponible pour ce filtre : aucun remplacement enregistré en base pour l'instant.",
            "Quantité changée", r => r.QuantityChanged.ToString(), r => $"quantité {r.QuantityChanged}"), replacements);
    }

    // ---- Familles de la variété (partie commune : TrackedItemListViewModel.Families.cs) ----

    protected override List<DynamicFilterFamily> LoadFamilies() =>
        App.Db.DynamicFilterFamilies.AsNoTracking().Where(f => f.VarietyId == Variety.Id).OrderBy(f => f.Nom).ToList();

    protected override DynamicFilterFamily CreateFamily() => new() { VarietyId = Variety.Id };

    protected override bool FamilyNameExists(string name, int excludedId) =>
        App.Db.DynamicFilterFamilies.AsNoTracking().Any(f => f.VarietyId == Variety.Id && f.Id != excludedId && f.Nom.ToLower() == name.ToLower());

    protected override int CountItemsInFamily(int familyId) => App.Db.DynamicFilters.Count(f => f.DynamicFilterFamilyId == familyId);

    protected override void DeleteFamilyKeepingItems(int familyId)
    {
        App.Db.DynamicFilters.Where(f => f.DynamicFilterFamilyId == familyId)
            .ExecuteUpdate(s => s.SetProperty(f => f.DynamicFilterFamilyId, (int?)null));
        App.Db.DynamicFilterFamilies.Where(f => f.Id == familyId).ExecuteDelete();
    }

    protected override string ItemsLabel => "filtres";

    protected override string FamilyDeleteDetail(int count) =>
        $"Ses {count} filtre(s) ne seront pas supprimés : ils passeront en « {DynamicFilter.NoFamilyLabel} ».";
}
