namespace FiltresApp.Core.Models;

/// <summary>Famille de lieux de la "Liste K7" (ex. "Urgences", "AP RDC", "LITS PORTES"...), avec sa
/// propre périodicité de remplacement. Dans le classeur Excel d'origine, la feuille "Liste K7" n'avait
/// pas de notion de famille distincte : le nom de la famille et sa périodicité étaient simplement
/// concaténés dans une ligne de la colonne LIEU (ex. "AP  RDC periodicités  1/4/7/10"), qui servait à la
/// fois de titre de section et de ligne de décompte. Voir le README, section "Familles K7 et migration"
/// pour le détail de la reconstitution de ces familles à partir des données déjà importées.</summary>
public class K7Family
{
    public int Id { get; set; }

    public string Nom { get; set; } = string.Empty;

    /// <summary>Liste des mois (1-12) de la périodicité, même format que <see cref="PeriodicFilter.Periodicity"/>
    /// ("/1/4/7/10/"). Peut être vide si la périodicité n'a pas pu être extraite du texte d'origine.</summary>
    public string Periodicite { get; set; } = string.Empty;

    public List<K7Location> Locations { get; set; } = new();

    public List<int> GetPeriodicityMonths()
    {
        if (string.IsNullOrWhiteSpace(Periodicite)) return new List<int>();
        return Periodicite.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, out var m) ? m : 0)
            .Where(m => m is >= 1 and <= 12)
            .Distinct()
            .OrderBy(m => m)
            .ToList();
    }

    public static string FormatPeriodicityMonths(IEnumerable<int> months)
    {
        var distinct = months.Where(m => m is >= 1 and <= 12).Distinct().OrderBy(m => m).ToList();
        return distinct.Count == 0 ? string.Empty : "/" + string.Join("/", distinct) + "/";
    }

    private static string MonthShortName(int month) => month switch
    {
        1 => "Jan", 2 => "Fév", 3 => "Mar", 4 => "Avr", 5 => "Mai", 6 => "Jun",
        7 => "Jul", 8 => "Aoû", 9 => "Sep", 10 => "Oct", 11 => "Nov", 12 => "Déc",
        _ => "?"
    };

    public string PeriodicityDisplay => string.Join(", ", GetPeriodicityMonths().Select(MonthShortName));
}
