namespace FiltresApp.Core.Models;

/// <summary>Un remplacement réalisé pour un PeriodicFilter, correspondant aux colonnes
/// "changement réalisé en [mois]" / "Date de changement en [mois]" de la feuille Excel.</summary>
public class FilterReplacement
{
    public int Id { get; set; }
    public int PeriodicFilterId { get; set; }
    public PeriodicFilter? PeriodicFilter { get; set; }

    /// <summary>Mois calendaire (1-12) auquel se rapporte ce remplacement.</summary>
    public int Month { get; set; }
    public int Year { get; set; }

    public int QuantityDone { get; set; }
    public DateOnly? DateDone { get; set; }

    /// <summary>Compteur d'heures de fonctionnement relevé à la réalisation (vues où
    /// <see cref="PeriodicView.TracksOperatingHours"/> est activé), null sinon.</summary>
    public int? OperatingHours { get; set; }
}
