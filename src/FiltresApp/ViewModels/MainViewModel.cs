using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using FiltresApp.ViewModels.Filtres;
using Microsoft.EntityFrameworkCore;

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

    /// <summary>Appelé par <see cref="FilterVarietyListViewModel.ToggleEditMode"/> (bouton rouge/vert
    /// "Mode édition" de la page d'accueil "Filtres F7 à H14") après avoir basculé le réglage : si on
    /// vient de désactiver le mode alors qu'on est justement sur cette page, elle n'a plus rien à y
    /// montrer - redirige vers la première variété, comme le ferait une (re)sélection du menu (voir
    /// <see cref="OnSelectedItemChanged"/>).</summary>
    public void RefreshAfterEditModeChange()
    {
        if (!App.Settings.AllowFilterVarietyCreation && SelectedItem == _dynamicFiltersMenu && _dynamicFiltersMenu.Children.Count > 0)
        {
            _dynamicFiltersMenu.IsExpanded = true;
            SelectedItem = _dynamicFiltersMenu.Children[0];
        }
    }

    /// <summary>Appelé par <see cref="DynamicFilterListViewModel.ToggleVarietyManagement"/> (bouton "Gérer
    /// les variétés" de l'écran d'une variété) une fois le mode édition activé : ouvre la page d'accueil
    /// "Filtres F7 à H14", seul endroit où créer/modifier/supprimer des variétés - sinon inatteignable
    /// tant que le mode est désactivé (redirection automatique vers la première variété).</summary>
    public void NavigateToVarietyManagement()
    {
        // Sans cela, OnSelectedItemChanged redirigerait aussitôt vers la première variété (le réglage
        // AllowFilterVarietyCreation est indépendant du mode édition de la page d'une variété) et le
        // bouton n'aurait aucun effet.
        App.Settings.AllowFilterVarietyCreation = true;
        _dynamicFiltersMenu.IsExpanded = true;
        SelectedItem = _dynamicFiltersMenu;
    }

    private NavigationItem CreateVarietyItem(FilterVariety variety) =>
        new(variety.Nom, "▫", () => new DynamicFilterListViewModel(variety, this), variety.Id);

    /// <summary>Appelé par <see cref="FilterVarietyListViewModel.AddVariety"/> juste après la création en
    /// base : ajoute le sous-menu correspondant et le sélectionne.</summary>
    public void AddVarietyNavigationItem(FilterVariety variety)
    {
        var item = CreateVarietyItem(variety);
        _dynamicFiltersMenu.Children.Add(item);
        ResortVarietyNavigationItems();
        _dynamicFiltersMenu.IsExpanded = true;
        SelectedItem = item;
    }

    /// <summary>Appelé par <see cref="FilterVarietyListViewModel.EditVariety"/> : met à jour le libellé du
    /// sous-menu (sans le recréer, pour garder le même écran ouvert le cas échéant) et sa position, l'ordre
    /// d'affichage ayant pu changer.</summary>
    public void UpdateVarietyNavigationItem(int varietyId, string newName)
    {
        var item = _dynamicFiltersMenu.Children.FirstOrDefault(c => c.VarietyId == varietyId);
        if (item is not null) item.Title = newName;
        ResortVarietyNavigationItems();
    }

    /// <summary>Réordonne les sous-menus de variété selon leur <see cref="FilterVariety.Ordre"/> actuel en
    /// base (puis nom), en déplaçant les éléments existants (même identité d'objet, pas de recréation) pour
    /// ne pas perdre les écrans déjà ouverts.</summary>
    public void ResortVarietyNavigationItems()
    {
        var order = App.Db.FilterVarieties.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).Select(v => v.Id).ToList();
        for (var target = 0; target < order.Count; target++)
        {
            var item = _dynamicFiltersMenu.Children.FirstOrDefault(c => c.VarietyId == order[target]);
            if (item is null) continue;
            var current = _dynamicFiltersMenu.Children.IndexOf(item);
            if (current != target) _dynamicFiltersMenu.Children.Move(current, target);
        }
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

    /// <summary>Dernier écran effectivement affiché (hors redirections), pour distinguer une entrée
    /// authentique dans la section "Filtres F7 à H14" d'une resélection accidentelle du menu parent
    /// pendant qu'on y est déjà (voir <see cref="OnSelectedItemChanged"/>).</summary>
    private NavigationItem? _lastDisplayedItem;

    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        // Tant que la création de variétés est désactivée (Paramètres), le menu "Filtres F7 à H14"
        // lui-même n'a plus de page de gestion à afficher : ouvrir directement la première variété.
        if (value == _dynamicFiltersMenu && !App.Settings.AllowFilterVarietyCreation && _dynamicFiltersMenu.Children.Count > 0)
        {
            // Si on est déjà sur une variété (ou déjà "sur" le menu), on ignore : sans ce garde-fou, le
            // simple fait de replier le menu (clic sur le chevron) redéclencherait cette redirection, qui
            // forcerait IsExpanded à true et rouvrirait le menu immédiatement - le rendant impossible à
            // replier. Le menu reste repliable à tout moment, la redirection ne joue qu'à l'entrée dans la
            // section depuis un autre écran.
            var alreadyInSection = _lastDisplayedItem == _dynamicFiltersMenu || (_lastDisplayedItem is not null && _dynamicFiltersMenu.Children.Contains(_lastDisplayedItem));
            if (alreadyInSection)
            {
                SelectedItem = _lastDisplayedItem;
                return;
            }

            _dynamicFiltersMenu.IsExpanded = true;
            SelectedItem = _dynamicFiltersMenu.Children[0];
            return;
        }

        // Sélectionner le menu lui-même (création de variétés autorisée) le déplie aussi : sans ce
        // garde-fou, il ne se déplierait qu'au clic sur le chevron.
        if (value == _dynamicFiltersMenu) _dynamicFiltersMenu.IsExpanded = true;

        _lastDisplayedItem = value;
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
