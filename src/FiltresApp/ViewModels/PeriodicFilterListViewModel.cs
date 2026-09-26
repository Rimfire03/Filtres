using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

public partial class PeriodicFilterListViewModel : ObservableObject, IReloadable
{
    private readonly FilterCategory _category;
    private readonly string _locationColumnLabel;

    public string Title { get; }
    public bool ShowHourCounter => _category == FilterCategory.Charbon;
    public bool ShowK7Reference => _category == FilterCategory.G3;

    /// <summary>Regroupement automatique par famille déduite de la Dimension, demandé uniquement pour
    /// l'écran "Filtres G3" (voir <see cref="PeriodicFilterRowViewModel.DimensionFamilyLabel"/>) :
    /// "Filtres à laver" (Dimension contient "laver"), "Filtres à remplacer" (Dimension reconnue comme
    /// une dimension physique, voir <see cref="DimensionFormatService"/>), "Sans dimension" sinon.</summary>
    public bool ShowDimensionFamilyGrouping => _category == FilterCategory.G3;

    /// <summary>Option "Changé tous les 15 jours" (menu contextuel + colonne indicateur de la grille) :
    /// demandée uniquement pour l'écran "Filtres G4 plissés".</summary>
    public bool ShowChangedEvery15DaysOption => _category == FilterCategory.G4Plisse;

    /// <summary>Année consultée, partagée par tous les écrans (voir <see cref="FiltresApp.App.YearContext"/>).
    /// Changer d'année ne supprime jamais rien : l'historique des années précédentes reste en base et
    /// redevient consultable en resélectionnant cette année.</summary>
    public YearContext YearContext => App.YearContext;

    [ObservableProperty] private ObservableCollection<PeriodicFilterRowViewModel> _filters = new();
    [ObservableProperty] private PeriodicFilterRowViewModel? _selectedFilter;
    [ObservableProperty] private int? _monthFilter;
    [ObservableProperty] private string _monthFilterLabel = "Tous les mois";

    /// <summary>Options du sélecteur "Mois consulté" (case à cocher/date de la grille) pour l'année
    /// choisie : Décembre de l'année précédente en premier (pratique pour finir de pointer un
    /// changement fait fin décembre une fois basculé sur la nouvelle année), puis Janvier à Décembre de
    /// l'année choisie. Reconstruites à chaque changement d'année (voir constructeur).</summary>
    [ObservableProperty] private List<ConsultedMonthOption> _consultedMonthOptions = new();

    /// <summary>Mois actuellement lu/écrit par les colonnes "Réalisé" / "Date du changement" de la
    /// grille. Par défaut, le mois calendaire du jour dans l'année choisie.</summary>
    [ObservableProperty] private ConsultedMonthOption? _selectedConsultedMonth;

    partial void OnSelectedConsultedMonthChanged(ConsultedMonthOption? value) => RefreshRows();

    /// <summary>Reconstruit <see cref="ConsultedMonthOptions"/> pour l'année choisie, en conservant la
    /// même position dans la liste (donc le même mois "relatif") qu'avant le changement d'année.</summary>
    private void RefreshConsultedMonthOptions() =>
        (ConsultedMonthOptions, SelectedConsultedMonth) =
            ConsultedMonthOption.Rebuild(YearContext.Year, ConsultedMonthOptions, SelectedConsultedMonth);

    /// <summary>Source du ComboBox "Filtrer par mois" (clé nullable : null = "Tous les mois", sinon
    /// numéro de mois 1-12). La valeur interne stockée/filtrée reste toujours un entier 1-12 ; seul
    /// l'affichage montre le nom du mois en toutes lettres au lieu du chiffre.</summary>
    public List<KeyValuePair<int?, string>> MonthFilterOptions { get; } =
        new List<KeyValuePair<int?, string>> { new(null, "Tous les mois") }
            .Concat(ConsultedMonthOption.MonthLabels.Select((label, i) => new KeyValuePair<int?, string>(i + 1, label)))
            .ToList();

