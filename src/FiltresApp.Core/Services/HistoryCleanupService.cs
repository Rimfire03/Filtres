using FiltresApp.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Services;

/// <summary>Suppression de l'historique des remplacements d'une année, en deux fonctions séparées :
/// filtres à périodicité (G4 plissé, G4 plan, G3, Charbon) d'un côté, filtres F7 à H13 de l'autre.
/// Les filtres eux-mêmes ne sont jamais supprimés.</summary>
public static class HistoryCleanupService
{
    /// <summary>Années ayant au moins un remplacement enregistré (G4 plissé, G4 plan, G3, Charbon).</summary>
    public static List<int> GetPeriodicYears(FiltresDbContext ctx) =>
        ctx.FilterReplacements.AsNoTracking().Select(r => r.Year).Distinct().OrderByDescending(y => y).ToList();

    public static int CountPeriodic(FiltresDbContext ctx, int year) =>
        ctx.FilterReplacements.Count(r => r.Year == year);

    public static int DeletePeriodicYear(FiltresDbContext ctx, int year) =>
        ctx.FilterReplacements.Where(r => r.Year == year).ExecuteDelete();

    /// <summary>Années ayant au moins un remplacement daté (filtres F7 à H13).</summary>
    public static List<int> GetOpacimetricYears(FiltresDbContext ctx) =>
        ctx.OpacimetricReplacements.AsNoTracking()
            .Where(r => r.DateChanged != null)
            .Select(r => r.DateChanged!.Value.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

    public static int CountOpacimetric(FiltresDbContext ctx, int year) =>
        ctx.OpacimetricReplacements.Count(r => r.DateChanged != null && r.DateChanged.Value.Year == year);

    public static int DeleteOpacimetricYear(FiltresDbContext ctx, int year) =>
        ctx.OpacimetricReplacements.Where(r => r.DateChanged != null && r.DateChanged.Value.Year == year).ExecuteDelete();
}
