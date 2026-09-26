using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

/// <summary>Écran "Commande" : rattachement des filtres aux lignes (fenêtre complète et menu rapide).</summary>
public partial class OrderListViewModel
{
    /// <summary>Ouvre le sélecteur de rattachement manuel filtre <-> ligne de commande : liste, par
    /// défaut, uniquement les filtres à périodicité (G4 plissé, G4 plan, G3, Charbon) dont la dimension
    /// correspond à celle de la ligne de commande (comparaison tolérante, voir
    /// <see cref="DimensionMatchService"/>), avec catégorie/emplacement/dimension pour identification, et
    /// coche ceux déjà rattachés. Signale en rouge les filtres déjà rattachés à une AUTRE ligne. Le besoin
    /// (mars/septembre) est recalculé automatiquement à l'affichage à partir du rattachement enregistré.
    /// L'enregistrement applique la règle d'exclusivité (un filtre = une seule ligne à la fois), voir
    /// <see cref="FilterLinkService"/>.</summary>
    [RelayCommand]
    private void LinkFilters()
    {
        if (SelectedLine is not null) OpenLinkWindow(SelectedLine);
    }

    /// <summary>Filtre rattachable (à périodicité ou d'une variété "Filtres F7 à H14"), vu depuis une
    /// ligne de commande.</summary>
    private sealed record LinkCandidate(FilterRef Ref, string Category, string Location, string Dimension,
        bool IsLinked, string? LinkedElsewhere, bool DimensionMatches, Func<FilterPickItem> ToPickItem);

    /// <summary>Tous les filtres rattachables pour <paramref name="line"/> (chargée avec ses rattachements),
    /// dans l'ordre catégorie puis emplacement, filtres "F7 à H14" en dernier (groupés par variété).</summary>
    private List<LinkCandidate> LoadLinkCandidates(OrderLine line)
    {
        // Filtres déjà rattachés à une AUTRE ligne : indicateur rouge / mention "déjà rattaché à".
        var periodicElsewhere = FilterLinkService.PeriodicLinkedLines(App.Db, excludedLineId: line.Id);

        var linkedPeriodic = line.FilterLinks.Select(l => l.PeriodicFilterId).ToHashSet();
        var linkedDynamic = line.DynamicLinks.Select(l => l.DynamicFilterId).ToHashSet();

        var candidates = new List<LinkCandidate>();
        foreach (var f in App.Db.PeriodicFilters.AsNoTracking().OrderBy(f => f.Category).ThenBy(f => f.Location).ToList())
        {
            // Filtres lavables ("à laver", G3) : jamais rattachables, exclus des candidats (voir
            // PeriodicFilterRowViewModel.IsWashable, qui masque aussi leur puce "Lié").
            if (f.IsWashable) continue;

            var category = OrderLine.FamilyLabelFor(f.Category);
            var isLinked = linkedPeriodic.Contains(f.Id);
            var elsewhere = periodicElsewhere.GetValueOrDefault(f.Id);
            var matches = DimensionMatchService.Matches(line, f);
            candidates.Add(new LinkCandidate(FilterRef.Periodic(f.Id), category, f.Location, f.Dimension, isLinked, elsewhere, matches,
                () => new FilterPickItem(f, category, isLinked, matches, elsewhere)));
        }
        var dynamicElsewhere = FilterLinkService.DynamicLinkedLines(App.Db, excludedLineId: line.Id);
        foreach (var f in App.Db.DynamicFilters.AsNoTracking().Include(f => f.Variety).OrderBy(f => f.Variety!.Nom).ThenBy(f => f.Location).ToList())
        {
            var elsewhere = dynamicElsewhere.GetValueOrDefault(f.Id);
            var isLinked = linkedDynamic.Contains(f.Id);
            var matches = DimensionMatchService.Matches(line, f.Dimension);
            var category = f.Variety?.Nom ?? "";
            candidates.Add(new LinkCandidate(FilterRef.Dynamic(f.Id), category, f.Location, f.Dimension, isLinked, elsewhere, matches,
                () => new FilterPickItem(f, category, isLinked, matches, elsewhere)));
        }
        return candidates;
    }

    /// <summary>Remplace les rattachements de la ligne, enregistre et recharge l'écran.</summary>
    private void SaveLinks(OrderLine line, IEnumerable<FilterRef> selected)
    {
        var tracked = App.Db.OrderLines.Include(l => l.FilterLinks).Include(l => l.DynamicLinks).First(l => l.Id == line.Id);
        FilterLinkService.SetLinks(App.Db, tracked, selected);
        App.Db.SaveChanges();
        Load();
        SelectedLine = Lines.FirstOrDefault(l => l.Id == line.Id);
    }

    /// <summary>Fenêtre complète "Rattacher des filtres..." pour une ligne (bouton, ou menu rapide).</summary>
    public void OpenLinkWindow(OrderLine line)
    {
        if (!App.GuardWritable()) return;
        SelectedLine = line;

        var items = LoadLinkCandidates(line).Select(c => c.ToPickItem()).ToList();
        var selected = App.Dialogs.PickFilterLinks(items);
        if (selected is null) return;
        SaveLinks(line, selected);
    }

    public record QuickLinkOption(FilterRef Ref, string Label, bool IsLinked);

    /// <summary>Nombre maximal de filtres de dimension correspondante proposés dans le menu rapide.</summary>
    public const int QuickLinkMaxSuggestions = 20;

    /// <summary>Menu rapide (clic droit sur "Filtres liés") : filtres déjà rattachés à la ligne, puis
    /// filtres de dimension correspondante (même comparaison approximative que la fenêtre complète),
    /// limités à <see cref="QuickLinkMaxSuggestions"/>. Le second élément indique combien de suggestions
    /// ont été omises.</summary>
    public (List<QuickLinkOption> Options, int Omitted) GetQuickLinkOptions(OrderLine line)
    {
        var candidates = LoadLinkCandidates(line);

        static string Label(LinkCandidate c)
        {
            var label = $"{c.Category} — {c.Location} ({c.Dimension})";
            return c.LinkedElsewhere is null ? label : $"{label} — déjà rattaché à « {c.LinkedElsewhere} »";
        }

        var options = candidates.Where(c => c.IsLinked).Select(c => new QuickLinkOption(c.Ref, Label(c), true)).ToList();
        var suggestions = candidates.Where(c => !c.IsLinked && c.DimensionMatches).ToList();
        options.AddRange(suggestions.Take(QuickLinkMaxSuggestions).Select(c => new QuickLinkOption(c.Ref, Label(c), false)));
        return (options, Math.Max(0, suggestions.Count - QuickLinkMaxSuggestions));
    }

    /// <summary>Coche / décoche un filtre depuis le menu rapide. Un filtre rattaché à une autre ligne lui
    /// est retiré (un filtre = une seule ligne), comme dans la fenêtre complète.</summary>
    public void SetQuickLink(OrderLine line, FilterRef filter, bool link)
    {
        if (!App.GuardWritable()) return;
        var selected = line.FilterLinks.Select(l => FilterRef.Periodic(l.PeriodicFilterId))
            .Concat(line.DynamicLinks.Select(l => FilterRef.Dynamic(l.DynamicFilterId)))
            .ToHashSet();
        if (link) selected.Add(filter);
        else selected.Remove(filter);
        SaveLinks(line, selected);
    }
}
