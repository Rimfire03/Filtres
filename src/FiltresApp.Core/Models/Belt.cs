using System.ComponentModel.DataAnnotations.Schema;

namespace FiltresApp.Core.Models;

/// <summary>Courroie d'une centrale (menu "Paramètres", module activable "Courroies") : pas de périodicité
/// mensuelle fixe, juste un historique de remplacements successifs (nombre changé + date) - même principe
/// que <see cref="DynamicFilter"/> (menu "Filtres F7 à H14"), sans notion de variété (un seul module).</summary>
public class Belt
{
    public int Id { get; set; }

    public string Location { get; set; } = string.Empty;
    public string? BeltType { get; set; }
    public int QuantityInPlace { get; set; }

    /// <summary>Commentaire libre, affiché en dernière colonne de la grille.</summary>
    public string? Commentaire { get; set; }

    /// <summary>Couleur de ligne (clic droit sur la grille), référence libre vers <see cref="RowColor.Id"/>.</summary>
    public int? RowColorId { get; set; }

    public int? BeltFamilyId { get; set; }
    public BeltFamily? Family { get; set; }

    public const string NoFamilyLabel = "Sans famille";

    [NotMapped]
    public string FamilyGroupLabel => Family?.Nom ?? NoFamilyLabel;

    public List<BeltReplacement> Replacements { get; set; } = new();

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

public class BeltReplacement
{
    public int Id { get; set; }
    public int BeltId { get; set; }
    public Belt? Belt { get; set; }

    public int QuantityChanged { get; set; }
    public DateOnly? DateChanged { get; set; }
}

/// <summary>Famille de courroies, créée à la main et attribuée manuellement à chaque courroie.</summary>
public class BeltFamily
{
    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;
}
