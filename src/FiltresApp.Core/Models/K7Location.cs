using System.ComponentModel.DataAnnotations.Schema;

namespace FiltresApp.Core.Models;

/// <summary>Feuille "Liste K7" : table de référence des lieux pour les filtres G3 type K7.
/// Chaque lieu est rattaché à une <see cref="K7Family"/> (famille + périodicité), voir README, section
/// "Familles K7 et migration".</summary>
public class K7Location : ISiteScoped
{
    /// <summary>Site auquel appartient la ligne (module MultiSite, voir <see cref="Site"/>).</summary>
    public int SiteId { get; set; }

    public int Id { get; set; }
    public string Lieu { get; set; } = string.Empty;
    public string? NumeroPorte { get; set; }
    public DateOnly? ChangementRealise { get; set; }
    public int NbFiltres { get; set; }

    public int? K7FamilyId { get; set; }
    public K7Family? Family { get; set; }

    /// <summary>True si cette ligne correspond en réalité à une ancienne ligne "titre de famille" du
    /// classeur Excel d'origine (ex. "AP  RDC periodicités  1/4/7/10"), reconstituée en <see cref="K7Family"/>
    /// lors de la migration (voir README). Ces lignes ne sont pas supprimées (aucune perte de donnée)
    /// mais sont masquées de la liste des lieux détaillés de leur famille pour éviter un doublon
    /// visuel avec l'en-tête de groupe.</summary>
    public bool IsFamilyHeader { get; set; }

    /// <summary>Couleur de ligne (clic droit sur la grille), référence libre vers <see cref="RowColor.Id"/>
    /// (voir Paramètres, "Couleurs de ligne") - null si aucune couleur choisie.</summary>
    public int? RowColorId { get; set; }

    [NotMapped]
    public string FamilyGroupLabel => Family is null
        ? "Non classé"
        : string.IsNullOrEmpty(Family.PeriodicityDisplay)
            ? Family.Nom
            : $"{Family.Nom} (périodicités : {Family.PeriodicityDisplay})";
}
