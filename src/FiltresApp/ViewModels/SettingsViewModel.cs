using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Data;
using FiltresApp.Core.Services;
using FiltresApp.Services;
using Microsoft.Win32;

namespace FiltresApp.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _databasePath;
    [ObservableProperty] private string _pdfExportPath;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _exportStatusMessage = string.Empty;

    [ObservableProperty] private bool _autoUpdateEnabled;
    [ObservableProperty] private string _updateStatusMessage = string.Empty;
    [ObservableProperty] private bool _isCheckingForUpdate;
    [ObservableProperty] private bool _isInstallingUpdate;
    [ObservableProperty] private bool _updateAvailable;

    private UpdateInfo? _pendingUpdate;

    public string CurrentVersion => App.CurrentVersion;
    public int DatabaseVersion => App.DatabaseVersion;

    /// <summary>Année à exporter en Excel (voir bouton "Exporter l'année en Excel" ci-dessous),
    /// partagée avec le sélecteur d'année global de la barre latérale.</summary>
    public YearContext YearContext => App.YearContext;

    // ---- Suppression de l'historique d'une année (deux fonctions séparées) ----
    [ObservableProperty] private List<int> _periodicHistoryYears = new();
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeletePeriodicHistoryCommand))]
    private int? _selectedPeriodicHistoryYear;

    [ObservableProperty] private List<int> _opacimetricHistoryYears = new();
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteOpacimetricHistoryCommand))]
    private int? _selectedOpacimetricHistoryYear;

    [ObservableProperty] private string _historyStatusMessage = string.Empty;

    public SettingsViewModel()
    {
        _databasePath = App.Settings.DatabasePath;
        _pdfExportPath = App.Settings.PdfExportPath;
        _autoUpdateEnabled = App.Settings.AutoUpdateEnabled;
        LoadHistoryYears();
    }

    private void LoadHistoryYears()
    {
        PeriodicHistoryYears = HistoryCleanupService.GetPeriodicYears(App.Db);
        SelectedPeriodicHistoryYear = PeriodicHistoryYears.Count > 0 ? PeriodicHistoryYears[^1] : null;
        OpacimetricHistoryYears = HistoryCleanupService.GetOpacimetricYears(App.Db);
        SelectedOpacimetricHistoryYear = OpacimetricHistoryYears.Count > 0 ? OpacimetricHistoryYears[^1] : null;
    }

    private bool CanDeletePeriodicHistory() => SelectedPeriodicHistoryYear.HasValue;
    private bool CanDeleteOpacimetricHistory() => SelectedOpacimetricHistoryYear.HasValue;

    /// <summary>Supprime l'historique de l'année choisie pour G4 plissé, G4 plan, G3 et Charbon
    /// uniquement (pas F7 à H13, qui a sa propre fonction).</summary>
    [RelayCommand(CanExecute = nameof(CanDeletePeriodicHistory))]
    private void DeletePeriodicHistory()
    {
        if (SelectedPeriodicHistoryYear is not int year) return;
        DeleteHistory(year, "Filtres G4 plissés, G4 plan, G3 et Charbon", "Les filtres F7 à H13 ne sont pas concernés.",
            HistoryCleanupService.CountPeriodic, HistoryCleanupService.DeletePeriodicYear);
    }

    /// <summary>Supprime l'historique de l'année choisie pour les filtres F7 à H13 uniquement.</summary>
    [RelayCommand(CanExecute = nameof(CanDeleteOpacimetricHistory))]
    private void DeleteOpacimetricHistory()
    {
        if (SelectedOpacimetricHistoryYear is not int year) return;
        DeleteHistory(year, "Filtres F7 à H13", "Les filtres G4 plissés, G4 plan, G3 et Charbon ne sont pas concernés.",
            HistoryCleanupService.CountOpacimetric, HistoryCleanupService.DeleteOpacimetricYear);
    }

    private void DeleteHistory(int year, string scope, string notConcerned,
        Func<FiltresDbContext, int, int> count, Func<FiltresDbContext, int, int> delete)
    {
        if (!App.GuardWritable()) return;

        var n = count(App.Db, year);
        if (!App.Dialogs.ShowConfirm("Supprimer l'historique",
                $"Supprimer définitivement l'historique de l'année {year} ?\n\n{scope} : {n} remplacement(s) enregistré(s) seront supprimés. " +
                $"Les filtres eux-mêmes sont conservés. {notConcerned}\n\n" +
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
    }

    /// <summary>La case à cocher se sauvegarde immédiatement : contrairement aux autres champs, il n'y
    /// a pas de raison de faire attendre l'utilisateur jusqu'au bouton "Enregistrer les paramètres".</summary>
    partial void OnAutoUpdateEnabledChanged(bool value)
    {
        App.Settings.AutoUpdateEnabled = value;
        App.Settings.Save();
    }

    [RelayCommand]
    private async Task CheckForUpdates()
    {
        IsCheckingForUpdate = true;
        UpdateAvailable = false;
        _pendingUpdate = null;
        UpdateStatusMessage = "Recherche d'une mise à jour...";
        try
        {
            var info = await App.Updater.CheckForUpdateAsync(App.CurrentVersion);
            if (info is null)
            {
                UpdateStatusMessage = $"Vous utilisez la dernière version ({App.CurrentVersion}).";
            }
            else
            {
                _pendingUpdate = info;
                UpdateAvailable = true;
                UpdateStatusMessage = $"Nouvelle version disponible : {info.Version} (actuelle : {App.CurrentVersion}).";
            }
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"Erreur pendant la vérification : {ex.Message}";
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_pendingUpdate is null) return;

        if (!App.Dialogs.ShowConfirm("Installer la mise à jour",
                $"Télécharger et installer la version {_pendingUpdate.Version} ? L'application va se fermer puis redémarrer automatiquement."))
        {
            return;
        }

        IsInstallingUpdate = true;
        UpdateStatusMessage = "Téléchargement de la mise à jour...";
        try
        {
            await App.Updater.DownloadAndApplyAsync(_pendingUpdate);
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"Erreur pendant l'installation : {ex.Message}";
            IsInstallingUpdate = false;
        }
    }

    [RelayCommand]
    private void ExportExcelYear()
    {
        try
        {
            var path = App.ExcelExport.ExportYear(App.Db, App.Settings.ResolvedPdfExportPath, YearContext.Year);
            ExportStatusMessage = $"Export Excel de l'année {YearContext.Year} généré : {path}";
        }
        catch (Exception ex)
        {
            ExportStatusMessage = $"Erreur pendant l'export Excel : {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseDatabasePath()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Base de données SQLite (*.db)|*.db",
            FileName = "filtres.db",
            InitialDirectory = AppContext.BaseDirectory
        };
        if (dialog.ShowDialog() == true) DatabasePath = dialog.FileName;
    }

    [RelayCommand]
    private void BrowsePdfExportPath()
    {
        var dialog = new OpenFolderDialog
        {
            InitialDirectory = AppContext.BaseDirectory
        };
        if (dialog.ShowDialog() == true) PdfExportPath = dialog.FolderName;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        App.Settings.DatabasePath = DatabasePath;
        App.Settings.PdfExportPath = PdfExportPath;
        App.Settings.Save();

        try
        {
            App.ReloadDatabase(App.Settings.ResolvedDatabasePath);
        }
        catch (Exception ex)
        {
            // L'ancienne base est déjà fermée : on ne peut pas continuer sans base ouverte.
            System.Windows.MessageBox.Show($"Impossible d'ouvrir la base de données :\n{App.Settings.ResolvedDatabasePath}\n\n{ex.Message}\n\nL'application va se fermer.",
                "Base de données", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Stop);
            System.Windows.Application.Current.Shutdown();
            return;
        }
        OnPropertyChanged(nameof(DatabaseVersion));
        StatusMessage = "Paramètres enregistrés. La base de données a été rechargée.";
    }
}
