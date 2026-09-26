using FiltresApp.Core.Data;
using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Services;

/// <summary>Lectures des lignes de Commande / Inventaire (<see cref="OrderLine"/>).</summary>
public static class OrderLineQueries
{
    /// <summary>Lignes (non suivies) avec leurs filtres rattachés, nécessaires aux familles et aux besoins
    /// calculés.</summary>
    public static List<OrderLine> LoadWithLinks(FiltresDbContext db, OrderDocumentType type) =>
        db.OrderLines
            .Include(l => l.FilterLinks).ThenInclude(fl => fl.PeriodicFilter)
            .Include(l => l.DynamicLinks).ThenInclude(dl => dl.DynamicFilter).ThenInclude(f => f!.Variety)
            .AsNoTracking()
            .Where(l => l.DocumentType == type)
            .ToList();

    /// <summary>Ordre d'une nouvelle ligne : après la dernière.</summary>
    public static int NextOrdre(FiltresDbContext db, OrderDocumentType type) =>
        (db.OrderLines.Where(l => l.DocumentType == type).Max(l => (int?)l.Ordre) ?? 0) + 1;
}
