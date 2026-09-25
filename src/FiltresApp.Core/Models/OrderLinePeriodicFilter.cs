namespace FiltresApp.Core.Models;

/// <summary>Table de liaison many-to-many entre une ligne de "Commande chmy" / "pour devis"
/// (<see cref="OrderLine"/>) et un filtre à périodicité (<see cref="PeriodicFilter"/> : G4 plissé,
/// G4 plan, G3, Charbon). Le rattachement est manuel (choisi par l'utilisateur via le sélecteur de
/// l'écran "Commande chmy"/"pour devis"), pas de matching automatique par dimension. Il sert à calculer
/// automatiquement le besoin semestriel (<see cref="Services.OrderNeedCalculationService"/>) de la ligne
/// de commande à partir des filtres qui lui sont rattachés.</summary>
public class OrderLinePeriodicFilter
{
    public int Id { get; set; }

    public int OrderLineId { get; set; }
    public OrderLine? OrderLine { get; set; }

    public int PeriodicFilterId { get; set; }
    public PeriodicFilter? PeriodicFilter { get; set; }
}
