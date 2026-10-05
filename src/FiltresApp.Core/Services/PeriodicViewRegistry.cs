using FiltresApp.Core.Data;
using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Services;

/// <summary>Vues du menu « Changement filtre périodique » telles que connues de l'application (titre courant,
/// catégorie, calcul du besoin), lues une fois à l'ouverture de la base puis relues après chaque modification
/// de vue (<see cref="Load"/>). Sert aux libellés de famille de Commande / Inventaire (<see cref="OrderLine"/>),
/// qui ne doivent plus coder les titres en dur puisqu'ils sont modifiables.</summary>
public static class PeriodicViewRegistry
{
    /// <summary>Choix manuel de famille « vue créée par l'utilisateur » : <see cref="OrderLine.FamilyOverride"/> =
    /// ce nombre + l'identifiant de la vue (les vues d'origine gardent leur numéro de catégorie 0 à 3).</summary>
    public const int ViewOverrideBase = 1000;

    public sealed record Entry(int Id, string Title, FilterCategory? Category, bool ComputesNeed);

    private static readonly string[] DefaultTitles = { "Filtres G4 plissés", "Filtres G4 plan", "Filtres G3", "Charbon" };

    private static List<Entry> _entries = Defaults();

    private static List<Entry> Defaults() => DefaultTitles
        .Select((t, i) => new Entry(-(i + 1), t, (FilterCategory)i, (FilterCategory)i != FilterCategory.Charbon))
        .ToList();

    public static IReadOnlyList<Entry> Entries => _entries;

    public static void Load(FiltresDbContext db)
    {
        var views = db.PeriodicViews.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList();
        _entries = views.Count == 0
            ? Defaults()
            : views.Select(v => new Entry(v.Id, v.Nom, v.Category, v.ComputesNeed)).ToList();
    }

    /// <summary>Titre courant de la vue d'origine <paramref name="category"/>.</summary>
    public static string TitleFor(FilterCategory category) =>
        _entries.FirstOrDefault(e => e.Category == category)?.Title
        ?? ((int)category < DefaultTitles.Length ? DefaultTitles[(int)category] : category.ToString());

    public static string? TitleForView(int? viewId) =>
        viewId is int id ? _entries.FirstOrDefault(e => e.Id == id)?.Title : null;

    /// <summary>Famille de Commande / Inventaire d'un filtre : titre de sa vue.</summary>
    public static string LabelFor(PeriodicFilter filter) =>
        TitleForView(filter.PeriodicViewId) ?? TitleFor(filter.Category);

    /// <summary>Vrai si <paramref name="familyLabel"/> est le titre d'une vue dont le besoin est calculé.</summary>
    public static bool IsComputedTitle(string familyLabel) =>
        _entries.Any(e => e.ComputesNeed && string.Equals(e.Title, familyLabel, StringComparison.CurrentCultureIgnoreCase));
}
