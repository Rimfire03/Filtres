using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Choix du filtre "Famille" d'un écran <see cref="TrackedItemListViewModel{TEntity, TFamily, TRow}"/> :
/// toutes les familles, sans famille, ou une famille précise.</summary>
public record FamilyOption(int? FamilyId, bool IsNoFamily, string Label)
{
    public bool IsAll => FamilyId is null && !IsNoFamily;
}

/// <summary>
/// Écran commun aux modules "Filtres F7 à H14" (une variété), "Courroies" et "Roulements" : éléments rangés
/// par familles créées à la main, historique de remplacements ponctuels sans périodicité mensuelle fixe.
/// Porte ce qui est identique d'un module à l'autre - mode édition, familles (voir
/// TrackedItemListViewModel.Families.cs), Ajouter / Modifier / Dupliquer / Supprimer, couleur de ligne,
/// "Consulter l'historique..." ; chaque module fournit ses requêtes, son formulaire d'édition, sa saisie d'un
/// remplacement et son impression.
/// </summary>
/// <remarks>Les requêtes filtrant sur la famille restent dans chaque module : la clé de famille n'est
/// exposée par <see cref="IFamilyTrackedItem"/> qu'en implémentation explicite, que EF Core ne sait pas
/// traduire en SQL.</remarks>
public abstract partial class TrackedItemListViewModel<TEntity, TFamily, TRow> : ObservableObject, IReloadable, ITrackedItemOwner<TEntity>
    where TEntity : class, IFamilyTrackedItem
    where TFamily : class, INamedFamily
    where TRow : TrackedItemRowViewModel<TEntity>
{
    public abstract string Title { get; }

    /// <summary>L'année consultée ne filtre pas des lignes de suivi mensuel (pas de périodicité fixe) : elle
    /// restreint "Dernier changement" / "Nb remplacements" aux remplacements datés de cette année-là ;
    /// l'historique complet reste toujours en base.</summary>
    public YearContext YearContext => App.YearContext;

    [ObservableProperty] private ObservableCollection<TRow> _rows = new();
    [ObservableProperty] private TRow? _selectedRow;

    /// <summary>Mode édition (bouton "Mode édition" / "Quitter le mode édition") : masque par défaut les
    /// boutons Ajouter/Modifier/Supprimer pour éviter les modifications accidentelles sur le terrain (le
    /// pointage courant, lui, reste toujours accessible). Propre à cet écran, remis à false à chaque
    /// ouverture.</summary>
    [ObservableProperty] private bool _isEditMode;

    public string EditModeButtonLabel => IsEditMode ? "Quitter le mode édition" : "Mode édition";

    partial void OnIsEditModeChanged(bool value) => OnPropertyChanged(nameof(EditModeButtonLabel));

    [RelayCommand]
    private void ToggleEditMode() => IsEditMode = !IsEditMode;

    /// <summary>Premier chargement, à appeler à la fin du constructeur du module (une fois ses propres
    /// champs initialisés, dont dépendent ses requêtes).</summary>
    protected void Initialize()
    {
        RefreshFamilies();
        Load();
    }

    /// <summary>Rechargé à chaque ouverture de l'écran : familles et données peuvent avoir changé ailleurs.</summary>
    public virtual void Reload()
    {
        RefreshFamilies();
        Load();
    }

    /// <summary>Éléments de la famille choisie (toutes, sans famille, ou une précise), familles par nom et
    /// "Sans famille" en dernier : ordre des séparateurs de la grille. Chargés sans suivi, historique de
    /// remplacements et famille inclus.</summary>
    protected abstract List<TEntity> LoadEntities(FamilyOption family);

    protected abstract TRow CreateRow(TEntity entity);

    protected virtual void Load() =>
        Rows = new ObservableCollection<TRow>(LoadEntities(SelectedFamily ?? AllFamilies).Select(CreateRow));

    /// <summary>Titre d'impression : celui de l'écran, suivi de la famille filtrée le cas échéant.</summary>
    protected string PrintTitle => SelectedFamily is null || SelectedFamily.IsAll ? Title : $"{Title} - {SelectedFamily.Label}";

    // ---- Ajouter / Modifier / Dupliquer / Supprimer ----

    /// <summary>Nouvel élément, rangé par défaut dans la famille filtrée (<paramref name="familyId"/>).</summary>
    protected abstract TEntity CreateEntity(int? familyId);

    /// <summary>Copie d'un élément, sans son historique de remplacements (ni son rattachement à Commande /
    /// Inventaire).</summary>
    protected abstract TEntity CopyEntity(TEntity source);

    /// <summary>Formulaire d'ajout / modification ; false si annulé.</summary>
    protected abstract bool EditEntity(TEntity entity, bool isNew);

    [RelayCommand]
    private void Add()
    {
        if (!App.GuardWritable()) return;
        var entity = CreateEntity(SelectedFamily?.FamilyId);
        if (!EditEntity(entity, true)) return;
        App.Db.Set<TEntity>().Add(entity);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void Edit()
    {
        if (!App.GuardWritable()) return;
        if (SelectedRow is null) return;
        var tracked = App.Db.Set<TEntity>().Find(SelectedRow.Id)!;
        if (!EditEntity(tracked, false)) return;
        App.Db.SaveChanges();
        Load();
    }

    /// <summary>Copie la ligne sélectionnée et ouvre directement son édition. N'enregistre rien si l'édition
    /// est annulée.</summary>
    [RelayCommand]
    private void Duplicate()
    {
        if (!App.GuardWritable()) return;
        if (SelectedRow is null) return;
        var copy = CopyEntity(SelectedRow.Entity);
        if (!EditEntity(copy, true)) return;
        App.Db.Set<TEntity>().Add(copy);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void Delete()
    {
        if (!App.GuardWritable()) return;
        if (SelectedRow is null) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer définitivement '{SelectedRow.Location}' ?")) return;
        var tracked = App.Db.Set<TEntity>().Find(SelectedRow.Id)!;
        App.Db.Set<TEntity>().Remove(tracked);
        App.Db.SaveChanges();
        Load();
    }

    // ---- Actions déléguées par les lignes (ITrackedItemOwner) ----

    /// <summary>Colonne "Date du changement" : enregistre un nouveau remplacement daté de
    /// <paramref name="date"/>, sans jamais modifier l'historique déjà enregistré, et le reporte sur
    /// <paramref name="entity"/> (chargé sans suivi) pour rafraîchir sa ligne sans recharger la grille.</summary>
    public abstract bool AddReplacement(TEntity entity, DateOnly date);

    /// <summary>Couleur de ligne (menu contextuel de la grille) : sauvegarde immédiate en base.</summary>
    public void SetRowColor(TEntity entity, int? colorId)
    {
        if (!App.GuardWritable()) return;
        var tracked = App.Db.Set<TEntity>().Find(entity.Id)!;
        tracked.RowColorId = colorId;
        App.Db.SaveChanges();
        entity.RowColorId = colorId;
    }

    /// <summary>Ouvre la fenêtre "Consulter l'historique..." de cet élément (voir
    /// <see cref="IDialogService.ShowReplacementHistory{TReplacement}"/>).</summary>
    protected abstract void OpenHistory(TEntity entity);

    /// <summary>"Consulter l'historique..." (menu contextuel de la grille). La grille principale est
    /// rechargée à la fermeture pour refléter une éventuelle suppression ("Dernier changement" / "Nb
    /// remplacements" de l'année).</summary>
    public void ShowHistory(TEntity entity)
    {
        OpenHistory(entity);
        Reload();
    }
}
