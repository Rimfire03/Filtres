namespace FiltresApp.Core.Models;

/// <summary>Famille de filtres d'une <see cref="FilterVariety"/> donnée, créée à la main et attribuée
/// manuellement à chaque filtre (même principe que l'ancien <c>OpacimetricFamily</c>). Les familles sont
/// propres à chaque variété : deux variétés différentes ne partagent pas leurs familles.</summary>
public class DynamicFilterFamily
{
    public int Id { get; set; }

    public int VarietyId { get; set; }
    public FilterVariety? Variety { get; set; }

    public string Nom { get; set; } = string.Empty;
}
