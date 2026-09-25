namespace FiltresApp.Core.Models;

/// <summary>Référence à un filtre rattachable à une ligne de Commande / Inventaire : filtre à périodicité
/// (G4 plissé, G4 plan, G3, Charbon) ou filtre F7 à H13, dont les identifiants sont indépendants.</summary>
public readonly record struct FilterRef(bool IsOpacimetric, int Id)
{
    public static FilterRef Periodic(int id) => new(false, id);
    public static FilterRef Opacimetric(int id) => new(true, id);
}
