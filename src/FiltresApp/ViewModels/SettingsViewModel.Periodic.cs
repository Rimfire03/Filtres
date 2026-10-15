using CommunityToolkit.Mvvm.ComponentModel;

namespace FiltresApp.ViewModels;

/// <summary>Réglage du module « Filtre » (écran « Paramètres du module », roue dentée) : case « Tout réalisé » dans le
/// titre de la colonne « Réalisé » des vues de changement de filtre périodique.</summary>
public partial class SettingsViewModel
{
    [ObservableProperty] private bool _allDoneCheckboxEnabled = App.Settings.AllDoneCheckboxEnabled;

    /// <summary>Sauvegardé immédiatement ; les vues relisent le réglage à leur ouverture.</summary>
    partial void OnAllDoneCheckboxEnabledChanged(bool value)
    {
        App.Settings.AllDoneCheckboxEnabled = value;
        App.Settings.Save();
    }
}
