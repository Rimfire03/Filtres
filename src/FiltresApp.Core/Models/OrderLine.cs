using System.ComponentModel.DataAnnotations.Schema;
using FiltresApp.Core.Services;

namespace FiltresApp.Core.Models;

public enum OrderDocumentType
{
    CommandeChmy
}

/// <summary>Feuille "Commande chmy". L'ancien type <see cref="OrderDocumentType"/> "PourDevis" (écran
/// "pour devis") a été retiré définitivement le 25/09/2026, données et code compris (voir README,
/// section "Suppression définitive de « pour devis » et « filtres à refacturer »").
/// <para>Depuis la fusion Inventaire / Commande chmy (voir README, section "Fusion Inventaire /
/// Commande chmy"), cette entité est aussi le support unique des lignes affichées par l'écran
/// "Inventaire" : les deux écrans lisent/écrivent exactement les mêmes lignes (celles de
/// <see cref="OrderDocumentType.CommandeChmy"/>), avec des colonnes de grille différentes adaptées à
/// chaque usage. L'ancienne entité <see cref="InventoryLine"/> reste en base (données historiques
/// préservées) mais n'est plus utilisée par l'interface.</para></summary>
public class OrderLine
{
    public int Id { get; set; }
    public OrderDocumentType DocumentType { get; set; }
    public int Ordre { get; set; }
    public string Designation { get; set; } = string.Empty;
    public string? Dimension { get; set; }
    public int? Quantite { get; set; }

    /// <summary>Affichée comme "Référence fournisseur" dans les écrans Commande et Inventaire.</summary>
    public string? Notes { get; set; }

    public string? Destination { get; set; }

    public const string NoFamilyLabel = "Sans famille";
    public const string MultipleFamiliesLabel = "Plusieurs familles";

    public static string FamilyLabelFor(FilterCategory category) => category switch
    {
        FilterCategory.G4Plisse => "Filtres G4 plissés",
        FilterCategory.G4Plan => "Filtres G4 plan",
        FilterCategory.G3 => "Filtres G3",
        FilterCategory.Charbon => "Charbon",
        _ => category.ToString()
    };

    /// <summary>Valeur de <see cref="FamilyOverride"/> qui force "Sans famille".</summary>
    public const int NoFamilyOverride = -1;

    /// <summary>Famille choisie manuellement (fenêtre Modifier) : null = automatique (d'après les filtres
    /// rattachés), <see cref="NoFamilyOverride"/> = "Sans famille", sinon valeur de <see cref="FilterCategory"/>.</summary>
    public int? FamilyOverride { get; set; }

    /// <summary>Famille affichée (séparateur des grilles Inventaire et Commande) : le choix manuel s'il y en
    /// a un, sinon la famille automatique.</summary>
    [NotMapped]
    public string FamilyGroupLabel => FamilyOverride switch
    {
        null => AutomaticFamilyLabel,
        NoFamilyOverride => NoFamilyLabel,
        int category => FamilyLabelFor((FilterCategory)category)
    };

    /// <summary>Famille déduite de la catégorie des filtres rattachés : "Sans famille" si aucun, "Plusieurs
    /// familles" s'ils sont de catégories différentes. Nécessite FilterLinks et leurs PeriodicFilter chargés.</summary>
    [NotMapped]
    public string AutomaticFamilyLabel
    {
        get
        {
            var categories = LinkedFilters.Select(f => f.Category).Distinct().ToList();
            return categories.Count switch
            {
                0 => NoFamilyLabel,
                1 => FamilyLabelFor(categories[0]),
                _ => MultipleFamiliesLabel
            };
        }
    }

    /// <summary>Quantité relevée à l'inventaire, saisie directement dans la grille de l'écran Inventaire.</summary>
    public int? Inventaire { get; set; }

    /// <summary>Marqueur technique (non affiché en UI) : identifiant de l'<see cref="InventoryLine"/>
    /// d'origine si cette ligne provient de la migration "fusion Inventaire / Commande chmy" (voir
    /// la migration 2 de DbContextFactory). Sert uniquement à rendre cette migration idempotente (ne
    /// pas dupliquer les lignes à chaque démarrage).</summary>
    public int? MigratedFromInventoryLineId { get; set; }

    /// <summary>Rattachement manuel (choisi par l'utilisateur, pas de matching automatique par
    /// dimension) à un ou plusieurs filtres à périodicité (G4 plissé, G4 plan, G3, Charbon), pour le
    /// calcul automatique du besoin semestriel. Voir <see cref="OrderNeedCalculationService"/>.</summary>
    public List<OrderLinePeriodicFilter> FilterLinks { get; set; } = new();

    [NotMapped]
    public int LinkedFilterCount => FilterLinks.Count;

    [NotMapped]
    public string LinkedFilterCountDisplay => LinkedFilterCount switch
    {
        0 => "Aucun filtre lié",
        1 => "1 filtre lié",
        _ => $"{LinkedFilterCount} filtres liés"
    };

    [NotMapped]
    public IEnumerable<PeriodicFilter> LinkedFilters => FilterLinks.Where(l => l.PeriodicFilter != null).Select(l => l.PeriodicFilter!);

    /// <summary>Besoin calculé pour l'inventaire de septembre (15/07 au 31/07), somme sur tous les
    /// filtres rattachés. Nul (pas affiché) tant qu'aucun filtre n'est rattaché.</summary>
    [NotMapped]
    public int? NeedSeptembre => FilterLinks.Count == 0 ? null : OrderNeedCalculationService.ComputeNeedSeptembre(LinkedFilters);

    /// <summary>Besoin calculé pour l'inventaire de mars (15/01 au 31/01), somme sur tous les filtres
    /// rattachés. Nul (pas affiché) tant qu'aucun filtre n'est rattaché.</summary>
    [NotMapped]
    public int? NeedMars => FilterLinks.Count == 0 ? null : OrderNeedCalculationService.ComputeNeedMars(LinkedFilters);

    /// <summary>Colonne "Quantité" de l'écran Inventaire : plus grand des deux besoins calculés moins la
    /// quantité relevée à l'inventaire. Vide tant qu'aucun filtre n'est rattaché ; 0 si le stock dépasse
    /// le besoin.</summary>
    [NotMapped]
    public int? InventoryQuantity => NeedMars is null && NeedSeptembre is null
        ? null
        : Math.Max(0, Math.Max(NeedMars ?? 0, NeedSeptembre ?? 0) - (Inventaire ?? 0));
}
