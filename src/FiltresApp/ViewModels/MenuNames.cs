using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

/// <summary>Unicité des titres de menus du module Filtre : une vue « Changement filtre périodique », une
/// variété « Changement sur encrassement » ou un menu parent ne peuvent pas porter le même titre (ils servent
/// aussi de familles dans Commande / Inventaire), ni un libellé réservé.</summary>
public static class MenuNames
{
    private static readonly string[] Reserved =
    {
        OrderLine.NoFamilyLabel, OrderLine.MultipleFamiliesLabel, "Toutes les familles", OrderLine.AutomaticFamilyChoice
    };

    public static bool IsTaken(string name, int? excludedPeriodicViewId = null, int? excludedVarietyId = null, string? excludedMenuKey = null)
    {
        var n = name.Trim().ToLowerInvariant();
        if (n.Length == 0) return true;
        if (Reserved.Any(r => r.ToLowerInvariant() == n)) return true;

        if (App.Db.PeriodicViews.AsNoTracking().Any(v => v.Id != (excludedPeriodicViewId ?? -1) && v.Nom.ToLower() == n)) return true;
        if (App.Db.FilterVarieties.AsNoTracking().Any(v => v.Id != (excludedVarietyId ?? -1) && v.Nom.ToLower() == n)) return true;
        return App.Db.MenuEntries.AsNoTracking().Any(m => m.Key != (excludedMenuKey ?? "") && m.Title.ToLower() == n);
    }
}
