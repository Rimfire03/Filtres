using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Actions qu'une ligne de grille délègue à son écran (voir <see cref="TrackedItemListViewModel{TEntity, TFamily, TRow}"/>),
/// qui seul accède à la base.</summary>
public interface ITrackedItemOwner<in TEntity>
{
    /// <summary>Enregistre un remplacement daté ; false si rien n'a été enregistré (lecture seule, saisie
    /// annulée...).</summary>
    bool AddReplacement(TEntity entity, DateOnly date);

    void SetRowColor(TEntity entity, int? colorId);

    void ShowHistory(TEntity entity);
}

/// <summary>
/// Ligne de grille commune aux écrans "Filtres F7 à H14", "Courroies" et "Roulements" : habille un élément
/// suivi sans périodicité mensuelle fixe pour restreindre l'affichage "Dernier changement" / "Nb
/// remplacements" à l'année consultée, sans jamais toucher à l'historique complet conservé en base. Saisie
/// d'une date de changement (avec flash vert de confirmation), couleur de ligne et "Consulter
/// l'historique..." sont délégués à l'écran (<see cref="ITrackedItemOwner{TEntity}"/>). Dérive de
/// <see cref="LinkedFilterRowViewModel"/> pour la puce "Lié" des filtres F7 à H14 ; les modules sans
/// rattachement Commande / Inventaire la masquent (<see cref="LinkedFilterRowViewModel.ShowsLinkDot"/>).
/// </summary>
public abstract partial class TrackedItemRowViewModel<TEntity> : LinkedFilterRowViewModel
    where TEntity : IFamilyTrackedItem
{
    private readonly ITrackedItemOwner<TEntity> _owner;
    private readonly int _year;

    public TEntity Entity { get; }

    protected TrackedItemRowViewModel(TEntity entity, int year, ITrackedItemOwner<TEntity> owner, string? linkedOrderLine = null)
        : base(linkedOrderLine)
    {
        Entity = entity;
        _year = year;
        _owner = owner;
    }

    /// <summary>"Consulter l'historique..." (menu contextuel de la grille).</summary>
    [RelayCommand]
    private void ShowHistory() => _owner.ShowHistory(Entity);

    public int Id => Entity.Id;
    public string FamilyGroupLabel => Entity.FamilyGroupLabel;
    public string Location => Entity.Location;
    public string? Commentaire => Entity.Commentaire;

    // ---- Date de changement : pas de mois à cocher, une date saisie enregistre un nouveau remplacement ----

    private DateTime? _newChangeDate;

    /// <summary>Colonne "Date du changement" : champ de saisie (toujours vide au repos, pas l'affichage
    /// d'une valeur existante) - choisir une date enregistre un nouveau remplacement daté de ce jour puis
    /// se réinitialise. Voir <see cref="ITrackedItemOwner{TEntity}.AddReplacement"/>.</summary>
    public DateTime? NewChangeDate
    {
        get => _newChangeDate;
        set
        {
            if (value is { } picked)
            {
                var saved = _owner.AddReplacement(Entity, DateOnly.FromDateTime(picked));
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

    private IEnumerable<IDatedReplacement> ReplacementsForYear =>
        Entity.DatedReplacements.Where(r => r.DateChanged.HasValue && r.DateChanged.Value.Year == _year);

    public DateOnly? LastChangedDateInYear => ReplacementsForYear
        .Select(r => r.DateChanged!.Value)
        .OrderByDescending(d => d)
        .Cast<DateOnly?>()
        .FirstOrDefault();

    public int ReplacementCountInYear => ReplacementsForYear.Count();

    /// <summary>Bref flash vert de la ligne (voir FlashingCellStyle dans Styles/Controls.xaml : fondu
    /// d'apparition au passage à true, fondu de disparition au retour à false) pour confirmer visuellement
    /// l'enregistrement d'un changement, sans attendre un rechargement de la grille.</summary>
    [ObservableProperty] private bool _isFlashing;

    private async void TriggerSavedFlash()
    {
        IsFlashing = true;
        await System.Threading.Tasks.Task.Delay(500);
        IsFlashing = false;
    }

    /// <summary>Couleur de ligne (menu contextuel, voir RowColorMenu) : sauvegarde immédiate en base via
    /// <see cref="ITrackedItemOwner{TEntity}.SetRowColor"/>.</summary>
    public int? RowColorId
    {
        get => Entity.RowColorId;
        set
        {
            _owner.SetRowColor(Entity, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(RowColorBrush));
        }
    }

    public Brush? RowColorBrush => RowColorPalette.BrushFor(RowColorId);
}
