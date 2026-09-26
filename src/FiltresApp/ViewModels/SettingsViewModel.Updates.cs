using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Services;

namespace FiltresApp.ViewModels;

/// <summary>Paramètres : vérification et installation des mises à jour.</summary>
public partial class SettingsViewModel
{
    [ObservableProperty] private string _updateStatusMessage = string.Empty;
    [ObservableProperty] private bool _isCheckingForUpdate;
    [ObservableProperty] private bool _isInstallingUpdate;
    [ObservableProperty] private bool _updateAvailable;

    private UpdateInfo? _pendingUpdate;

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
}
