using System.Globalization;
using FiltresApp.Core.Data;
using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Services;

/// <summary>Charge prévisionnelle d'un mois : nombre de filtres à changer, quantité à fournir et réalisation.</summary>
public sealed record MonthlyWorkloadRow(int Month, string MonthName, int FiltersDue, int QuantityToSupply,
    int DoneOfDue, double? CompletionRate, IReadOnlyDictionary<string, int> DueByView)
{
    public string CompletionText => CompletionRate is { } r ? $"{r:P0}" : "-";
}

/// <summary>Filtre périodique dont une échéance passée n'a pas été réalisée.</summary>
public sealed record OverdueRow(string View, string Location, string Dimension, int Month, int Year, int DaysLate, int Quantity)
{
    public string MonthText => $"{Month:00}/{Year}";
}

/// <summary>Compteur d'heures de fonctionnement : durée moyenne entre deux changements d'un filtre.</summary>
public sealed record OperatingHoursRow(string View, string Location, int Changes, double? AverageHours)
{
    public string AverageText => AverageHours is { } h ? $"{h:0} h" : "-";
}

/// <summary>Nombre de changements et quantité pour une année (et une variété, un type...).</summary>
public sealed record YearlyRow(string Label, int Year, int Changes, int Quantity);

/// <summary>Intervalle entre changements successifs d'un élément (filtre, courroie, roulement).</summary>
public sealed record IntervalRow(string Name, string Group, int Changes, double? AverageDays, int? MinDays, int? MaxDays, DateOnly? LastChange)
{
    public string AverageText => AverageDays is { } d ? $"{d:0} j" : "-";
    public string MinText => MinDays is { } d ? $"{d} j" : "-";
    public string MaxText => MaxDays is { } d ? $"{d} j" : "-";
    public string LastText => LastChange is { } l ? l.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture) : "jamais";
}

/// <summary>Élément sans changement depuis longtemps (ou jamais changé).</summary>
public sealed record UnchangedRow(string Name, string Group, DateOnly? LastChange, int? MonthsSince)
{
    public string LastText => LastChange is { } l ? l.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture) : "jamais";
    public string SinceText => MonthsSince is { } m ? $"{m} mois" : "-";
}

/// <summary>Changements de courroies pour une année.</summary>
public sealed record BeltYearRow(int Year, int Changes, int Quantity, int Soufflage, int Extraction);

/// <summary>Stock minimum d'un type de courroie : 2 changements complets de la centrale qui en utilise le plus.</summary>
public sealed record BeltStockRow(string BeltType, int MaxInOneCentrale, string Centrale, int MinimumStock, int ChangedLast12Months);

/// <summary>Changements de roulements pour une année.</summary>
public sealed record BearingYearRow(int Year, int Changes, int Avant, int Arriere, int Volute);

/// <summary>Calculs de statistiques sur le site courant (le contexte est déjà filtré par site). Aucune écriture.
/// Les règles de planification reprennent celles des écrans : périodicité mensuelle, option « 15 jours »,
/// filtres lavables sans remplacement.</summary>
public static class StatisticsService
{
    public static readonly string[] MonthNames =
        ["Janvier", "Février", "Mars", "Avril", "Mai", "Juin", "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre"];

    private static string ViewName(PeriodicFilter f) => f.PeriodicView?.Nom ?? f.Category.ToString();

    // ---- Filtres périodiques ----

    public static List<MonthlyWorkloadRow> MonthlyWorkload(FiltresDbContext ctx, int year)
    {
        var filters = LoadPeriodicFilters(ctx, year);
        var rows = new List<MonthlyWorkloadRow>();
        for (var m = 1; m <= 12; m++)
        {
            var due = filters.Where(f => f.GetPeriodicityMonths().Contains(m)).ToList();
            var supply = due.Where(f => !f.IsWashable).Sum(f => f.QuantityInPlace * (f.ChangedEvery15Days ? 2 : 1));
            var done = due.Count(f => IsDone(f, m, year));
            var byView = due.GroupBy(ViewName).ToDictionary(g => g.Key, g => g.Count());
            rows.Add(new MonthlyWorkloadRow(m, MonthNames[m - 1], due.Count, supply, done,
                due.Count == 0 ? null : (double)done / due.Count, byView));
        }
        return rows;
    }

    /// <summary>Échéances des mois écoulés de l'année en cours non réalisées (le mois en cours n'est pas en retard).</summary>
    public static List<OverdueRow> Overdue(FiltresDbContext ctx, DateOnly today)
    {
        var year = today.Year;
        var result = new List<OverdueRow>();
        foreach (var f in LoadPeriodicFilters(ctx, year))
        {
            foreach (var m in f.GetPeriodicityMonths().Where(m => m < today.Month))
            {
                if (IsDone(f, m, year)) continue;
                var endOfMonth = new DateOnly(year, m, DateTime.DaysInMonth(year, m));
                result.Add(new OverdueRow(ViewName(f), f.Location, f.Dimension, m, year,
                    today.DayNumber - endOfMonth.DayNumber, f.IsWashable ? 0 : f.QuantityInPlace));
            }
        }
        return result.OrderByDescending(r => r.DaysLate).ToList();
    }

