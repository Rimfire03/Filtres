using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Module activable "Courroies" (menu Paramètres) : même disposition que "Filtres F7 à H14"
/// (familles créées à la main, historique de remplacements ponctuels sans périodicité mensuelle fixe), sans
/// notion de variété (un seul module, pas plusieurs créées par l'utilisateur). Partie commune : voir
/// <see cref="TrackedItemListViewModel{TEntity, TFamily, TRow}"/>.</summary>
public partial class BeltListViewModel : TrackedItemListViewModel<Belt, BeltFamily, BeltRowViewModel>
{
    public override string Title => "Courroies";

    public BeltListViewModel() => Initialize();

    protected override List<Belt> LoadEntities(FamilyOption family)
    {
        var query = App.Db.Belts.Include(b => b.Replacements).Include(b => b.Family).AsNoTracking();
        if (family.IsNoFamily) query = query.Where(b => b.BeltFamilyId == null);
        else if (family.FamilyId is int familyId) query = query.Where(b => b.BeltFamilyId == familyId);
        return query.OrderBy(b => b.BeltFamilyId == null).ThenBy(b => b.Family!.Nom).ThenBy(b => b.Location).ToList();
    }

    protected override BeltRowViewModel CreateRow(Belt belt) => new(belt, YearContext.Year, this);

    /// <summary>Colonne "Date du changement" : une date choisie ouvre une petite fenêtre demandant quelles
    /// fonctions (soufflage / extraction) ont eu leurs courroies changées - seules celles qui ont une quantité
    /// y sont proposées -, puis enregistre un remplacement daté de cette date dont la quantité est la somme des
    /// fonctions cochées. N'enregistre rien si la fenêtre est annulée ou si rien n'est coché (même principe
    /// que les roulements, voir BearingListViewModel.AddReplacement).</summary>
    public override bool AddReplacement(Belt belt, DateOnly date)
    {
        if (!App.GuardWritable()) return false;

        var functions = new List<(string Label, int Quantity, string? Type)>();
        if (belt.QuantitySoufflage > 0) functions.Add(("Soufflage", belt.QuantitySoufflage, belt.BeltTypeSoufflage));
        if (belt.QuantityExtraction > 0) functions.Add(("Extraction", belt.QuantityExtraction, belt.BeltTypeExtraction));
        if (functions.Count == 0)
        {
            App.Dialogs.ShowMessage("Changement de courroies",
                $"« {belt.Location} » n'a aucune quantité de courroies (soufflage ni extraction) : renseignez-la d'abord avec « Modifier ».");
            return false;
        }

        var selected = new List<int>();
        var fields = new List<EditField>
        {
            EditField.ChecklistField("Courroies changées", functions.Select(f => string.IsNullOrWhiteSpace(f.Type) ? $"{f.Label} ({f.Quantity})" : $"{f.Label} ({f.Quantity}) - {f.Type}").ToList(), () => selected, v => selected = v)
        };
        if (!App.Dialogs.EditFields($"Changement du {date:dd/MM/yyyy} - « {belt.Location} »", fields)) return false;
        if (selected.Count == 0) return false;

        var changed = selected.Select(i => functions[i]).ToList();
        var tracked = App.Db.Belts.Include(b => b.Replacements).First(b => b.Id == belt.Id);
        tracked.Replacements.Add(new BeltReplacement
        {
            DateChanged = date,
            QuantityChanged = changed.Sum(c => c.Quantity),
            ChangedSoufflage = changed.Any(c => c.Label == "Soufflage"),
            ChangedExtraction = changed.Any(c => c.Label == "Extraction")
        });
        App.Db.SaveChanges();
        belt.Replacements = tracked.Replacements.Select(r => new BeltReplacement
        {
            Id = r.Id, BeltId = r.BeltId, QuantityChanged = r.QuantityChanged, DateChanged = r.DateChanged,
            ChangedSoufflage = r.ChangedSoufflage, ChangedExtraction = r.ChangedExtraction
        }).ToList();
        App.YearContext.EnsureYear(date.Year);
        return true;
    }

    protected override Belt CreateEntity(int? familyId) => new() { BeltFamilyId = familyId };

    protected override Belt CopyEntity(Belt source) => new()
    {
        Location = source.Location,
        BeltTypeSoufflage = source.BeltTypeSoufflage,
        BeltTypeExtraction = source.BeltTypeExtraction,
        QuantitySoufflage = source.QuantitySoufflage,
        QuantityExtraction = source.QuantityExtraction,
        Commentaire = source.Commentaire,
        BeltFamilyId = source.BeltFamilyId,
        RowColorId = source.RowColorId
    };

