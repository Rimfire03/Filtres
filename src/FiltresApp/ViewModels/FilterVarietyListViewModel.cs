using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

/// <summary>Page d'accueil du menu dépliant "Filtres F7 à H14" (sélectionnée quand on clique sur le menu
/// parent lui-même, avant de dérouler une variété précise) : liste les variétés créées librement par
/// l'utilisateur et permet d'en ajouter, renommer ou supprimer (uniquement si elle ne contient plus aucun
/// filtre). Chaque variété créée ici apparaît comme sous-menu, avec la même disposition que l'ancien
/// écran unique "Filtres F7 à H13" (voir <see cref="DynamicFilterListViewModel"/>).</summary>
public partial class FilterVarietyListViewModel : ObservableObject, IReloadable
{
    public string Title => "Filtres F7 à H14";

    private readonly MainViewModel _main;

    /// <summary>Boutons de création / renommage / suppression masqués tant que ce réglage (Paramètres,
    /// remis à non à chaque lancement) n'est pas activé : cette page ne sert alors qu'à afficher le
    /// message d'absence de variété, le cas normal (au moins une variété créée) redirigeant
    /// automatiquement vers la première d'entre elles, voir <see cref="MainViewModel.OnSelectedItemChanged"/>.</summary>
    public bool CanManageVarieties => App.Settings.AllowFilterVarietyCreation;
    public bool CannotManageVarieties => !CanManageVarieties;

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
        OnPropertyChanged(nameof(CanManageVarieties));
        OnPropertyChanged(nameof(CannotManageVarieties));
    }

    [RelayCommand]
    private void AddVariety()
    {
        if (!App.GuardWritable()) return;
        if (!App.Settings.AllowFilterVarietyCreation)
        {
            App.Dialogs.ShowMessage("Nouvelle variété",
                "La création de nouvelles variétés de filtres est désactivée. Activez « Autoriser la création de nouvelles variétés de filtres (mode édition) » dans Paramètres pour en créer une.");
            return;
        }
        var entity = new FilterVariety
        {
            Ordre = (App.Db.FilterVarieties.Max(v => (int?)v.Ordre) ?? 0) + 1
        };
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
        if (!EditVarietyFields(tracked, "Modifier la variété")) return;
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

    /// <summary>Nom et ordre d'affichage (position du sous-menu sous "Filtres F7 à H14", plus petit
    /// d'abord) : voir <see cref="MainViewModel.ResortVarietyNavigationItems"/>.</summary>
    private bool EditVarietyFields(FilterVariety variety, string title)
    {
        var fields = new List<EditField>
        {
            EditField.Text("Nom de la variété", () => variety.Nom, v => variety.Nom = v.Trim(), required: true),
            EditField.IntField("Ordre d'affichage", () => variety.Ordre, v => variety.Ordre = v)
        };
        if (!App.Dialogs.EditFields(title, fields)) return false;

        var name = variety.Nom;
        if (App.Db.FilterVarieties.AsNoTracking().Any(v => v.Id != variety.Id && v.Nom.ToLower() == name.ToLower()))
        {
            App.Dialogs.ShowMessage("Variété", $"Une variété « {name} » existe déjà.");
            if (variety.Id != 0) App.Db.Entry(variety).Reload();
            return false;
        }
        return true;
    }
}