    public PeriodicFilterListViewModel(FilterCategory category, string title, string locationColumnLabel)
    {
        _category = category;
        Title = title;
        _locationColumnLabel = locationColumnLabel;
        HeaderFilter = new NameDimensionFilter(ApplyFilters);
        RefreshConsultedMonthOptions();
        App.YearContext.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(Services.YearContext.Year)) return;
            RefreshConsultedMonthOptions();
            RefreshRows();
        };
        Load();
    }

    /// <summary>Déclenché par la sélection dans le ComboBox "Filtrer par mois" (remplace les anciens
    /// boutons numérotés 1-12 : voir MonthFilterOptions).</summary>
    partial void OnMonthFilterChanged(int? value)
    {
        MonthFilterLabel = value.HasValue ? ConsultedMonthOption.MonthLabels[value.Value - 1] : "Tous les mois";
        ApplyFilters();
    }

    // ---- Filtres placés sous les titres de colonnes ----

    /// <summary>Colonnes "Filtres" (nom contient) et "Dimension".</summary>
    public NameDimensionFilter HeaderFilter { get; }

    private List<PeriodicFilter> _allFilters = new();
    private Dictionary<int, string> _linkedLines = new();

    /// <summary>Applique les filtres d'affichage (mois, nom, dimension) sans relire la base.</summary>
    private void ApplyFilters()
    {
        var filtered = _allFilters.Where(f =>
            (!MonthFilter.HasValue || f.GetPeriodicityMonths().Contains(MonthFilter.Value))
            && HeaderFilter.Matches(f.Location, f.Dimension));

        // Trié explicitement ici (plutôt que via CollectionViewSource.SortDescriptions, qui ne garantit
        // pas l'ordre des groupes dans la grille) : rang de famille (Filtres à remplacer avant Filtres à
        // laver avant Sans dimension, écran G3 uniquement, voir DimensionFamilyRank), puis emplacement.
        Filters = new ObservableCollection<PeriodicFilterRowViewModel>(
            filtered.OrderBy(f => f.DimensionFamilyRank).ThenBy(f => f.Location, StringComparer.CurrentCultureIgnoreCase)
                .Select(f => new PeriodicFilterRowViewModel(f, this, _linkedLines.GetValueOrDefault(f.Id))));
    }

    private void RefreshRows()
    {
        foreach (var row in Filters) row.RefreshAll();
    }

    /// <summary>Rechargé à chaque ouverture de l'écran : le rattachement aux lignes de Commande /
    /// Inventaire (puce verte / rouge) se modifie depuis l'écran Commande.</summary>
    public void Reload() => Load();

    private void Load()
    {
        // Ligne de Commande / Inventaire à laquelle chaque filtre est rattaché (un filtre = une ligne au plus).
        _linkedLines = FilterLinkService.PeriodicLinkedLines(App.Db, _category);

        _allFilters = App.Db.PeriodicFilters
            .Include(f => f.Replacements)
            .Where(f => f.Category == _category)
            .AsNoTracking()
            .OrderBy(f => f.Location)
            .ToList();

        HeaderFilter.RefreshDimensions(_allFilters.Select(f => f.Dimension));
        ApplyFilters();
    }

    [RelayCommand]
    private void ResetFilter()
    {
        MonthFilter = null;
        HeaderFilter.Reset();
    }

    [RelayCommand]
    private void AddFilter()
    {
        if (!App.GuardWritable()) return;
        var entity = new PeriodicFilter { Category = _category };
        if (!EditEntity(entity, isNew: true)) return;

        App.Db.PeriodicFilters.Add(entity);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void EditFilter()
    {
        if (!App.GuardWritable()) return;
        if (SelectedFilter is null) return;
        var tracked = App.Db.PeriodicFilters.Include(f => f.Replacements).First(f => f.Id == SelectedFilter.Id);
        if (!EditEntity(tracked, isNew: false)) return;

        App.Db.SaveChanges();
        Load();
    }

    private bool EditEntity(PeriodicFilter entity, bool isNew)
    {
        var months = entity.GetPeriodicityMonths();
        var fields = new List<EditField>
        {
            EditField.Text(_locationColumnLabel, () => entity.Location, v => entity.Location = v, required: true),
            EditField.Multiline("Dimension", () => entity.Dimension, v => entity.Dimension = v ?? ""),
            EditField.Text("Type", () => entity.MediaType, v => entity.MediaType = v),
            EditField.IntField("Quantité en place", () => entity.QuantityInPlace, v => entity.QuantityInPlace = v),
            EditField.MonthsField("Périodicité de remplacement (mois)", () => months, v => months = v)
        };

        if (ShowK7Reference)
            fields.Add(EditField.NullableText("Référence Liste K7", () => entity.K7Reference, v => entity.K7Reference = v));

        if (ShowHourCounter)
            fields.Add(EditField.NullableInt("Compteur d'heures", () => entity.HourCounter, v => entity.HourCounter = v));

        fields.Add(EditField.Multiline("Notes", () => entity.Notes, v => entity.Notes = v));

        var ok = App.Dialogs.EditFields(isNew ? "Ajouter un filtre" : "Modifier le filtre", fields);
        if (ok)
        {
            entity.Periodicity = PeriodicFilter.FormatPeriodicityMonths(months);
            ProposeDimensionCorrection(() => entity.Dimension, v => entity.Dimension = v);
        }
        return ok;
    }

    /// <summary>Propose une correction du champ Dimension selon la norme harmonisée ("aaaa x bbbb x
    /// cccc", voir <see cref="DimensionFormatService"/>) juste après validation du formulaire, si le
    /// texte saisi ne correspond pas déjà exactement à cette norme. N'affiche rien si aucun motif de
    /// dimension n'a pu être reconnu (le texte reste inchangé, ex. "A laver"). Partagée avec
    /// <see cref="OpacimetricFilterListViewModel"/> (même règle pour les filtres F7 à H13).</summary>
    internal static void ProposeDimensionCorrection(Func<string> get, Action<string> set)
    {
        var raw = get();
        var normalized = DimensionFormatService.Normalize(raw);
        if (normalized is null || normalized == raw) return;

        if (App.Dialogs.ShowConfirm("Format de dimension",
                "Le format standard des dimensions est « aaaa x bbbb x cccc » (le plus grand des deux " +
                "premiers nombres en premier, l'épaisseur toujours en dernier).\n\n" +
                $"Remplacer :\n« {raw} »\n\npar :\n« {normalized} » ?"))
        {
            set(normalized);
        }
    }

    [RelayCommand]
    private void DeleteFilter()
    {
        if (!App.GuardWritable()) return;
        if (SelectedFilter is null) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer définitivement '{SelectedFilter.Location}' ?")) return;

        var tracked = App.Db.PeriodicFilters.First(f => f.Id == SelectedFilter.Id);
        App.Db.PeriodicFilters.Remove(tracked);
        App.Db.SaveChanges();
        Load();
    }

    /// <summary>Case à cocher de la grille : trouve ou crée la ligne de suivi (mois consulté, voir
    /// <see cref="SelectedConsultedMonth"/>) et fixe la date du jour. Ne supprime jamais les lignes des
    /// autres mois/années : c'est ce qui permet de changer d'année sans perdre l'historique.</summary>
    public void SetReplacementDone(PeriodicFilter filter, bool done)
    {
        if (!App.GuardWritable() || SelectedConsultedMonth is not { } consulted) return;
        var year = consulted.Year;
        var month = consulted.Month;
        var tracked = App.Db.PeriodicFilters.Include(f => f.Replacements).First(f => f.Id == filter.Id);
        var existing = tracked.Replacements.FirstOrDefault(r => r.Month == month && r.Year == year);

        if (done)
        {
            if (existing is null)
            {
                existing = new FilterReplacement { Month = month, Year = year };
                tracked.Replacements.Add(existing);
            }
            existing.DateDone = DateOnly.FromDateTime(DateTime.Today);
            existing.QuantityDone = filter.QuantityInPlace;
        }
        else if (existing is not null)
        {
            // Décocher réinitialise le statut et vide la date plutôt que de laisser une ligne
            // "réalisée sans date" ambiguë ; la ligne de suivi du mois est simplement supprimée.
            tracked.Replacements.Remove(existing);
            App.Db.FilterReplacements.Remove(existing);
        }

        App.Db.SaveChanges();
        SyncReplacements(filter, tracked.Replacements);
    }

    /// <summary>Bascule l'option "Changé tous les 15 jours" (menu contextuel de la grille, G4 plissé
    /// uniquement) et sauvegarde immédiatement en base.</summary>
    public void SetChangedEvery15Days(PeriodicFilter filter, bool value)
    {
        if (!App.GuardWritable()) return;
        var tracked = App.Db.PeriodicFilters.First(f => f.Id == filter.Id);
        tracked.ChangedEvery15Days = value;
        App.Db.SaveChanges();
        filter.ChangedEvery15Days = value;
    }

    /// <summary>"Consulter l'historique..." (menu contextuel de la grille, visible sur toutes les
    /// catégories) : ouvre une fenêtre de lecture seule listant, pour l'année choisie parmi celles où ce
    /// filtre précis a effectivement de l'historique, les 12 mois avec statut réalisé/date. Ne modifie
    /// jamais rien (l'édition reste via la case à cocher de la grille principale).</summary>
    public void ShowHistory(PeriodicFilter filter)
    {
        var replacements = App.Db.FilterReplacements
            .AsNoTracking()
            .Where(r => r.PeriodicFilterId == filter.Id)
            .ToList();
        App.Dialogs.ShowFilterHistory(_locationColumnLabel, filter.Location, filter.Dimension, replacements);
    }

    public void SetReplacementDate(PeriodicFilter filter, DateOnly? date)
    {
        if (!App.GuardWritable() || SelectedConsultedMonth is not { } consulted) return;
        var year = consulted.Year;
        var month = consulted.Month;
        var tracked = App.Db.PeriodicFilters.Include(f => f.Replacements).First(f => f.Id == filter.Id);
        var existing = tracked.Replacements.FirstOrDefault(r => r.Month == month && r.Year == year);

        if (date is null)
        {
            if (existing is not null)
            {
                tracked.Replacements.Remove(existing);
                App.Db.FilterReplacements.Remove(existing);
            }
        }
        else
        {
            if (existing is null)
            {
                existing = new FilterReplacement { Month = month, Year = year, QuantityDone = filter.QuantityInPlace };
                tracked.Replacements.Add(existing);
            }
            existing.DateDone = date;
        }

        App.Db.SaveChanges();
        SyncReplacements(filter, tracked.Replacements);
    }

    /// <summary>Répercute l'état des remplacements sur l'objet affiché (issu d'une requête AsNoTracking)
    /// pour rafraîchir la ligne concernée sans recharger toute la grille.</summary>
    private static void SyncReplacements(PeriodicFilter filter, List<FilterReplacement> trackedReplacements)
    {
        filter.Replacements = trackedReplacements.Select(r => new FilterReplacement
        {
            Id = r.Id,
            PeriodicFilterId = r.PeriodicFilterId,
            Month = r.Month,
            Year = r.Year,
            QuantityDone = r.QuantityDone,
            DateDone = r.DateDone
        }).ToList();
    }

    /// <summary>Seule impression de l'écran : feuille de terrain reprenant uniquement ce qui est visible
    /// (lignes affichées après le filtre par mois, colonnes non masquées sur ce poste, dans l'ordre de la
    /// grille), avec une grande case à cocher "Fait" par ligne.</summary>
    [RelayCommand]
    private void Print()
    {
        var columns = PrintableColumns().Where(c => !IsColumnHidden(c.Key)).ToList();
        var headers = columns.Select(c => c.Header).ToArray();
        var rows = Filters.Select(f => columns.Select(c => c.Value(f)).ToArray()).ToList();
        var title = Title;
        if (MonthFilter.HasValue) title += $" - {MonthFilterLabel}";
        title += HeaderFilter.TitleSuffix;
        App.Printer.PrintTable(title, headers, rows, includeCheckboxColumn: true);
    }

    /// <summary>Colonnes de la grille (même ordre, mêmes titres que PeriodicFilterView.xaml).</summary>
    private IEnumerable<(string Key, string Header, Func<PeriodicFilterRowViewModel, string> Value)> PrintableColumns()
    {
        yield return ("Lié", "Lié", f => f.IsWashable ? "" : f.IsLinkedToOrder ? "Oui" : "Non");
        yield return ("Filtres", "Filtres", f => f.Location);
        yield return ("Dimension", "Dimension", f => f.Dimension);
        yield return ("Type", "Type", f => f.MediaType);
        yield return ("Qté en place", "Qté en place", f => f.QuantityInPlace.ToString());
        yield return ("Périodicité", "Périodicité", f => f.PeriodicityDisplay);
        yield return ("Prochaine échéance", "Prochaine échéance", f => f.NextDueDate?.ToString("MM/yyyy") ?? "-");
        yield return ("Dernier changement", "Dernier changement", f => f.LastDoneDate?.ToString("dd/MM/yyyy") ?? "-");
        yield return ("Réalisé", "Réalisé", f => f.IsDoneForConsultedMonth ? "Oui" : "");
        yield return ("Date du changement", "Date du changement", f => f.DateDoneForConsultedMonth?.ToString("dd/MM/yyyy") ?? "");
    }

    /// <summary>Colonne masquée sur ce poste (voir ColumnChooser).</summary>
    private bool IsColumnHidden(string key) => ColumnPreferences.IsHidden(Title, key);

}