    /// <summary>Durée moyenne entre deux changements, pour les vues qui relèvent le compteur d'heures.</summary>
    public static List<OperatingHoursRow> OperatingHours(FiltresDbContext ctx)
    {
        var filters = ctx.PeriodicFilters.AsNoTracking()
            .Include(f => f.PeriodicView).Include(f => f.Replacements)
            .Where(f => f.PeriodicView != null && f.PeriodicView.TracksOperatingHours).ToList();
        var rows = new List<OperatingHoursRow>();
        foreach (var f in filters)
        {
            var readings = f.Replacements.Where(r => r.OperatingHours.HasValue && r.DateDone.HasValue)
                .OrderBy(r => r.DateDone).Select(r => r.OperatingHours!.Value).ToList();
            if (readings.Count == 0) continue;
            var deltas = readings.Zip(readings.Skip(1), (a, b) => b - a).Where(d => d > 0).ToList();
            rows.Add(new OperatingHoursRow(ViewName(f), f.Location, readings.Count,
                deltas.Count == 0 ? null : deltas.Average()));
        }
        return rows.OrderByDescending(r => r.AverageHours ?? 0).ToList();
    }

    public static List<int> YearsWithData(FiltresDbContext ctx) =>
        ctx.FilterReplacements.AsNoTracking().Select(r => r.Year).Distinct().OrderBy(y => y).ToList();

    private static List<PeriodicFilter> LoadPeriodicFilters(FiltresDbContext ctx, int year) =>
        ctx.PeriodicFilters.AsNoTracking()
            .Include(f => f.PeriodicView)
            .Include(f => f.Replacements.Where(r => r.Year == year))
            .ToList();

    private static bool IsDone(PeriodicFilter f, int month, int year) =>
        f.Replacements.Any(r => r.Month == month && r.Year == year && r.DateDone.HasValue);

    // ---- Changement sur encrassement ----

    /// <summary>Changements par année et par variété.</summary>
    public static List<YearlyRow> FoulingYearly(FiltresDbContext ctx) =>
        ctx.DynamicFilterReplacements.AsNoTracking()
            .Where(r => r.DateChanged != null)
            .Select(r => new { Variety = r.DynamicFilter!.Variety!.Nom, r.DateChanged, r.QuantityChanged })
            .AsEnumerable()
            .GroupBy(r => (r.Variety, r.DateChanged!.Value.Year))
            .Select(g => new YearlyRow(g.Key.Variety, g.Key.Year, g.Count(), g.Sum(x => x.QuantityChanged)))
            .OrderBy(r => r.Year).ThenBy(r => r.Label).ToList();

    /// <summary>Intervalle entre changements par filtre, du plus court (s'encrasse vite) au plus long.</summary>
    public static List<IntervalRow> FoulingIntervals(FiltresDbContext ctx) =>
        ctx.DynamicFilters.AsNoTracking().Include(f => f.Variety).Include(f => f.Replacements).ToList()
            .Select(f => Interval(f.Location + (string.IsNullOrWhiteSpace(f.Dimension) ? "" : " - " + f.Dimension),
                f.Variety?.Nom ?? "", f.Replacements.Select(r => r.DateChanged)))
            .OrderBy(r => r.AverageDays ?? double.MaxValue).ToList();

    public static List<UnchangedRow> FoulingUnchanged(FiltresDbContext ctx, DateOnly today, int months) =>
        ctx.DynamicFilters.AsNoTracking().Include(f => f.Variety).Include(f => f.Replacements).ToList()
            .Select(f => Unchanged(f.Location, f.Variety?.Nom ?? "", f.Replacements.Select(r => r.DateChanged), today))
            .Where(r => r.MonthsSince is null || r.MonthsSince >= months)
            .OrderByDescending(r => r.MonthsSince ?? int.MaxValue).ToList();

    // ---- Courroies ----

    public static List<BeltYearRow> BeltYearly(FiltresDbContext ctx) =>
        ctx.BeltReplacements.AsNoTracking().Where(r => r.DateChanged != null).ToList()
            .GroupBy(r => r.DateChanged!.Value.Year)
            .Select(g => new BeltYearRow(g.Key, g.Count(), g.Sum(r => r.QuantityChanged),
                g.Count(r => r.ChangedSoufflage), g.Count(r => r.ChangedExtraction)))
            .OrderBy(r => r.Year).ToList();

    public static List<IntervalRow> BeltIntervals(FiltresDbContext ctx) =>
        ctx.Belts.AsNoTracking().Include(b => b.Family).Include(b => b.Replacements).ToList()
            .Select(b => Interval(b.Location, b.Family?.Nom ?? "", b.Replacements.Select(r => r.DateChanged)))
            .OrderByDescending(r => r.Changes).ThenBy(r => r.Name).ToList();

