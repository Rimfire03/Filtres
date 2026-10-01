using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Habille un <see cref="Belt"/> (module "Courroies") pour restreindre l'affichage "Dernier
/// changement" / "Nb remplacements" à l'année consultée, sans jamais toucher à l'historique complet
/// conservé en base. Pas de rattachement Commande / Inventaire pour ce module (pas de pastille "Lié").</summary>
public partial class BeltRowViewModel : ObservableObject
{
    private readonly BeltListViewModel _owner;
    public Belt Belt { get; }
    private readonly int _year;

    public BeltRowViewModel(Belt belt, int year, BeltListViewModel owner)
    {
        Belt = belt;
        _year = year;
        _owner = owner;
    }

    [RelayCommand]
    private void ShowHistory() => _owner.ShowHistory(Belt);

    public int Id => Belt.Id;
    public string FamilyGroupLabel => Belt.FamilyGroupLabel;
    public string Location => Belt.Location;
    public string? BeltType => Belt.BeltType;
    public int QuantityInPlace => Belt.QuantityInPlace;
    public string? Commentaire => Belt.Commentaire;

    // ---- Date de changement : pas de mois à cocher, une date saisie enregistre un nouveau remplacement ----

    private DateTime? _newChangeDate;

    public DateTime? NewChangeDate
    {
        get => _newChangeDate;
        set
        {
            if (value is { } picked)
            {
                var saved = _owner.AddReplacement(Belt, DateOnly.FromDateTime(picked));
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

    private IEnumerable<BeltReplacement> ReplacementsForYear =>
        Belt.Replacements.Where(r => r.DateChanged.HasValue && r.DateChanged.Value.Year == _year);

    public DateOnly? LastChangedDateInYear => ReplacementsForYear
        .Select(r => r.DateChanged!.Value)
        .OrderByDescending(d => d)
        .Cast<DateOnly?>()
        .FirstOrDefault();

    public int ReplacementCountInYear => ReplacementsForYear.Count();

    /// <summary>Bref flash vert de la ligne (voir BeltView.xaml, DataTrigger sur IsFlashing) pour confirmer
    /// visuellement l'enregistrement d'un changement.</summary>
    [ObservableProperty] private bool _isFlashing;

    private async void TriggerSavedFlash()
    {
        IsFlashing = true;
        await System.Threading.Tasks.Task.Delay(500);
        IsFlashing = false;
    }

    public int? RowColorId
    {
        get => Belt.RowColorId;
        set
        {
            _owner.SetRowColor(Belt, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowColorBrush));
        }
    }

    public Brush? RowColorBrush => RowColorPalette.BrushFor(RowColorId);
}
