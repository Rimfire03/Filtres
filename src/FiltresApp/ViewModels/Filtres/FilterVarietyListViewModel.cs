using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using FiltresApp.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Page d'accueil du menu dépliant "Filtres F7 à H14" (sélectionnée quand on clique sur le menu
/// parent lui-même, avant de dérouler une variété précise) : liste les variétés créées librement par
/// l'utilisateur et permet d'en ajouter, renommer ou supprimer (uniquement si elle ne contient plus aucun
/// filtre). Chaque variété créée ici apparaît comme sous-menu, avec la même disposition que l'ancien
/// écran unique "Filtres F7 à H13" (voir <see cref="DynamicFilterListViewModel"/>).</summary>
public partial class FilterVarietyListViewModel : ObservableObject, IReloadable
{
    public string Title => _main.FoulingMenuTitle;

    private readonly MainViewModel _main;

    /// <summary>Boutons de création / renommage / suppression masqués tant que ce réglage (Paramètres,
    /// remis à non à chaque lancement) n'est pas activé : cette page ne sert alors qu'à afficher le
    /// message d'absence de variété, le cas normal (au moins une variété créée) redirigeant
    /// automatiquement vers la première d'entre elles, voir <see cref="MainViewModel.OnSelectedItemChanged"/>.</summary>
    public bool CanManageVarieties => App.Settings.AllowFilterVarietyCreation;
    public bool CannotManageVarieties => !CanManageVarieties;

    /// <summary>Bouton "Mode édition" de cette page (rouge = désactivé, vert = activé, voir
    /// <see cref="CanManageVarieties"/>) : autorise la création de nouvelles variétés. Non persisté, toujours
    /// désactivé au lancement de l'application (App.Settings.AllowFilterVarietyCreation).</summary>
    public string EditModeButtonLabel => CanManageVarieties ? "Quitter mode édition" : "Mode édition";

