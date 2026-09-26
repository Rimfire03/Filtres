namespace FiltresApp.Core.Models;

public enum FilterKind
{
    Periodic,
    Dynamic
}

/// <summary>Référence à un filtre rattachable à une ligne de Commande / Inventaire : filtre à périodicité
/// (G4 plissé, G4 plan, G3, Charbon) ou filtre d'une variété créée sous "Filtres F7 à H14"
/// (<see cref="DynamicFilter"/>), dont les identifiants sont indépendants.</summary>
public readonly record struct FilterRef(FilterKind Kind, int Id)
{
    public static FilterRef Periodic(int id) => new(FilterKind.Periodic, id);
    public static FilterRef Dynamic(int id) => new(FilterKind.Dynamic, id);
}
