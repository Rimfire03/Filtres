using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

/// <summary>Écran d'une variété créée librement sous le menu dépliant "Filtres F7 à H14" (voir
/// <see cref="FilterVariety"/>) : même disposition que l'ancien écran unique "Filtres F7 à H13"
/// (familles créées à la main, historique de remplacements ponctuels sans périodicité mensuelle fixe,
/// rattachement à Commande / Inventaire), généralisée à une variété quelconque.</summary>
public partial class DynamicFilterListViewModel : ObservableObject, IReloadable
{
    public FilterVariety Variety { get; private set; }
    public string Title => Variety.Nom;

    /// <summary>L'année consultée ne filtre pas des lignes de suivi mensuel (cette feuille n'a pas de
    /// périodicité fixe) : elle restreint "Dernier changement" / "Nb remplacements" aux remplacements
    /// datés de cette année-là ; l'historique complet reste toujours en base.</summary>
    public YearContext YearContext => App.YearContext;

    [ObservableProperty] private ObservableCollection<DynamicFilterRowViewModel> _filters = new();
    [ObservableProperty] private DynamicFilterRowViewModel? _selectedFilter;

    // ---- Familles (créées à la main, attribuées manuellement à chaque filtre) ----

    public record FamilyOption(int? FamilyId, bool IsNoFamily, string Label)
    {
        public bool IsAll => FamilyId is null && !IsNoFamily;
    }

    private static readonly FamilyOption AllFamilies = new(null, false, "Toutes les familles");
    private static readonly FamilyOption NoFamily = new(null, true, DynamicFilter.NoFamilyLabel);

