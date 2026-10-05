namespace FiltresApp.Core.Models;

public enum FilterCategory
{
    G4Plisse,
    G4Plan,
    G3,
    Charbon,

    /// <summary>Filtre d'une vue créée par l'utilisateur sous « Changement filtre périodique » (voir <see cref="PeriodicView"/>).</summary>
    Custom
}