    protected override bool EditEntity(Belt entity, bool isNew)
    {
        var fields = new List<EditField>
        {
            FamilyField(entity.BeltFamilyId, id => entity.BeltFamilyId = id),
            EditField.Multiline("Nom de la centrale", () => entity.Location, v => entity.Location = v, required: true),
            EditField.NullableText("Type de courroies soufflage", () => entity.BeltTypeSoufflage, v => entity.BeltTypeSoufflage = v, uppercase: true),
            EditField.IntField("Nombre de courroies soufflage (0 = aucune)", () => entity.QuantitySoufflage, v => entity.QuantitySoufflage = v),
            EditField.NullableText("Type de courroies extraction", () => entity.BeltTypeExtraction, v => entity.BeltTypeExtraction = v, uppercase: true),
            EditField.IntField("Nombre de courroies extraction (0 = aucune)", () => entity.QuantityExtraction, v => entity.QuantityExtraction = v),
            EditField.Multiline("Commentaire", () => entity.Commentaire, v => entity.Commentaire = v)
        };
        if (!App.Dialogs.EditFields(isNew ? "Ajouter une courroie" : "Modifier la courroie", fields)) return false;

        // Règle : un type renseigné impose au moins 1 courroie, pas de type = forcément 0 courroie.
        var adjustments = new List<string>();
        (entity.QuantitySoufflage, var s) = ApplyTypeQuantityRule("soufflage", entity.BeltTypeSoufflage, entity.QuantitySoufflage);
        (entity.QuantityExtraction, var e) = ApplyTypeQuantityRule("extraction", entity.BeltTypeExtraction, entity.QuantityExtraction);
        if (s is not null) adjustments.Add(s);
        if (e is not null) adjustments.Add(e);
        if (adjustments.Count > 0)
            App.Dialogs.ShowMessage("Courroies", "Quantités ajustées selon le type renseigné :\n- " + string.Join("\n- ", adjustments));
        return true;
    }

    /// <summary>Règle type / quantité d'une fonction : un type renseigné impose une quantité d'au moins 1, l'absence
    /// de type impose 0. Retourne la quantité corrigée et, si elle a changé, la phrase qui l'explique.</summary>
    internal static (int Quantity, string? Message) ApplyTypeQuantityRule(string function, string? type, int quantity)
    {
        if (string.IsNullOrWhiteSpace(type))
            return quantity == 0 ? (0, null) : (0, $"{function} : pas de type, donc 0 courroie (au lieu de {quantity}).");
        return quantity >= 1 ? (quantity, null) : (1, $"{function} : type « {type.Trim()} » renseigné, donc au moins 1 courroie.");
    }

    private static string Qty(int quantity) => quantity > 0 ? quantity.ToString() : "-";

    [RelayCommand]
    private void Print()
    {
        var headers = new[] { "Nom de la centrale", "Type soufflage", "Nb soufflage", "Type extraction", "Nb extraction", $"Dernier changement ({YearContext.Year})", $"Nb remplacements ({YearContext.Year})", "Commentaire" };
        var rows = PrintService.BuildGroupedRows(Rows, b => b.FamilyGroupLabel, b => new[]
        {
            b.Location, b.BeltTypeSoufflage ?? "", Qty(b.QuantitySoufflage), b.BeltTypeExtraction ?? "", Qty(b.QuantityExtraction),
            b.LastChangedDateInYear?.ToString("dd/MM/yyyy") ?? "-", b.ReplacementCountInYear.ToString(), b.Commentaire ?? ""
        }, headers.Length);
        var rowColors = PrintService.BuildGroupedRowColors(Rows, b => b.FamilyGroupLabel, b => RowColorPalette.ColorFor(b.RowColorId));
        App.Printer.PrintTable(PrintTitle, headers, rows, rowColors: rowColors);
    }

    /// <summary>Remplacements de cette courroie précise (qté changée + date), par année.</summary>
    protected override void OpenHistory(Belt belt)
    {
        var replacements = App.Db.BeltReplacements.AsNoTracking().Where(r => r.BeltId == belt.Id).ToList();
        App.Dialogs.ShowReplacementHistory(new ReplacementHistory<BeltReplacement>(
            $"Nom de la centrale : {belt.Location}",
            "Aucun historique disponible pour cette courroie : aucun remplacement enregistré en base pour l'instant.",
            "Courroies changées", r => r.ChangedLabel, r => r.ChangedLabel,
            DateColumnWidth: 1.2, DetailColumnWidth: 1.6), replacements);
    }

    // ---- Familles (partie commune : TrackedItemListViewModel.Families.cs) ----

    protected override List<BeltFamily> LoadFamilies() => App.Db.BeltFamilies.AsNoTracking().OrderBy(f => f.Nom).ToList();

    protected override BeltFamily CreateFamily() => new();

    protected override bool FamilyNameExists(string name, int excludedId) =>
        App.Db.BeltFamilies.AsNoTracking().Any(f => f.Id != excludedId && f.Nom.ToLower() == name.ToLower());

    protected override int CountItemsInFamily(int familyId) => App.Db.Belts.Count(b => b.BeltFamilyId == familyId);

    protected override void DeleteFamilyKeepingItems(int familyId)
    {
        App.Db.Belts.Where(b => b.BeltFamilyId == familyId).ExecuteUpdate(s => s.SetProperty(b => b.BeltFamilyId, (int?)null));
        App.Db.BeltFamilies.Where(f => f.Id == familyId).ExecuteDelete();
    }

    protected override string ItemsLabel => "courroies";

    protected override string FamilyDeleteDetail(int count) =>
        $"Ses {count} courroie(s) ne seront pas supprimées : elles passeront en « {Belt.NoFamilyLabel} ».";
}
