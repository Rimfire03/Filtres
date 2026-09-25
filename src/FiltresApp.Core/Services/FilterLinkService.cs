using FiltresApp.Core.Data;
using FiltresApp.Core.Models;

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
    /// <see cref="OrderLine.FilterLinks"/> chargé) par l'ensemble <paramref name="selectedFilterIds"/> :
    /// retire les filtres décochés, ajoute les filtres nouvellement cochés. Pour chaque filtre
    /// nouvellement rattaché, si celui-ci était déjà rattaché à une autre ligne de commande, cet ancien
    /// rattachement est supprimé au préalable (un filtre = un seul rattachement actif à la fois).
    /// N'appelle pas <c>SaveChanges</c> : à la charge de l'appelant.</summary>
    public static void SetLinks(FiltresDbContext db, OrderLine trackedLine, IEnumerable<int> selectedFilterIds)
    {
        var selectedSet = selectedFilterIds.ToHashSet();
        var currentIds = trackedLine.FilterLinks.Select(l => l.PeriodicFilterId).ToHashSet();

        foreach (var link in trackedLine.FilterLinks.Where(l => !selectedSet.Contains(l.PeriodicFilterId)).ToList())
        {
            trackedLine.FilterLinks.Remove(link);
            db.OrderLinePeriodicFilters.Remove(link);
        }

        var newlySelectedIds = selectedSet.Where(id => !currentIds.Contains(id)).ToList();
        if (newlySelectedIds.Count == 0) return;

        var existingLinksElsewhere = db.OrderLinePeriodicFilters
            .Where(l => newlySelectedIds.Contains(l.PeriodicFilterId) && l.OrderLineId != trackedLine.Id)
            .ToList();
        if (existingLinksElsewhere.Count > 0)
            db.OrderLinePeriodicFilters.RemoveRange(existingLinksElsewhere);

        foreach (var filterId in newlySelectedIds)
            trackedLine.FilterLinks.Add(new OrderLinePeriodicFilter { OrderLineId = trackedLine.Id, PeriodicFilterId = filterId });
    }
}
