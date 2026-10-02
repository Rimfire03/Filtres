namespace FiltresApp.Core.Services;

/// <summary>Textes de la page de garde du bon de commande PDF. Valeurs par défaut = modèle de bon de
/// commande actuel ; chaque ligne laissée vide est simplement omise de la page.</summary>
public class CoverPageInfo
{
    public string Organisation { get; set; } = "POLE GESTION ET STRATEGIE";
    public string Direction { get; set; } = "Direction des Services Techniques";
    public string Contact { get; set; } = "Tél. 04.70.35.76.50 - Fax. 04.70.35.77.19";
    public string Title { get; set; } = "BON DE COMMANDE FILTRES";
    public string Delivery { get; set; } = "LIVRAISON IMPERATIVE DE PLEIN PIED AVEC CAMION HAYON";
    public string Market { get; set; } = "Marché RESAH N°2019-012";
}
