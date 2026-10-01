using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Familles du module "Courroies" : créées à la main, attribuées à chaque courroie, filtre
/// d'affichage de l'écran.</summary>
public partial class BeltListViewModel
{
    public record FamilyOption(int? FamilyId, bool IsNoFamily, string Label)
    {
        public bool IsAll => FamilyId is null && !IsNoFamily;
    }

    private static readonly FamilyOption AllFamilies = new(null, false, "Toutes les familles");
    private static readonly FamilyOption NoFamily = new(null, true, Belt.NoFamilyLabel);

    public List<BeltFamily> Families { get; private set; } = new();
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

    private void RefreshFamilies(int? selectFamilyId = null)
    {
        Families = App.Db.BeltFamilies.AsNoTracking().OrderBy(f => f.Nom).ToList();
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
        var family = new BeltFamily();
        if (!EditFamilyName(family, "Nouvelle famille « Courroies »")) return;
        App.Db.BeltFamilies.Add(family);
        App.Db.SaveChanges();
        RefreshFamilies(family.Id);
        Load();
    }

    [RelayCommand(CanExecute = nameof(IsFamilySelected))]
    private void RenameFamily()
    {
        if (!App.GuardWritable() || SelectedFamily?.FamilyId is not int id) return;
        var tracked = App.Db.BeltFamilies.First(f => f.Id == id);
        if (!EditFamilyName(tracked, "Renommer la famille")) return;
        App.Db.SaveChanges();
        RefreshFamilies(id);
        Load();
    }

    /// <summary>Les courroies de la famille ne sont pas supprimées : elles passent en "Sans famille".</summary>
    [RelayCommand(CanExecute = nameof(IsFamilySelected))]
    private void DeleteFamily()
    {
        if (!App.GuardWritable() || SelectedFamily?.FamilyId is not int id) return;
        var count = App.Db.Belts.Count(b => b.BeltFamilyId == id);
        var detail = count == 0 ? "" : $"\n\nSes {count} courroie(s) ne seront pas supprimées : elles passeront en « {Belt.NoFamilyLabel} ».";
        if (!App.Dialogs.ShowConfirm("Supprimer la famille", $"Supprimer la famille « {SelectedFamily.Label} » ?{detail}")) return;

        App.Db.Belts.Where(b => b.BeltFamilyId == id).ExecuteUpdate(s => s.SetProperty(b => b.BeltFamilyId, (int?)null));
        App.Db.BeltFamilies.Where(f => f.Id == id).ExecuteDelete();
        App.Db.ChangeTracker.Clear();

        RefreshFamilies();
        Load();
    }

    private bool EditFamilyName(BeltFamily family, string title)
    {
        var fields = new List<EditField>
        {
            EditField.Text("Nom de la famille", () => family.Nom, v => family.Nom = v.Trim(), required: true)
        };
        if (!App.Dialogs.EditFields(title, fields)) return false;

        var name = family.Nom;
        string? error = null;
        if (string.Equals(name, Belt.NoFamilyLabel, StringComparison.OrdinalIgnoreCase))
            error = $"« {Belt.NoFamilyLabel} » est réservé aux courroies sans famille : choisissez un autre nom.";
        else if (App.Db.BeltFamilies.AsNoTracking().Any(f => f.Id != family.Id && f.Nom.ToLower() == name.ToLower()))
            error = $"Une famille « {name} » existe déjà.";

        if (error is null) return true;
        App.Dialogs.ShowMessage("Famille", error);
        if (family.Id != 0) App.Db.Entry(family).Reload();
        return false;
    }
}
