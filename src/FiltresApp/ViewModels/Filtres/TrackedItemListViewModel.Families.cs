using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Familles d'un écran <see cref="TrackedItemListViewModel{TEntity, TFamily, TRow}"/> : créées à la
/// main, attribuées à chaque élément, filtre d'affichage de l'écran.</summary>
public abstract partial class TrackedItemListViewModel<TEntity, TFamily, TRow>
{
    protected static readonly FamilyOption AllFamilies = new(null, false, "Toutes les familles");
    private static readonly FamilyOption NoFamily = new(null, true, INamedFamily.NoFamilyLabel);

    public List<TFamily> Families { get; private set; } = new();
    [ObservableProperty] private List<FamilyOption> _familyOptions = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFamilySelected))]
    [NotifyCanExecuteChangedFor(nameof(RenameFamilyCommand), nameof(DeleteFamilyCommand))]
    private FamilyOption? _selectedFamily;

    public bool IsFamilySelected => SelectedFamily?.FamilyId is not null;

    private bool _refreshingFamilies;

    partial void OnSelectedFamilyChanged(FamilyOption? value)
    {
        if (!_refreshingFamilies) Load();
    }

    /// <summary>Familles de l'écran, triées par nom, sans suivi.</summary>
    protected abstract List<TFamily> LoadFamilies();

    /// <summary>Nouvelle famille vide (rattachée le cas échéant à la variété de l'écran).</summary>
    protected abstract TFamily CreateFamily();

    /// <summary>Une autre famille de l'écran que <paramref name="excludedId"/> porte-t-elle déjà ce nom (sans
    /// tenir compte de la casse) ?</summary>
    protected abstract bool FamilyNameExists(string name, int excludedId);

    protected abstract int CountItemsInFamily(int familyId);

    /// <summary>Supprime la famille ; ses éléments ne sont pas supprimés mais passent en "Sans famille".</summary>
    protected abstract void DeleteFamilyKeepingItems(int familyId);

    /// <summary>Désignation des éléments au pluriel, pour les messages ("courroies", "filtres"...).</summary>
    protected abstract string ItemsLabel { get; }

    /// <summary>Précision du message de confirmation de suppression d'une famille contenant
    /// <paramref name="count"/> éléments (accordée selon le module).</summary>
    protected abstract string FamilyDeleteDetail(int count);

    /// <summary>Titre de la fenêtre de création d'une famille.</summary>
    protected virtual string NewFamilyTitle => $"Nouvelle famille « {Title} »";

    /// <summary>Choix "Famille" du formulaire d'édition d'un élément : "Sans famille" puis chaque famille ;
    /// <paramref name="setFamilyId"/> reçoit l'identifiant choisi (null pour "Sans famille").</summary>
    protected EditField FamilyField(int? currentFamilyId, Action<int?> setFamilyId)
    {
        var families = Families;
        var familyNames = new List<string> { INamedFamily.NoFamilyLabel };
        familyNames.AddRange(families.Select(f => f.Nom));
        var familyIndex = currentFamilyId is int id ? families.FindIndex(f => f.Id == id) + 1 : 0;
        return EditField.ComboField("Famille", familyNames, () => familyIndex,
            v => setFamilyId(v >= 1 && v <= families.Count ? families[v - 1].Id : null));
    }

    /// <summary>Choix de la colonne "Famille" de la grille : "Sans famille" puis chaque famille (relus à chaque
    /// rechargement des familles).</summary>
    public List<string> FamilyQuickChoices => new List<string> { INamedFamily.NoFamilyLabel }.Concat(Families.Select(f => f.Nom)).ToList();

    /// <summary>Enregistre en base la famille de l'élément (null = sans famille). À fournir par les modules qui
    /// proposent la colonne "Famille" (Courroies, Roulements).</summary>
    protected virtual void SaveEntityFamily(int entityId, int? familyId) => throw new NotSupportedException();

    /// <summary>Reporte la famille sur l'élément affiché (chargé sans suivi) sans recharger la grille.</summary>
    protected virtual void ApplyFamilyInMemory(TEntity entity, TFamily? family) => throw new NotSupportedException();

    /// <summary>Colonne "Famille" : change la famille de la ligne en un clic, sans ouvrir "Modifier". Ne recharge
    /// jamais toute la liste (la grille sauterait tout en haut) : la ligne est retirée puis réinsérée au même
    /// index pour que le regroupement la range dans sa nouvelle famille, ou simplement retirée si elle sort du
    /// filtre "Famille" affiché (même technique que la colonne Famille de Commande).</summary>
    public void SetFamilyChoice(TEntity entity, string choice)
    {
        if (choice == entity.FamilyGroupLabel || !App.GuardWritable()) return;
        var family = choice == INamedFamily.NoFamilyLabel ? null : Families.FirstOrDefault(f => f.Nom == choice);
        if (family is null && choice != INamedFamily.NoFamilyLabel) return;

        SaveEntityFamily(entity.Id, family?.Id);
        ApplyFamilyInMemory(entity, family);

        var row = _allRows.FirstOrDefault(r => ReferenceEquals(r.Entity, entity));
        if (row is null) return;

        var selected = SelectedFamily ?? AllFamilies;
        var stillShown = selected.IsAll || (selected.IsNoFamily ? family is null : selected.FamilyId == family?.Id);
        var index = Rows.IndexOf(row);
        if (!stillShown)
        {
            _allRows.Remove(row);
            if (index >= 0) Rows.RemoveAt(index);
            return;
        }
        if (index < 0) return;
        Rows.RemoveAt(index);
        Rows.Insert(index, row);
        SelectedRow = row;
    }

    /// <summary>Relit les familles en conservant le filtre choisi s'il existe toujours.</summary>
    private void RefreshFamilies(int? selectFamilyId = null)
    {
        Families = LoadFamilies();
        OnPropertyChanged(nameof(FamilyQuickChoices));
        var options = new List<FamilyOption> { AllFamilies, NoFamily };
        options.AddRange(Families.Select(f => new FamilyOption(f.Id, false, f.Nom)));

        var previous = SelectedFamily;
        _refreshingFamilies = true;
        try
        {
            FamilyOptions = options;
            SelectedFamily = selectFamilyId is int id
                ? options.First(o => o.FamilyId == id)
                : options.FirstOrDefault(o => previous is not null && o.FamilyId == previous.FamilyId && o.IsNoFamily == previous.IsNoFamily)
                  ?? AllFamilies;
        }
        finally
        {
            _refreshingFamilies = false;
        }
    }

    [RelayCommand]
    private void AddFamily()
    {
        if (!App.GuardWritable()) return;
        var family = CreateFamily();
        if (!EditFamilyName(family, NewFamilyTitle)) return;
        App.Db.Set<TFamily>().Add(family);
        App.Db.SaveChanges();
        RefreshFamilies(family.Id);
        Load();
    }

    [RelayCommand(CanExecute = nameof(IsFamilySelected))]
    private void RenameFamily()
    {
        if (!App.GuardWritable() || SelectedFamily?.FamilyId is not int id) return;
        var tracked = App.Db.Set<TFamily>().Find(id)!;
        if (!EditFamilyName(tracked, "Renommer la famille")) return;
        App.Db.SaveChanges();
        RefreshFamilies(id);
        Load();
    }

    /// <summary>Les éléments de la famille ne sont pas supprimés : ils passent en "Sans famille".</summary>
    [RelayCommand(CanExecute = nameof(IsFamilySelected))]
    private void DeleteFamily()
    {
        if (!App.GuardWritable() || SelectedFamily?.FamilyId is not int id) return;
        var count = CountItemsInFamily(id);
        var detail = count == 0 ? "" : "\n\n" + FamilyDeleteDetail(count);
        if (!App.Dialogs.ShowConfirm("Supprimer la famille", $"Supprimer la famille « {SelectedFamily.Label} » ?{detail}")) return;

        DeleteFamilyKeepingItems(id);
        // Les éléments éventuellement suivis par le contexte gardent l'ancienne famille en mémoire.
        App.Db.ChangeTracker.Clear();

        RefreshFamilies();
        Load();
    }

    private bool EditFamilyName(TFamily family, string title)
    {
        var fields = new List<EditField>
        {
            EditField.Text("Nom de la famille", () => family.Nom, v => family.Nom = v.Trim(), required: true)
        };
        if (!App.Dialogs.EditFields(title, fields)) return false;

        var name = family.Nom;
        string? error = null;
        if (string.Equals(name, INamedFamily.NoFamilyLabel, StringComparison.OrdinalIgnoreCase))
            error = $"« {INamedFamily.NoFamilyLabel} » est réservé aux {ItemsLabel} sans famille : choisissez un autre nom.";
        else if (FamilyNameExists(name, family.Id))
            error = $"Une famille « {name} » existe déjà.";

        if (error is null) return true;
        App.Dialogs.ShowMessage("Famille", error);
        // Le renommage refusé ne doit pas rester en mémoire sur l'entité suivie.
        if (family.Id != 0) App.Db.Entry(family).Reload();
        return false;
    }
}
