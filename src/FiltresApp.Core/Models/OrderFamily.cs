namespace FiltresApp.Core.Models;

/// <summary>Famille de lignes des écrans Inventaire et Commande (mêmes lignes <see cref="OrderLine"/>),
/// utilisée pour les regrouper et les filtrer.</summary>
public class OrderFamily
{
    public int Id { get; set; }
    public string Nom { get; set; } = string.Empty;
}
