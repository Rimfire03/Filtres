using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FiltresApp.ViewModels;

/// <summary>Élément de la barre latérale : soit une feuille (écran classique, sans enfants), soit un menu
/// dépliant (ex. "Filtres F7 à H14") avec des sous-menus créés dynamiquement (une variété = un enfant).</summary>
public partial class NavigationItem : ObservableObject
{
    [ObservableProperty] private string _title;
    public string Icon { get; }
    private readonly Func<object>? _factory;
    private object? _viewModel;

    /// <summary>Identifiant de la variété représentée par ce sous-menu, ou null pour un élément de premier
    /// niveau / le menu parent lui-même.</summary>
    public int? VarietyId { get; }

    public ObservableCollection<NavigationItem> Children { get; } = new();

    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isSelected;

    public NavigationItem(string title, string icon, Func<object>? factory, int? varietyId = null)
    {
        _title = title;
        Icon = icon;
        _factory = factory;
        VarietyId = varietyId;
    }

    public object? GetOrCreateViewModel() => _viewModel ??= _factory?.Invoke();

    public void Reset() => _viewModel = null;

    public override string ToString() => Title;
}
