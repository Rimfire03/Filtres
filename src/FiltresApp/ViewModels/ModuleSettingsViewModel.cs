using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FiltresApp.ViewModels;

public enum ModuleSettingsScope { Filtre, Belts, Bearings }

/// <summary>Écran "Paramètres du module..." atteint via la roue dentée à côté de chaque case "Activer le
/// module..." de l'écran Paramètres (voir SettingsView.xaml, carte "Modules") : regroupe les réglages
/// propres à UN module, pour ne pas surcharger l'écran Paramètres général. Réutilise le même
/// <see cref="ViewModels.SettingsViewModel"/> partagé (passé en paramètre) que l'écran Paramètres lui-même
/// - pas une copie séparée, donc aucune désynchronisation possible entre les deux écrans.</summary>
public partial class ModuleSettingsViewModel : ObservableObject
{
    public SettingsViewModel Settings { get; }
    public ModuleSettingsScope Scope { get; }
    private readonly MainViewModel _main;

    public ModuleSettingsViewModel(SettingsViewModel settings, MainViewModel main, ModuleSettingsScope scope)
    {
        Settings = settings;
        _main = main;
        Scope = scope;
    }

    public string Title => Scope switch
    {
        ModuleSettingsScope.Filtre => "Paramètres du module « Filtre »",
        ModuleSettingsScope.Belts => "Paramètres du module « Courroies »",
        ModuleSettingsScope.Bearings => "Paramètres du module « Roulements »",
        _ => "Paramètres du module"
    };

    /// <summary>Seul le module "Filtre" a des réglages propres pour l'instant (Courroies / Roulements
    /// affichent un message d'attente) - voir ModuleSettingsView.xaml.</summary>
    public bool IsFiltre => Scope == ModuleSettingsScope.Filtre;
    public bool IsOther => !IsFiltre;

    /// <summary>"← Retour aux Paramètres" (en haut de l'écran).</summary>
    [RelayCommand]
    private void Back() => _main.SelectedItem = _main.SettingsItem;
}
