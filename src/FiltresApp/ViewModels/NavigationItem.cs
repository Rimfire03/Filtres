using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FiltresApp.ViewModels;

/// <summary>Élément de la barre latérale : soit une feuille (écran classique, sans enfants), soit un menu
/// dépliant (ex. "Filtres F7 à H14") avec des sous-menus créés dynamiquement (une variété = un enfant).</summary>
public partial class NavigationItem : ObservableObject
{
    [ObservableProperty] private string _title;
    [ObservableProperty] private string _icon;
    private readonly Func<object>? _factory;
    private object? _viewModel;

    /// <summary>Identifiant de la variété représentée par ce sous-menu, ou null pour un élément de premier
    /// niveau / le menu parent lui-même.</summary>
    public int? VarietyId { get; }

    public ObservableCollection<NavigationItem> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    /// <summary>Simple ligne de séparation dans le menu défilant (ex. entre les écrans fixes et les modules
    /// activables "Courroies" / "Roulements"), pas un écran : jamais sélectionnable, voir MainWindow.xaml.</summary>
    public bool IsSeparator { get; }

    /// <summary>Faux si la fonctionnalité du module correspondant n'est pas présente dans la licence active
    /// (voir LicenseManager.HasFeature, réévalué par MainViewModel.RefreshModuleVisibility) : l'entrée reste
    /// visible (l'utilisateur l'a activée dans Paramètres) mais grisée et non cliquable, voir
    /// MainWindow.xaml - jamais juste masquée, pour que l'utilisateur comprenne qu'une licence est requise.</summary>
    [ObservableProperty] private bool _isLicensed = true;

    public NavigationItem(string title, string icon, Func<object>? factory, int? varietyId = null, bool isSeparator = false)
    {
        _title = title;
        _icon = icon;
        _factory = factory;
        VarietyId = varietyId;
        IsSeparator = isSeparator;
    }

    public static NavigationItem Separator() => new(string.Empty, string.Empty, null, isSeparator: true);

    public object? GetOrCreateViewModel() => _viewModel ??= _factory?.Invoke();

    public void Reset() => _viewModel = null;

    public override string ToString() => Title;
}
