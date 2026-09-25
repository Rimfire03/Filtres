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

    public int? OrderFamilyId { get; set; }
    public OrderFamily? Family { get; set; }

    [NotMapped]
    public string FamilyName => Family?.Nom ?? string.Empty;

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
}
