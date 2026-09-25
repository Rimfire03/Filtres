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

    /// <summary>Mois lu et écrit par les colonnes "Réalisé" / "Date du changement" de la grille : toujours
    /// le mois en cours, pour l'année choisie dans la barre latérale.</summary>
    public int CurrentMonth => DateTime.Today.Month;

    public string CurrentMonthLabel => $"{MonthLabels[CurrentMonth - 1]} {YearContext.Year}";

    public static readonly string[] MonthLabels =
        { "Janvier", "Février", "Mars", "Avril", "Mai", "Juin", "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre" };

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
            if (e.PropertyName != nameof(Services.YearContext.Year)) return;
            OnPropertyChanged(nameof(CurrentMonthLabel));
            RefreshRows();
        };
        Load();
    }

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

    /// <summary>Rechargé à chaque ouverture de l'écran : le rattachement aux lignes de Commande /
    /// Inventaire (puce verte / rouge) se modifie depuis l'écran Commande.</summary>
    public void Reload() => Load();

    private void Load()
    {
        // Ligne de Commande / Inventaire à laquelle chaque filtre est rattaché (un filtre = une ligne au plus).
        var linkedLines = App.Db.OrderLinePeriodicFilters
            .AsNoTracking()
            .Where(l => l.PeriodicFilter!.Category == _category)
            .Select(l => new { l.PeriodicFilterId, l.OrderLine!.Designation })
            .ToList()
            .GroupBy(l => l.PeriodicFilterId)
            .ToDictionary(g => g.Key, g => g.First().Designation);

        var all = App.Db.PeriodicFilters
            .Include(f => f.Replacements)
            .Where(f => f.Category == _category)
            .AsNoTracking()
            .OrderBy(f => f.Location)
            .ToList();

        var filtered = MonthFilter.HasValue ? all.Where(f => f.GetPeriodicityMonths().Contains(MonthFilter.Value)) : all;
        Filters = new ObservableCollection<PeriodicFilterRowViewModel>(filtered.Select(f => new PeriodicFilterRowViewModel(f, this, linkedLines.GetValueOrDefault(f.Id))));
    }

    [RelayCommand]
    private void ResetFilter() => MonthFilter = null;

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
            EditField.Text("Dimension", () => entity.Dimension, v => entity.Dimension = v),
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
        if (ok) entity.Periodicity = PeriodicFilter.FormatPeriodicityMonths(months);
        return ok;
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

    [RelayCommand]
    private void MarkReplacement()
    {
        if (!App.GuardWritable()) return;
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

    /// <summary>Case à cocher de la grille : trouve ou crée la ligne de suivi (mois en cours / année
    /// consultée) et fixe la date du jour. Ne supprime jamais les lignes des autres mois/années : c'est
    /// ce qui permet de changer d'année sans perdre l'historique.</summary>
    public void SetReplacementDone(PeriodicFilter filter, bool done)
    {
        if (!App.GuardWritable()) return;
        var year = YearContext.Year;
        var month = CurrentMonth;
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
        if (!App.GuardWritable()) return;
        var year = YearContext.Year;
        var month = CurrentMonth;
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
        var title = MonthFilter.HasValue ? $"{Title} - {MonthFilterLabel}" : Title;
        App.Printer.PrintTable(title, headers, rows, includeCheckboxColumn: true);
    }

    /// <summary>Colonnes de la grille (même ordre, mêmes titres que PeriodicFilterView.xaml).</summary>
    private IEnumerable<(string Key, string Header, Func<PeriodicFilterRowViewModel, string> Value)> PrintableColumns()
    {
        yield return ("Lié", "Lié", f => f.IsLinkedToOrder ? "Oui" : "Non");
        yield return (_locationColumnLabel, _locationColumnLabel, f => f.Location);
        yield return ("Dimension", "Dimension", f => f.Dimension);
        yield return ("Type", "Type", f => f.MediaType);
        yield return ("Qté en place", "Qté en place", f => f.QuantityInPlace.ToString());
        yield return ("Périodicité", "Périodicité", f => f.PeriodicityDisplay);
        yield return ("Prochaine échéance", "Prochaine échéance", f => f.NextDueDate?.ToString("MM/yyyy") ?? "-");
        yield return ("Dernier changement", "Dernier changement", f => f.LastDoneDate?.ToString("dd/MM/yyyy") ?? "-");
        yield return ("Réalisé", "Réalisé", f => f.IsDoneForCurrentMonth ? "Oui" : "");
        yield return ("Date du changement", "Date du changement", f => f.DateDoneForCurrentMonth?.ToString("dd/MM/yyyy") ?? "");
    }

    /// <summary>Colonne masquée sur ce poste (voir ColumnChooser). La colonne d'emplacement, dont le titre
    /// est lié au ViewModel, peut être mémorisée par sa position si ce titre n'était pas encore résolu.</summary>
    private bool IsColumnHidden(string key) =>
        ColumnPreferences.IsHidden(Title, key) || (key == _locationColumnLabel && ColumnPreferences.IsHidden(Title, "#1"));

    [RelayCommand]
    private void ExportExcelYear()
    {
        var path = App.ExcelExport.ExportYear(App.Db, App.Settings.ResolvedPdfExportPath, YearContext.Year);
        App.Dialogs.ShowMessage("Export Excel",
            $"Export de l'année {YearContext.Year} généré avec succès (toutes catégories, une feuille par catégorie).\n\nIl est stocké dans :\n{path}");
    }
}
