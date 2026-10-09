using System.ComponentModel.DataAnnotations.Schema;

namespace FiltresApp.Core.Models;

/// <summary>Jeu de roulements d'une centrale (menu "Paramètres", module activable "Roulements") : pas de
/// périodicité mensuelle fixe, un historique de remplacements où chaque enregistrement précise lesquels
/// des trois roulements (avant / arrière / volute) ont été changés à cette date - même principe que
/// <see cref="DynamicFilter"/> (menu "Filtres F7 à H14"), sans notion de variété (un seul module).</summary>
public class BearingUnit : IFamilyTrackedItem, ISiteScoped
{
    /// <summary>Site auquel appartient la ligne (module MultiSite, voir <see cref="Site"/>).</summary>
    public int SiteId { get; set; }

    public int Id { get; set; }

    public string Location { get; set; } = string.Empty;

    /// <summary>Type de centrale : une des deux valeurs de <see cref="CentraleTypeOptions"/>.</summary>
    public string CentraleType { get; set; } = CentraleTypeOptions[0];

    public string? RefAvant { get; set; }
    public string? RefArriere { get; set; }
    public string? RefVolute { get; set; }

    /// <summary>Commentaire libre, affiché en dernière colonne de la grille.</summary>
    public string? Commentaire { get; set; }

    /// <summary>Couleur de ligne (clic droit sur la grille), référence libre vers <see cref="RowColor.Id"/>.</summary>
    public int? RowColorId { get; set; }

    public int? BearingFamilyId { get; set; }
    public BearingFamily? Family { get; set; }

    public const string NoFamilyLabel = INamedFamily.NoFamilyLabel;

    [NotMapped]
    public string FamilyGroupLabel => Family?.Nom ?? NoFamilyLabel;

    public List<BearingReplacement> Replacements { get; set; } = new();

    [NotMapped]
    public DateOnly? LastChangedDate => Replacements
        .Where(r => r.DateChanged.HasValue)
        .Select(r => r.DateChanged!.Value)
        .OrderByDescending(d => d)
        .Cast<DateOnly?>()
        .FirstOrDefault();

    [NotMapped]
    public int ReplacementCount => Replacements.Count;

    public static readonly string[] CentraleTypeOptions = { "Courroies", "Entraînement direct" };

    int? IFamilyTrackedItem.FamilyId => BearingFamilyId;
    IEnumerable<IDatedReplacement> IFamilyTrackedItem.DatedReplacements => Replacements;
}

/// <summary>Un enregistrement de changement : la date, et lesquels des trois roulements ont été changés ce
/// jour-là (au moins un, choisi à la saisie - voir BearingListViewModel.AddReplacement).</summary>
public class BearingReplacement : IDatedReplacement, ISiteScoped
{
    /// <summary>Site auquel appartient la ligne (module MultiSite, voir <see cref="Site"/>).</summary>
    public int SiteId { get; set; }

    public int Id { get; set; }
    public int BearingUnitId { get; set; }
    public BearingUnit? BearingUnit { get; set; }

    public DateOnly? DateChanged { get; set; }
    public bool ChangedAvant { get; set; }
    public bool ChangedArriere { get; set; }
    public bool ChangedVolute { get; set; }

    [NotMapped]
    public string ChangedLabel
    {
        get
        {
            var parts = new List<string>();
            if (ChangedAvant) parts.Add("Avant");
            if (ChangedArriere) parts.Add("Arrière");
            if (ChangedVolute) parts.Add("Volute");
            return parts.Count == 0 ? "-" : string.Join(", ", parts);
        }
    }
}

/// <summary>Famille de jeux de roulements, créée à la main et attribuée manuellement à chaque ligne.</summary>
public class BearingFamily : INamedFamily, ISiteScoped
{
    /// <summary>Site auquel appartient la ligne (module MultiSite, voir <see cref="Site"/>).</summary>
    public int SiteId { get; set; }

    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;
}
