using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Module activable "Courroies" (menu Paramètres) : même disposition que "Filtres F7 à H14"
/// (familles créées à la main, historique de remplacements ponctuels sans périodicité mensuelle fixe), sans
/// notion de variété (un seul module, pas plusieurs créées par l'utilisateur). Familles : voir
/// BeltListViewModel.Families.cs.</summary>
public partial class BeltListViewModel : ObservableObject, IReloadable
{
    public string Title => "Courroies";

    /// <summary>L'année consultée ne filtre pas des lignes de suivi mensuel (ce module n'a pas de
    /// périodicité fixe) : elle restreint "Dernier changement" / "Nb remplacements" aux remplacements
    /// datés de cette année-là ; l'historique complet reste toujours en base.</summary>
    public YearContext YearContext => App.YearContext;

    [ObservableProperty] private ObservableCollection<BeltRowViewModel> _belts = new();
    [ObservableProperty] private BeltRowViewModel? _selectedBelt;

    /// <summary>Mode édition (bouton "Mode édition" / "Quitter le mode édition") : masque par défaut les
    /// boutons Ajouter/Modifier/Supprimer pour éviter les modifications accidentelles sur le terrain (le
    /// pointage courant, lui, reste toujours accessible). Remis à false à chaque ouverture.</summary>
    [ObservableProperty] private bool _isEditMode;

    public string EditModeButtonLabel => IsEditMode ? "Quitter le mode édition" : "Mode édition";

    partial void OnIsEditModeChanged(bool value) => OnPropertyChanged(nameof(EditModeButtonLabel));

    [RelayCommand]
    private void ToggleEditMode() => IsEditMode = !IsEditMode;

    public BeltListViewModel()
    {
        RefreshFamilies();
        Load();
    }

    /// <summary>Colonne "Date du changement" : saisir une date enregistre directement un nouveau
    /// remplacement (quantité en place) daté de cette date, sans jamais modifier l'historique déjà
    /// enregistré. Le champ de saisie se vide ensuite - voir <see cref="ShowHistory"/> pour consulter
    /// l'historique complet.</summary>
    public bool AddReplacement(Belt belt, DateOnly date)
    {
        if (!App.GuardWritable()) return false;
        var tracked = App.Db.Belts.Include(b => b.Replacements).First(b => b.Id == belt.Id);
        tracked.Replacements.Add(new BeltReplacement { QuantityChanged = tracked.QuantityInPlace, DateChanged = date });
        App.Db.SaveChanges();
        belt.Replacements = tracked.Replacements.Select(r => new BeltReplacement
        {
            Id = r.Id, BeltId = r.BeltId, QuantityChanged = r.QuantityChanged, DateChanged = r.DateChanged
        }).ToList();
        App.YearContext.EnsureYear(date.Year);
        return true;
    }

    public void Reload()
    {
        RefreshFamilies();
        Load();
    }

    private List<Belt> _loadedBelts = new();

    private void Load()
    {
        var query = App.Db.Belts.Include(b => b.Replacements).Include(b => b.Family).AsNoTracking();

        var selected = SelectedFamily ?? AllFamilies;
        if (selected.IsNoFamily) query = query.Where(b => b.BeltFamilyId == null);
        else if (selected.FamilyId is int familyId) query = query.Where(b => b.BeltFamilyId == familyId);

        _loadedBelts = query
            .OrderBy(b => b.BeltFamilyId == null)
            .ThenBy(b => b.Family!.Nom)
            .ThenBy(b => b.Location)
            .ToList();

        Belts = new ObservableCollection<BeltRowViewModel>(
            _loadedBelts.Select(b => new BeltRowViewModel(b, YearContext.Year, this)));
    }

