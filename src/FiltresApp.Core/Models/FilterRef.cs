namespace FiltresApp.Core.Models;

/// <summary>Référence à un filtre à périodicité (G4 plissé, G4 plan, G3, Charbon) rattachable à une ligne
/// de Commande / Inventaire.</summary>
public readonly record struct FilterRef(int Id)
{
    public static FilterRef Periodic(int id) => new(id);
}
