using CommunityToolkit.Mvvm.ComponentModel;
using FiltresApp.Core.Models;

namespace FiltresApp.ViewModels;

/// <summary>Ligne du sélecteur de rattachement manuel filtre <-> ligne de commande
/// (<see cref="Views.Dialogs.FilterLinkWindow"/>) : habille un <see cref="PeriodicFilter"/> avec une
/// case à cocher et son libellé de catégorie, pour permettre à l'utilisateur d'identifier et de choisir
/// explicitement les filtres à rattacher (pas de matching automatique par dimension pour la sélection
/// elle-même - mais la liste proposée est filtrée par dimension correspondante, voir
/// <see cref="Core.Services.DimensionMatchService"/> et <see cref="DimensionMatches"/>).</summary>
public partial class FilterPickItem : ObservableObject
{
    public FilterRef Ref { get; }
    public string CategoryLabel { get; }
    public string Location { get; }
    public string Dimension { get; }
    public string MediaType { get; }
    public int QuantityInPlace { get; }
    public string PeriodicityDisplay { get; }

    /// <summary>Vrai si la dimension de ce filtre correspond (comparaison tolérante) à celle de la ligne
    /// de commande pour laquelle le sélecteur a été ouvert. Détermine si le filtre est proposé par défaut
    /// (voir <see cref="Views.Dialogs.FilterLinkWindow"/>, case "Afficher tous les filtres").</summary>
    public bool DimensionMatches { get; }

    /// <summary>Non nul si ce filtre est déjà rattaché à une AUTRE ligne de commande que celle en cours
    /// d'édition : contient la désignation de cette autre ligne, affichée en rouge dans le sélecteur pour
    /// prévenir l'utilisateur avant qu'il ne re-rattache le filtre (ce qui déplacerait le rattachement,
    /// voir <see cref="Core.Services.FilterLinkService"/>).</summary>
    public string? LinkedElsewhereLabel { get; }

    public bool IsLinkedElsewhere => LinkedElsewhereLabel != null;

    [ObservableProperty] private bool _isSelected;

    public FilterPickItem(PeriodicFilter filter, string categoryLabel, bool isSelected, bool dimensionMatches, string? linkedElsewhereLabel)
    {
        Ref = FilterRef.Periodic(filter.Id);
        CategoryLabel = categoryLabel;
        Location = filter.Location;
        Dimension = filter.Dimension;
        MediaType = filter.MediaType;
        QuantityInPlace = filter.QuantityInPlace;
        PeriodicityDisplay = filter.PeriodicityDisplay;
        DimensionMatches = dimensionMatches;
        LinkedElsewhereLabel = linkedElsewhereLabel;
        _isSelected = isSelected;
    }

    /// <summary>Texte de recherche libre utilisé par le filtre du sélecteur.</summary>
    public string SearchText => $"{CategoryLabel} {Location} {Dimension} {MediaType}".ToLowerInvariant();
}