    [ObservableProperty] private ObservableCollection<FilterVariety> _varieties = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditVarietyCommand), nameof(DeleteVarietyCommand))]
    private FilterVariety? _selectedVariety;

    public FilterVarietyListViewModel(MainViewModel main)
    {
        _main = main;
        Load();
    }

    public void Reload() => Load();

    private void Load()
    {
        Varieties = new ObservableCollection<FilterVariety>(
            App.Db.FilterVarieties.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList());
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanManageVarieties));
        OnPropertyChanged(nameof(CannotManageVarieties));
        OnPropertyChanged(nameof(EditModeButtonLabel));
    }

    /// <summary>Bouton "Mode édition" (rouge/vert) : bascule directement le réglage, pas de garde d'écriture
    /// (App.GuardWritable) ni de vérification supplémentaire - c'est une préférence d'affichage locale, pas
    /// une donnée de la base.</summary>
    [RelayCommand]
    private void ToggleEditMode()
    {
        App.Settings.AllowFilterVarietyCreation = !App.Settings.AllowFilterVarietyCreation;
        App.Settings.Save();
        _main.RefreshAfterEditModeChange();
        Load();
    }

    [RelayCommand]
    private void AddVariety()
    {
        if (!App.GuardWritable()) return;
        if (!App.Settings.AllowFilterVarietyCreation)
        {
            App.Dialogs.ShowMessage("Nouvelle variété",
                "La création de nouvelles variétés de filtres est désactivée. Cliquez sur « Mode édition » pour en créer une.");
            return;
        }
        var entity = new FilterVariety();
        if (!EditVarietyFields(entity, "Nouvelle variété de filtre")) return;
        App.Db.FilterVarieties.Add(entity);
        App.Db.SaveChanges();
        Load();
        _main.AddVarietyNavigationItem(entity);
    }

    private bool CanEditVariety() => SelectedVariety is not null;

    [RelayCommand(CanExecute = nameof(CanEditVariety))]
    private void EditVariety()
    {
        if (!App.GuardWritable() || SelectedVariety is null) return;
        var tracked = App.Db.FilterVarieties.First(v => v.Id == SelectedVariety.Id);
        var oldName = tracked.Nom;
        if (!EditVarietyFields(tracked, "Modifier la variété")) return;
        if (!string.Equals(oldName, tracked.Nom, StringComparison.Ordinal)) SettingsViewModel.RenameVarietyInOrderLines(oldName, tracked.Nom);
        App.Db.SaveChanges();
        Load();
        _main.UpdateVarietyNavigationItem(tracked.Id, tracked.Nom);
    }

    private bool CanDeleteVariety() => SelectedVariety is not null;

    [RelayCommand(CanExecute = nameof(CanDeleteVariety))]
    private void DeleteVariety()
    {
        if (!App.GuardWritable() || SelectedVariety is null) return;
        var id = SelectedVariety.Id;
        var count = App.Db.DynamicFilters.Count(f => f.VarietyId == id);
        if (count > 0)
        {
            App.Dialogs.ShowMessage("Supprimer la variété",
                $"Impossible de supprimer « {SelectedVariety.Nom} » : {count} filtre(s) y sont encore rattaché(s). Supprimez-les d'abord.");
            return;
        }

        if (!App.Dialogs.ShowConfirm("Supprimer la variété", $"Supprimer définitivement la variété « {SelectedVariety.Nom} » ?")) return;

        App.Db.DynamicFilterFamilies.Where(f => f.VarietyId == id).ExecuteDelete();
        App.Db.FilterVarieties.Where(v => v.Id == id).ExecuteDelete();
        App.Db.ChangeTracker.Clear();

        Load();
        _main.RemoveVarietyNavigationItem(id);
    }

    /// <summary>Nom et position du sous-menu sous "Filtres F7 à H14" : plutôt qu'un numéro d'ordre brut, un
    /// sélecteur "Placer après" (une variété existante, ou "en premier"). La position choisie est ensuite
    /// traduite en <see cref="FilterVariety.Ordre"/> pour toutes les variétés par <see cref="ApplyOrdering"/>.</summary>
    private bool EditVarietyFields(FilterVariety variety, string title)
    {
        var ordered = App.Db.FilterVarieties.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList();
        var others = ordered.Where(v => v.Id != variety.Id).ToList();

        var positionLabels = new List<string> { "(en premier)" };
        positionLabels.AddRange(others.Select(o => $"Après « {o.Nom} »"));

        // Position actuelle : juste après son prédécesseur direct dans l'ordre existant (dernière position
        // pour une variété toute nouvelle, qui n'a pas encore de prédécesseur/successeur).
        int? currentAfterId;
        if (variety.Id == 0) currentAfterId = others.Count > 0 ? others[^1].Id : null;
        else
        {
            var selfIndex = ordered.FindIndex(v => v.Id == variety.Id);
            currentAfterId = selfIndex > 0 ? ordered[selfIndex - 1].Id : null;
        }
        var currentPositionIndex = currentAfterId is int afterId ? others.FindIndex(o => o.Id == afterId) + 1 : 0;

        int? chosenAfterId = currentAfterId;
        var fields = new List<EditField>
        {
            EditField.Text("Nom de la variété", () => variety.Nom, v => variety.Nom = v.Trim(), required: true),
            EditField.ComboField("Placer après", positionLabels, () => currentPositionIndex,
                v => chosenAfterId = v >= 1 && v <= others.Count ? others[v - 1].Id : null)
        };
        if (!App.Dialogs.EditFields(title, fields)) return false;

        var name = variety.Nom;
        if (MenuNames.IsTaken(name, excludedVarietyId: variety.Id))
        {
            App.Dialogs.ShowMessage("Variété", $"Une variété « {name} » existe déjà.");
            if (variety.Id != 0) App.Db.Entry(variety).Reload();
            return false;
        }

        ApplyOrdering(variety, chosenAfterId);
        return true;
    }

    /// <summary>Repositionne <paramref name="variety"/> juste après <paramref name="afterVarietyId"/> (ou
    /// en premier si null) et renumérote séquentiellement (0, 1, 2...) l'ensemble des variétés, y compris
    /// celles non modifiées : leur <see cref="FilterVariety.Ordre"/> est mis à jour sur les entités suivies
    /// par le contexte, à sauvegarder par l'appelant avec <paramref name="variety"/> (même SaveChanges).</summary>
    private static void ApplyOrdering(FilterVariety variety, int? afterVarietyId)
    {
        var others = App.Db.FilterVarieties.Where(v => v.Id != variety.Id).OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList();
        var insertIndex = afterVarietyId is int id ? others.FindIndex(v => v.Id == id) + 1 : 0;
        if (insertIndex < 0) insertIndex = others.Count;
        others.Insert(insertIndex, variety);
        for (var i = 0; i < others.Count; i++) others[i].Ordre = i;
    }
}
