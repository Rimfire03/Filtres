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
