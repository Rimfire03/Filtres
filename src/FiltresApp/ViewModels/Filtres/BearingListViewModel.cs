using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Module activable "Roulements" (menu Paramètres) : même disposition que "Filtres F7 à H14"
/// (familles créées à la main, historique de remplacements ponctuels sans périodicité mensuelle fixe), sans
/// notion de variété. Chaque jeu a trois roulements (avant/arrière/volute) : un remplacement précise
/// lesquels ont été changés ce jour-là (choisi dans une petite fenêtre à la saisie de la date, voir
/// <see cref="AddReplacement"/>). Partie commune : voir
/// <see cref="TrackedItemListViewModel{TEntity, TFamily, TRow}"/>.</summary>
public partial class BearingListViewModel : TrackedItemListViewModel<BearingUnit, BearingFamily, BearingRowViewModel>
{
    public override string Title => "Roulements";

    public BearingListViewModel() => Initialize();

    protected override List<BearingUnit> LoadEntities(FamilyOption family)
    {
        var query = App.Db.BearingUnits.Include(b => b.Replacements).Include(b => b.Family).AsNoTracking();
        if (family.IsNoFamily) query = query.Where(b => b.BearingFamilyId == null);
        else if (family.FamilyId is int familyId) query = query.Where(b => b.BearingFamilyId == familyId);
        return query.OrderBy(b => b.BearingFamilyId == null).ThenBy(b => b.Family!.Nom).ThenBy(b => b.Location).ToList();
    }

    protected override BearingRowViewModel CreateRow(BearingUnit bearing) => new(bearing, YearContext.Year, this);

    /// <summary>Colonne "Date du changement" : une date choisie ouvre une petite fenêtre demandant lesquels
    /// des trois roulements ont été changés (au moins un), puis enregistre un remplacement daté de cette
    /// date. N'enregistre rien si la fenêtre est annulée ou si aucun roulement n'est coché.</summary>
    public override bool AddReplacement(BearingUnit bearing, DateOnly date)
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

    protected override BearingUnit CreateEntity(int? familyId) => new() { BearingFamilyId = familyId };

    protected override BearingUnit CopyEntity(BearingUnit source) => new()
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

    protected override bool EditEntity(BearingUnit entity, bool isNew)
    {
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
            FamilyField(entity.BearingFamilyId, id => entity.BearingFamilyId = id),
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
    private void Print()
    {
        var headers = new[] { "Nom de la centrale", "Type de centrale", "Réf. avant", "Réf. arrière", "Réf. volute", $"Dernier changement ({YearContext.Year})", $"Nb remplacements ({YearContext.Year})", "Commentaire" };
        var rows = PrintService.BuildGroupedRows(Rows, b => b.FamilyGroupLabel, b => new[]
        {
            b.Location, b.CentraleType, b.RefAvant ?? "", b.RefArriere ?? "", b.RefVolute ?? "",
            b.LastChangedDateInYear?.ToString("dd/MM/yyyy") ?? "-", b.ReplacementCountInYear.ToString(), b.Commentaire ?? ""
        }, headers.Length);
        var rowColors = PrintService.BuildGroupedRowColors(Rows, b => b.FamilyGroupLabel, b => RowColorPalette.ColorFor(b.RowColorId));
        App.Printer.PrintTable(PrintTitle, headers, rows, rowColors: rowColors);
    }

    /// <summary>Remplacements de ce jeu précis (date + roulements changés), par année.</summary>
    protected override void OpenHistory(BearingUnit bearing)
    {
        var replacements = App.Db.BearingReplacements.AsNoTracking().Where(r => r.BearingUnitId == bearing.Id).ToList();
        App.Dialogs.ShowReplacementHistory(new ReplacementHistory<BearingReplacement>(
            $"Nom de la centrale : {bearing.Location}",
            "Aucun historique disponible pour ce jeu de roulements : aucun remplacement enregistré en base pour l'instant.",
            "Roulements changés", r => r.ChangedLabel, r => r.ChangedLabel,
            DateColumnWidth: 1.2, DetailColumnWidth: 1.4), replacements);
    }

    // ---- Familles (partie commune : TrackedItemListViewModel.Families.cs) ----

    protected override List<BearingFamily> LoadFamilies() => App.Db.BearingFamilies.AsNoTracking().OrderBy(f => f.Nom).ToList();

    protected override BearingFamily CreateFamily() => new();

    protected override bool FamilyNameExists(string name, int excludedId) =>
        App.Db.BearingFamilies.AsNoTracking().Any(f => f.Id != excludedId && f.Nom.ToLower() == name.ToLower());

    protected override int CountItemsInFamily(int familyId) => App.Db.BearingUnits.Count(b => b.BearingFamilyId == familyId);

    protected override void DeleteFamilyKeepingItems(int familyId)
    {
        App.Db.BearingUnits.Where(b => b.BearingFamilyId == familyId).ExecuteUpdate(s => s.SetProperty(b => b.BearingFamilyId, (int?)null));
        App.Db.BearingFamilies.Where(f => f.Id == familyId).ExecuteDelete();
    }

    protected override string ItemsLabel => "jeux";

    protected override string FamilyDeleteDetail(int count) =>
        $"Ses {count} jeu(x) de roulements ne seront pas supprimés : ils passeront en « {BearingUnit.NoFamilyLabel} ».";
}
