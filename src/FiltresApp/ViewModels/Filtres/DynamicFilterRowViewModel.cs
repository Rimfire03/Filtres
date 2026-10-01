using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>
/// Habille un <see cref="DynamicFilter"/> (variété créée sous le menu "Filtres F7 à H14", sans
/// périodicité mensuelle fixe) pour restreindre l'affichage "Dernier changement" / "Nb remplacements" à
/// l'année consultée, sans jamais toucher à l'historique complet conservé en base.
/// </summary>
public partial class DynamicFilterRowViewModel : LinkedFilterRowViewModel
{
    private readonly DynamicFilterListViewModel _owner;
    public DynamicFilter Filter { get; }
    private readonly int _year;

    public DynamicFilterRowViewModel(DynamicFilter filter, int year, DynamicFilterListViewModel owner, string? linkedOrderLine)
        : base(linkedOrderLine)
    {
        Filter = filter;
        _year = year;
        _owner = owner;
    }

    /// <summary>"Consulter l'historique..." (menu contextuel de la grille) : voir
    /// <see cref="DynamicFilterListViewModel.ShowHistory"/>.</summary>
    [RelayCommand]
    private void ShowHistory() => _owner.ShowHistory(Filter);

    public int Id => Filter.Id;
    public string FamilyGroupLabel => Filter.FamilyGroupLabel;
    public string Location => Filter.Location;
    public string Dimension => Filter.Dimension;
    public string? FilterType => Filter.FilterType;

    /// <summary>Appelé après sauvegarde par <see cref="DynamicFilterListViewModel.SetFilterType"/>
    /// (menu rapide au clic droit) : met à jour la valeur affichée sans recharger toute la grille.</summary>
    public void ApplyFilterType(string? newType)
    {
        Filter.FilterType = newType;
        OnPropertyChanged(nameof(FilterType));
    }
    public int QuantityInPlace => Filter.QuantityInPlace;
    public string? Commentaire => Filter.Commentaire;

    // ---- Date de changement : pas de mois à cocher, une date saisie enregistre un nouveau remplacement ----

    private DateTime? _newChangeDate;

    /// <summary>Colonne "Date du changement" : champ de saisie (toujours vide au repos, pas l'affichage
    /// d'une valeur existante) - choisir une date enregistre un nouveau remplacement daté de ce jour puis
    /// se réinitialise. Voir <see cref="DynamicFilterListViewModel.AddReplacement"/>.</summary>
    public DateTime? NewChangeDate
    {
        get => _newChangeDate;
        set
        {
            if (value is { } picked)
            {
                var saved = _owner.AddReplacement(Filter, DateOnly.FromDateTime(picked));
                RefreshHistoryColumns();
                if (saved) TriggerSavedFlash();
                _newChangeDate = null;
                // Réinitialisation différée après ce tour de message : la remettre à null tout de suite,
                // dans le même appel que celui déclenché par le DatePicker lui-même suite à la sélection
                // d'une date, perturbe son état interne (popup/TextBox) et empêchait parfois toute saisie
                // suivante de s'enregistrer.
                Application.Current.Dispatcher.BeginInvoke(() => OnPropertyChanged(), DispatcherPriority.Background);
            }
            else
            {
                _newChangeDate = null;
                OnPropertyChanged();
            }
        }
    }

    public void RefreshHistoryColumns()
    {
        OnPropertyChanged(nameof(LastChangedDateInYear));
        OnPropertyChanged(nameof(ReplacementCountInYear));
    }

    private IEnumerable<DynamicFilterReplacement> ReplacementsForYear =>
        Filter.Replacements.Where(r => r.DateChanged.HasValue && r.DateChanged.Value.Year == _year);

    public DateOnly? LastChangedDateInYear => ReplacementsForYear
        .Select(r => r.DateChanged!.Value)
        .OrderByDescending(d => d)
        .Cast<DateOnly?>()
        .FirstOrDefault();

    public int ReplacementCountInYear => ReplacementsForYear.Count();

    /// <summary>Bref flash vert de la ligne (voir DynamicFilterView.xaml, DataTrigger sur IsFlashing) pour
    /// confirmer visuellement l'enregistrement d'un changement, sans attendre un rechargement de la grille.</summary>
    [ObservableProperty] private bool _isFlashing;

    private async void TriggerSavedFlash()
    {
        IsFlashing = true;
        await System.Threading.Tasks.Task.Delay(500);
        IsFlashing = false;
    }

    /// <summary>Couleur de ligne (menu contextuel, voir RowColorMenu) : sauvegarde immédiate en base via
    /// <see cref="DynamicFilterListViewModel.SetRowColor"/>.</summary>
    public int? RowColorId
    {
        get => Filter.RowColorId;
        set
        {
            _owner.SetRowColor(Filter, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowColorBrush));
        }
    }

    public Brush? RowColorBrush => RowColorPalette.BrushFor(RowColorId);
}
