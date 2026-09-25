using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;

namespace FiltresApp.ViewModels;

/// <summary>
/// Habille un <see cref="PeriodicFilter"/> pour l'affichage en grille : expose en plus l'état
/// "changement réalisé" / date pour le mois en cours et l'année consultée (case à cocher de la
/// grille), en déléguant la lecture/écriture en base au ViewModel parent.
/// </summary>
public partial class PeriodicFilterRowViewModel : ObservableObject
{
    private readonly PeriodicFilterListViewModel _owner;

    public PeriodicFilter Filter { get; }

    public PeriodicFilterRowViewModel(PeriodicFilter filter, PeriodicFilterListViewModel owner, string? linkedOrderLine)
    {
        Filter = filter;
        _owner = owner;
        LinkedOrderLine = linkedOrderLine;
    }

    /// <summary>Dimension de la ligne de Commande / Inventaire à laquelle ce filtre est rattaché, ou null.</summary>
    public string? LinkedOrderLine { get; }

    /// <summary>Puce verte (rattaché) ou rouge (non rattaché) en tête de ligne.</summary>
    public bool IsLinkedToOrder => LinkedOrderLine is not null;

    public string LinkToolTip => IsLinkedToOrder
        ? $"Rattaché à la ligne « {LinkedOrderLine} » de Commande / Inventaire"
        : "Rattaché à aucune ligne de Commande / Inventaire";

    /// <summary>True si la Dimension contient "laver" (filtre lavable, réutilisé plutôt que remplacé) :
    /// ces filtres ne peuvent jamais être rattachés à une ligne de Commande / Inventaire (voir
    /// OrderListViewModel.LoadLinkCandidates, qui les exclut des candidats), donc la fonction "Lié" n'a
    /// pas de sens pour eux et est masquée dans la grille.</summary>
    public bool IsWashable => Filter.IsWashable;

    /// <summary>Famille déduite de la Dimension pour le regroupement de la grille (voir
    /// <see cref="PeriodicFilterListViewModel.ShowDimensionFamilyGrouping"/>, écran "Filtres G3"
    /// uniquement) : chaîne vide sur les autres écrans, pour qu'aucun bandeau de groupe ne s'affiche
    /// (voir PeriodicFilterView.xaml, en-tête de groupe masqué quand le nom est vide).</summary>
    public string DimensionFamilyLabel
    {
        get
        {
            return _owner.ShowDimensionFamilyGrouping ? Filter.DimensionFamilyLabel : "";
        }
    }

    /// <summary>Ordre d'affichage des groupes de <see cref="DimensionFamilyLabel"/> (voir
    /// PeriodicFilterView.xaml, SortDescription sur la grille groupée) : filtres à remplacer en premier.</summary>
    public int DimensionFamilyRank => Filter.DimensionFamilyRank;

    public int Id => Filter.Id;
    public string Location => Filter.Location;
    public string Dimension => Filter.Dimension;
    public string MediaType => Filter.MediaType;
    public int QuantityInPlace => Filter.QuantityInPlace;
    public string PeriodicityDisplay => Filter.PeriodicityDisplay;
    public string? K7Reference => Filter.K7Reference;
    public int? HourCounter => Filter.HourCounter;
    public DateOnly? NextDueDate => Filter.NextDueDate;
    public DateOnly? LastDoneDate => Filter.LastDoneDate;

    /// <summary>Visible uniquement pour l'écran "Filtres G4 plissés" (voir
    /// <see cref="PeriodicFilterListViewModel.ShowChangedEvery15DaysOption"/>).</summary>
    public bool ShowChangedEvery15DaysOption => _owner.ShowChangedEvery15DaysOption;

    /// <summary>Option "Changé tous les 15 jours" : bascule au clic dans le menu contextuel de la
    /// grille, sauvegarde immédiate en base, et double le besoin calculé pour ce filtre (voir
    /// OrderNeedCalculationService).</summary>
    public bool ChangedEvery15Days
    {
        get => Filter.ChangedEvery15Days;
        set
        {
            _owner.SetChangedEvery15Days(Filter, value);
            OnPropertyChanged();
        }
    }

    private FilterReplacement? CurrentReplacement => _owner.SelectedConsultedMonth is { } consulted
        ? Filter.Replacements.FirstOrDefault(r => r.Month == consulted.Month && r.Year == consulted.Year)
        : null;

    /// <summary>Case à cocher "Changement réalisé" pour le mois consulté (voir
    /// <see cref="PeriodicFilterListViewModel.SelectedConsultedMonth"/>). Cocher fixe automatiquement la
    /// date du jour (éditable ensuite via <see cref="DateDoneForConsultedMonth"/>) ; décocher réinitialise
    /// le statut et vide la date (choix délibéré : on ne veut pas garder une ligne "réalisée sans date",
    /// ambiguë).</summary>
    public bool IsDoneForConsultedMonth
    {
        get => CurrentReplacement?.DateDone is not null;
        set
        {
            _owner.SetReplacementDone(Filter, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(DateDoneForConsultedMonth));
            OnPropertyChanged(nameof(NextDueDate));
            OnPropertyChanged(nameof(LastDoneDate));
        }
    }

    public DateTime? DateDoneForConsultedMonth
    {
        get => CurrentReplacement?.DateDone?.ToDateTime(TimeOnly.MinValue);
        set
        {
            _owner.SetReplacementDate(Filter, value.HasValue ? DateOnly.FromDateTime(value.Value) : null);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDoneForConsultedMonth));
            OnPropertyChanged(nameof(NextDueDate));
            OnPropertyChanged(nameof(LastDoneDate));
        }
    }

    /// <summary>"Consulter l'historique..." (menu contextuel de la grille, toutes catégories) : ouvre une
    /// fenêtre de lecture seule pour ce filtre (voir <see cref="PeriodicFilterListViewModel.ShowHistory"/>).</summary>
    [RelayCommand]
    private void ShowHistory() => _owner.ShowHistory(Filter);

    public void RefreshAll()
    {
        OnPropertyChanged(nameof(IsDoneForConsultedMonth));
        OnPropertyChanged(nameof(DateDoneForConsultedMonth));
        OnPropertyChanged(nameof(NextDueDate));
        OnPropertyChanged(nameof(LastDoneDate));
    }
}
