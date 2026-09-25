using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

public partial class PeriodicFilterListViewModel : ObservableObject
{
    private readonly FilterCategory _category;
    private readonly string _locationColumnLabel;

    public string Title { get; }
    public string LocationColumnLabel => _locationColumnLabel;
    public bool ShowHourCounter => _category == FilterCategory.Charbon;
    public bool ShowK7Reference => _category == FilterCategory.G3;

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

    /// <summary>Mois "consulté" : celui pour lequel la colonne case à cocher / date de la grille
    /// lit et écrit le suivi. Indépendant du filtre d'affichage par mois (<see cref="MonthFilter"/>),
    /// qui ne fait que masquer les lignes non dues ce mois-ci.</summary>
    [ObservableProperty] private int _consultedMonth = DateTime.Today.Month;

    public static readonly string[] MonthLabels =
        { "Janvier", "Février", "Mars", "Avril", "Mai", "Juin", "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre" };

    /// <summary>Source du ComboBox "Mois consulté" (clé = numéro de mois 1-12, valeur = libellé).</summary>
    public List<KeyValuePair<int, string>> MonthOptions { get; } =
        MonthLabels.Select((label, i) => new KeyValuePair<int, string>(i + 1, label)).ToList();

    /// <summary>Source du ComboBox "Filtrer par mois" (clé nullable : null = "Tous les mois", sinon
    /// numéro de mois 1-12). La valeur interne stockée/filtrée reste toujours un entier 1-12 ; seul
    /// l'affichage montre le nom du mois en toutes lettres au lieu du chiffre.</summary>
    public List<KeyValuePair<int?, string>> MonthFilterOptions { get; } =
        new List<KeyValuePair<int?, string>> { new(null, "Tous les mois") }
            .Concat(MonthLabels.Select((label, i) => new KeyValuePair<int?, string>(i + 1, label)))
            .ToList();

