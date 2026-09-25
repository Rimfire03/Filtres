using System.ComponentModel.DataAnnotations.Schema;

namespace FiltresApp.Core.Models;

/// <summary>Feuille "Liste K7" : table de référence des lieux pour les filtres G3 type K7.
/// Chaque lieu est rattaché à une <see cref="K7Family"/> (famille + périodicité), voir README, section
/// "Familles K7 et migration".</summary>
public class K7Location
{
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

    [NotMapped]
    public string FamilyGroupLabel => Family is null
        ? "Non classé"
        : string.IsNullOrEmpty(Family.PeriodicityDisplay)
            ? Family.Nom
            : $"{Family.Nom} (périodicités : {Family.PeriodicityDisplay})";
}
