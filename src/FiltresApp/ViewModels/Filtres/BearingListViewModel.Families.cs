using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Familles du module "Roulements" : créées à la main, attribuées à chaque jeu de roulements,
/// filtre d'affichage de l'écran.</summary>
public partial class BearingListViewModel
{
    public record FamilyOption(int? FamilyId, bool IsNoFamily, string Label)
    {
        public bool IsAll => FamilyId is null && !IsNoFamily;
    }

    private static readonly FamilyOption AllFamilies = new(null, false, "Toutes les familles");
    private static readonly FamilyOption NoFamily = new(null, true, BearingUnit.NoFamilyLabel);

    public List<BearingFamily> Families { get; private set; } = new();
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
        Families = App.Db.BearingFamilies.AsNoTracking().OrderBy(f => f.Nom).ToList();
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
        var family = new BearingFamily();
        if (!EditFamilyName(family, "Nouvelle famille « Roulements »")) return;
        App.Db.BearingFamilies.Add(family);
        App.Db.SaveChanges();
        RefreshFamilies(family.Id);
        Load();
    }

    [RelayCommand(CanExecute = nameof(IsFamilySelected))]
    private void RenameFamily()
    {
        if (!App.GuardWritable() || SelectedFamily?.FamilyId is not int id) return;
        var tracked = App.Db.BearingFamilies.First(f => f.Id == id);
        if (!EditFamilyName(tracked, "Renommer la famille")) return;
        App.Db.SaveChanges();
        RefreshFamilies(id);
        Load();
    }

    [RelayCommand(CanExecute = nameof(IsFamilySelected))]
    private void DeleteFamily()
    {
        if (!App.GuardWritable() || SelectedFamily?.FamilyId is not int id) return;
        var count = App.Db.BearingUnits.Count(b => b.BearingFamilyId == id);
        var detail = count == 0 ? "" : $"\n\nSes {count} jeu(x) de roulements ne seront pas supprimés : ils passeront en « {BearingUnit.NoFamilyLabel} ».";
        if (!App.Dialogs.ShowConfirm("Supprimer la famille", $"Supprimer la famille « {SelectedFamily.Label} » ?{detail}")) return;

        App.Db.BearingUnits.Where(b => b.BearingFamilyId == id).ExecuteUpdate(s => s.SetProperty(b => b.BearingFamilyId, (int?)null));
        App.Db.BearingFamilies.Where(f => f.Id == id).ExecuteDelete();
        App.Db.ChangeTracker.Clear();

        RefreshFamilies();
        Load();
    }

    private bool EditFamilyName(BearingFamily family, string title)
    {
        var fields = new List<EditField>
        {
            EditField.Text("Nom de la famille", () => family.Nom, v => family.Nom = v.Trim(), required: true)
        };
        if (!App.Dialogs.EditFields(title, fields)) return false;

        var name = family.Nom;
        string? error = null;
        if (string.Equals(name, BearingUnit.NoFamilyLabel, StringComparison.OrdinalIgnoreCase))
            error = $"« {BearingUnit.NoFamilyLabel} » est réservé aux jeux sans famille : choisissez un autre nom.";
        else if (App.Db.BearingFamilies.AsNoTracking().Any(f => f.Id != family.Id && f.Nom.ToLower() == name.ToLower()))
            error = $"Une famille « {name} » existe déjà.";

        if (error is null) return true;
        App.Dialogs.ShowMessage("Famille", error);
        if (family.Id != 0) App.Db.Entry(family).Reload();
        return false;
    }
}
