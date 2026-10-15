using System.ComponentModel.DataAnnotations.Schema;

namespace FiltresApp.Core.Models;

/// <summary>Type de ZAC (module « Suivi ZAC ») : créé à la main, sert de famille aux pièces suivies (mêmes
/// écrans de gestion que les familles de courroies / roulements).</summary>
public class ZacFamily : INamedFamily, ISiteScoped
{
    public int SiteId { get; set; }

    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;
}

/// <summary>Pièce suivie du module « Suivi ZAC » : contrôlée à intervalle régulier. Même principe que
/// <see cref="BearingUnit"/> (famille = type de ZAC, historique de contrôles datés).</summary>
public class ZacItem : IFamilyTrackedItem, ISiteScoped
{
    public int SiteId { get; set; }

    public int Id { get; set; }

    /// <summary>Nom de la pièce.</summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>Intervalle entre deux contrôles, en mois : le prochain contrôle est recalculé à chaque saisie.</summary>
    public int IntervalMonths { get; set; } = 12;

    /// <summary>Date du prochain contrôle (saisie à la création, puis recalculée : date du dernier contrôle +
    /// <see cref="IntervalMonths"/>).</summary>
    public DateOnly? NextControlDate { get; set; }

    public string? Commentaire { get; set; }
    public int? RowColorId { get; set; }

    public int? ZacFamilyId { get; set; }
    public ZacFamily? Family { get; set; }

    public const string NoFamilyLabel = INamedFamily.NoFamilyLabel;

    [NotMapped]
    public string FamilyGroupLabel => Family?.Nom ?? NoFamilyLabel;

    public List<ZacControl> Controls { get; set; } = new();

    [NotMapped]
    public DateOnly? LastControlDate => Controls
        .Where(c => c.DateChanged.HasValue)
        .Select(c => c.DateChanged!.Value)
        .OrderByDescending(d => d)
        .Cast<DateOnly?>()
        .FirstOrDefault();

    int? IFamilyTrackedItem.FamilyId => ZacFamilyId;
    IEnumerable<IDatedReplacement> IFamilyTrackedItem.DatedReplacements => Controls;
}

/// <summary>Un contrôle daté d'une pièce, avec un commentaire et ses fichiers joints.</summary>
public class ZacControl : IDatedReplacement, ISiteScoped
{
    public int SiteId { get; set; }

    public int Id { get; set; }
    public int ZacItemId { get; set; }
    public ZacItem? ZacItem { get; set; }

    public DateOnly? DateChanged { get; set; }
    public string? Comment { get; set; }

    public List<ZacAttachment> Attachments { get; set; } = new();

    /// <summary>Valeurs mesurées lors du contrôle, saisies après l'enregistrement du contrôle.</summary>
    public List<ZacMeasurement> Measurements { get; set; } = new();

    [NotMapped]
    public int AttachmentCount => Attachments.Count;
}

/// <summary>Valeur mesurée lors d'un contrôle (comptage particulaire, pression différentielle...) : désignation libre
/// (des propositions sont faites, voir <c>ZacService.SuggestedMeasurements</c>), valeur, unité et conformité.</summary>
public class ZacMeasurement : ISiteScoped
{
    public int SiteId { get; set; }

    public int Id { get; set; }
    public int ZacControlId { get; set; }
    public ZacControl? ZacControl { get; set; }

    public string Name { get; set; } = string.Empty;
    public decimal? Value { get; set; }

    /// <summary>Résultat non numérique (classe « ISO 5 », régime « Flux d'air unidirectionnel »...). Vide pour une valeur
    /// numérique (<see cref="Value"/>).</summary>
    public string? TextValue { get; set; }

    public string? Unit { get; set; }

    /// <summary>Spécification (valeur attendue) relevée dans le rapport, sous forme canonique : « ≥ 15 », « &gt; 6 »,
    /// « 0,25 – 0,35 », « 15 ± 5 », « ≤ 5 », « ISO 5 » ou un texte. Voir <c>ZacSpecification</c>. Vide : seuil ISO 14644-1
    /// des comptages particulaires, le cas échéant.</summary>
    public string? Specification { get; set; }

    /// <summary>Validation manuelle de la valeur (case à cocher) : la conformité est calculée automatiquement d'après le
    /// seuil ISO de la pièce ; la validation manuelle l'emporte (valeur acceptée malgré le calcul, ou validée quand il
    /// n'existe pas de seuil). Colonne « IsConform » en base.</summary>
    [System.ComponentModel.DataAnnotations.Schema.Column("IsConform")]
    public bool? ManualValidation { get; set; }
}

/// <summary>Fichier joint à un contrôle, stocké dans la base (compressé quand c'est utile).</summary>
public class ZacAttachment : ISiteScoped
{
    public int SiteId { get; set; }

    public int Id { get; set; }
    public int ZacControlId { get; set; }
    public ZacControl? ZacControl { get; set; }

    public string FileName { get; set; } = string.Empty;

    /// <summary>Taille d'origine du fichier, en octets.</summary>
    public long OriginalSize { get; set; }

    /// <summary>Vrai si <see cref="Data"/> est compressé (GZip).</summary>
    public bool IsCompressed { get; set; }

    public byte[] Data { get; set; } = Array.Empty<byte>();
}
