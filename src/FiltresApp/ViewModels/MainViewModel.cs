using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services.Licensing;
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

    /// <summary>Menu dépliant « Changement filtre périodique » : ses enfants sont les vues (G4 plissés, G4 plan,
    /// G3, Charbon et celles créées par l'utilisateur, voir <see cref="PeriodicView"/>). Même fonctionnement que
    /// <see cref="_dynamicFiltersMenu"/> : le menu lui-même affiche la page de gestion des vues
    /// (<see cref="PeriodicViewListViewModel"/>) quand le mode édition est activé, sinon ouvre la première vue.</summary>
    private readonly NavigationItem _periodicMenu;

    /// <summary>Menu dépliant géré par une page d'accueil de gestion (voir <see cref="_expandableMenus"/>).</summary>
    private sealed record ExpandableMenu(NavigationItem Item, Func<bool> AllowManagement);

    private readonly List<ExpandableMenu> _expandableMenus = new();

    /// <summary>Titres des menus dépliants (modifiables dans « Paramètres du module Filtre »).</summary>
    public string PeriodicMenuTitle => _periodicMenu.Title;
    public string FoulingMenuTitle => _dynamicFiltersMenu.Title;

    /// <summary>Menu "Aide" : à part de <see cref="NavigationItems"/> (pas dans l'arbre défilant), affiché
    /// séparément tout en bas de la barre latérale (voir MainWindow.xaml) - toujours accessible sans avoir
    /// à faire défiler le reste du menu.</summary>
    public NavigationItem HelpItem { get; } = new("Aide", "❓", () => new HelpViewModel());

    /// <summary>Menu "Paramètres" : à part de <see cref="NavigationItems"/> (pas dans l'arbre défilant),
    /// épinglé juste au-dessus de "Aide" tout en bas de la barre latérale (voir MainWindow.xaml), séparé du
    /// reste du menu par un séparateur.</summary>
    public NavigationItem SettingsItem { get; private set; } = null!;

    /// <summary>Paramètres spécifiques à chaque module (roue dentée à côté de chaque case "Activer le
    /// module..." dans Paramètres, voir SettingsViewModel.OpenFiltreModuleSettingsCommand et consorts) : ni
    /// dans <see cref="NavigationItems"/> ni épinglés, atteints uniquement depuis ce bouton - réutilisent le
    /// même <see cref="SettingsViewModel"/> partagé que "Paramètres" (pas une copie séparée), voir
    /// <see cref="ModuleSettingsViewModel"/>.</summary>
    public NavigationItem FiltreSettingsItem { get; private set; } = null!;
    public NavigationItem BeltsSettingsItem { get; private set; } = null!;
    public NavigationItem BearingsSettingsItem { get; private set; } = null!;
    public NavigationItem MultiSiteSettingsItem { get; private set; } = null!;

    /// <summary>Année consultée, partagée par tous les écrans de suivi de filtres (remplace l'ancienne
    /// remise à zéro annuelle : changer l'année ne supprime rien, l'historique reste consultable).</summary>
    public YearContext YearContext => App.YearContext;

    /// <summary>Sélecteur de site (module MultiSite), au-dessus du sélecteur d'année : visible seulement si le module est utilisable (activé et couvert par la licence) et que plusieurs sites existent.</summary>
    public IReadOnlyList<Site> Sites => App.Sites;
    public bool ShowSiteSelector => App.MultiSiteAvailable;

    public Site? SelectedSite
    {
        get => App.Sites.FirstOrDefault(s => s.Id == App.CurrentSite.Id);
        set
        {
            if (value is null || value.Id == App.CurrentSite.Id) return;
            if (App.Dialogs.ShowConfirm("Changer de site", $"Ouvrir le site « {value.Nom} » ? Le logiciel va redémarrer."))
                App.SwitchSite(value.Id);
            else
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(SelectedSite))); // remet l'ancien choix
        }
    }

    [ObservableProperty] private NavigationItem? _selectedItem;
    [ObservableProperty] private object? _currentViewModel;

    /// <summary>Logo de l'entreprise en haut de la barre latérale (null si aucun).</summary>
    public System.Windows.Media.ImageSource? LogoImage => App.CompanyLogoImage;

    /// <summary>Modules activables depuis Paramètres (voir AppSettings.ShowBeltsModule / ShowBearingsModule) :
    /// ajoutés/retirés de <see cref="NavigationItems"/> sans redémarrer l'application, voir
    /// <see cref="RefreshModuleVisibility"/>.</summary>
    private readonly NavigationItem _beltsItem = new("Courroies", "➰", () => new BeltListViewModel());
    private readonly NavigationItem _bearingsItem = new("Roulements", "⚙", () => new BearingListViewModel());
    private readonly NavigationItem _modulesSeparatorBefore = NavigationItem.Separator();
    private readonly NavigationItem _modulesSeparatorBetween = NavigationItem.Separator();
    private readonly NavigationItem _modulesSeparatorAfter = NavigationItem.Separator();

    /// <summary>Module "Filtre" (activable comme Courroies / Roulements, voir AppSettings.ShowFiltresModule) :
    /// G4 plissés, G4 plan, G3, Charbon, F7 à H14, Liste K7, Inventaire, Commande forment un seul et même
    /// module (pas de séparateur entre eux), toujours inséré en tête de liste.</summary>
    private readonly List<NavigationItem> _filtreItems;

    public MainViewModel()
    {
        var periodicEntry = LoadMenuEntry(MenuEntry.PeriodicMenuKey);
        var foulingEntry = LoadMenuEntry(MenuEntry.FoulingMenuKey);

        _periodicMenu = new NavigationItem(periodicEntry.Title, string.Empty, () => new PeriodicViewListViewModel(this)) { IsExpanded = periodicEntry.DefaultExpanded };
        foreach (var view in App.Db.PeriodicViews.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList())
            _periodicMenu.Children.Add(CreatePeriodicViewItem(view));

        _dynamicFiltersMenu = new NavigationItem(foulingEntry.Title, string.Empty, () => new FilterVarietyListViewModel(this)) { IsExpanded = foulingEntry.DefaultExpanded };
        foreach (var variety in App.Db.FilterVarieties.OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList())
            _dynamicFiltersMenu.Children.Add(CreateVarietyItem(variety));

        _expandableMenus.Add(new ExpandableMenu(_periodicMenu, () => App.Settings.AllowPeriodicViewCreation));
        _expandableMenus.Add(new ExpandableMenu(_dynamicFiltersMenu, () => App.Settings.AllowFilterVarietyCreation));

        _filtreItems = new List<NavigationItem>
        {
            _periodicMenu,
            _dynamicFiltersMenu,
            new("Liste K7", "🗂", () => new K7ListViewModel()),
            new("Inventaire", "📦", () => new InventoryListViewModel()),
            new("Commande", "🧾", () => new OrderListViewModel(OrderDocumentType.CommandeChmy, "Commande"))
            // "Pour devis" et "Filtres à refacturer" ont été supprimés définitivement le 25/09/2026,
            // données et code compris (voir README, section "Suppression définitive de « pour devis » et
            // « filtres à refacturer »") : il ne s'agit plus seulement d'un retrait de la navigation.
            // "Paramètres" n'est plus ici : épinglé à part, juste au-dessus de "Aide" (voir SettingsItem).
        };

        SettingsItem = new NavigationItem("Paramètres", "⚙", () => new SettingsViewModel(this));
        FiltreSettingsItem = new NavigationItem("Paramètres du module Filtre", "⚙",
            () => new ModuleSettingsViewModel((SettingsViewModel)SettingsItem.GetOrCreateViewModel()!, this, ModuleSettingsScope.Filtre));
        BeltsSettingsItem = new NavigationItem("Paramètres du module Courroies", "⚙",
            () => new ModuleSettingsViewModel((SettingsViewModel)SettingsItem.GetOrCreateViewModel()!, this, ModuleSettingsScope.Belts));
        BearingsSettingsItem = new NavigationItem("Paramètres du module Roulements", "⚙",
            () => new ModuleSettingsViewModel((SettingsViewModel)SettingsItem.GetOrCreateViewModel()!, this, ModuleSettingsScope.Bearings));
        MultiSiteSettingsItem = new NavigationItem("Paramètres du module MultiSite", "⚙",
            () => new ModuleSettingsViewModel((SettingsViewModel)SettingsItem.GetOrCreateViewModel()!, this, ModuleSettingsScope.MultiSite));

        NavigationItems = new ObservableCollection<NavigationItem>();
        RefreshModuleVisibility();
        App.ModuleVisibilityChanged += RefreshModuleVisibility;
        App.CompanyLogoChanged += () => OnPropertyChanged(nameof(LogoImage));
        App.SitesChanged += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            OnPropertyChanged(nameof(Sites));
            OnPropertyChanged(nameof(SelectedSite));
            OnPropertyChanged(nameof(ShowSiteSelector));
        });
        App.ModuleVisibilityChanged += () => OnPropertyChanged(nameof(ShowSiteSelector));
        LicenseManager.Changed += () => System.Windows.Application.Current?.Dispatcher.BeginInvoke(OnLicenseChanged);
    }

    /// <summary>Pied de page (à gauche) : statut de la licence, en couleur d'alerte pour une licence expirée
    /// ou une démo à 2 jours de la fin ou moins. Mis à jour à chaque changement d'état de la licence.</summary>
    public string LicenseFooter => LicenseManager.FooterText;

    public System.Windows.Media.Brush LicenseFooterBrush => LicenseManager.FooterIsAlert
        ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71))
        : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x64, 0x74, 0x8B));

    /// <summary>Licence mise à jour (validation, prolongation, expiration, démo...) : recalcule les modules
    /// autorisés dans le menu sans redémarrer l'application.</summary>
    private void OnLicenseChanged()
    {
        OnPropertyChanged(nameof(LicenseFooter));
        OnPropertyChanged(nameof(LicenseFooterBrush));
        OnPropertyChanged(nameof(ShowSiteSelector));
        RefreshModuleVisibility();
    }

    /// <summary>Premier élément sélectionnable du menu défilant (ignore les séparateurs) - sert de repli
    /// quand l'écran actuellement affiché est masqué (module désactivé), ou null si le menu est vide (tous
    /// les modules désactivés : Paramètres/Aide restent accessibles via la barre latérale).</summary>
    private NavigationItem? FirstSelectableItem() => NavigationItems.FirstOrDefault(i => !i.IsSeparator);

    /// <summary>Insère ou retire "Courroies" / "Roulements" en fin de menu défilant, entourés d'un seul
    /// séparateur avant et après le groupe (pas un par module : voir le commentaire du constructeur) -
    /// appelé à la construction puis à chaque bascule dans Paramètres.</summary>
    private void RefreshModuleVisibility()
    {
        void Sync(NavigationItem item, bool shouldShow)
        {
            var present = NavigationItems.Contains(item);
            if (shouldShow && !present)
            {
                // Si le séparateur de fin existe déjà (un autre module du groupe est déjà affiché),
                // insérer avant lui plutôt qu'en toute fin de liste (après le séparateur).
                if (NavigationItems.Contains(_modulesSeparatorAfter))
                    NavigationItems.Insert(NavigationItems.IndexOf(_modulesSeparatorAfter), item);
                else
                    NavigationItems.Add(item);
            }
            else if (!shouldShow && present)
            {
                if (SelectedItem == item) SelectedItem = FirstSelectableItem();
                NavigationItems.Remove(item);
            }
        }

        // Module "Filtre" : toujours en tête, inséré/retiré en bloc (pas de séparateur interne, voir
        // le commentaire sur _filtreItems).
        var filtreVisible = NavigationItems.Contains(_filtreItems[0]);
        if (App.Settings.ShowFiltresModule && !filtreVisible)
        {
            for (var i = 0; i < _filtreItems.Count; i++) NavigationItems.Insert(i, _filtreItems[i]);
        }
        else if (!App.Settings.ShowFiltresModule && filtreVisible)
        {
            foreach (var item in _filtreItems)
            {
                if (SelectedItem == item || (SelectedItem is not null && _expandableMenus.Any(m => m.Item.Children.Contains(SelectedItem)))) SelectedItem = null;
                NavigationItems.Remove(item);
            }
        }

        Sync(_beltsItem, App.Settings.ShowBeltsModule);
        Sync(_bearingsItem, App.Settings.ShowBearingsModule);

        // Repli final si le module Filtre vient d'être masqué pendant qu'un de ses écrans était affiché :
        // fait après Sync (Courroies / Roulements peuvent maintenant être la meilleure option disponible).
        if (SelectedItem is null) SelectedItem = FirstSelectableItem();

        var anyModuleVisible = NavigationItems.Contains(_beltsItem) || NavigationItems.Contains(_bearingsItem);
        var beforePresent = NavigationItems.Contains(_modulesSeparatorBefore);
        if (anyModuleVisible && !beforePresent)
        {
            var firstModuleIndex = NavigationItems.IndexOf(NavigationItems.First(i => i == _beltsItem || i == _bearingsItem));
            NavigationItems.Insert(firstModuleIndex, _modulesSeparatorBefore);
            NavigationItems.Add(_modulesSeparatorAfter);
        }
        else if (!anyModuleVisible && beforePresent)
        {
            NavigationItems.Remove(_modulesSeparatorBefore);
            NavigationItems.Remove(_modulesSeparatorAfter);
        }

        // Séparateur entre "Courroies" et "Roulements" eux-mêmes, uniquement quand les deux sont affichés
        // (quel que soit leur ordre, qui dépend de l'ordre d'activation).
        var beltsPresent = NavigationItems.Contains(_beltsItem);
        var bearingsPresent = NavigationItems.Contains(_bearingsItem);
        var betweenPresent = NavigationItems.Contains(_modulesSeparatorBetween);
        if (beltsPresent && bearingsPresent && !betweenPresent)
        {
            var firstIndex = Math.Min(NavigationItems.IndexOf(_beltsItem), NavigationItems.IndexOf(_bearingsItem));
            NavigationItems.Insert(firstIndex + 1, _modulesSeparatorBetween);
        }
        else if (!(beltsPresent && bearingsPresent) && betweenPresent)
        {
            NavigationItems.Remove(_modulesSeparatorBetween);
        }

        RefreshLicenseGating();
    }

    /// <summary>Un module activé dans Paramètres mais dont la fonctionnalité n'est pas couverte par la
    /// licence active reste visible (l'utilisateur comprend ainsi qu'il existe) mais grisé et non cliquable
    /// (voir NavigationItem.IsLicensed et MainWindow.xaml) - jamais juste masqué silencieusement.</summary>
    private void RefreshLicenseGating()
    {
        var filtresLicensed = LicenseManager.HasFeature("filtres");
        foreach (var item in _filtreItems)
        {
            item.IsLicensed = filtresLicensed;
            foreach (var child in item.Children) child.IsLicensed = filtresLicensed;
        }

        _beltsItem.IsLicensed = LicenseManager.HasFeature("courroies");
        _bearingsItem.IsLicensed = LicenseManager.HasFeature("roulements");

        // Écran d'un module qui vient de devenir non autorisé (expiration, modules retirés) : repli sur
        // Paramètres, où se trouve la carte Licence.
        if (SelectedItem is { IsLicensed: false }) SelectedItem = SettingsItem;
    }

    /// <summary>Appelé par <see cref="FilterVarietyListViewModel.ToggleEditMode"/> (bouton rouge/vert
    /// "Mode édition" de la page d'accueil "Filtres F7 à H14") après avoir basculé le réglage : si on
    /// vient de désactiver le mode alors qu'on est justement sur cette page, elle n'a plus rien à y
    /// montrer - redirige vers la première variété, comme le ferait une (re)sélection du menu (voir
    /// <see cref="OnSelectedItemChanged"/>).</summary>
    public void RefreshAfterEditModeChange()
    {
        foreach (var menu in _expandableMenus)
        {
            if (!menu.AllowManagement() && SelectedItem == menu.Item && menu.Item.Children.Count > 0)
            {
                menu.Item.IsExpanded = true;
                SelectedItem = menu.Item.Children[0];
            }
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

    /// <summary>Même chose pour le menu « Changement filtre périodique » (bouton « Gérer les vues » d'une vue).</summary>
    public void NavigateToPeriodicViewManagement()
    {
        App.Settings.AllowPeriodicViewCreation = true;
        _periodicMenu.IsExpanded = true;
        SelectedItem = _periodicMenu;
    }

    // ---- Titres, icônes et état par défaut des menus (voir MenuEntry, Paramètres du module Filtre) ----

    private static MenuEntry LoadMenuEntry(string key) =>
        App.Db.MenuEntries.AsNoTracking().FirstOrDefault(m => m.Key == key) ?? MenuEntry.DefaultFor(key);

    /// <summary>Relit titres et icônes des deux menus dépliants (après modification dans les paramètres du module)
    /// sans toucher à leur état plié / déplié courant, et oublie leur écran déjà ouvert pour que son titre soit
    /// relu.</summary>
    public void RefreshMenuEntries()
    {
        var periodic = LoadMenuEntry(MenuEntry.PeriodicMenuKey);
        var fouling = LoadMenuEntry(MenuEntry.FoulingMenuKey);
        _periodicMenu.Title = periodic.Title;

        _dynamicFiltersMenu.Title = fouling.Title;

        _periodicMenu.Reset();
        _dynamicFiltersMenu.Reset();
        OnPropertyChanged(nameof(PeriodicMenuTitle));
        OnPropertyChanged(nameof(FoulingMenuTitle));
    }

    // ---- Vues du menu « Changement filtre périodique » ----

    private static PeriodicView LoadPeriodicView(int id) =>
        App.Db.PeriodicViews.AsNoTracking().First(v => v.Id == id);

    private NavigationItem CreatePeriodicViewItem(PeriodicView view)
    {
        var id = view.Id;
        // Relit la vue à la création de l'écran : un titre ou un libellé modifié est ainsi toujours pris en compte.
        return new NavigationItem(view.Nom, view.Icon, () => new PeriodicFilterListViewModel(LoadPeriodicView(id), this), id);
    }

    /// <summary>Appelé par <see cref="PeriodicViewListViewModel"/> après la création d'une vue : ajoute le
    /// sous-menu et le sélectionne.</summary>
    public void AddPeriodicViewNavigationItem(PeriodicView view)
    {
        var item = CreatePeriodicViewItem(view);
        _periodicMenu.Children.Add(item);
        ResortPeriodicViewNavigationItems();
        _periodicMenu.IsExpanded = true;
        SelectedItem = item;
    }

    /// <summary>Met à jour titre et icône du sous-menu d'une vue et oublie son écran (relu à la prochaine
    /// ouverture) ; l'ordre d'affichage ayant pu changer, les sous-menus sont reclassés.</summary>
    public void UpdatePeriodicViewNavigationItem(PeriodicView view)
    {
        var item = _periodicMenu.Children.FirstOrDefault(c => c.VarietyId == view.Id);
        if (item is not null)
        {
            item.Title = view.Nom;
            item.Icon = view.Icon;
            item.Reset();
            if (SelectedItem == item) CurrentViewModel = item.GetOrCreateViewModel();
        }
        ResortPeriodicViewNavigationItems();
    }

    public void ResortPeriodicViewNavigationItems()
    {
        var order = App.Db.PeriodicViews.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).Select(v => v.Id).ToList();
        for (var target = 0; target < order.Count; target++)
        {
            var item = _periodicMenu.Children.FirstOrDefault(c => c.VarietyId == order[target]);
            if (item is null) continue;
            var current = _periodicMenu.Children.IndexOf(item);
            if (current != target) _periodicMenu.Children.Move(current, target);
        }
    }

    public void RemovePeriodicViewNavigationItem(int viewId)
    {
        var item = _periodicMenu.Children.FirstOrDefault(c => c.VarietyId == viewId);
        if (item is null) return;
        _periodicMenu.Children.Remove(item);
        if (SelectedItem == item) SelectedItem = _periodicMenu;
    }

    private NavigationItem CreateVarietyItem(FilterVariety variety) =>
        new(variety.Nom, variety.Icon, () => new DynamicFilterListViewModel(variety, this), variety.Id);

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

    /// <summary>Met à jour titre et icône du sous-menu d'une variété après modification dans « Paramètres du
    /// module Filtre » et oublie son écran (relu à la prochaine ouverture, avec le nouveau titre).</summary>
    public void RefreshVarietyNavigationItem(FilterVariety variety)
    {
        var item = _dynamicFiltersMenu.Children.FirstOrDefault(c => c.VarietyId == variety.Id);
        if (item is null) return;
        item.Title = variety.Nom;
        item.Icon = variety.Icon;
        item.Reset();
        if (SelectedItem == item) CurrentViewModel = item.GetOrCreateViewModel();
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
        // Tant que la gestion des vues / variétés est désactivée (mode édition), un menu dépliant n'a plus de
        // page de gestion à afficher : ouvrir directement son premier sous-menu (« Changement filtre
        // périodique » comme « Changement sur encrassement »).
        foreach (var menu in _expandableMenus)
        {
            if (value != menu.Item || menu.AllowManagement() || menu.Item.Children.Count == 0) continue;

            // Si on est déjà dans la section (ou déjà "sur" le menu), on ignore : sans ce garde-fou, le
            // simple fait de replier le menu (clic sur le chevron) redéclencherait cette redirection, qui
            // forcerait IsExpanded à true et rouvrirait le menu immédiatement - le rendant impossible à
            // replier. Le menu reste repliable à tout moment, la redirection ne joue qu'à l'entrée dans la
            // section depuis un autre écran.
            var alreadyInSection = _lastDisplayedItem == menu.Item || (_lastDisplayedItem is not null && menu.Item.Children.Contains(_lastDisplayedItem));
            if (alreadyInSection)
            {
                SelectedItem = _lastDisplayedItem;
                return;
            }

            menu.Item.IsExpanded = true;
            SelectedItem = menu.Item.Children[0];
            return;
        }

        // Sélectionner le menu lui-même (gestion autorisée) le déplie aussi : sans ce garde-fou, il ne se
        // déplierait qu'au clic sur le chevron.
        foreach (var menu in _expandableMenus)
            if (value == menu.Item) menu.Item.IsExpanded = true;

        _lastDisplayedItem = value;
        foreach (var item in NavigationItems) item.IsSelected = item == value;
        foreach (var menu in _expandableMenus)
            foreach (var child in menu.Item.Children) child.IsSelected = child == value;
        HelpItem.IsSelected = value == HelpItem;
        SettingsItem.IsSelected = value == SettingsItem;

        // Serveur de base de données : d'autres postes écrivent en même temps. Le contexte partagé garde les
        // lignes déjà lues telles quelles ; on l'oublie à chaque changement d'écran (rien n'est en attente, les
        // saisies sont enregistrées tout de suite) pour que le rechargement ci-dessous relise le serveur.
        if (App.DbFactory.IsServer && !App.Db.ChangeTracker.HasChanges()) App.Db.ChangeTracker.Clear();

        var vm = value?.GetOrCreateViewModel();
        // Recharge les données à chaque fois qu'on (re)sélectionne l'écran : nécessaire notamment pour
        // que "Inventaire" et "Commande" (qui partagent désormais les mêmes lignes en base, voir
        // README) reflètent immédiatement les ajouts/modifications faits depuis l'autre écran.
        if (vm is IReloadable reloadable) reloadable.Reload();
        CurrentViewModel = vm;
    }

    /// <summary>Sélectionne "Aide" (bouton à part, en bas de la barre latérale - voir MainWindow.xaml).</summary>
    [RelayCommand]
    private void ShowHelp() => SelectedItem = HelpItem;

    /// <summary>Sélectionne "Paramètres" (bouton à part, juste au-dessus de "Aide" - voir MainWindow.xaml).</summary>
    [RelayCommand]
    private void ShowSettings() => SelectedItem = SettingsItem;
}
