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
public class OrderLine : ISiteScoped
{
    /// <summary>Site auquel appartient la ligne (module MultiSite, voir <see cref="Site"/>).</summary>
    public int SiteId { get; set; }

    public int Id { get; set; }
    public OrderDocumentType DocumentType { get; set; }
    public int Ordre { get; set; }
    public string Designation { get; set; } = string.Empty;
    public string? Dimension { get; set; }
    public int? Quantite { get; set; }

    /// <summary>Affichée comme "Référence fournisseur" dans les écrans Commande et Inventaire.</summary>
    public string? Notes { get; set; }

    public string? Destination { get; set; }

    /// <summary>Couleur de ligne (clic droit sur la grille), référence libre vers <see cref="RowColor.Id"/>
    /// (voir Paramètres, "Couleurs de ligne") - null si aucune couleur choisie.</summary>
    public int? RowColorId { get; set; }

    public const string NoFamilyLabel = "Sans famille";
    public const string MultipleFamiliesLabel = "Plusieurs familles";

    public static string FamilyLabelFor(FilterCategory category) => PeriodicViewRegistry.TitleFor(category);

    /// <summary>Famille d'un filtre périodique : titre de sa vue (modifiable, voir <see cref="PeriodicViewRegistry"/>).</summary>
    public static string FamilyLabelFor(PeriodicFilter filter) => PeriodicViewRegistry.LabelFor(filter);

    /// <summary>Valeur de <see cref="FamilyOverride"/> qui force "Sans famille".</summary>
    public const int NoFamilyOverride = -1;

    /// <summary>Valeur de <see cref="FamilyOverride"/> qui force comme famille une variété "Filtres F7 à
    /// H14", nommée dans <see cref="FamilyOverrideType"/>.</summary>
    public const int DynamicTypeOverride = 100;

    /// <summary>Famille choisie manuellement (fenêtre Modifier / colonne Famille) : null = automatique
    /// (d'après les filtres rattachés), <see cref="NoFamilyOverride"/> = "Sans famille",
    /// <see cref="DynamicTypeOverride"/> = variété "Filtres F7 à H14" choisie
    /// (<see cref="FamilyOverrideType"/>), sinon valeur de <see cref="FilterCategory"/>.</summary>
    public int? FamilyOverride { get; set; }

    /// <summary>Nom de la variété "Filtres F7 à H14" choisie comme famille quand
    /// <see cref="FamilyOverride"/> vaut <see cref="DynamicTypeOverride"/> : chaque variété créée
    /// (<see cref="FilterVariety"/>) est elle-même une famille possible pour Commande / Inventaire,
    /// indépendamment du Type (texte libre) éventuellement saisi sur ses filtres.</summary>
    public string? FamilyOverrideType { get; set; }

    public const string AutomaticFamilyChoice = "Automatique";

    /// <summary>Choix affiché par la colonne "Famille" de l'écran Commande : "Automatique" ou la famille
    /// choisie manuellement.</summary>
    [NotMapped]
    public string FamilyChoiceLabel => FamilyOverride is null ? AutomaticFamilyChoice : FamilyGroupLabel;

    /// <summary>Famille affichée (séparateur des grilles Inventaire et Commande) : le choix manuel s'il y en
    /// a un, sinon la famille automatique.</summary>
    [NotMapped]
    public string FamilyGroupLabel => FamilyOverride switch
    {
        null => AutomaticFamilyLabel,
        NoFamilyOverride => NoFamilyLabel,
        DynamicTypeOverride => string.IsNullOrWhiteSpace(FamilyOverrideType) ? AutomaticFamilyLabel : FamilyOverrideType.Trim(),
        >= PeriodicViewRegistry.ViewOverrideBase => PeriodicViewRegistry.TitleForView(FamilyOverride - PeriodicViewRegistry.ViewOverrideBase) ?? AutomaticFamilyLabel,
        int category => FamilyLabelFor((FilterCategory)category)
    };

