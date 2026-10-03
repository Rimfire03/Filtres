using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Module activable "Roulements" (menu Paramètres) : même disposition que "Filtres F7 à H14"
/// (familles créées à la main, historique de remplacements ponctuels sans périodicité mensuelle fixe), sans
/// notion de variété. Chaque jeu a trois roulements (avant/arrière/volute) : un remplacement précise
/// lesquels ont été changés ce jour-là (choisi dans une petite fenêtre à la saisie de la date, voir
/// <see cref="AddReplacement"/>). Familles : voir BearingListViewModel.Families.cs.</summary>
public partial class BearingListViewModel : ObservableObject, IReloadable
{
    public string Title => "Roulements";

    public YearContext YearContext => App.YearContext;

    [ObservableProperty] private ObservableCollection<BearingRowViewModel> _bearings = new();
    [ObservableProperty] private BearingRowViewModel? _selectedBearing;

    [ObservableProperty] private bool _isEditMode;

    public string EditModeButtonLabel => IsEditMode ? "Quitter le mode édition" : "Mode édition";

    partial void OnIsEditModeChanged(bool value) => OnPropertyChanged(nameof(EditModeButtonLabel));

    [RelayCommand]
    private void ToggleEditMode() => IsEditMode = !IsEditMode;

    public BearingListViewModel()
    {
        RefreshFamilies();
        Load();
    }

    /// <summary>Colonne "Date du changement" : une date choisie ouvre une petite fenêtre demandant lesquels
    /// des trois roulements ont été changés (au moins un), puis enregistre un remplacement daté de cette
    /// date. N'enregistre rien si la fenêtre est annulée ou si aucun roulement n'est coché. Le champ de
    /// saisie se vide ensuite - voir <see cref="ShowHistory"/> pour consulter l'historique complet.</summary>
    public bool AddReplacement(BearingUnit bearing, DateOnly date)
    {
        if (!App.GuardWritable()) return false;

        // "Entraînement direct" : pas de roulement volute (voir BearingRowViewModel.IsDirectDrive), donc
        // pas de case à cocher correspondante ici.
        var hasVolute = bearing.CentraleType != BearingUnit.CentraleTypeOptions[1];
        var items = hasVolute ? new List<string> { "Avant", "Arrière", "Volute" } : new List<string> { "Avant", "Arrière" };
        var selected = new List<int>();
        var fields = new List<EditField>
        {
            EditField.ChecklistField("Roulements changés", items, () => selected, v => selected = v)
        };
        if (!App.Dialogs.EditFields($"Changement du {date:dd/MM/yyyy} - « {bearing.Location} »", fields)) return false;
        if (selected.Count == 0) return false;

        var tracked = App.Db.BearingUnits.Include(b => b.Replacements).First(b => b.Id == bearing.Id);
        tracked.Replacements.Add(new BearingReplacement
        {
            DateChanged = date,
            ChangedAvant = selected.Contains(0),
            ChangedArriere = selected.Contains(1),
            ChangedVolute = hasVolute && selected.Contains(2)
        });
        App.Db.SaveChanges();
        bearing.Replacements = tracked.Replacements.Select(r => new BearingReplacement
        {
            Id = r.Id, BearingUnitId = r.BearingUnitId, DateChanged = r.DateChanged,
            ChangedAvant = r.ChangedAvant, ChangedArriere = r.ChangedArriere, ChangedVolute = r.ChangedVolute
        }).ToList();
        App.YearContext.EnsureYear(date.Year);
        return true;
    }

    public void Reload()
    {
        RefreshFamilies();
        Load();
    }

    private List<BearingUnit> _loadedBearings = new();

    private void Load()
    {
        var query = App.Db.BearingUnits.Include(b => b.Replacements).Include(b => b.Family).AsNoTracking();

        var selected = SelectedFamily ?? AllFamilies;
        if (selected.IsNoFamily) query = query.Where(b => b.BearingFamilyId == null);
        else if (selected.FamilyId is int familyId) query = query.Where(b => b.BearingFamilyId == familyId);

        _loadedBearings = query
            .OrderBy(b => b.BearingFamilyId == null)
            .ThenBy(b => b.Family!.Nom)
            .ThenBy(b => b.Location)
            .ToList();

        Bearings = new ObservableCollection<BearingRowViewModel>(
            _loadedBearings.Select(b => new BearingRowViewModel(b, YearContext.Year, this)));
    }

