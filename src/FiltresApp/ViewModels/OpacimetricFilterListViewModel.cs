using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

public partial class OpacimetricFilterListViewModel : ObservableObject, IReloadable
{
    public string Title => "Filtres F7 à H13";

    /// <summary>Cette feuille n'a pas de périodicité mensuelle fixe (contrairement aux 4 autres
    /// catégories) : c'est un historique de remplacements ponctuels. L'année consultée ne filtre donc
    /// pas des lignes de suivi mensuel, mais restreint "Dernier changement" / "Nb remplacements" aux
    /// remplacements datés de cette année-là ; l'historique complet reste toujours en base et l'export
    /// Excel de l'année inclut cette catégorie comme les 4 autres.</summary>
    public YearContext YearContext => App.YearContext;

    [ObservableProperty] private ObservableCollection<OpacimetricFilterRowViewModel> _filters = new();
    [ObservableProperty] private OpacimetricFilterRowViewModel? _selectedFilter;

    // ---- Familles (créées à la main, attribuées manuellement à chaque filtre) ----

    public record FamilyOption(int? FamilyId, bool IsNoFamily, string Label)
    {
        public bool IsAll => FamilyId is null && !IsNoFamily;
    }

    private static readonly FamilyOption AllFamilies = new(null, false, "Toutes les familles");
    private static readonly FamilyOption NoFamily = new(null, true, OpacimetricFilter.NoFamilyLabel);

