using System.ComponentModel.DataAnnotations.Schema;

namespace FiltresApp.Core.Models;

/// <summary>Feuille "Filtres F7 a H13" : pas de périodicité mensuelle fixe, juste un historique
/// de remplacements successifs (qté changée + date).</summary>
public class OpacimetricFilter
{
    public int Id { get; set; }
    public string Location { get; set; } = string.Empty;
    public string Dimension { get; set; } = string.Empty;
    public string? FilterType { get; set; }
    public int QuantityInPlace { get; set; }
    public string? Notes { get; set; }

    public int? OpacimetricFamilyId { get; set; }
    public OpacimetricFamily? Family { get; set; }

    public const string NoFamilyLabel = "Sans famille";

    /// <summary>Titre du séparateur de famille dans la grille F7 à H13.</summary>
    [NotMapped]
    public string FamilyGroupLabel => Family?.Nom ?? NoFamilyLabel;

    public List<OpacimetricReplacement> Replacements { get; set; } = new();

    [NotMapped]
    public DateOnly? LastChangedDate => Replacements
        .Where(r => r.DateChanged.HasValue)
        .Select(r => r.DateChanged!.Value)
        .OrderByDescending(d => d)
        .Cast<DateOnly?>()
        .FirstOrDefault();

    [NotMapped]
    public int ReplacementCount => Replacements.Count;
}

public class OpacimetricReplacement
{
    public int Id { get; set; }
    public int OpacimetricFilterId { get; set; }
    public OpacimetricFilter? OpacimetricFilter { get; set; }

    public int QuantityChanged { get; set; }
    public DateOnly? DateChanged { get; set; }
}