    /// <summary>Stock minimum par type : 2 × la quantité en place de la centrale qui en utilise le plus
    /// (soufflage et extraction du même type additionnés).</summary>
    public static List<BeltStockRow> BeltStock(FiltresDbContext ctx, DateOnly today)
    {
        var belts = ctx.Belts.AsNoTracking().Include(b => b.Replacements).ToList();
        var since = today.AddYears(-1);
        var perType = new Dictionary<string, (string Display, int Max, string Centrale, int Changed)>(StringComparer.OrdinalIgnoreCase);

        foreach (var belt in belts)
        {
            var sets = new List<(string Type, int Qty, bool Souff)>();
            if (!string.IsNullOrWhiteSpace(belt.BeltTypeSoufflage) && belt.QuantitySoufflage > 0)
                sets.Add((belt.BeltTypeSoufflage.Trim(), belt.QuantitySoufflage, true));
            if (!string.IsNullOrWhiteSpace(belt.BeltTypeExtraction) && belt.QuantityExtraction > 0)
                sets.Add((belt.BeltTypeExtraction.Trim(), belt.QuantityExtraction, false));

            foreach (var g in sets.GroupBy(s => s.Type, StringComparer.OrdinalIgnoreCase))
            {
                var qty = g.Sum(s => s.Qty);
                var changed = belt.Replacements.Where(r => r.DateChanged >= since).Sum(r => ChangedForType(r, belt, g));
                perType.TryGetValue(g.Key, out var cur);
                var best = qty > cur.Max ? (qty, belt.Location) : (cur.Max, cur.Centrale);
                perType[g.Key] = (cur.Display ?? g.Key, best.Item1, best.Item2, cur.Changed + changed);
            }
        }

        return perType.Select(kv => new BeltStockRow(kv.Value.Display, kv.Value.Max, kv.Value.Centrale,
                kv.Value.Max * 2, kv.Value.Changed))
            .OrderBy(r => r.BeltType, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Part d'un changement attribuée à un type : tout si un seul côté a été changé, au prorata des
    /// quantités en place si les deux l'ont été (estimation, la base ne détaille pas le côté de chaque courroie).</summary>
    private static int ChangedForType(BeltReplacement r, Belt belt, IEnumerable<(string Type, int Qty, bool Souff)> typeSets)
    {
        var sets = typeSets.ToList();
        var share = 0.0;
        var totalInPlace = (r.ChangedSoufflage ? belt.QuantitySoufflage : 0) + (r.ChangedExtraction ? belt.QuantityExtraction : 0);
        if (totalInPlace == 0) return 0;
        foreach (var s in sets)
        {
            var changed = s.Souff ? r.ChangedSoufflage : r.ChangedExtraction;
            if (changed) share += (double)s.Qty / totalInPlace;
        }
        return (int)Math.Round(r.QuantityChanged * share);
    }

    // ---- Roulements ----

    public static List<BearingYearRow> BearingYearly(FiltresDbContext ctx) =>
        ctx.BearingReplacements.AsNoTracking().Where(r => r.DateChanged != null).ToList()
            .GroupBy(r => r.DateChanged!.Value.Year)
            .Select(g => new BearingYearRow(g.Key, g.Count(), g.Count(r => r.ChangedAvant),
                g.Count(r => r.ChangedArriere), g.Count(r => r.ChangedVolute)))
            .OrderBy(r => r.Year).ToList();

    public static List<IntervalRow> BearingIntervals(FiltresDbContext ctx) =>
        ctx.BearingUnits.AsNoTracking().Include(b => b.Family).Include(b => b.Replacements).ToList()
            .Select(b => Interval(b.Location, b.CentraleType, b.Replacements.Select(r => r.DateChanged)))
            .OrderByDescending(r => r.Changes).ThenBy(r => r.Name).ToList();

    public static List<UnchangedRow> BearingUnchanged(FiltresDbContext ctx, DateOnly today, int years) =>
        ctx.BearingUnits.AsNoTracking().Include(b => b.Replacements).ToList()
            .Select(b => Unchanged(b.Location, b.CentraleType, b.Replacements.Select(r => r.DateChanged), today))
            .Where(r => r.MonthsSince is null || r.MonthsSince >= years * 12)
            .OrderByDescending(r => r.MonthsSince ?? int.MaxValue).ToList();

    // ---- Outils communs ----

    private static IntervalRow Interval(string name, string group, IEnumerable<DateOnly?> dates)
    {
        var sorted = dates.Where(d => d.HasValue).Select(d => d!.Value).Distinct().OrderBy(d => d).ToList();
        var gaps = sorted.Zip(sorted.Skip(1), (a, b) => b.DayNumber - a.DayNumber).ToList();
        return new IntervalRow(name, group, sorted.Count,
            gaps.Count == 0 ? null : gaps.Average(),
            gaps.Count == 0 ? null : gaps.Min(),
            gaps.Count == 0 ? null : gaps.Max(),
            sorted.Count == 0 ? null : sorted[^1]);
    }

    private static UnchangedRow Unchanged(string name, string group, IEnumerable<DateOnly?> dates, DateOnly today)
    {
        var last = dates.Where(d => d.HasValue).Select(d => d!.Value).DefaultIfEmpty().Max();
        if (last == default) return new UnchangedRow(name, group, null, null);
        var months = (today.Year - last.Year) * 12 + today.Month - last.Month;
        return new UnchangedRow(name, group, last, Math.Max(0, months));
    }
}
