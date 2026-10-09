namespace FiltresApp.Core.Models;

/// <summary>Feuille "inventaire" d'origine : bon de commande de filtres, saisie libre.
/// <para><b>Entité historique, plus utilisée par l'interface</b> depuis la fusion Inventaire / Commande
/// chmy (voir <see cref="OrderLine"/> et le README, section "Fusion Inventaire / Commande chmy") : les
/// lignes existantes ont été migrées vers <see cref="OrderLine"/> (marquées via
/// <see cref="OrderLine.MigratedFromInventoryLineId"/>) et restent accessibles ici uniquement comme
/// copie de sauvegarde ; la table et les données ne sont pas supprimées.</para></summary>
public class InventoryLine : ISiteScoped
{
    /// <summary>Site auquel appartient la ligne (module MultiSite, voir <see cref="Site"/>).</summary>
    public int SiteId { get; set; }

    public int Id { get; set; }
    public int Ordre { get; set; }
    public string Designation { get; set; } = string.Empty;
    public string? Dimension { get; set; }
    public int? Quantite { get; set; }
    public string? Unite { get; set; }
    public string? Notes { get; set; }
}
