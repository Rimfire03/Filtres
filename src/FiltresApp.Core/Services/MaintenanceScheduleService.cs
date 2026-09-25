using FiltresApp.Core.Models;

namespace FiltresApp.Core.Services;

/// <summary>Calcule la prochaine échéance de remplacement à partir de la périodicité (liste de mois)
/// et de l'historique des remplacements réalisés, en remplacement des formules Excel
/// (MAX/COUNTA sur les colonnes "changement prévu en [mois]").</summary>
public static class MaintenanceScheduleService
{
    public static DateOnly? GetNextDueDate(PeriodicFilter filter, DateOnly today)
    {
        var months = filter.GetPeriodicityMonths();
        if (months.Count == 0) return null;

        var lastDone = filter.Replacements
            .Where(r => r.DateDone.HasValue)
            .Select(r => r.DateDone!.Value)
            .OrderByDescending(d => d)
            .FirstOrDefault();

        // Point de départ : le lendemain du dernier changement réalisé, ou aujourd'hui si jamais fait.
        var searchFrom = lastDone == default ? today : lastDone.AddDays(1);

        for (var i = 0; i < 24; i++)
        {
            var candidateMonth = searchFrom.Month;
            var candidateYear = searchFrom.Year;
            if (months.Contains(candidateMonth))
            {
                return new DateOnly(candidateYear, candidateMonth, 1);
            }
            searchFrom = searchFrom.AddMonths(1);
        }

        return null;
    }

    public static bool IsDueInMonth(PeriodicFilter filter, int month) =>
        filter.GetPeriodicityMonths().Contains(month);

    public static FilterReplacement? GetReplacementForMonth(PeriodicFilter filter, int month, int year) =>
        filter.Replacements.FirstOrDefault(r => r.Month == month && r.Year == year);

    public static DateOnly? GetLastReplacementDate(PeriodicFilter filter) =>
        filter.Replacements.Where(r => r.DateDone.HasValue)
            .Select(r => r.DateDone!.Value)
            .OrderByDescending(d => d)
            .Cast<DateOnly?>()
            .FirstOrDefault();
}
