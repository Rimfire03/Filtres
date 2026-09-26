using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FiltresApp.Core.Models;
using FiltresApp.Services;

namespace FiltresApp.ViewModels;

public partial class MainViewModel : ObservableObject
{
    public ObservableCollection<NavigationItem> NavigationItems { get; }

    /// <summary>Menu dépliant "Filtres F7 à H14" : ses enfants sont les variétés créées librement par
    /// l'utilisateur (voir <see cref="FilterVariety"/>). Sélectionner le menu lui-même affiche la page
    /// d'accueil de gestion des variétés (<see cref="FilterVarietyListViewModel"/>) ; sélectionner un
    /// enfant affiche l'écran de cette variété (<see cref="DynamicFilterListViewModel"/>), avec la même
    /// disposition que l'ancien écran unique "Filtres F7 à H13".</summary>
    private readonly NavigationItem _dynamicFiltersMenu;

    /// <summary>Année consultée, partagée par tous les écrans de suivi de filtres (remplace l'ancienne
    /// remise à zéro annuelle : changer l'année ne supprime rien, l'historique reste consultable).</summary>
    public YearContext YearContext => App.YearContext;

    [ObservableProperty] private NavigationItem? _selectedItem;
    [ObservableProperty] private object? _currentViewModel;

    /// <summary>Logo de l'entreprise en haut de la barre latérale (null si aucun).</summary>
    public System.Windows.Media.ImageSource? LogoImage => App.CompanyLogoImage;

    public MainViewModel()
    {
        _dynamicFiltersMenu = new NavigationItem("Filtres F7 à H14", "🟪", () => new FilterVarietyListViewModel(this));
        foreach (var variety in App.Db.FilterVarieties.OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList())
            _dynamicFiltersMenu.Children.Add(CreateVarietyItem(variety));

        NavigationItems = new ObservableCollection<NavigationItem>
        {
            new("Filtres G4 plissés", "🟦", () => new PeriodicFilterListViewModel(FilterCategory.G4Plisse, "Filtres G4 plissés", "Nom de la centrale d'air")),
            new("Filtres G4 plan", "🟦", () => new PeriodicFilterListViewModel(FilterCategory.G4Plan, "Filtres G4 plan", "Emplacement de l'appareil")),
            new("Filtres G3", "🟩", () => new PeriodicFilterListViewModel(FilterCategory.G3, "Filtres G3", "Emplacement de l'appareil")),
            new("Charbon", "⬛", () => new PeriodicFilterListViewModel(FilterCategory.Charbon, "Charbon", "Emplacement de l'appareil")),
            _dynamicFiltersMenu,
            new("Liste K7", "🗂", () => new K7ListViewModel()),
            new("Inventaire", "📦", () => new InventoryListViewModel()),
            new("Commande", "🧾", () => new OrderListViewModel(OrderDocumentType.CommandeChmy, "Commande")),
            new("Paramètres", "⚙", () => new SettingsViewModel())
            // "Pour devis" et "Filtres à refacturer" ont été supprimés définitivement le 25/09/2026,
            // données et code compris (voir README, section "Suppression définitive de « pour devis » et
            // « filtres à refacturer »") : il ne s'agit plus seulement d'un retrait de la navigation.
        };

        App.CompanyLogoChanged += () => OnPropertyChanged(nameof(LogoImage));
        SelectedItem = NavigationItems[0];
    }

    private static NavigationItem CreateVarietyItem(FilterVariety variety) =>
        new(variety.Nom, "▫", () => new DynamicFilterListViewModel(variety), variety.Id);

    /// <summary>Appelé par <see cref="FilterVarietyListViewModel.AddVariety"/> juste après la création en
    /// base : ajoute le sous-menu correspondant et le sélectionne.</summary>
    public void AddVarietyNavigationItem(FilterVariety variety)
    {
        var item = CreateVarietyItem(variety);
        _dynamicFiltersMenu.Children.Add(item);
        _dynamicFiltersMenu.IsExpanded = true;
        SelectedItem = item;
    }

    /// <summary>Appelé par <see cref="FilterVarietyListViewModel.RenameVariety"/> : met à jour le libellé
    /// du sous-menu sans le recréer (garde le même écran ouvert le cas échéant).</summary>
    public void RenameVarietyNavigationItem(int varietyId, string newName)
    {
        var item = _dynamicFiltersMenu.Children.FirstOrDefault(c => c.VarietyId == varietyId);
        if (item is not null) item.Title = newName;
    }

    /// <summary>Appelé par <see cref="FilterVarietyListViewModel.DeleteVariety"/> : retire le sous-menu. Si
    /// c'était l'écran affiché, retombe sur la page d'accueil "Filtres F7 à H14".</summary>
    public void RemoveVarietyNavigationItem(int varietyId)
    {
        var item = _dynamicFiltersMenu.Children.FirstOrDefault(c => c.VarietyId == varietyId);
        if (item is null) return;
        _dynamicFiltersMenu.Children.Remove(item);
        if (SelectedItem == item) SelectedItem = _dynamicFiltersMenu;
    }

    /// <summary>Oublie les écrans déjà ouverts (sauf l'écran courant) pour qu'ils relisent la base à la
    /// prochaine ouverture, après une modification faite depuis un autre écran.</summary>
    public void ResetOtherScreens()
    {
        foreach (var item in NavigationItems)
        {
            if (item != SelectedItem) item.Reset();
            foreach (var child in item.Children)
                if (child != SelectedItem) child.Reset();
        }
    }

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        foreach (var item in NavigationItems) item.IsSelected = item == value;
        foreach (var child in _dynamicFiltersMenu.Children) child.IsSelected = child == value;

        var vm = value?.GetOrCreateViewModel();
        // Recharge les données à chaque fois qu'on (re)sélectionne l'écran : nécessaire notamment pour
        // que "Inventaire" et "Commande" (qui partagent désormais les mêmes lignes en base, voir
        // README) reflètent immédiatement les ajouts/modifications faits depuis l'autre écran.
        if (vm is IReloadable reloadable) reloadable.Reload();
        CurrentViewModel = vm;
    }
}
