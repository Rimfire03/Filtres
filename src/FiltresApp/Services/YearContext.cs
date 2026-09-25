using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FiltresApp.Services;

/// <summary>
/// Contexte d'année partagé par tous les écrans de suivi (remplace l'ancienne "remise à zéro
/// annuelle" : au lieu d'effacer l'historique, on change simplement l'année consultée, et
/// l'historique de toutes les années précédentes reste en base). Instance unique exposée par
/// <see cref="FiltresApp.App.YearContext"/>.
/// </summary>
public partial class YearContext : ObservableObject
{
    [ObservableProperty] private int _year;

    public ObservableCollection<int> AvailableYears { get; } = new();

    public YearContext(int currentYear, IEnumerable<int> yearsInData)
    {
        var years = new SortedSet<int> { currentYear, currentYear + 1 };
        foreach (var y in yearsInData) years.Add(y);
        // Garde toujours quelques années passées disponibles même si aucune donnée n'y est encore rattachée.
        years.Add(currentYear - 1);

        foreach (var y in years) AvailableYears.Add(y);
        _year = currentYear;
    }

    /// <summary>Garantit que l'année est bien présente dans la liste déroulante (ex. après un import
    /// qui aurait rapatrié des données sur une année non encore listée).</summary>
    public void EnsureYear(int year)
    {
        if (!AvailableYears.Contains(year))
        {
            AvailableYears.Add(year);
            var sorted = AvailableYears.OrderBy(y => y).ToList();
            AvailableYears.Clear();
            foreach (var y in sorted) AvailableYears.Add(y);
        }
    }
}
