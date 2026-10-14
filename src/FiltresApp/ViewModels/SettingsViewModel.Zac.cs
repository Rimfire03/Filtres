using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FiltresApp.ViewModels;

/// <summary>Réglages du module « Suivi ZAC » (écran « Paramètres du module », roue dentée de la carte Modules) :
/// alerte affichée à l'ouverture de l'application et délai de l'alerte orange.</summary>
public partial class SettingsViewModel
{
    [ObservableProperty] private bool _zacAlertEnabled = App.Settings.ZacAlertEnabled;
    [ObservableProperty] private int _zacAlertDays = Math.Max(0, App.Settings.ZacAlertDays);

    partial void OnZacAlertEnabledChanged(bool value)
    {
        App.Settings.ZacAlertEnabled = value;
        App.Settings.Save();
    }

    partial void OnZacAlertDaysChanged(int value)
    {
        if (value < 0)
        {
            ZacAlertDays = 0;
            return;
        }
        App.Settings.ZacAlertDays = value;
        App.Settings.Save();
    }

    /// <summary>Roue dentée à côté de « Activer le module Suivi ZAC ».</summary>
    [RelayCommand]
    private void OpenZacModuleSettings() => _main.SelectedItem = _main.ZacSettingsItem;
}