    [RelayCommand]
    private void AddBearing()
    {
        if (!App.GuardWritable()) return;
        var entity = new BearingUnit { BearingFamilyId = SelectedFamily?.FamilyId };
        if (!EditEntity(entity, true)) return;
        App.Db.BearingUnits.Add(entity);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void EditBearing()
    {
        if (!App.GuardWritable()) return;
        if (SelectedBearing is null) return;
        var tracked = App.Db.BearingUnits.Include(b => b.Replacements).First(b => b.Id == SelectedBearing.Id);
        if (!EditEntity(tracked, false)) return;
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void DuplicateBearing()
    {
        if (!App.GuardWritable()) return;
        if (SelectedBearing is null) return;
        var source = SelectedBearing.Bearing;
        var copy = new BearingUnit
        {
            Location = source.Location,
            CentraleType = source.CentraleType,
            RefAvant = source.RefAvant,
            RefArriere = source.RefArriere,
            RefVolute = source.RefVolute,
            Commentaire = source.Commentaire,
            BearingFamilyId = source.BearingFamilyId,
            RowColorId = source.RowColorId
        };
        if (!EditEntity(copy, true)) return;
        App.Db.BearingUnits.Add(copy);
        App.Db.SaveChanges();
        Load();
    }

    private bool EditEntity(BearingUnit entity, bool isNew)
    {
        var families = Families;
        var familyNames = new List<string> { BearingUnit.NoFamilyLabel };
        familyNames.AddRange(families.Select(f => f.Nom));
        var familyIndex = entity.BearingFamilyId is int id ? families.FindIndex(f => f.Id == id) + 1 : 0;

        var typeOptions = BearingUnit.CentraleTypeOptions.ToList();
        var typeIndex = Math.Max(0, Array.IndexOf(BearingUnit.CentraleTypeOptions, entity.CentraleType));

        var typeField = EditField.ComboField("Type de centrale", typeOptions, () => typeIndex, v => entity.CentraleType = typeOptions[v]);
        // "Entraînement direct" (index 1) : pas de roulement volute (voir BearingRowViewModel.IsDirectDrive
        // et VoluteCellStyle) - le sélecteur n'est alors pas accessible et sa valeur devient forcément nulle
        // (voir DynamicEditWindow.RefreshConditionalFields, qui vide le champ dès qu'il se désactive).
        var voluteField = EditField.NullableText("Référence roulement volute", () => entity.RefVolute, v => entity.RefVolute = v);
        voluteField.EnabledWhenFieldEquals = typeField;
        voluteField.EnabledPredicate = v => !(v is int i && i == 1);

        var fields = new List<EditField>
        {
            EditField.ComboField("Famille", familyNames, () => familyIndex,
                v => entity.BearingFamilyId = v >= 1 && v <= families.Count ? families[v - 1].Id : null),
            EditField.Multiline("Nom de la centrale", () => entity.Location, v => entity.Location = v, required: true),
            typeField,
            EditField.NullableText("Référence roulement avant", () => entity.RefAvant, v => entity.RefAvant = v),
            EditField.NullableText("Référence roulement arrière", () => entity.RefArriere, v => entity.RefArriere = v),
            voluteField,
            EditField.Multiline("Commentaire", () => entity.Commentaire, v => entity.Commentaire = v)
        };
        var ok = App.Dialogs.EditFields(isNew ? "Ajouter un jeu de roulements" : "Modifier le jeu de roulements", fields);
        // "Entraînement direct" : pas de roulement volute (voir BearingRowViewModel.IsDirectDrive et
        // VoluteCellStyle) - toute référence saisie avant un changement de type est effacée.
        if (ok && entity.CentraleType == BearingUnit.CentraleTypeOptions[1]) entity.RefVolute = null;
        return ok;
    }

    [RelayCommand]
    private void DeleteBearing()
    {
        if (!App.GuardWritable()) return;
        if (SelectedBearing is null) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer définitivement '{SelectedBearing.Location}' ?")) return;
        var tracked = App.Db.BearingUnits.First(b => b.Id == SelectedBearing.Id);
        App.Db.BearingUnits.Remove(tracked);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void Print()
    {
        var headers = new[] { "Nom de la centrale", "Type de centrale", "Réf. avant", "Réf. arrière", "Réf. volute", $"Dernier changement ({YearContext.Year})", $"Nb remplacements ({YearContext.Year})", "Commentaire" };
        var rows = PrintService.BuildGroupedRows(Bearings, b => b.FamilyGroupLabel, b => new[]
        {
            b.Location, b.CentraleType, b.RefAvant ?? "", b.RefArriere ?? "", b.RefVolute ?? "",
            b.LastChangedDateInYear?.ToString("dd/MM/yyyy") ?? "-", b.ReplacementCountInYear.ToString(), b.Commentaire ?? ""
        }, headers.Length);
        var rowColors = PrintService.BuildGroupedRowColors(Bearings, b => b.FamilyGroupLabel, b => RowColorPalette.ColorFor(b.RowColorId));
        var documentTitle = SelectedFamily is null || SelectedFamily.IsAll ? Title : $"{Title} - {SelectedFamily.Label}";
        App.Printer.PrintTable(documentTitle, headers, rows, rowColors: rowColors);
    }

    /// <summary>"Consulter l'historique..." (menu contextuel de la grille) : ouvre une fenêtre listant, pour
    /// l'année choisie, la liste chronologique des remplacements (roulements changés + date), avec
    /// possibilité d'en supprimer (clic droit). La grille principale est rechargée à la fermeture.</summary>
    public void ShowHistory(BearingUnit bearing)
    {
        var replacements = App.Db.BearingReplacements.AsNoTracking().Where(r => r.BearingUnitId == bearing.Id).ToList();
        App.Dialogs.ShowReplacementHistory(new ReplacementHistory<BearingReplacement>(
            $"Nom de la centrale : {bearing.Location}",
            "Aucun historique disponible pour ce jeu de roulements : aucun remplacement enregistré en base pour l'instant.",
            "Roulements changés", r => r.ChangedLabel, r => r.ChangedLabel,
            DateColumnWidth: 1.2, DetailColumnWidth: 1.4), replacements);
        Reload();
    }

    public void SetRowColor(BearingUnit bearing, int? colorId)
    {
        if (!App.GuardWritable()) return;
        var tracked = App.Db.BearingUnits.First(b => b.Id == bearing.Id);
        tracked.RowColorId = colorId;
        App.Db.SaveChanges();
        bearing.RowColorId = colorId;
    }
}
