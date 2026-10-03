namespace FiltresApp.Core.Models;

/// <summary>Remplacement daté d'un élément suivi sans périodicité fixe (<see cref="DynamicFilterReplacement"/>,
/// <see cref="BeltReplacement"/>, <see cref="BearingReplacement"/>) : ce qui suffit à l'historique par année
/// et à la suppression d'un enregistrement.</summary>
public interface IDatedReplacement
{
    int Id { get; }
    DateOnly? DateChanged { get; }
}

/// <summary>Famille créée à la main (<see cref="DynamicFilterFamily"/>, <see cref="BeltFamily"/>,
/// <see cref="BearingFamily"/>).</summary>
public interface INamedFamily
{
    int Id { get; }
    string Nom { get; set; }
}

/// <summary>Élément suivi par famille avec un historique de remplacements datés, sans périodicité mensuelle
/// fixe : filtre d'une variété "F7 à H14" (<see cref="DynamicFilter"/>), courroie (<see cref="Belt"/>), jeu
/// de roulements (<see cref="BearingUnit"/>). Les membres propres à cette interface (famille, historique) sont
/// implémentés explicitement dans chaque modèle : non publics, ils ne sont donc pas mappés par EF Core.</summary>
public interface IFamilyTrackedItem
{
    int Id { get; }
    string Location { get; }
    string? Commentaire { get; }
    int? RowColorId { get; set; }
    int? FamilyId { get; }
    string FamilyGroupLabel { get; }
    IEnumerable<IDatedReplacement> DatedReplacements { get; }
}
