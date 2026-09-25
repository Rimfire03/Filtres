using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FiltresApp.Core.Models;
using FiltresApp.Services;

namespace FiltresApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<NavigationItem> NavigationItems { get; }

    /// <summary>Année consultée, partagée par tous les écrans de suivi de filtres (remplace l'ancienne
    /// remise à zéro annuelle : changer l'année ne supprime rien, l'historique reste consultable).</summary>
    public YearContext YearContext => App.YearContext;

    [ObservableProperty] private NavigationItem? _selectedItem;
    [ObservableProperty] private object? _currentViewModel;

    public MainViewModel()
    {
        NavigationItems = new ObservableCollection<NavigationItem>
        {
            new("Filtres G4 plissés", "🟦", () => new PeriodicFilterListViewModel(FilterCategory.G4Plisse, "Filtres G4 plissés", "Nom de la centrale d'air")),
            new("Filtres G4 plan", "🟦", () => new PeriodicFilterListViewModel(FilterCategory.G4Plan, "Filtres G4 plan", "Emplacement de l'appareil")),
            new("Filtres G3", "🟩", () => new PeriodicFilterListViewModel(FilterCategory.G3, "Filtres G3", "Emplacement de l'appareil")),
            new("Filtres F7 à H13", "🟨", () => new OpacimetricFilterListViewModel()),
            new("Charbon", "⬛", () => new PeriodicFilterListViewModel(FilterCategory.Charbon, "Charbon", "Emplacement de l'appareil")),
            new("Liste K7", "🗂", () => new K7ListViewModel()),
            new("Inventaire", "📦", () => new InventoryListViewModel()),
            new("Commande", "🧾", () => new OrderListViewModel(OrderDocumentType.CommandeChmy, "Commande")),
            new("Paramètres", "⚙", () => new SettingsViewModel())
            // "Pour devis" et "Filtres à refacturer" ont été supprimés définitivement le 25/09/2026,
            // données et code compris (voir README, section "Suppression définitive de « pour devis » et
            // « filtres à refacturer »") : il ne s'agit plus seulement d'un retrait de la navigation.
        };

        SelectedItem = NavigationItems[0];
    }

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        var vm = value?.GetOrCreateViewModel();
        // Recharge les données à chaque fois qu'on (re)sélectionne l'écran : nécessaire notamment pour
        // que "Inventaire" et "Commande" (qui partagent désormais les mêmes lignes en base, voir
        // README) reflètent immédiatement les ajouts/modifications faits depuis l'autre écran.
        if (vm is IReloadable reloadable) reloadable.Reload();
        CurrentViewModel = vm;
    }
}
