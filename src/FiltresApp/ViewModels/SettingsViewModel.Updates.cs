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

    /// <summary>Télécharge l'archive dans le dossier Téléchargements et l'y révèle dans l'explorateur :
    /// l'installation (fermer l'application, extraire l'archive, remplacer le contenu du dossier
    /// d'installation) reste manuelle, voir UpdateService.</summary>
    [RelayCommand]
    private async Task InstallUpdate()
    {
        if (_pendingUpdate is null) return;

        if (!App.Dialogs.ShowConfirm("Télécharger la mise à jour",
                $"Télécharger la version {_pendingUpdate.Version} ? Elle devra ensuite être installée manuellement : fermez l'application, extrayez l'archive téléchargée, puis remplacez le contenu du dossier d'installation."))
        {
            return;
        }

        IsInstallingUpdate = true;
        UpdateStatusMessage = "Téléchargement de la mise à jour...";
        try
        {
            var zipPath = await App.Updater.DownloadUpdateAsync(_pendingUpdate);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{zipPath}\"") { UseShellExecute = true });
            UpdateStatusMessage = $"Mise à jour téléchargée : {zipPath}. Fermez l'application puis installez-la manuellement.";
        }
        catch (Exception ex)
        {
            UpdateStatusMessage = $"Erreur pendant le téléchargement : {ex.Message}";
        }
        finally
        {
            IsInstallingUpdate = false;
        }
    }
}
