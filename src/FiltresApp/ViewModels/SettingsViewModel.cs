using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Services;
using Microsoft.Win32;

namespace FiltresApp.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private string _databasePath;
    [ObservableProperty] private string _pdfExportPath;
    [ObservableProperty] private string _importSourcePath = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private string _exportStatusMessage = string.Empty;

    /// <summary>Année à exporter en Excel (voir bouton "Exporter l'année en Excel" ci-dessous),
    /// partagée avec le sélecteur d'année global de la barre latérale.</summary>
    public YearContext YearContext => App.YearContext;

    public SettingsViewModel()
    {
        _databasePath = App.Settings.DatabasePath;
        _pdfExportPath = App.Settings.PdfExportPath;
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
    private void BrowseImportSource()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Classeur Excel (*.xlsm;*.xlsx)|*.xlsm;*.xlsx"
        };
        if (dialog.ShowDialog() == true) ImportSourcePath = dialog.FileName;
    }

    [RelayCommand]
    private void SaveSettings()
    {
        App.Settings.DatabasePath = DatabasePath;
        App.Settings.PdfExportPath = PdfExportPath;
        App.Settings.Save();

        App.ReloadDatabase(App.Settings.ResolvedDatabasePath);
        StatusMessage = "Paramètres enregistrés. La base de données a été rechargée.";
    }

    [RelayCommand]
    private void ImportFromExcel()
    {
        if (string.IsNullOrWhiteSpace(ImportSourcePath) || !File.Exists(ImportSourcePath))
        {
            StatusMessage = "Merci de choisir un fichier Excel (.xlsm/.xlsx) valide.";
            return;
        }

        if (!App.Dialogs.ShowConfirm("Importer depuis Excel",
                "Cette opération va REMPLACER toutes les données actuelles de l'application par celles du fichier Excel sélectionné. Continuer ?"))
        {
            return;
        }

        try
        {
            var result = App.Importer.Import(ImportSourcePath, App.Settings.ResolvedDatabasePath);
            App.ReloadDatabase(App.Settings.ResolvedDatabasePath);
            StatusMessage = $"Import terminé : {result.Total} lignes importées.";
            if (result.Warnings.Count > 0)
                StatusMessage += $" Avertissements : {string.Join("; ", result.Warnings)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Erreur pendant l'import : {ex.Message}";
        }
    }
}
