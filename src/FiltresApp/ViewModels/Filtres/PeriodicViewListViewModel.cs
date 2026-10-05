using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Page d'accueil du menu dépliant « Changement filtre périodique » (affichée quand on clique sur le
/// menu parent avec le mode édition activé) : liste les vues (G4 plissés, G4 plan, G3, Charbon et celles créées
/// par l'utilisateur) et permet d'en ajouter, modifier (titre, icône, libellé de colonne, position) ou
/// supprimer (uniquement une vue créée, et seulement si elle ne contient plus aucun filtre). Même
/// fonctionnement que <see cref="FilterVarietyListViewModel"/> pour le menu « Changement sur encrassement ».</summary>
public partial class PeriodicViewListViewModel : ObservableObject, IReloadable
{
    private readonly MainViewModel _main;

    public string Title => _main.PeriodicMenuTitle;

    /// <summary>Boutons de gestion masqués tant que le mode édition (remis à non à chaque lancement) n'est pas
    /// activé : le menu redirige alors directement vers la première vue, voir
    /// <see cref="MainViewModel.OnSelectedItemChanged"/>.</summary>
    public bool CanManageViews => App.Settings.AllowPeriodicViewCreation;
    public bool CannotManageViews => !CanManageViews;
    public string EditModeButtonLabel => CanManageViews ? "Quitter mode édition" : "Mode édition";

    [ObservableProperty] private ObservableCollection<PeriodicView> _views = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditViewCommand), nameof(DeleteViewCommand))]
    private PeriodicView? _selectedView;

    public PeriodicViewListViewModel(MainViewModel main)
    {
        _main = main;
        Load();
    }

    public void Reload() => Load();

    private void Load()
    {
        Views = new ObservableCollection<PeriodicView>(
            App.Db.PeriodicViews.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList());
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CanManageViews));
        OnPropertyChanged(nameof(CannotManageViews));
        OnPropertyChanged(nameof(EditModeButtonLabel));
    }

    [RelayCommand]
    private void ToggleEditMode()
    {
        App.Settings.AllowPeriodicViewCreation = !App.Settings.AllowPeriodicViewCreation;
        _main.RefreshAfterEditModeChange();
        Load();
    }

    [RelayCommand]
    private void AddView()
    {
        if (!App.GuardWritable()) return;
        if (!App.Settings.AllowPeriodicViewCreation)
        {
            App.Dialogs.ShowMessage("Nouvelle vue", "La création de vues est désactivée. Cliquez sur « Mode édition » pour en créer une.");
            return;
        }
        var entity = new PeriodicView { Ordre = int.MaxValue };
        if (!EditViewFields(entity, "Nouvelle vue de filtres périodiques")) return;
        App.Db.PeriodicViews.Add(entity);
        App.Db.SaveChanges();
        PeriodicViewRegistry.Load(App.Db);
        Load();
        _main.AddPeriodicViewNavigationItem(entity);
    }

    private bool CanEditView() => SelectedView is not null;

    [RelayCommand(CanExecute = nameof(CanEditView))]
    private void EditView()
    {
        if (!App.GuardWritable() || SelectedView is null) return;
        var tracked = App.Db.PeriodicViews.First(v => v.Id == SelectedView.Id);
        if (!EditViewFields(tracked, "Modifier la vue")) return;
        App.Db.SaveChanges();
        PeriodicViewRegistry.Load(App.Db);
        Load();
        _main.UpdatePeriodicViewNavigationItem(tracked);
    }

    [RelayCommand(CanExecute = nameof(CanEditView))]
    private void DeleteView()
    {
        if (!App.GuardWritable() || SelectedView is null) return;
        var view = SelectedView;
        if (view.IsBuiltIn)
        {
            App.Dialogs.ShowMessage("Supprimer la vue", $"« {view.Nom} » est une vue d'origine : elle ne peut pas être supprimée (son titre et son icône restent modifiables).");
            return;
        }
        var count = App.Db.PeriodicFilters.Count(f => f.PeriodicViewId == view.Id);
        if (count > 0)
        {
            App.Dialogs.ShowMessage("Supprimer la vue", $"Impossible de supprimer « {view.Nom} » : {count} filtre(s) y sont encore rattaché(s). Supprimez-les d'abord (ou déplacez-les vers une autre vue).");
            return;
        }
        if (!App.Dialogs.ShowConfirm("Supprimer la vue", $"Supprimer définitivement la vue « {view.Nom} » ?")) return;

        App.Db.PeriodicViews.Where(v => v.Id == view.Id).ExecuteDelete();
        App.Db.ChangeTracker.Clear();
        PeriodicViewRegistry.Load(App.Db);
        Load();
        _main.RemovePeriodicViewNavigationItem(view.Id);
    }

    /// <summary>Titre, icône, libellé de la première colonne et position de la vue (sélecteur « Placer après »,
    /// traduit en <see cref="PeriodicView.Ordre"/> pour toutes les vues par <see cref="ApplyOrdering"/>).</summary>
    private bool EditViewFields(PeriodicView view, string title)
    {
        var ordered = App.Db.PeriodicViews.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList();
        var others = ordered.Where(v => v.Id != view.Id).ToList();

        var positionLabels = new List<string> { "(en premier)" };
        positionLabels.AddRange(others.Select(o => $"Après « {o.Nom} »"));

        int? currentAfterId;
        if (view.Id == 0) currentAfterId = others.Count > 0 ? others[^1].Id : null;
        else
        {
            var selfIndex = ordered.FindIndex(v => v.Id == view.Id);
            currentAfterId = selfIndex > 0 ? ordered[selfIndex - 1].Id : null;
        }
        var currentPositionIndex = currentAfterId is int afterId ? others.FindIndex(o => o.Id == afterId) + 1 : 0;

        int? chosenAfterId = currentAfterId;
        var iconChoices = MenuIcons.WithCurrent(view.Icon);
        var fields = new List<EditField>
        {
            EditField.Text("Titre de la vue", () => view.Nom, v => view.Nom = v.Trim(), required: true),
            EditField.ComboField("Icône devant le titre", iconChoices, () => Math.Max(0, iconChoices.IndexOf(view.Icon)),
                v => view.Icon = v >= 0 && v < iconChoices.Count ? iconChoices[v] : PeriodicView.DefaultIcon),
            EditField.Text("Libellé de la première colonne", () => view.LocationLabel, v => view.LocationLabel = string.IsNullOrWhiteSpace(v) ? PeriodicView.DefaultLocationLabel : v.Trim()),
            EditField.ComboField("Placer après", positionLabels, () => currentPositionIndex,
                v => chosenAfterId = v >= 1 && v <= others.Count ? others[v - 1].Id : null)
        };
        if (!App.Dialogs.EditFields(title, fields)) return false;

        if (MenuNames.IsTaken(view.Nom, excludedPeriodicViewId: view.Id))
        {
            App.Dialogs.ShowMessage("Vue", $"Le titre « {view.Nom} » est déjà utilisé (ou réservé) : choisissez-en un autre.");
            if (view.Id != 0) App.Db.Entry(view).Reload();
            return false;
        }

        ApplyOrdering(view, chosenAfterId);
        return true;
    }

    private static void ApplyOrdering(PeriodicView view, int? afterViewId)
    {
        var others = App.Db.PeriodicViews.Where(v => v.Id != view.Id).OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList();
        var insertIndex = afterViewId is int id ? others.FindIndex(v => v.Id == id) + 1 : 0;
        if (insertIndex < 0) insertIndex = others.Count;
        others.Insert(insertIndex, view);
        for (var i = 0; i < others.Count; i++) others[i].Ordre = i;
    }
}
