using FiltresApp.Core.Data;
using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Services;

/// <summary>
/// Applique le rattachement manuel filtre &lt;-&gt; ligne de commande choisi dans le sélecteur
/// (<see cref="Views.Dialogs.FilterLinkWindow"/> côté WPF), en imposant la règle d'exclusivité : un
/// filtre périodique (<see cref="PeriodicFilter"/>) ne peut être rattaché qu'à <b>une seule</b> ligne de
/// commande (<see cref="OrderLine"/>) à la fois (relation en pratique one-to-many : une ligne de commande
/// peut avoir plusieurs filtres, mais un filtre n'appartient qu'à une seule ligne).
/// <para>La table de liaison many-to-many <see cref="OrderLinePeriodicFilter"/> est conservée telle
/// quelle en base (pas de migration de schéma destructrice) : la contrainte d'exclusivité est appliquée
/// ici, au niveau applicatif, avant chaque enregistrement - en supprimant tout rattachement existant d'un
/// filtre nouvellement sélectionné vers une AUTRE ligne avant de créer le nouveau.</para>
/// </summary>
public static class FilterLinkService
{
    /// <summary>Remplace le rattachement de <paramref name="trackedLine"/> (suivie par le contexte, avec
    /// <see cref="OrderLine.FilterLinks"/> chargés) par l'ensemble <paramref name="selected"/> : retire les
    /// filtres décochés, ajoute les filtres nouvellement cochés. Un filtre nouvellement rattaché qui
    /// l'était à une autre ligne lui est retiré (un filtre = un seul rattachement actif à la fois).
    /// N'appelle pas <c>SaveChanges</c> : à la charge de l'appelant.</summary>
    public static void SetLinks(FiltresDbContext db, OrderLine trackedLine, IEnumerable<FilterRef> selected)
    {
        var selectedList = selected.ToList();
        var periodicIds = selectedList.Where(r => r.Kind == FilterKind.Periodic).Select(r => r.Id).ToHashSet();
        var dynamicIds = selectedList.Where(r => r.Kind == FilterKind.Dynamic).Select(r => r.Id).ToHashSet();

        var currentPeriodic = trackedLine.FilterLinks.Select(l => l.PeriodicFilterId).ToHashSet();
        foreach (var link in trackedLine.FilterLinks.Where(l => !periodicIds.Contains(l.PeriodicFilterId)).ToList())
        {
            trackedLine.FilterLinks.Remove(link);
            db.OrderLinePeriodicFilters.Remove(link);
        }
        var newPeriodic = periodicIds.Where(id => !currentPeriodic.Contains(id)).ToList();
        if (newPeriodic.Count > 0)
        {
            db.OrderLinePeriodicFilters.RemoveRange(db.OrderLinePeriodicFilters
                .Where(l => newPeriodic.Contains(l.PeriodicFilterId) && l.OrderLineId != trackedLine.Id));
            foreach (var id in newPeriodic)
                trackedLine.FilterLinks.Add(new OrderLinePeriodicFilter { OrderLineId = trackedLine.Id, PeriodicFilterId = id });
        }

        var currentDynamic = trackedLine.DynamicLinks.Select(l => l.DynamicFilterId).ToHashSet();
        foreach (var link in trackedLine.DynamicLinks.Where(l => !dynamicIds.Contains(l.DynamicFilterId)).ToList())
        {
            trackedLine.DynamicLinks.Remove(link);
            db.OrderLineDynamicFilters.Remove(link);
        }
        var newDynamic = dynamicIds.Where(id => !currentDynamic.Contains(id)).ToList();
        if (newDynamic.Count > 0)
        {
            db.OrderLineDynamicFilters.RemoveRange(db.OrderLineDynamicFilters
                .Where(l => newDynamic.Contains(l.DynamicFilterId) && l.OrderLineId != trackedLine.Id));
            foreach (var id in newDynamic)
                trackedLine.DynamicLinks.Add(new OrderLineDynamicFilter { OrderLineId = trackedLine.Id, DynamicFilterId = id });
        }
    }

    /// <summary>Pour chaque filtre à périodicité rattaché, la dimension (Designation) de sa ligne de
    /// Commande / Inventaire. Optionnel : limité à une catégorie, ou hors d'une ligne donnée.</summary>
    public static Dictionary<int, string> PeriodicLinkedLines(FiltresDbContext db, FilterCategory? category = null, int? excludedLineId = null)
    {
        var links = db.OrderLinePeriodicFilters.AsNoTracking();
        if (category is FilterCategory c) links = links.Where(l => l.PeriodicFilter!.Category == c);
        if (excludedLineId is int id) links = links.Where(l => l.OrderLineId != id);
        return links.Select(l => new { l.PeriodicFilterId, l.OrderLine!.Designation })
            .ToList()
            .GroupBy(l => l.PeriodicFilterId)
            .ToDictionary(g => g.Key, g => g.First().Designation);
    }

    /// <summary>Même chose pour les filtres d'une variété donnée (menu "Filtres F7 à H14").</summary>
    public static Dictionary<int, string> DynamicLinkedLines(FiltresDbContext db, int varietyId, int? excludedLineId = null)
    {
        var links = db.OrderLineDynamicFilters.AsNoTracking().Where(l => l.DynamicFilter!.VarietyId == varietyId);
        if (excludedLineId is int id) links = links.Where(l => l.OrderLineId != id);
        return links.Select(l => new { l.DynamicFilterId, l.OrderLine!.Designation })
            .ToList()
            .GroupBy(l => l.DynamicFilterId)
            .ToDictionary(g => g.Key, g => g.First().Designation);
    }
}