    public List<OpacimetricFamily> Families { get; private set; } = new();
    [ObservableProperty] private List<FamilyOption> _familyOptions = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFamilySelected))]
    [NotifyCanExecuteChangedFor(nameof(RenameFamilyCommand), nameof(DeleteFamilyCommand))]
    private FamilyOption? _selectedFamily;

    public bool IsFamilySelected => SelectedFamily?.FamilyId is not null;

    private bool _refreshingFamilies;

    // ---- Mois consulté (case "Réalisé" / "Date du changement" de la grille) ----

    /// <summary>Options du sélecteur "Mois consulté" : Décembre de l'année précédente, puis Janvier à
    /// Décembre de l'année choisie (même fonctionnement que les écrans G4 / G3 / Charbon).</summary>
    [ObservableProperty] private List<ConsultedMonthOption> _consultedMonthOptions = new();
    [ObservableProperty] private ConsultedMonthOption? _selectedConsultedMonth;

    partial void OnSelectedConsultedMonthChanged(ConsultedMonthOption? value)
    {
        foreach (var row in Filters) row.RefreshConsultedMonth();
    }

    private void RefreshConsultedMonthOptions() =>
        (ConsultedMonthOptions, SelectedConsultedMonth) =
            ConsultedMonthOption.Rebuild(YearContext.Year, ConsultedMonthOptions, SelectedConsultedMonth);

    public OpacimetricFilterListViewModel()
    {
        HeaderFilter = new NameDimensionFilter(ApplyFilters);
        RefreshConsultedMonthOptions();
        App.YearContext.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Services.YearContext.Year)) return;
            RefreshConsultedMonthOptions();
            Load();
        };
        RefreshFamilies();
        Load();
    }

    private static bool IsInMonth(OpacimetricReplacement r, ConsultedMonthOption m) =>
        r.DateChanged is DateOnly d && m.Contains(d);

    /// <summary>Case "Réalisé" : cochée, crée un remplacement (quantité en place) daté du jour si le mois
    /// consulté est le mois en cours, sinon du 1er du mois consulté ; décochée, supprime les remplacements
    /// datés dans ce mois. L'historique des autres mois n'est jamais touché.</summary>
    public void SetReplacementDone(OpacimetricFilter filter, bool done)
    {
        if (!App.GuardWritable() || SelectedConsultedMonth is not { } month) return;
        var tracked = App.Db.OpacimetricFilters.Include(f => f.Replacements).First(f => f.Id == filter.Id);
        var inMonth = tracked.Replacements.Where(r => IsInMonth(r, month)).ToList();

        if (done && inMonth.Count == 0)
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var date = month.Contains(today) ? today : new DateOnly(month.Year, month.Month, 1);
            tracked.Replacements.Add(new OpacimetricReplacement { QuantityChanged = tracked.QuantityInPlace, DateChanged = date });
        }
        else if (!done)
        {
            foreach (var r in inMonth) App.Db.OpacimetricReplacements.Remove(r);
        }

        App.Db.SaveChanges();
        SyncReplacements(filter, tracked.Replacements);
        App.YearContext.EnsureYear(month.Year);
    }

    /// <summary>Colonne "Date du changement" : fixe la date du remplacement du mois consulté (le crée au
    /// besoin), ou le supprime si la date est vidée. La date doit rester dans le mois consulté.</summary>
    public void SetReplacementDate(OpacimetricFilter filter, DateOnly? date)
    {
        if (!App.GuardWritable() || SelectedConsultedMonth is not { } month) return;
        if (date is DateOnly d && !month.Contains(d))
        {
            App.Dialogs.ShowMessage("Date du changement", $"La date doit être en {month.Label} (mois consulté). Changez de mois consulté pour saisir un autre mois.");
            return;
        }

        var tracked = App.Db.OpacimetricFilters.Include(f => f.Replacements).First(f => f.Id == filter.Id);
        var inMonth = tracked.Replacements.Where(r => IsInMonth(r, month)).OrderBy(r => r.DateChanged).ToList();

        if (date is null)
        {
            foreach (var r in inMonth) App.Db.OpacimetricReplacements.Remove(r);
        }
        else if (inMonth.Count > 0)
        {
            inMonth[^1].DateChanged = date;
        }
        else
        {
            tracked.Replacements.Add(new OpacimetricReplacement { QuantityChanged = tracked.QuantityInPlace, DateChanged = date });
        }

        App.Db.SaveChanges();
        SyncReplacements(filter, tracked.Replacements);
    }

    /// <summary>Répercute les remplacements enregistrés sur l'objet affiché (issu d'une requête sans suivi).</summary>
    private void SyncReplacements(OpacimetricFilter filter, List<OpacimetricReplacement> tracked)
    {
        filter.Replacements = tracked
            .Where(r => App.Db.Entry(r).State != EntityState.Deleted && App.Db.Entry(r).State != EntityState.Detached)
            .Select(r => new OpacimetricReplacement
            {
                Id = r.Id,
                OpacimetricFilterId = r.OpacimetricFilterId,
                QuantityChanged = r.QuantityChanged,
                DateChanged = r.DateChanged
            }).ToList();
    }

    partial void OnSelectedFamilyChanged(FamilyOption? value)
    {
        if (!_refreshingFamilies) Load();
    }

    /// <summary>Relit les familles en conservant le filtre choisi s'il existe toujours.</summary>
    private void RefreshFamilies(int? selectFamilyId = null)
    {
        Families = App.Db.OpacimetricFamilies.AsNoTracking().OrderBy(f => f.Nom).ToList();
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
        RefreshFamilies();
        Load();
    }

    private void Load()
    {
        // Ligne de Commande / Inventaire à laquelle chaque filtre est rattaché (un filtre = une ligne au plus).
        _linkedLines = FilterLinkService.OpacimetricLinkedLines(App.Db);

        var query = App.Db.OpacimetricFilters
            .Include(f => f.Replacements)
            .Include(f => f.Family)
            .AsNoTracking();

        var selected = SelectedFamily ?? AllFamilies;
        if (selected.IsNoFamily) query = query.Where(f => f.OpacimetricFamilyId == null);
        else if (selected.FamilyId is int familyId) query = query.Where(f => f.OpacimetricFamilyId == familyId);

        // Familles par nom, "Sans famille" en dernier : ordre des séparateurs de la grille.
        _loadedFilters = query
            .OrderBy(f => f.OpacimetricFamilyId == null)
            .ThenBy(f => f.Family!.Nom)
            .ThenBy(f => f.Location)
            .ToList();

        HeaderFilter.RefreshDimensions(_loadedFilters.Select(f => f.Dimension));
        ApplyFilters();
    }

    // ---- Filtres placés sous les titres de colonnes (même fonctionnement que G4 / G3 / Charbon) ----

    /// <summary>Colonnes "Nom de la centrale d'air" (nom contient) et "Dimension".</summary>
    public NameDimensionFilter HeaderFilter { get; }

    private List<OpacimetricFilter> _loadedFilters = new();
    private Dictionary<int, string> _linkedLines = new();

    /// <summary>Applique les filtres nom / dimension sans relire la base.</summary>
    private void ApplyFilters()
    {
        var filtered = _loadedFilters.Where(f => HeaderFilter.Matches(f.Location, f.Dimension));

        Filters = new ObservableCollection<OpacimetricFilterRowViewModel>(
            filtered.Select(f => new OpacimetricFilterRowViewModel(f, YearContext.Year, this, _linkedLines.GetValueOrDefault(f.Id))));
    }

    [RelayCommand]
    private void ResetFilters() => HeaderFilter.Reset();

    [RelayCommand]
    private void AddFamily()
    {
        if (!App.GuardWritable()) return;
        var family = new OpacimetricFamily();
        if (!EditFamilyName(family, "Nouvelle famille F7 à H13")) return;
        App.Db.OpacimetricFamilies.Add(family);
        App.Db.SaveChanges();
        RefreshFamilies(family.Id);
        Load();
    }

    [RelayCommand(CanExecute = nameof(IsFamilySelected))]
    private void RenameFamily()
    {
        if (!App.GuardWritable() || SelectedFamily?.FamilyId is not int id) return;
        var tracked = App.Db.OpacimetricFamilies.First(f => f.Id == id);
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
        var count = App.Db.OpacimetricFilters.Count(f => f.OpacimetricFamilyId == id);
        var detail = count == 0 ? "" : $"\n\nSes {count} filtre(s) ne seront pas supprimés : ils passeront en « {OpacimetricFilter.NoFamilyLabel} ».";
        if (!App.Dialogs.ShowConfirm("Supprimer la famille", $"Supprimer la famille « {SelectedFamily.Label} » ?{detail}")) return;

        App.Db.OpacimetricFilters.Where(f => f.OpacimetricFamilyId == id)
            .ExecuteUpdate(s => s.SetProperty(f => f.OpacimetricFamilyId, (int?)null));
        App.Db.OpacimetricFamilies.Where(f => f.Id == id).ExecuteDelete();
        // Les filtres éventuellement suivis par le contexte gardent l'ancienne famille en mémoire.
        App.Db.ChangeTracker.Clear();

        RefreshFamilies();
        Load();
    }

    private bool EditFamilyName(OpacimetricFamily family, string title)
    {
        var fields = new List<EditField>
        {
            EditField.Text("Nom de la famille", () => family.Nom, v => family.Nom = v.Trim(), required: true)
        };
        if (!App.Dialogs.EditFields(title, fields)) return false;

        var name = family.Nom;
        string? error = null;
        if (string.Equals(name, OpacimetricFilter.NoFamilyLabel, StringComparison.OrdinalIgnoreCase))
            error = $"« {OpacimetricFilter.NoFamilyLabel} » est réservé aux filtres sans famille : choisissez un autre nom.";
        else if (App.Db.OpacimetricFamilies.AsNoTracking().Any(f => f.Id != family.Id && f.Nom.ToLower() == name.ToLower()))
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
        var entity = new OpacimetricFilter { OpacimetricFamilyId = SelectedFamily?.FamilyId };
        if (!EditEntity(entity, true)) return;
        App.Db.OpacimetricFilters.Add(entity);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void EditFilter()
    {
        if (!App.GuardWritable()) return;
        if (SelectedFilter is null) return;
        var tracked = App.Db.OpacimetricFilters.Include(f => f.Replacements).First(f => f.Id == SelectedFilter.Id);
        if (!EditEntity(tracked, false)) return;
        App.Db.SaveChanges();
        Load();
    }

    private bool EditEntity(OpacimetricFilter entity, bool isNew)
    {
        var families = Families;
        var familyNames = new List<string> { OpacimetricFilter.NoFamilyLabel };
        familyNames.AddRange(families.Select(f => f.Nom));
        var familyIndex = entity.OpacimetricFamilyId is int id ? families.FindIndex(f => f.Id == id) + 1 : 0;

        var fields = new List<EditField>
        {
            EditField.ComboField("Famille", familyNames, () => familyIndex,
                v => entity.OpacimetricFamilyId = v >= 1 && v <= families.Count ? families[v - 1].Id : null),
            EditField.Text("Nom de la centrale d'air", () => entity.Location, v => entity.Location = v, required: true),
            EditField.Multiline("Dimension", () => entity.Dimension, v => entity.Dimension = v ?? ""),
            EditField.NullableText("Type", () => entity.FilterType, v => entity.FilterType = v),
            EditField.IntField("Quantité en place", () => entity.QuantityInPlace, v => entity.QuantityInPlace = v),
            EditField.Multiline("Notes", () => entity.Notes, v => entity.Notes = v)
        };
        var ok = App.Dialogs.EditFields(isNew ? "Ajouter un filtre F7/H13" : "Modifier le filtre", fields);
        if (ok) PeriodicFilterListViewModel.ProposeDimensionCorrection(() => entity.Dimension, v => entity.Dimension = v);
        return ok;
    }

    [RelayCommand]
    private void DeleteFilter()
    {
        if (!App.GuardWritable()) return;
        if (SelectedFilter is null) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer définitivement '{SelectedFilter.Location}' ?")) return;
        var tracked = App.Db.OpacimetricFilters.First(f => f.Id == SelectedFilter.Id);
        App.Db.OpacimetricFilters.Remove(tracked);
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

    /// <summary>Valeurs déjà utilisées pour "Type" (toutes familles confondues), pour le menu rapide au
    /// clic droit sur la colonne (voir OpacimetricFilterView.xaml.cs) : liste dynamique, pas de valeurs
    /// figées en dur, puisque cette colonne reste un champ texte libre.</summary>
    public List<string> GetDistinctFilterTypes() =>
        App.Db.OpacimetricFilters.AsNoTracking()
            .Select(f => f.FilterType)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct()
            .OrderBy(t => t)
            .Select(t => t!)
            .ToList();

    /// <summary>Applique le type choisi dans le menu rapide, sauvegarde immédiatement, et met à jour la
    /// ligne affichée sans recharger toute la grille (pas de <see cref="Load"/> : perdrait la sélection).</summary>
    /// <param name="newType">Nouveau type, ou null pour effacer la cellule.</param>
    public void SetFilterType(OpacimetricFilterRowViewModel row, string? newType)
    {
        if (!App.GuardWritable()) return;
        var tracked = App.Db.OpacimetricFilters.First(f => f.Id == row.Id);
        tracked.FilterType = newType;
        App.Db.SaveChanges();
        row.ApplyFilterType(newType);
    }

    /// <summary>"Consulter l'historique..." (menu contextuel de la grille) : ouvre une fenêtre de lecture
    /// seule listant, pour l'année choisie parmi celles où ce filtre précis a de l'historique, la liste
    /// chronologique des remplacements (qté changée + date). Cette feuille n'a pas de mois fixe (voir
    /// Views.Dialogs.OpacimetricHistoryWindow).</summary>
    public void ShowHistory(OpacimetricFilter filter)
    {
        var replacements = App.Db.OpacimetricReplacements
            .AsNoTracking()
            .Where(r => r.OpacimetricFilterId == filter.Id)
            .ToList();
        App.Dialogs.ShowOpacimetricHistory(filter.Location, filter.Dimension, replacements);
    }
}
