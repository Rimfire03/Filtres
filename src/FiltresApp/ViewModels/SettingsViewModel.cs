using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Services;
using FiltresApp.Services;
using Microsoft.Win32;

namespace FiltresApp.ViewModels;

/// <summary>Écran "Paramètres" : chemins, options, export Excel. Autres sections : SettingsViewModel.Logo.cs,
/// SettingsViewModel.History.cs, SettingsViewModel.Updates.cs.</summary>
public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _databasePath;
    [ObservableProperty] private string _pdfExportPath;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _exportStatusMessage = string.Empty;

    [ObservableProperty] private string _backupSourcePath = string.Empty;
    [ObservableProperty] private string _backupStatusMessage = string.Empty;

    [ObservableProperty] private bool _autoUpdateEnabled;
    [ObservableProperty] private bool _linkDimensionFilterEnabled;
    [ObservableProperty] private bool _allowFilterVarietyCreation;

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

    public SettingsViewModel()
    {
        _databasePath = App.Settings.DatabasePath;
        _pdfExportPath = App.Settings.PdfExportPath;
        _autoUpdateEnabled = App.Settings.AutoUpdateEnabled;
        _linkDimensionFilterEnabled = App.Settings.LinkDimensionFilterEnabled;
        _allowFilterVarietyCreation = App.Settings.AllowFilterVarietyCreation;
        LoadHistoryYears();
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

    /// <summary>Un changement de chemin de base de données exige un redémarrage complet (voir
    /// <see cref="App.Restart"/>) : un simple rechargement laisserait les écrans déjà ouverts (barre
    /// latérale, année consultée...) avec des données de l'ancienne base en mémoire.</summary>
    [RelayCommand]
    private void SaveSettings()
    {
        var databasePathChanged = !string.Equals(DatabasePath, App.Settings.DatabasePath, StringComparison.Ordinal);

        if (databasePathChanged && !App.Dialogs.ShowConfirm("Changement de base de données",
                "Le chemin de la base de données a changé. L'application va se fermer puis redémarrer automatiquement pour l'ouvrir.\n\n" +
                "Continuer ?"))
        {
            DatabasePath = App.Settings.DatabasePath; // annulé : revient au chemin actuellement ouvert
            return;
        }

        App.Settings.DatabasePath = DatabasePath;
        App.Settings.PdfExportPath = PdfExportPath;
        App.Settings.Save();

        if (databasePathChanged)
        {
            App.Restart();
            return;
        }

        StatusMessage = "Paramètres enregistrés.";
    }

    [RelayCommand]
    private void ExportDatabase()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Base de données SQLite (*.db)|*.db",
            FileName = $"save_db_filtre_{DateTime.Now:yyyyMMdd_HHmmss}_{App.CurrentVersion}.db",
            InitialDirectory = System.IO.Path.GetDirectoryName(App.Settings.ResolvedDatabasePath)
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            App.DbFactory.ExportTo(dialog.FileName);
            BackupStatusMessage = $"Sauvegarde exportée : {dialog.FileName}";
        }
        catch (Exception ex)
        {
            BackupStatusMessage = $"Erreur pendant l'export de la base : {ex.Message}";
        }
    }

    [RelayCommand]
    private void BrowseBackupSource()
    {
        var dialog = new OpenFileDialog { Filter = "Sauvegarde de base de données (*.db)|*.db" };
        if (dialog.ShowDialog() == true) BackupSourcePath = dialog.FileName;
    }

    /// <summary>Remplace le fichier de base courant par la sauvegarde choisie, puis redémarre : voir
    /// <see cref="App.ImportDatabaseBackup"/>, qui déclenche au redémarrage la vérification/mise à jour
    /// automatique de la version de schéma de la sauvegarde importée.</summary>
    [RelayCommand]
    private void ImportDatabase()
    {
        if (!App.GuardWritable()) return;

        if (string.IsNullOrWhiteSpace(BackupSourcePath) || !System.IO.File.Exists(BackupSourcePath))
        {
            BackupStatusMessage = "Merci de choisir un fichier de sauvegarde .db valide.";
            return;
        }

        if (!App.Dialogs.ShowConfirm("Importer une sauvegarde",
                "Cette opération va REMPLACER toutes les données actuelles de l'application par celles du fichier de sauvegarde sélectionné, " +
                "puis redémarrer l'application (la version de la sauvegarde sera vérifiée et mise à jour si besoin, comme pour toute base plus ancienne). Continuer ?"))
        {
            return;
        }

        try
        {
            App.ImportDatabaseBackup(BackupSourcePath);
        }
        catch (Exception ex)
        {
            BackupStatusMessage = $"Erreur pendant l'import de la sauvegarde : {ex.Message}";
        }
    }
}
