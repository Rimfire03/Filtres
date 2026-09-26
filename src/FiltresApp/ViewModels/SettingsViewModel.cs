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
    [ObservableProperty] private bool _linkDimensionFilterEnabled;
    [ObservableProperty] private bool _allowFilterVarietyCreation;
    [ObservableProperty] private string _updateStatusMessage = string.Empty;
    [ObservableProperty] private bool _isCheckingForUpdate;
    [ObservableProperty] private bool _isInstallingUpdate;
    [ObservableProperty] private bool _updateAvailable;

    private UpdateInfo? _pendingUpdate;

    public string CurrentVersion => App.CurrentVersion;
    public int DatabaseVersion => App.DatabaseVersion;

    /// <summary>Taille du fichier de base de données, affichée en Ko ou Mo. Recalculée après tout ce qui
    /// peut la faire varier sensiblement (rechargement, suppression d'historique).</summary>
    public string DatabaseSizeDisplay => FormatFileSize(App.Settings.ResolvedDatabasePath);

    private static string FormatFileSize(string path)
    {
        long bytes;
        try { bytes = new System.IO.FileInfo(path).Length; }
        catch { return "inconnue"; }
        return bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.0} Ko" : $"{bytes / 1024.0 / 1024.0:0.0} Mo";
    }

    /// <summary>Liste des années proposées (barre latérale et export Excel).</summary>
    public YearContext YearContext => App.YearContext;

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

    // ---- Logo de l'entreprise (stocké dans la base, commun à tous les postes) ----
    public System.Windows.Media.ImageSource? LogoImage => App.CompanyLogoImage;
    public bool HasLogo => App.CompanyLogo is not null;
    [ObservableProperty] private string _logoStatusMessage = string.Empty;

    [RelayCommand]
    private void ChooseLogo()
    {
        if (!App.GuardWritable()) return;
        var dialog = new OpenFileDialog
        {
            Title = "Choisir le logo de l'entreprise",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };
        if (dialog.ShowDialog() != true) return;

        var info = new System.IO.FileInfo(dialog.FileName);
        if (info.Length > CompanyLogoService.MaxBytes)
        {
            LogoStatusMessage = $"Image trop volumineuse ({info.Length / 1024} Ko) : 2 Mo au maximum.";
            return;
        }

        var data = System.IO.File.ReadAllBytes(dialog.FileName);
        if (App.TryCreateImage(data) is null)
        {
            LogoStatusMessage = "Ce fichier n'est pas une image lisible.";
            return;
        }

        App.SetCompanyLogo(data);
        RefreshLogo();
        LogoStatusMessage = "Logo enregistré dans la base : il est utilisé par tous les postes.";
    }

    [RelayCommand]
    private void RemoveLogo()
    {
        if (!App.GuardWritable() || App.CompanyLogo is null) return;
        if (!App.Dialogs.ShowConfirm("Retirer le logo", "Retirer le logo de l'entreprise pour tous les postes ?")) return;
        App.SetCompanyLogo(null);
        RefreshLogo();
        LogoStatusMessage = "Logo retiré.";
    }

    private void RefreshLogo()
    {
        OnPropertyChanged(nameof(LogoImage));
        OnPropertyChanged(nameof(HasLogo));
    }

    public SettingsViewModel()
    {
        _databasePath = App.Settings.DatabasePath;
        _pdfExportPath = App.Settings.PdfExportPath;
        _autoUpdateEnabled = App.Settings.AutoUpdateEnabled;
        _linkDimensionFilterEnabled = App.Settings.LinkDimensionFilterEnabled;
        _allowFilterVarietyCreation = App.Settings.AllowFilterVarietyCreation;
        LoadHistoryYears();
    }

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

    /// <summary>La case à cocher se sauvegarde immédiatement : contrairement aux autres champs, il n'y
    /// a pas de raison de faire attendre l'utilisateur jusqu'au bouton "Enregistrer les paramètres".</summary>
    partial void OnAutoUpdateEnabledChanged(bool value)
    {
        App.Settings.AutoUpdateEnabled = value;
        App.Settings.Save();
    }

    partial void OnLinkDimensionFilterEnabledChanged(bool value)
    {
        App.Settings.LinkDimensionFilterEnabled = value;
        App.Settings.Save();
    }

    partial void OnAllowFilterVarietyCreationChanged(bool value)
    {
        App.Settings.AllowFilterVarietyCreation = value;
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

    /// <summary>Année à exporter en Excel : sélecteur propre à cette carte (indépendant de l'année consultée
    /// dans la barre latérale), placé par défaut sur l'année en cours.</summary>
    [ObservableProperty] private int _selectedExportYear = DateTime.Today.Year;

    [RelayCommand]
    private void ExportExcelYear()
    {
        try
        {
            var year = SelectedExportYear;
            var path = App.ExcelExport.ExportYear(App.Db, App.Settings.ResolvedPdfExportPath, year);
            ExportStatusMessage = $"Export Excel de l'année {year} généré : {path}";
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
        OnPropertyChanged(nameof(DatabaseSizeDisplay));
        StatusMessage = "Paramètres enregistrés. La base de données a été rechargée.";
    }
}
