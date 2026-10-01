using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Habille un <see cref="BearingUnit"/> (module "Roulements") pour restreindre l'affichage
/// "Dernier changement" / "Nb remplacements" à l'année consultée. Pas de rattachement Commande / Inventaire
/// pour ce module (pas de pastille "Lié").</summary>
public partial class BearingRowViewModel : ObservableObject
{
    private readonly BearingListViewModel _owner;
    public BearingUnit Bearing { get; }
    private readonly int _year;

    public BearingRowViewModel(BearingUnit bearing, int year, BearingListViewModel owner)
    {
        Bearing = bearing;
        _year = year;
        _owner = owner;
    }

    [RelayCommand]
    private void ShowHistory() => _owner.ShowHistory(Bearing);

    public int Id => Bearing.Id;
    public string FamilyGroupLabel => Bearing.FamilyGroupLabel;
    public string Location => Bearing.Location;
    public string CentraleType => Bearing.CentraleType;

    /// <summary>"Entraînement direct" : pas de roulement volute (voir VoluteCellStyle, case noire dans la
    /// grille, et BearingListViewModel qui force la référence à null et retire "Volute" de la sélection à
    /// la saisie d'un changement).</summary>
    public bool IsDirectDrive => Bearing.CentraleType == BearingUnit.CentraleTypeOptions[1];

    public string? RefAvant => Bearing.RefAvant;
    public string? RefArriere => Bearing.RefArriere;
    public string? RefVolute => Bearing.RefVolute;
    public string? Commentaire => Bearing.Commentaire;

    // ---- Date de changement : une date saisie ouvre la sélection des roulements changés (voir
    // BearingListViewModel.AddReplacement) avant d'enregistrer un nouveau remplacement ----

    private DateTime? _newChangeDate;

    public DateTime? NewChangeDate
    {
        get => _newChangeDate;
        set
        {
            if (value is { } picked)
            {
                var saved = _owner.AddReplacement(Bearing, DateOnly.FromDateTime(picked));
                RefreshHistoryColumns();
                if (saved) TriggerSavedFlash();
                _newChangeDate = null;
                // Réinitialisation différée après ce tour de message : la remettre à null tout de suite,
                // dans le même appel que celui déclenché par le DatePicker lui-même, perturbe son état
                // interne (popup/TextBox) et empêche parfois toute saisie suivante de s'enregistrer.
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

    private IEnumerable<BearingReplacement> ReplacementsForYear =>
        Bearing.Replacements.Where(r => r.DateChanged.HasValue && r.DateChanged.Value.Year == _year);

    public DateOnly? LastChangedDateInYear => ReplacementsForYear
        .Select(r => r.DateChanged!.Value)
        .OrderByDescending(d => d)
        .Cast<DateOnly?>()
        .FirstOrDefault();

    public int ReplacementCountInYear => ReplacementsForYear.Count();

    /// <summary>Bref flash vert de la ligne (voir BearingView.xaml, DataTrigger sur IsFlashing) pour
    /// confirmer visuellement l'enregistrement d'un changement.</summary>
    [ObservableProperty] private bool _isFlashing;

    private async void TriggerSavedFlash()
    {
        IsFlashing = true;
        await System.Threading.Tasks.Task.Delay(500);
        IsFlashing = false;
    }

    public int? RowColorId
    {
        get => Bearing.RowColorId;
        set
        {
            _owner.SetRowColor(Bearing, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowColorBrush));
        }
    }

    public Brush? RowColorBrush => RowColorPalette.BrushFor(RowColorId);
}
