using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Familles d'une variété "Filtres F7 à H14" : créées à la main, attribuées à chaque filtre, filtre
/// d'affichage de l'écran.</summary>
public partial class DynamicFilterListViewModel
{
    // ---- Familles (créées à la main, attribuées manuellement à chaque filtre) ----

    public record FamilyOption(int? FamilyId, bool IsNoFamily, string Label)
    {
        public bool IsAll => FamilyId is null && !IsNoFamily;
    }

    private static readonly FamilyOption AllFamilies = new(null, false, "Toutes les familles");
    private static readonly FamilyOption NoFamily = new(null, true, DynamicFilter.NoFamilyLabel);

    public List<DynamicFilterFamily> Families { get; private set; } = new();
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

    /// <summary>Relit les familles en conservant le filtre choisi s'il existe toujours.</summary>
    private void RefreshFamilies(int? selectFamilyId = null)
    {
        Families = App.Db.DynamicFilterFamilies.AsNoTracking().Where(f => f.VarietyId == Variety.Id).OrderBy(f => f.Nom).ToList();
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
        var family = new DynamicFilterFamily { VarietyId = Variety.Id };
        if (!EditFamilyName(family, $"Nouvelle famille « {Variety.Nom} »")) return;
        App.Db.DynamicFilterFamilies.Add(family);
        App.Db.SaveChanges();
        RefreshFamilies(family.Id);
        Load();
    }

    [RelayCommand(CanExecute = nameof(IsFamilySelected))]
    private void RenameFamily()
    {
        if (!App.GuardWritable() || SelectedFamily?.FamilyId is not int id) return;
        var tracked = App.Db.DynamicFilterFamilies.First(f => f.Id == id);
        if (!EditFamilyName(tracked, "Renommer la famille")) return;
        App.Db.SaveChanges();
        RefreshFamilies(id);
        Load();
    }

    /// <summary>Les filtres de la famille ne sont pas supprimés : ils passent en "Sans famille".</summary>
    [RelayCommand(CanExecute = nameof(IsFamilySelected))]
    private void DeleteFamily()
    {
        if (!App.GuardWritable() || SelectedFamily?.FamilyId is not int id) return;
        var count = App.Db.DynamicFilters.Count(f => f.DynamicFilterFamilyId == id);
        var detail = count == 0 ? "" : $"\n\nSes {count} filtre(s) ne seront pas supprimés : ils passeront en « {DynamicFilter.NoFamilyLabel} ».";
        if (!App.Dialogs.ShowConfirm("Supprimer la famille", $"Supprimer la famille « {SelectedFamily.Label} » ?{detail}")) return;

        App.Db.DynamicFilters.Where(f => f.DynamicFilterFamilyId == id)
            .ExecuteUpdate(s => s.SetProperty(f => f.DynamicFilterFamilyId, (int?)null));
        App.Db.DynamicFilterFamilies.Where(f => f.Id == id).ExecuteDelete();
        // Les filtres éventuellement suivis par le contexte gardent l'ancienne famille en mémoire.
        App.Db.ChangeTracker.Clear();

        RefreshFamilies();
        Load();
    }

    private bool EditFamilyName(DynamicFilterFamily family, string title)
    {
        var fields = new List<EditField>
        {
            EditField.Text("Nom de la famille", () => family.Nom, v => family.Nom = v.Trim(), required: true)
        };
        if (!App.Dialogs.EditFields(title, fields)) return false;

        var name = family.Nom;
        string? error = null;
        if (string.Equals(name, DynamicFilter.NoFamilyLabel, StringComparison.OrdinalIgnoreCase))
            error = $"« {DynamicFilter.NoFamilyLabel} » est réservé aux filtres sans famille : choisissez un autre nom.";
        else if (App.Db.DynamicFilterFamilies.AsNoTracking().Any(f => f.VarietyId == Variety.Id && f.Id != family.Id && f.Nom.ToLower() == name.ToLower()))
            error = $"Une famille « {name} » existe déjà.";

        if (error is null) return true;
        App.Dialogs.ShowMessage("Famille", error);
        // Le renommage refusé ne doit pas rester en mémoire sur l'entité suivie.
        if (family.Id != 0) App.Db.Entry(family).Reload();
        return false;
    }
}
