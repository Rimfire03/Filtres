using FiltresApp.Core.Models;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Ligne de la grille d'une variété créée sous le menu "Filtres F7 à H14" (voir
/// <see cref="TrackedItemRowViewModel{TEntity}"/>), avec la puce "Lié" de rattachement à Commande /
/// Inventaire.</summary>
public class DynamicFilterRowViewModel : TrackedItemRowViewModel<DynamicFilter>
{
    public DynamicFilterRowViewModel(DynamicFilter filter, int year, ITrackedItemOwner<DynamicFilter> owner, string? linkedOrderLine)
        : base(filter, year, owner, linkedOrderLine) { }

    public string Dimension => Entity.Dimension;
    public string? FilterType => Entity.FilterType;
    public int QuantityInPlace => Entity.QuantityInPlace;

    /// <summary>Appelé après sauvegarde par <see cref="DynamicFilterListViewModel.SetFilterType"/>
    /// (menu rapide au clic droit) : met à jour la valeur affichée sans recharger toute la grille.</summary>
    public void ApplyFilterType(string? newType)
    {
        Entity.FilterType = newType;
        OnPropertyChanged(nameof(FilterType));
    }
}