    public PeriodicFilterListViewModel(FilterCategory category, string title, string locationColumnLabel)
    {
        _category = category;
        Title = title;
        _locationColumnLabel = locationColumnLabel;
        App.YearContext.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Services.YearContext.Year)) RefreshRows();
        };
        Load();
    }

    partial void OnConsultedMonthChanged(int value) => RefreshRows();

    /// <summary>Déclenché par la sélection dans le ComboBox "Filtrer par mois" (remplace les anciens
    /// boutons numérotés 1-12 : voir MonthFilterOptions).</summary>
    partial void OnMonthFilterChanged(int? value)
    {
        MonthFilterLabel = value.HasValue ? MonthLabels[value.Value - 1] : "Tous les mois";
        Load();
    }

    private void RefreshRows()
    {
        foreach (var row in Filters) row.RefreshAll();
    }

    private void Load()
    {
        var all = App.Db.PeriodicFilters
            .Include(f => f.Replacements)
            .Where(f => f.Category == _category)
            .AsNoTracking()
            .OrderBy(f => f.Location)
            .ToList();

        var filtered = MonthFilter.HasValue ? all.Where(f => f.GetPeriodicityMonths().Contains(MonthFilter.Value)) : all;
        Filters = new ObservableCollection<PeriodicFilterRowViewModel>(filtered.Select(f => new PeriodicFilterRowViewModel(f, this)));
    }

    [RelayCommand]
    private void ResetFilter() => MonthFilter = null;

    [RelayCommand]
    private void AddFilter()
    {
        var entity = new PeriodicFilter { Category = _category };
        if (!EditEntity(entity, isNew: true)) return;

        App.Db.PeriodicFilters.Add(entity);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void EditFilter()
    {
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
            EditField.Text("Dimension", () => entity.Dimension, v => entity.Dimension = v),
            EditField.Text("Média / type de filtre", () => entity.MediaType, v => entity.MediaType = v),
            EditField.IntField("Quantité en place", () => entity.QuantityInPlace, v => entity.QuantityInPlace = v),
            EditField.MonthsField("Périodicité de remplacement (mois)", () => months, v => months = v)
        };

        if (ShowK7Reference)
            fields.Add(EditField.NullableText("Référence Liste K7", () => entity.K7Reference, v => entity.K7Reference = v));

        if (ShowHourCounter)
            fields.Add(EditField.NullableInt("Compteur d'heures", () => entity.HourCounter, v => entity.HourCounter = v));

        fields.Add(EditField.Multiline("Notes", () => entity.Notes, v => entity.Notes = v));

        var ok = App.Dialogs.EditFields(isNew ? "Ajouter un filtre" : "Modifier le filtre", fields);
        if (ok) entity.Periodicity = PeriodicFilter.FormatPeriodicityMonths(months);
        return ok;
    }

    [RelayCommand]
    private void DeleteFilter()
    {
        if (SelectedFilter is null) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer définitivement '{SelectedFilter.Location}' ?")) return;

        var tracked = App.Db.PeriodicFilters.First(f => f.Id == SelectedFilter.Id);
        App.Db.PeriodicFilters.Remove(tracked);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void MarkReplacement()
    {
        if (SelectedFilter is null) return;

        var defaultMonth = SelectedFilter.NextDueDate?.Month ?? DateTime.Today.Month;
        var picked = App.Dialogs.PickReplacement(defaultMonth, SelectedFilter.QuantityInPlace);
        if (picked is null) return;

        var tracked = App.Db.PeriodicFilters.Include(f => f.Replacements).First(f => f.Id == SelectedFilter.Id);
        var existing = tracked.Replacements.FirstOrDefault(r => r.Month == picked.Value.Month && r.Year == picked.Value.Date.Year);
        if (existing != null)
        {
            existing.QuantityDone = picked.Value.Quantity;
            existing.DateDone = picked.Value.Date;
        }
        else
        {
            tracked.Replacements.Add(new FilterReplacement
            {
                Month = picked.Value.Month,
                Year = picked.Value.Date.Year,
                QuantityDone = picked.Value.Quantity,
                DateDone = picked.Value.Date
            });
        }

        App.Db.SaveChanges();
        App.YearContext.EnsureYear(picked.Value.Date.Year);
        Load();
    }

    /// <summary>Case à cocher de la grille : trouve ou crée la ligne de suivi (mois consulté / année
    /// consultée) et fixe la date du jour. Ne supprime jamais les lignes des autres mois/années : c'est
    /// ce qui permet de changer d'année sans perdre l'historique.</summary>
    public void SetReplacementDone(PeriodicFilter filter, bool done)
    {
        var year = YearContext.Year;
        var month = ConsultedMonth;
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
        var year = YearContext.Year;
        var month = ConsultedMonth;
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

    [RelayCommand]
    private void Print()
    {
        var headers = BuildHeaders();
        var rows = Filters.Select(BuildRow).ToList();
        App.Printer.PrintTable(Title, headers, rows);
    }

    /// <summary>Impression dédiée au mois consulté : feuille de terrain simple à cocher au marqueur
    /// (pas un rapport de données) avec uniquement emplacement/dimension/qté en place et une grande
    /// case à cocher vierge (voir PrintService.PrintMonth).</summary>
    [RelayCommand]
    private void PrintMonth()
    {
        var rows = Filters
            .Where(f => f.Filter.GetPeriodicityMonths().Contains(ConsultedMonth))
            .Select(f => (f.Location, f.Dimension, f.QuantityInPlace.ToString()))
            .ToList();

        App.Printer.PrintMonth(Title, ConsultedMonth, YearContext.Year, _locationColumnLabel, rows);
    }

    [RelayCommand]
    private void ExportExcelYear()
    {
        var path = App.ExcelExport.ExportYear(App.Db, App.Settings.ResolvedPdfExportPath, YearContext.Year);
        App.Dialogs.ShowMessage("Export Excel",
            $"Export de l'année {YearContext.Year} généré avec succès (toutes catégories, une feuille par catégorie).\n\nIl est stocké dans :\n{path}");
    }

    private string[] BuildHeaders()
    {
        var headers = new List<string> { _locationColumnLabel, "Dimension", "Média", "Qté en place", "Périodicité" };
        if (ShowK7Reference) headers.Add("Réf. K7");
        if (ShowHourCounter) headers.Add("Compteur h.");
        headers.Add("Prochaine échéance");
        headers.Add("Dernier changement");
        return headers.ToArray();
    }

    private string[] BuildRow(PeriodicFilterRowViewModel f)
    {
        var row = new List<string>
        {
            f.Location, f.Dimension, f.MediaType, f.QuantityInPlace.ToString(), f.PeriodicityDisplay
        };
        if (ShowK7Reference) row.Add(f.K7Reference ?? "");
        if (ShowHourCounter) row.Add(f.HourCounter?.ToString() ?? "");
        row.Add(f.NextDueDate?.ToString("MM/yyyy") ?? "-");
        row.Add(f.LastDoneDate?.ToString("dd/MM/yyyy") ?? "-");
        return row.ToArray();
    }
}
