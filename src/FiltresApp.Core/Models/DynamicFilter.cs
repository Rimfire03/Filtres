using System.ComponentModel.DataAnnotations.Schema;

namespace FiltresApp.Core.Models;

/// <summary>Filtre d'une <see cref="FilterVariety"/> créée librement par l'utilisateur (menu dépliant
/// "Filtres F7 à H14") : pas de périodicité mensuelle fixe, juste un historique de remplacements
/// successifs (qté changée + date). Équivalent généralisé de l'ancien <c>OpacimetricFilter</c>
/// (auparavant une seule variété codée en dur, "F7 à H13").</summary>
public class DynamicFilter : IFamilyTrackedItem
{
    public int Id { get; set; }

    public int VarietyId { get; set; }
    public FilterVariety? Variety { get; set; }

    public string Location { get; set; } = string.Empty;
    public string Dimension { get; set; } = string.Empty;
    public string? FilterType { get; set; }
    public int QuantityInPlace { get; set; }

    /// <summary>Commentaire libre, affiché en dernière colonne de la grille (voir README).</summary>
    public string? Commentaire { get; set; }

    /// <summary>Couleur de ligne (clic droit sur la grille), référence libre vers <see cref="RowColor.Id"/>
    /// (voir Paramètres, "Couleurs de ligne") - null si aucune couleur choisie.</summary>
    public int? RowColorId { get; set; }

    public int? DynamicFilterFamilyId { get; set; }
    public DynamicFilterFamily? Family { get; set; }

    public const string NoFamilyLabel = "Sans famille";

    /// <summary>Titre du séparateur de famille dans la grille.</summary>
    [NotMapped]
    public string FamilyGroupLabel => Family?.Nom ?? NoFamilyLabel;

    public List<DynamicFilterReplacement> Replacements { get; set; } = new();

    [NotMapped]
    public DateOnly? LastChangedDate => Replacements
        .Where(r => r.DateChanged.HasValue)
        .Select(r => r.DateChanged!.Value)
        .OrderByDescending(d => d)
        .Cast<DateOnly?>()
        .FirstOrDefault();

    [NotMapped]
    public int ReplacementCount => Replacements.Count;

    int? IFamilyTrackedItem.FamilyId => DynamicFilterFamilyId;
    IEnumerable<IDatedReplacement> IFamilyTrackedItem.DatedReplacements => Replacements;
}

public class DynamicFilterReplacement : IDatedReplacement
{
    public int Id { get; set; }
    public int DynamicFilterId { get; set; }
    public DynamicFilter? DynamicFilter { get; set; }

    public int QuantityChanged { get; set; }
    public DateOnly? DateChanged { get; set; }
}
