using FiltresApp.Core.Data;
using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Services;

/// <summary>Enregistrement des remplacements de filtres. Les méthodes enregistrent immédiatement
/// (<c>SaveChanges</c>) et retournent une copie non suivie de l'historique du filtre, à reporter sur l'objet
/// affiché (chargé sans suivi) pour rafraîchir sa ligne sans recharger toute la grille.</summary>
public static class ReplacementTrackingService
{
    /// <summary>Filtre à périodicité : fixe (<paramref name="date"/> non nulle) ou efface (null) le
    /// remplacement du mois donné. Ne touche jamais aux autres mois / années. Une ligne créée reçoit la
    /// quantité <paramref name="quantity"/> ; une ligne existante aussi si <paramref name="refreshQuantity"/>.</summary>
    public static List<FilterReplacement> SetMonthReplacement(FiltresDbContext db, int filterId, int year, int month,
        DateOnly? date, int quantity, bool refreshQuantity)
    {
        var tracked = db.PeriodicFilters.Include(f => f.Replacements).First(f => f.Id == filterId);
        var existing = tracked.Replacements.FirstOrDefault(r => r.Month == month && r.Year == year);

        if (date is null)
        {
            // Pas de ligne "réalisée sans date", ambiguë : la ligne de suivi du mois est supprimée.
            if (existing is not null)
            {
                tracked.Replacements.Remove(existing);
                db.FilterReplacements.Remove(existing);
            }
        }
        else
        {
            if (existing is null)
            {
                existing = new FilterReplacement { Month = month, Year = year, QuantityDone = quantity };
                tracked.Replacements.Add(existing);
            }
            else if (refreshQuantity)
            {
                existing.QuantityDone = quantity;
            }
            existing.DateDone = date;
        }

        db.SaveChanges();
        return tracked.Replacements.Select(r => new FilterReplacement
        {
            Id = r.Id,
            PeriodicFilterId = r.PeriodicFilterId,
            Month = r.Month,
            Year = r.Year,
            QuantityDone = r.QuantityDone,
            DateDone = r.DateDone
        }).ToList();
    }

    /// <summary>Filtre d'une variété "F7 à H14" : ajoute un remplacement daté (quantité en place), sans
    /// jamais modifier l'historique déjà enregistré.</summary>
    public static List<DynamicFilterReplacement> AddDynamicReplacement(FiltresDbContext db, int filterId, DateOnly date)
    {
        var tracked = db.DynamicFilters.Include(f => f.Replacements).First(f => f.Id == filterId);
        tracked.Replacements.Add(new DynamicFilterReplacement { QuantityChanged = tracked.QuantityInPlace, DateChanged = date });
        db.SaveChanges();
        return tracked.Replacements.Select(r => new DynamicFilterReplacement
        {
            Id = r.Id,
            DynamicFilterId = r.DynamicFilterId,
            QuantityChanged = r.QuantityChanged,
            DateChanged = r.DateChanged
        }).ToList();
    }
}