    [RelayCommand]
    private void AddBelt()
    {
        if (!App.GuardWritable()) return;
        var entity = new Belt { BeltFamilyId = SelectedFamily?.FamilyId };
        if (!EditEntity(entity, true)) return;
        App.Db.Belts.Add(entity);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void EditBelt()
    {
        if (!App.GuardWritable()) return;
        if (SelectedBelt is null) return;
        var tracked = App.Db.Belts.Include(b => b.Replacements).First(b => b.Id == SelectedBelt.Id);
        if (!EditEntity(tracked, false)) return;
        App.Db.SaveChanges();
        Load();
    }

    /// <summary>Copie la ligne sélectionnée (sans son historique de remplacements) et ouvre directement son
    /// édition. N'enregistre rien si l'édition est annulée.</summary>
    [RelayCommand]
    private void DuplicateBelt()
    {
        if (!App.GuardWritable()) return;
        if (SelectedBelt is null) return;
        var source = SelectedBelt.Belt;
        var copy = new Belt
        {
            Location = source.Location,
            BeltType = source.BeltType,
            QuantityInPlace = source.QuantityInPlace,
            Commentaire = source.Commentaire,
            BeltFamilyId = source.BeltFamilyId,
            RowColorId = source.RowColorId
        };
        if (!EditEntity(copy, true)) return;
        App.Db.Belts.Add(copy);
        App.Db.SaveChanges();
        Load();
    }

    private bool EditEntity(Belt entity, bool isNew)
    {
        var families = Families;
        var familyNames = new List<string> { Belt.NoFamilyLabel };
        familyNames.AddRange(families.Select(f => f.Nom));
        var familyIndex = entity.BeltFamilyId is int id ? families.FindIndex(f => f.Id == id) + 1 : 0;

        var fields = new List<EditField>
        {
            EditField.ComboField("Famille", familyNames, () => familyIndex,
                v => entity.BeltFamilyId = v >= 1 && v <= families.Count ? families[v - 1].Id : null),
            EditField.Multiline("Nom de la centrale", () => entity.Location, v => entity.Location = v, required: true),
            EditField.NullableText("Type de courroies", () => entity.BeltType, v => entity.BeltType = v),
            EditField.IntField("Nombre", () => entity.QuantityInPlace, v => entity.QuantityInPlace = v),
            EditField.Multiline("Commentaire", () => entity.Commentaire, v => entity.Commentaire = v)
        };
        return App.Dialogs.EditFields(isNew ? "Ajouter une courroie" : "Modifier la courroie", fields);
    }

    [RelayCommand]
    private void DeleteBelt()
    {
        if (!App.GuardWritable()) return;
        if (SelectedBelt is null) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer définitivement '{SelectedBelt.Location}' ?")) return;
        var tracked = App.Db.Belts.First(b => b.Id == SelectedBelt.Id);
        App.Db.Belts.Remove(tracked);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void Print()
    {
        var headers = new[] { "Nom de la centrale", "Type de courroies", "Nombre", $"Dernier changement ({YearContext.Year})", $"Nb remplacements ({YearContext.Year})", "Commentaire" };
        var rows = PrintService.BuildGroupedRows(Belts, b => b.FamilyGroupLabel, b => new[]
        {
            b.Location, b.BeltType ?? "", b.QuantityInPlace.ToString(),
            b.LastChangedDateInYear?.ToString("dd/MM/yyyy") ?? "-", b.ReplacementCountInYear.ToString(), b.Commentaire ?? ""
        }, headers.Length);
        var rowColors = PrintService.BuildGroupedRowColors(Belts, b => b.FamilyGroupLabel, b => RowColorPalette.ColorFor(b.RowColorId));
        var documentTitle = SelectedFamily is null || SelectedFamily.IsAll ? Title : $"{Title} - {SelectedFamily.Label}";
        App.Printer.PrintTable(documentTitle, headers, rows, rowColors: rowColors);
    }

    /// <summary>"Consulter l'historique..." (menu contextuel de la grille) : ouvre une fenêtre listant, pour
    /// l'année choisie parmi celles où cette courroie précise a de l'historique, la liste chronologique des
    /// remplacements (qté changée + date), avec possibilité d'en supprimer (clic droit). La grille
    /// principale est rechargée à la fermeture pour refléter une éventuelle suppression.</summary>
    public void ShowHistory(Belt belt)
    {
        var replacements = App.Db.BeltReplacements.AsNoTracking().Where(r => r.BeltId == belt.Id).ToList();
        App.Dialogs.ShowReplacementHistory(new ReplacementHistory<BeltReplacement>(
            $"Nom de la centrale : {belt.Location}",
            "Aucun historique disponible pour cette courroie : aucun remplacement enregistré en base pour l'instant.",
            "Quantité changée", r => r.QuantityChanged.ToString(), r => $"quantité {r.QuantityChanged}"), replacements);
        Reload();
    }

    /// <summary>Couleur de ligne (menu contextuel de la grille) : sauvegarde immédiate en base.</summary>
    public void SetRowColor(Belt belt, int? colorId)
    {
        if (!App.GuardWritable()) return;
        var tracked = App.Db.Belts.First(b => b.Id == belt.Id);
        tracked.RowColorId = colorId;
        App.Db.SaveChanges();
        belt.RowColorId = colorId;
    }
}