    public List<DynamicFilterFamily> Families { get; private set; } = new();
    [ObservableProperty] private List<FamilyOption> _familyOptions = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFamilySelected))]
    [NotifyCanExecuteChangedFor(nameof(RenameFamilyCommand), nameof(DeleteFamilyCommand))]
    private FamilyOption? _selectedFamily;

    public bool IsFamilySelected => SelectedFamily?.FamilyId is not null;

    private bool _refreshingFamilies;

    // ---- Date de changement (colonne de la grille) ----

    public DynamicFilterListViewModel(FilterVariety variety)
    {
        Variety = variety;
        HeaderFilter = new NameDimensionFilter(ApplyFilters);
        App.YearContext.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Services.YearContext.Year)) return;
            Load();
        };
        RefreshFamilies();
        Load();
    }

    /// <summary>Colonne "Date du changement" : pas de mois à cocher pour cette feuille (contrairement aux
    /// écrans à périodicité) - saisir une date enregistre directement un nouveau remplacement (quantité en
    /// place) daté de cette date, sans jamais modifier l'historique déjà enregistré. Le champ de saisie se
    /// vide ensuite (colonne d'ajout, pas d'affichage d'une valeur existante) : voir <see cref="ShowHistory"/>
    /// pour consulter l'historique complet d'un filtre.</summary>
    public void AddReplacement(DynamicFilter filter, DateOnly date)
    {
        if (!App.GuardWritable()) return;
        filter.Replacements = ReplacementTrackingService.AddDynamicReplacement(App.Db, filter.Id, date);
        App.YearContext.EnsureYear(date.Year);
    }

    partial void OnSelectedFamilyChanged(FamilyOption? value)
    {
        if (!_refreshingFamilies) Load();
    }

    /// <summary>Relit les familles en conservant le filtre choisi s'il existe toujours.</summary>
    private void RefreshFamilies(int? selectFamilyId = null)
    {
        Families = App.Db.DynamicFilterFamilies.AsNoTracking().Where(f => f.VarietyId == Variety.Id).OrderBy(f => f.Nom).ToList();
        var options = new List<FamilyOption> { AllFamilies, NoFamily };
        options.AddRange(Families.Select(f => new FamilyOption(f.Id, false, f.Nom)));

        var previous = SelectedFamily;
        _refreshingFamilies = true;
        try
        {
            FamilyOptions = options;
            SelectedFamily = selectFamilyId is int id
                ? options.First(o => o.FamilyId == id)
                : options.FirstOrDefault(o => previous is not null && o.FamilyId == previous.FamilyId && o.IsNoFamily == previous.IsNoFamily)
                  ?? AllFamilies;
        }
        finally
        {
            _refreshingFamilies = false;
        }
    }

    /// <summary>Rechargé à chaque ouverture de l'écran : familles et rattachements à Commande / Inventaire
    /// (puce "Lié") peuvent avoir changé ailleurs.</summary>
    public void Reload()
    {
        // La variété elle-même peut avoir été renommée depuis l'écran de gestion des variétés.
        var fresh = App.Db.FilterVarieties.AsNoTracking().FirstOrDefault(v => v.Id == Variety.Id);
        if (fresh is not null)
        {
            Variety = fresh;
            OnPropertyChanged(nameof(Title));
        }
        RefreshFamilies();
        Load();
    }

    private void Load()
    {
        // Ligne de Commande / Inventaire à laquelle chaque filtre est rattaché (un filtre = une ligne au plus).
        _linkedLines = FilterLinkService.DynamicLinkedLines(App.Db, Variety.Id);

        var query = App.Db.DynamicFilters
            .Include(f => f.Replacements)
            .Include(f => f.Family)
            .AsNoTracking()
            .Where(f => f.VarietyId == Variety.Id);

        var selected = SelectedFamily ?? AllFamilies;
        if (selected.IsNoFamily) query = query.Where(f => f.DynamicFilterFamilyId == null);
        else if (selected.FamilyId is int familyId) query = query.Where(f => f.DynamicFilterFamilyId == familyId);

        // Familles par nom, "Sans famille" en dernier : ordre des séparateurs de la grille.
        _loadedFilters = query
            .OrderBy(f => f.DynamicFilterFamilyId == null)
            .ThenBy(f => f.Family!.Nom)
            .ThenBy(f => f.Location)
            .ToList();

        HeaderFilter.RefreshDimensions(_loadedFilters.Select(f => f.Dimension));
        ApplyFilters();
    }

    // ---- Filtres placés sous les titres de colonnes (même fonctionnement que G4 / G3 / Charbon) ----

    /// <summary>Colonnes "Nom de la centrale d'air" (nom contient) et "Dimension".</summary>
    public NameDimensionFilter HeaderFilter { get; }

    private List<DynamicFilter> _loadedFilters = new();
    private Dictionary<int, string> _linkedLines = new();

    /// <summary>Applique les filtres nom / dimension sans relire la base.</summary>
    private void ApplyFilters()
    {
        var filtered = _loadedFilters.Where(f => HeaderFilter.Matches(f.Location, f.Dimension));

        Filters = new ObservableCollection<DynamicFilterRowViewModel>(
            filtered.Select(f => new DynamicFilterRowViewModel(f, YearContext.Year, this, _linkedLines.GetValueOrDefault(f.Id))));
    }

    [RelayCommand]
    private void ResetFilters() => HeaderFilter.Reset();

    [RelayCommand]
    private void AddFamily()
    {
        if (!App.GuardWritable()) return;
        var family = new DynamicFilterFamily { VarietyId = Variety.Id };
        if (!EditFamilyName(family, $"Nouvelle famille « {Variety.Nom} »")) return;
        App.Db.DynamicFilterFamilies.Add(family);
        App.Db.SaveChanges();
        RefreshFamilies(family.Id);
        Load();
    }

    [RelayCommand(CanExecute = nameof(IsFamilySelected))]
    private void RenameFamily()
    {
        if (!App.GuardWritable() || SelectedFamily?.FamilyId is not int id) return;
        var tracked = App.Db.DynamicFilterFamilies.First(f => f.Id == id);
        if (!EditFamilyName(tracked, "Renommer la famille")) return;
        App.Db.SaveChanges();
        RefreshFamilies(id);
        Load();
    }

    /// <summary>Les filtres de la famille ne sont pas supprimés : ils passent en "Sans famille".</summary>
    [RelayCommand(CanExecute = nameof(IsFamilySelected))]
    private void DeleteFamily()
    {
        if (!App.GuardWritable() || SelectedFamily?.FamilyId is not int id) return;
        var count = App.Db.DynamicFilters.Count(f => f.DynamicFilterFamilyId == id);
        var detail = count == 0 ? "" : $"\n\nSes {count} filtre(s) ne seront pas supprimés : ils passeront en « {DynamicFilter.NoFamilyLabel} ».";
        if (!App.Dialogs.ShowConfirm("Supprimer la famille", $"Supprimer la famille « {SelectedFamily.Label} » ?{detail}")) return;

        App.Db.DynamicFilters.Where(f => f.DynamicFilterFamilyId == id)
            .ExecuteUpdate(s => s.SetProperty(f => f.DynamicFilterFamilyId, (int?)null));
        App.Db.DynamicFilterFamilies.Where(f => f.Id == id).ExecuteDelete();
        // Les filtres éventuellement suivis par le contexte gardent l'ancienne famille en mémoire.
        App.Db.ChangeTracker.Clear();

        RefreshFamilies();
        Load();
    }

    private bool EditFamilyName(DynamicFilterFamily family, string title)
    {
        var fields = new List<EditField>
        {
            EditField.Text("Nom de la famille", () => family.Nom, v => family.Nom = v.Trim(), required: true)
        };
        if (!App.Dialogs.EditFields(title, fields)) return false;

        var name = family.Nom;
        string? error = null;
        if (string.Equals(name, DynamicFilter.NoFamilyLabel, StringComparison.OrdinalIgnoreCase))
            error = $"« {DynamicFilter.NoFamilyLabel} » est réservé aux filtres sans famille : choisissez un autre nom.";
        else if (App.Db.DynamicFilterFamilies.AsNoTracking().Any(f => f.VarietyId == Variety.Id && f.Id != family.Id && f.Nom.ToLower() == name.ToLower()))
            error = $"Une famille « {name} » existe déjà.";

        if (error is null) return true;
        App.Dialogs.ShowMessage("Famille", error);
        // Le renommage refusé ne doit pas rester en mémoire sur l'entité suivie.
        if (family.Id != 0) App.Db.Entry(family).Reload();
        return false;
    }

    [RelayCommand]
    private void AddFilter()
    {
        if (!App.GuardWritable()) return;
        // Nouveau filtre rangé par défaut dans la famille filtrée.
        var entity = new DynamicFilter { VarietyId = Variety.Id, DynamicFilterFamilyId = SelectedFamily?.FamilyId };
        if (!EditEntity(entity, true)) return;
        App.Db.DynamicFilters.Add(entity);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void EditFilter()
    {
        if (!App.GuardWritable()) return;
        if (SelectedFilter is null) return;
        var tracked = App.Db.DynamicFilters.Include(f => f.Replacements).First(f => f.Id == SelectedFilter.Id);
        if (!EditEntity(tracked, false)) return;
        App.Db.SaveChanges();
        Load();
    }

    private bool EditEntity(DynamicFilter entity, bool isNew)
    {
        var families = Families;
        var familyNames = new List<string> { DynamicFilter.NoFamilyLabel };
        familyNames.AddRange(families.Select(f => f.Nom));
        var familyIndex = entity.DynamicFilterFamilyId is int id ? families.FindIndex(f => f.Id == id) + 1 : 0;

        var fields = new List<EditField>
        {
            EditField.ComboField("Famille", familyNames, () => familyIndex,
                v => entity.DynamicFilterFamilyId = v >= 1 && v <= families.Count ? families[v - 1].Id : null),
            EditField.Text("Nom de la centrale d'air", () => entity.Location, v => entity.Location = v, required: true),
            EditField.Multiline("Dimension", () => entity.Dimension, v => entity.Dimension = v ?? ""),
            EditField.NullableText("Type", () => entity.FilterType, v => entity.FilterType = v),
            EditField.IntField("Quantité en place", () => entity.QuantityInPlace, v => entity.QuantityInPlace = v),
            EditField.Multiline("Notes", () => entity.Notes, v => entity.Notes = v)
        };
        var ok = App.Dialogs.EditFields(isNew ? $"Ajouter un filtre « {Variety.Nom} »" : "Modifier le filtre", fields);
        if (ok) DimensionCorrectionPrompt.Propose(() => entity.Dimension, v => entity.Dimension = v);
        return ok;
    }

    [RelayCommand]
    private void DeleteFilter()
    {
        if (!App.GuardWritable()) return;
        if (SelectedFilter is null) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer définitivement '{SelectedFilter.Location}' ?")) return;
        var tracked = App.Db.DynamicFilters.First(f => f.Id == SelectedFilter.Id);
        App.Db.DynamicFilters.Remove(tracked);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void Print()
    {
        var headers = new[] { "Nom de la centrale d'air", "Dimension", "Type", "Qté en place", $"Dernier changement ({YearContext.Year})", $"Nb remplacements ({YearContext.Year})" };
        var rows = PrintService.BuildGroupedRows(Filters, f => f.FamilyGroupLabel, f => new[]
        {
            f.Location, f.Dimension, f.FilterType ?? "", f.QuantityInPlace.ToString(),
            f.LastChangedDateInYear?.ToString("dd/MM/yyyy") ?? "-", f.ReplacementCountInYear.ToString()
        }, headers.Length);
        var documentTitle = SelectedFamily is null || SelectedFamily.IsAll ? Title : $"{Title} - {SelectedFamily.Label}";
        documentTitle += HeaderFilter.TitleSuffix;
        App.Printer.PrintTable(documentTitle, headers, rows);
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

    /// <summary>"Consulter l'historique..." (menu contextuel de la grille) : ouvre une fenêtre de lecture
    /// seule listant, pour l'année choisie parmi celles où ce filtre précis a de l'historique, la liste
    /// chronologique des remplacements (qté changée + date). Cette feuille n'a pas de mois fixe.</summary>
    public void ShowHistory(DynamicFilter filter)
    {
        var replacements = App.Db.DynamicFilterReplacements
            .AsNoTracking()
            .Where(r => r.DynamicFilterId == filter.Id)
            .ToList();
        App.Dialogs.ShowDynamicFilterHistory(filter.Location, filter.Dimension, replacements);
    }
}
