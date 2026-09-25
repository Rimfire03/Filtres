using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

    /// <summary>Année à exporter en Excel (voir bouton "Exporter l'année en Excel" ci-dessous),
    /// partagée avec le sélecteur d'année global de la barre latérale.</summary>
    public YearContext YearContext => App.YearContext;

    public SettingsViewModel()
    {
        _databasePath = App.Settings.DatabasePath;
        _pdfExportPath = App.Settings.PdfExportPath;
        _autoUpdateEnabled = App.Settings.AutoUpdateEnabled;
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

        App.ReloadDatabase(App.Settings.ResolvedDatabasePath);
        StatusMessage = "Paramètres enregistrés. La base de données a été rechargée.";
    }
}
