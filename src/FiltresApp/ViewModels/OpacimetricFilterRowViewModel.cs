using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;

namespace FiltresApp.ViewModels;

/// <summary>
/// Habille un <see cref="OpacimetricFilter"/> (feuille F7 à H13, sans périodicité mensuelle fixe) pour
/// restreindre l'affichage "Dernier changement" / "Nb remplacements" à l'année consultée, sans jamais
/// toucher à l'historique complet conservé en base.
/// </summary>
public partial class OpacimetricFilterRowViewModel : ObservableObject
{
    private readonly OpacimetricFilterListViewModel _owner;
    public OpacimetricFilter Filter { get; }
    private readonly int _year;

    public OpacimetricFilterRowViewModel(OpacimetricFilter filter, int year, OpacimetricFilterListViewModel owner)
    {
        Filter = filter;
        _year = year;
        _owner = owner;
    }

    /// <summary>"Consulter l'historique..." (menu contextuel de la grille) : voir
    /// <see cref="OpacimetricFilterListViewModel.ShowHistory"/>.</summary>
    [RelayCommand]
    private void ShowHistory() => _owner.ShowHistory(Filter);

    public int Id => Filter.Id;
    public string Location => Filter.Location;
    public string Dimension => Filter.Dimension;
    public string? FilterType => Filter.FilterType;
    public int QuantityInPlace => Filter.QuantityInPlace;

    private IEnumerable<OpacimetricReplacement> ReplacementsForYear =>
        Filter.Replacements.Where(r => r.DateChanged.HasValue && r.DateChanged.Value.Year == _year);

    public DateOnly? LastChangedDateInYear => ReplacementsForYear
        .Select(r => r.DateChanged!.Value)
        .OrderByDescending(d => d)
        .Cast<DateOnly?>()
        .FirstOrDefault();

    public int ReplacementCountInYear => ReplacementsForYear.Count();
}
