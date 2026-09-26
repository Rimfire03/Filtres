using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;

namespace FiltresApp.ViewModels;

/// <summary>
/// Habille un <see cref="OpacimetricFilter"/> (feuille F7 à H13, sans périodicité mensuelle fixe) pour
/// restreindre l'affichage "Dernier changement" / "Nb remplacements" à l'année consultée, sans jamais
/// toucher à l'historique complet conservé en base.
/// </summary>
public partial class OpacimetricFilterRowViewModel : LinkedFilterRowViewModel
{
    private readonly OpacimetricFilterListViewModel _owner;
    public OpacimetricFilter Filter { get; }
    private readonly int _year;

    public OpacimetricFilterRowViewModel(OpacimetricFilter filter, int year, OpacimetricFilterListViewModel owner, string? linkedOrderLine)
        : base(linkedOrderLine)
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
    public string FamilyGroupLabel => Filter.FamilyGroupLabel;
    public string Location => Filter.Location;
    public string Dimension => Filter.Dimension;
    public string? FilterType => Filter.FilterType;

    /// <summary>Appelé après sauvegarde par <see cref="OpacimetricFilterListViewModel.SetFilterType"/>
    /// (menu rapide au clic droit) : met à jour la valeur affichée sans recharger toute la grille.</summary>
    public void ApplyFilterType(string? newType)
    {
        Filter.FilterType = newType;
        OnPropertyChanged(nameof(FilterType));
    }
    public int QuantityInPlace => Filter.QuantityInPlace;

    // ---- Mois consulté : case "Réalisé" et "Date du changement" ----

    private ConsultedMonthOption? ConsultedMonth => _owner.SelectedConsultedMonth;

    /// <summary>Dernier remplacement daté dans le mois consulté, s'il y en a un.</summary>
    private OpacimetricReplacement? ReplacementInConsultedMonth => ConsultedMonth is not { } m
        ? null
        : Filter.Replacements
            .Where(r => r.DateChanged is DateOnly d && m.Contains(d))
            .OrderBy(r => r.DateChanged)
            .LastOrDefault();

    public bool IsDoneForConsultedMonth
    {
        get => ReplacementInConsultedMonth is not null;
        set
        {
            _owner.SetReplacementDone(Filter, value);
            RefreshConsultedMonth();
        }
    }

    public DateTime? DateDoneForConsultedMonth
    {
        get => ReplacementInConsultedMonth?.DateChanged?.ToDateTime(TimeOnly.MinValue);
        set
        {
            _owner.SetReplacementDate(Filter, value.HasValue ? DateOnly.FromDateTime(value.Value) : null);
            RefreshConsultedMonth();
        }
    }

    /// <summary>Bornes du calendrier de la colonne "Date du changement" : le mois consulté.</summary>
    public DateTime? ConsultedMonthStart => ConsultedMonth is { } m ? new DateTime(m.Year, m.Month, 1) : null;
    public DateTime? ConsultedMonthEnd => ConsultedMonthStart?.AddMonths(1).AddDays(-1);

    public void RefreshConsultedMonth()
    {
        OnPropertyChanged(nameof(IsDoneForConsultedMonth));
        OnPropertyChanged(nameof(DateDoneForConsultedMonth));
        OnPropertyChanged(nameof(ConsultedMonthStart));
        OnPropertyChanged(nameof(ConsultedMonthEnd));
        OnPropertyChanged(nameof(LastChangedDateInYear));
        OnPropertyChanged(nameof(ReplacementCountInYear));
    }

    private IEnumerable<OpacimetricReplacement> ReplacementsForYear =>
        Filter.Replacements.Where(r => r.DateChanged.HasValue && r.DateChanged.Value.Year == _year);

    public DateOnly? LastChangedDateInYear => ReplacementsForYear
        .Select(r => r.DateChanged!.Value)
        .OrderByDescending(d => d)
        .Cast<DateOnly?>()
        .FirstOrDefault();

    public int ReplacementCountInYear => ReplacementsForYear.Count();
}
