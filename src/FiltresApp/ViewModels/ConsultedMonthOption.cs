namespace FiltresApp.ViewModels;

/// <summary>Un mois du sélecteur "Mois consulté" (écrans de filtres) : porte son propre numéro d'année,
/// puisque la première option (Décembre de l'année précédente) n'appartient pas à l'année choisie dans la
/// barre latérale.</summary>
public record ConsultedMonthOption(int Month, int Year, string Label)
{
    public static readonly string[] MonthLabels =
        { "Janvier", "Février", "Mars", "Avril", "Mai", "Juin", "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre" };

    public bool Contains(DateOnly date) => date.Year == Year && date.Month == Month;

    /// <summary>Options pour l'année choisie : Décembre de l'année précédente (pratique pour finir de pointer
    /// un changement fait fin décembre une fois basculé sur la nouvelle année), puis Janvier à Décembre.</summary>
    public static List<ConsultedMonthOption> ForYear(int year)
    {
        var options = new List<ConsultedMonthOption> { new(12, year - 1, $"Décembre {year - 1}") };
        options.AddRange(Enumerable.Range(1, 12).Select(m => new ConsultedMonthOption(m, year, $"{MonthLabels[m - 1]} {year}")));
        return options;
    }

    /// <summary>Reconstruit les options pour <paramref name="year"/> en gardant la même position dans la liste
    /// (donc le même mois "relatif") que <paramref name="previous"/> ; par défaut, le mois du jour.</summary>
    public static (List<ConsultedMonthOption> Options, ConsultedMonthOption Selected) Rebuild(
        int year, IList<ConsultedMonthOption> previousOptions, ConsultedMonthOption? previous)
    {
        var index = previous is null ? -1 : previousOptions.IndexOf(previous);
        if (index < 0) index = DateTime.Today.Month;

        var options = ForYear(year);
        return (options, options[Math.Clamp(index, 0, options.Count - 1)]);
    }
}
