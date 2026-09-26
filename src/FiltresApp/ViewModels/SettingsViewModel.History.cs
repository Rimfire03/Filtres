using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Data;
using FiltresApp.Core.Services;

namespace FiltresApp.ViewModels;

/// <summary>Paramètres : suppression de l'historique d'une année.</summary>
public partial class SettingsViewModel
{
    // ---- Suppression de l'historique d'une année (deux fonctions séparées) ----
    [ObservableProperty] private List<int> _periodicHistoryYears = new();
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeletePeriodicHistoryCommand))]
    private int? _selectedPeriodicHistoryYear;

    [ObservableProperty] private List<int> _dynamicHistoryYears = new();
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteDynamicHistoryCommand))]
    private int? _selectedDynamicHistoryYear;

    [ObservableProperty] private string _historyStatusMessage = string.Empty;

    private void LoadHistoryYears()
    {
        PeriodicHistoryYears = HistoryCleanupService.GetPeriodicYears(App.Db);
        SelectedPeriodicHistoryYear = PeriodicHistoryYears.Count > 0 ? PeriodicHistoryYears[^1] : null;
        DynamicHistoryYears = HistoryCleanupService.GetDynamicYears(App.Db);
        SelectedDynamicHistoryYear = DynamicHistoryYears.Count > 0 ? DynamicHistoryYears[^1] : null;
    }

    private bool CanDeletePeriodicHistory() => SelectedPeriodicHistoryYear.HasValue;
    private bool CanDeleteDynamicHistory() => SelectedDynamicHistoryYear.HasValue;

    /// <summary>Supprime l'historique de l'année choisie pour G4 plissé, G4 plan, G3 et Charbon.</summary>
    [RelayCommand(CanExecute = nameof(CanDeletePeriodicHistory))]
    private void DeletePeriodicHistory()
    {
        if (SelectedPeriodicHistoryYear is not int year) return;
        DeleteHistory(year, "Filtres G4 plissés, G4 plan, G3 et Charbon",
            HistoryCleanupService.CountPeriodic, HistoryCleanupService.DeletePeriodicYear);
    }

    /// <summary>Supprime l'historique de l'année choisie pour les filtres "Filtres F7 à H14", toutes
    /// variétés confondues.</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteDynamicHistory))]
    private void DeleteDynamicHistory()
    {
        if (SelectedDynamicHistoryYear is not int year) return;
        DeleteHistory(year, "Filtres F7 à H14 (toutes variétés)",
            HistoryCleanupService.CountDynamic, HistoryCleanupService.DeleteDynamicYear);
    }

    private void DeleteHistory(int year, string scope,
        Func<FiltresDbContext, int, int> count, Func<FiltresDbContext, int, int> delete)
    {
        if (!App.GuardWritable()) return;

        var n = count(App.Db, year);
        if (!App.Dialogs.ShowConfirm("Supprimer l'historique",
                $"Supprimer définitivement l'historique de l'année {year} ?\n\n{scope} : {n} remplacement(s) enregistré(s) seront supprimés. " +
                "Les filtres eux-mêmes sont conservés.\n\n" +
                "Une copie de sauvegarde de la base sera faite juste avant.")) return;

        try
        {
            var backup = App.DbFactory.CreateBackup($"avant-suppression-historique-{year}");
            var deleted = delete(App.Db, year);
            // Des remplacements supprimés peuvent encore être suivis en mémoire par le contexte.
            App.Db.ChangeTracker.Clear();
            (System.Windows.Application.Current.MainWindow?.DataContext as MainViewModel)?.ResetOtherScreens();
            HistoryStatusMessage = $"{scope} : historique {year} supprimé ({deleted} remplacement(s)). Sauvegarde : {backup}";
        }
        catch (Exception ex)
        {
            HistoryStatusMessage = $"Erreur pendant la suppression : {ex.Message}";
        }
        LoadHistoryYears();
        OnPropertyChanged(nameof(DatabaseSizeDisplay));
    }
}
