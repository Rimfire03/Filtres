namespace FiltresApp.Core.Models;

/// <summary>Rattachement d'un <see cref="DynamicFilter"/> (variété créée sous "Filtres F7 à H14") à une
/// ligne de Commande / Inventaire (<see cref="OrderLine"/>), équivalent de
/// <see cref="OrderLinePeriodicFilter"/> pour les filtres à périodicité. Un filtre n'est rattaché qu'à
/// une ligne à la fois.</summary>
public class OrderLineDynamicFilter
{
    public int Id { get; set; }

    public int OrderLineId { get; set; }
    public OrderLine? OrderLine { get; set; }

    public int DynamicFilterId { get; set; }
    public DynamicFilter? DynamicFilter { get; set; }
}
