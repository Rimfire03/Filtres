namespace FiltresApp.Core.Models;

/// <summary>Rattachement d'un filtre F7 à H13 (<see cref="OpacimetricFilter"/>) à une ligne de Commande /
/// Inventaire (<see cref="OrderLine"/>), équivalent de <see cref="OrderLinePeriodicFilter"/> pour les
/// filtres à périodicité. Un filtre n'est rattaché qu'à une ligne à la fois.</summary>
public class OrderLineOpacimetricFilter
{
    public int Id { get; set; }

    public int OrderLineId { get; set; }
    public OrderLine? OrderLine { get; set; }

    public int OpacimetricFilterId { get; set; }
    public OpacimetricFilter? OpacimetricFilter { get; set; }
}