    /// <summary>Famille déduite des filtres rattachés : catégorie pour les filtres à périodicité, nom de
    /// la variété pour les filtres d'une variété "Filtres F7 à H14" (chaque variété créée est elle-même
    /// une famille, voir <see cref="FamilyOverrideType"/>). "Sans famille" si aucune, "Plusieurs
    /// familles" si elles diffèrent. Nécessite FilterLinks (avec PeriodicFilter) et DynamicLinks (avec
    /// DynamicFilter.Variety) chargés.</summary>
    [NotMapped]
    public string AutomaticFamilyLabel
    {
        get
        {
            var families = LinkedFilters.Select(f => FamilyLabelFor(f))
                .Concat(DynamicLinks
                    .Select(l => l.DynamicFilter?.Variety?.Nom)
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Select(t => t!))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            return families.Count switch
            {
                0 => NoFamilyLabel,
                1 => families[0],
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

    /// <summary>Rattachement manuel à des filtres d'une variété "Filtres F7 à H14" (pas de besoin
    /// calculé : besoin saisi).</summary>
    public List<OrderLineDynamicFilter> DynamicLinks { get; set; } = new();

    [NotMapped]
    public int LinkedFilterCount => FilterLinks.Count + DynamicLinks.Count;

    [NotMapped]
    public string LinkedFilterCountDisplay => LinkedFilterCount switch
    {
        0 => "Aucun filtre lié",
        1 => "1 filtre lié",
        _ => $"{LinkedFilterCount} filtres liés"
    };

    [NotMapped]
    public IEnumerable<PeriodicFilter> LinkedFilters => FilterLinks.Where(l => l.PeriodicFilter != null).Select(l => l.PeriodicFilter!);

    /// <summary>Besoin saisi directement dans l'écran Commande, pour les lignes des autres familles.</summary>
    public int? ManualNeed { get; set; }

    [NotMapped]
    public bool UsesComputedNeed => PeriodicViewRegistry.IsComputedTitle(FamilyGroupLabel);

    /// <summary>Cellule "Besoin" de l'écran Commande saisissable (familles hors G4 plissé, G4 plan, G3).</summary>
    [NotMapped]
    public bool UsesManualNeed => !UsesComputedNeed;

    /// <summary>Besoin calculé pour l'inventaire de septembre (15/07 au 31/07), somme sur tous les
    /// filtres rattachés. Uniquement pour les familles G4 plissé, G4 plan et G3 ; nul sinon ou tant
    /// qu'aucun filtre n'est rattaché.</summary>
    [NotMapped]
    public int? NeedSeptembre => !UsesComputedNeed || FilterLinks.Count == 0 ? null : OrderNeedCalculationService.ComputeNeedSeptembre(LinkedFilters);

    /// <summary>Besoin calculé pour l'inventaire de mars (15/01 au 31/01), même règle que
    /// <see cref="NeedSeptembre"/>.</summary>
    [NotMapped]
    public int? NeedMars => !UsesComputedNeed || FilterLinks.Count == 0 ? null : OrderNeedCalculationService.ComputeNeedMars(LinkedFilters);

    /// <summary>Colonne "Besoin" de l'écran Commande : plus grand des deux besoins calculés pour les
    /// familles G4 plissé, G4 plan et G3, besoin saisi (<see cref="ManualNeed"/>) pour les autres.</summary>
    [NotMapped]
    public int? Need => UsesComputedNeed
        ? NeedMars is null && NeedSeptembre is null ? null : Math.Max(NeedMars ?? 0, NeedSeptembre ?? 0)
        : ManualNeed;

    /// <summary>Colonne "Quantité" de l'écran Inventaire et "Quantité à commander" de l'écran Commande :
    /// <see cref="Need"/> moins la quantité relevée à l'inventaire, 0 si le stock dépasse le besoin, vide
    /// sans besoin.</summary>
    [NotMapped]
    public int? InventoryQuantity => Need is null ? null : Math.Max(0, Need.Value - (Inventaire ?? 0));
}
