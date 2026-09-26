namespace FiltresApp.Core.Models;

/// <summary>Une "variété" de filtre créée librement par l'utilisateur sous le menu dépliant "Filtres F7 à
/// H14" (barre latérale) : chaque variété a son propre écran, avec la même disposition que l'ancien
/// écran "Filtres F7 à H13" (historique de remplacements par date, sans périodicité mensuelle fixe,
/// familles créées à la main, rattachement à Commande / Inventaire). Voir <see cref="DynamicFilter"/>.</summary>
public class FilterVariety
{
    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;

    /// <summary>Ordre d'affichage dans le menu dépliant.</summary>
    public int Ordre { get; set; }
}
