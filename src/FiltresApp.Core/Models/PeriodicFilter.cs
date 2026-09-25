using System.ComponentModel.DataAnnotations.Schema;
using FiltresApp.Core.Services;

namespace FiltresApp.Core.Models;

/// <summary>
/// Filtre à périodicité mensuelle fixe : G4 plissé, G4 plan, G3, Charbon.
/// Correspond aux feuilles Excel "Filtres G4 plissés", "Filtres G4 plan", "Filtres G3", "Charbon".
/// </summary>
public class PeriodicFilter
{
    public int Id { get; set; }
    public FilterCategory Category { get; set; }

    /// <summary>Nom de la centrale d'air (G4 plissé) ou emplacement de l'appareil (G4 plan, G3, Charbon).</summary>
    public string Location { get; set; } = string.Empty;

    public string Dimension { get; set; } = string.Empty;

    /// <summary>Type de média : ex "MEDIA PLISSE G4", "A LAVER", "cousu sur fil d'acier", "Charbon".</summary>
    public string MediaType { get; set; } = string.Empty;

    public int QuantityInPlace { get; set; }

    /// <summary>Liste des mois (1-12) où le filtre doit être changé, stockée en texte "/1/3/5/7/9/11/".</summary>
    public string Periodicity { get; set; } = string.Empty;

    /// <summary>Uniquement pour Charbon : compteur d'heures.</summary>
    public int? HourCounter { get; set; }

    /// <summary>Référence libre vers la Liste K7 (pour G3 de type K7).</summary>
    public string? K7Reference { get; set; }

    public string? Notes { get; set; }

    /// <summary>Uniquement pertinent pour G4 plissé : quand activé, le filtre est changé tous les 15
    /// jours (deux fois plus souvent que le rythme habituel), et le besoin calculé par
    /// <see cref="Services.OrderNeedCalculationService"/> pour ce filtre est doublé.</summary>
    public bool ChangedEvery15Days { get; set; }

    public List<FilterReplacement> Replacements { get; set; } = new();

    [NotMapped]
    public DateOnly? NextDueDate => MaintenanceScheduleService.GetNextDueDate(this, DateOnly.FromDateTime(DateTime.Today));

    [NotMapped]
    public DateOnly? LastDoneDate => MaintenanceScheduleService.GetLastReplacementDate(this);

    [NotMapped]
    public string PeriodicityDisplay => string.Join("/", GetPeriodicityMonths());

    public List<int> GetPeriodicityMonths()
    {
        if (string.IsNullOrWhiteSpace(Periodicity)) return new List<int>();
        return Periodicity.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var m) ? m : 0)
            .Where(m => m is >= 1 and <= 12)
            .Distinct()
            .OrderBy(m => m)
            .ToList();
    }

    public static string FormatPeriodicityMonths(IEnumerable<int> months)
    {
        var distinct = months.Where(m => m is >= 1 and <= 12).Distinct().OrderBy(m => m).ToList();
        if (distinct.Count == 0) return string.Empty;
        return "/" + string.Join("/", distinct) + "/";
    }
}
