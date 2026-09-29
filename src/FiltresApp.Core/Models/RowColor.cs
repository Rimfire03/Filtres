namespace FiltresApp.Core.Models;

/// <summary>Couleur de ligne disponible dans le menu contextuel (clic droit) de chaque grille de filtres,
/// K7, Commande et Inventaire, réglées dans Paramètres. Nom personnalisé affiché tel quel dans le menu
/// contextuel (voir <see cref="Hex"/> pour l'aperçu visuel).</summary>
public class RowColor
{
    public int Id { get; set; }

    public string Nom { get; set; } = string.Empty;

    /// <summary>Couleur au format "#RRGGBB".</summary>
    public string Hex { get; set; } = "#FFFFFF";

    public int Ordre { get; set; }
}
