using System.ComponentModel.DataAnnotations.Schema;

namespace FiltresApp.Core.Models;

/// <summary>Courroie d'une centrale (menu "Paramètres", module activable "Courroies") : pas de périodicité
/// mensuelle fixe, juste un historique de remplacements successifs (nombre changé + date) - même principe
/// que <see cref="DynamicFilter"/> (menu "Filtres F7 à H14"), sans notion de variété (un seul module).</summary>
public class Belt : IFamilyTrackedItem
{
    public int Id { get; set; }

    public string Location { get; set; } = string.Empty;
    /// <summary>Type (référence) des courroies de soufflage et d'extraction : peuvent différer (voir migration 26,
    /// qui remplace l'ancienne colonne unique "BeltType", conservée en base mais plus lue).</summary>
    public string? BeltTypeSoufflage { get; set; }
    public string? BeltTypeExtraction { get; set; }

    /// <summary>Courroies de la fonction soufflage de la centrale (0 = pas de soufflage).</summary>
    public int QuantitySoufflage { get; set; }

    /// <summary>Courroies de la fonction extraction de la centrale (0 = pas d'extraction).</summary>
    public int QuantityExtraction { get; set; }

    /// <summary>Total soufflage + extraction (non mappé : l'ancienne colonne "QuantityInPlace" reste en base
    /// mais n'est plus lue, voir migration 25).</summary>
    [NotMapped]
    public int QuantityInPlace => QuantitySoufflage + QuantityExtraction;

    /// <summary>Commentaire libre, affiché en dernière colonne de la grille.</summary>
    public string? Commentaire { get; set; }

    /// <summary>Couleur de ligne (clic droit sur la grille), référence libre vers <see cref="RowColor.Id"/>.</summary>
    public int? RowColorId { get; set; }

    public int? BeltFamilyId { get; set; }
    public BeltFamily? Family { get; set; }

    public const string NoFamilyLabel = INamedFamily.NoFamilyLabel;

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

    int? IFamilyTrackedItem.FamilyId => BeltFamilyId;
    IEnumerable<IDatedReplacement> IFamilyTrackedItem.DatedReplacements => Replacements;
}

public class BeltReplacement : IDatedReplacement
{
    public int Id { get; set; }
    public int BeltId { get; set; }
    public Belt? Belt { get; set; }

    /// <summary>Nombre de courroies changées (somme des quantités des fonctions cochées).</summary>
    public int QuantityChanged { get; set; }
    public DateOnly? DateChanged { get; set; }

    /// <summary>Fonctions dont les courroies ont été changées ce jour-là (au moins une pour un enregistrement
    /// récent, voir BeltListViewModel.AddReplacement ; aucune pour un ancien enregistrement, qui ne porte que
    /// <see cref="QuantityChanged"/>).</summary>
    public bool ChangedSoufflage { get; set; }
    public bool ChangedExtraction { get; set; }

    [NotMapped]
    public string ChangedLabel
    {
        get
        {
            var parts = new List<string>();
            if (ChangedSoufflage) parts.Add("Soufflage");
            if (ChangedExtraction) parts.Add("Extraction");
            return parts.Count == 0 ? $"quantité {QuantityChanged}" : $"{string.Join(", ", parts)} - quantité {QuantityChanged}";
        }
    }
}

/// <summary>Famille de courroies, créée à la main et attribuée manuellement à chaque courroie.</summary>
public class BeltFamily : INamedFamily
{
    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;
}
