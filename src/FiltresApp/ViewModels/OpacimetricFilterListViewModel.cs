using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

public partial class OpacimetricFilterListViewModel : ObservableObject
{
    public string Title => "Filtres F7 à H13";

    /// <summary>Cette feuille n'a pas de périodicité mensuelle fixe (contrairement aux 4 autres
    /// catégories) : c'est un historique de remplacements ponctuels. L'année consultée ne filtre donc
    /// pas des lignes de suivi mensuel, mais restreint "Dernier changement" / "Nb remplacements" aux
    /// remplacements datés de cette année-là ; l'historique complet reste toujours en base et l'export
    /// Excel de l'année inclut cette catégorie comme les 4 autres.</summary>
    public YearContext YearContext => App.YearContext;

    [ObservableProperty] private ObservableCollection<OpacimetricFilterRowViewModel> _filters = new();
    [ObservableProperty] private OpacimetricFilterRowViewModel? _selectedFilter;

    // ---- Familles (créées à la main, attribuées manuellement à chaque filtre) ----

    public record FamilyOption(int? FamilyId, bool IsNoFamily, string Label)
    {
        public bool IsAll => FamilyId is null && !IsNoFamily;
    }

    private static readonly FamilyOption AllFamilies = new(null, false, "Toutes les familles");
    private static readonly FamilyOption NoFamily = new(null, true, OpacimetricFilter.NoFamilyLabel);

    public List<OpacimetricFamily> Families { get; private set; } = new();
    [ObservableProperty] private List<FamilyOption> _familyOptions = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFamilySelected))]
    [NotifyCanExecuteChangedFor(nameof(RenameFamilyCommand), nameof(DeleteFamilyCommand))]
    private FamilyOption? _selectedFamily;

    public bool IsFamilySelected => SelectedFamily?.FamilyId is not null;

    private bool _refreshingFamilies;

    public OpacimetricFilterListViewModel()
    {
        App.YearContext.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Services.YearContext.Year)) Load();
        };
        RefreshFamilies();
        Load();
    }

    partial void OnSelectedFamilyChanged(FamilyOption? value)
    {
        if (!_refreshingFamilies) Load();
    }

    /// <summary>Relit les familles en conservant le filtre choisi s'il existe toujours.</summary>
    private void RefreshFamilies(int? selectFamilyId = null)
    {
        Families = App.Db.OpacimetricFamilies.AsNoTracking().OrderBy(f => f.Nom).ToList();
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

    private void Load()
    {
        var query = App.Db.OpacimetricFilters
            .Include(f => f.Replacements)
            .Include(f => f.Family)
            .AsNoTracking();

        var selected = SelectedFamily ?? AllFamilies;
        if (selected.IsNoFamily) query = query.Where(f => f.OpacimetricFamilyId == null);
        else if (selected.FamilyId is int familyId) query = query.Where(f => f.OpacimetricFamilyId == familyId);

        // Familles par nom, "Sans famille" en dernier : ordre des séparateurs de la grille.
        var all = query
            .OrderBy(f => f.OpacimetricFamilyId == null)
            .ThenBy(f => f.Family!.Nom)
            .ThenBy(f => f.Location)
            .ToList();
        Filters = new ObservableCollection<OpacimetricFilterRowViewModel>(
            all.Select(f => new OpacimetricFilterRowViewModel(f, YearContext.Year, this)));
    }

    [RelayCommand]
    private void AddFamily()
    {
        if (!App.GuardWritable()) return;
        var family = new OpacimetricFamily();
        if (!EditFamilyName(family, "Nouvelle famille F7 à H13")) return;
        App.Db.OpacimetricFamilies.Add(family);
        App.Db.SaveChanges();
        RefreshFamilies(family.Id);
        Load();
    }

    [RelayCommand(CanExecute = nameof(IsFamilySelected))]
    private void RenameFamily()
    {
        if (!App.GuardWritable() || SelectedFamily?.FamilyId is not int id) return;
        var tracked = App.Db.OpacimetricFamilies.First(f => f.Id == id);
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
        var count = App.Db.OpacimetricFilters.Count(f => f.OpacimetricFamilyId == id);
        var detail = count == 0 ? "" : $"\n\nSes {count} filtre(s) ne seront pas supprimés : ils passeront en « {OpacimetricFilter.NoFamilyLabel} ».";
        if (!App.Dialogs.ShowConfirm("Supprimer la famille", $"Supprimer la famille « {SelectedFamily.Label} » ?{detail}")) return;

        App.Db.OpacimetricFilters.Where(f => f.OpacimetricFamilyId == id)
            .ExecuteUpdate(s => s.SetProperty(f => f.OpacimetricFamilyId, (int?)null));
        App.Db.OpacimetricFamilies.Where(f => f.Id == id).ExecuteDelete();
        // Les filtres éventuellement suivis par le contexte gardent l'ancienne famille en mémoire.
        App.Db.ChangeTracker.Clear();

        RefreshFamilies();
        Load();
    }

    private bool EditFamilyName(OpacimetricFamily family, string title)
    {
        var fields = new List<EditField>
        {
            EditField.Text("Nom de la famille", () => family.Nom, v => family.Nom = v.Trim(), required: true)
        };
        if (!App.Dialogs.EditFields(title, fields)) return false;

        var name = family.Nom;
        string? error = null;
        if (string.Equals(name, OpacimetricFilter.NoFamilyLabel, StringComparison.OrdinalIgnoreCase))
            error = $"« {OpacimetricFilter.NoFamilyLabel} » est réservé aux filtres sans famille : choisissez un autre nom.";
        else if (App.Db.OpacimetricFamilies.AsNoTracking().Any(f => f.Id != family.Id && f.Nom.ToLower() == name.ToLower()))
            error = $"Une famille « {name} » existe déjà.";

        if (error is null) return true;
        App.Dialogs.ShowMessage("Famille", error);
        // Le renommage refusé ne doit pas rester en mémoire sur l'entité suivie.
        if (family.Id != 0) App.Db.Entry(family).Reload();
        return false;
    }

    [RelayCommand]
    private void AddFilter()
    {
        if (!App.GuardWritable()) return;
        // Nouveau filtre rangé par défaut dans la famille filtrée.
        var entity = new OpacimetricFilter { OpacimetricFamilyId = SelectedFamily?.FamilyId };
        if (!EditEntity(entity, true)) return;
        App.Db.OpacimetricFilters.Add(entity);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void EditFilter()
    {
        if (!App.GuardWritable()) return;
        if (SelectedFilter is null) return;
        var tracked = App.Db.OpacimetricFilters.Include(f => f.Replacements).First(f => f.Id == SelectedFilter.Id);
        if (!EditEntity(tracked, false)) return;
        App.Db.SaveChanges();
        Load();
    }

    private bool EditEntity(OpacimetricFilter entity, bool isNew)
    {
        var families = Families;
        var familyNames = new List<string> { OpacimetricFilter.NoFamilyLabel };
        familyNames.AddRange(families.Select(f => f.Nom));
        var familyIndex = entity.OpacimetricFamilyId is int id ? families.FindIndex(f => f.Id == id) + 1 : 0;

        var fields = new List<EditField>
        {
            EditField.ComboField("Famille", familyNames, () => familyIndex,
                v => entity.OpacimetricFamilyId = v >= 1 && v <= families.Count ? families[v - 1].Id : null),
            EditField.Text("Nom de la centrale d'air", () => entity.Location, v => entity.Location = v, required: true),
            EditField.Text("Dimension", () => entity.Dimension, v => entity.Dimension = v),
            EditField.NullableText("Type", () => entity.FilterType, v => entity.FilterType = v),
            EditField.IntField("Quantité en place", () => entity.QuantityInPlace, v => entity.QuantityInPlace = v),
            EditField.Multiline("Notes", () => entity.Notes, v => entity.Notes = v)
        };
        return App.Dialogs.EditFields(isNew ? "Ajouter un filtre F7/H13" : "Modifier le filtre", fields);
    }

    [RelayCommand]
    private void DeleteFilter()
    {
        if (!App.GuardWritable()) return;
        if (SelectedFilter is null) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer définitivement '{SelectedFilter.Location}' ?")) return;
        var tracked = App.Db.OpacimetricFilters.First(f => f.Id == SelectedFilter.Id);
        App.Db.OpacimetricFilters.Remove(tracked);
        App.Db.SaveChanges();
        Load();
    }

    [RelayCommand]
    private void MarkReplacement()
    {
        if (!App.GuardWritable()) return;
        if (SelectedFilter is null) return;
        var picked = App.Dialogs.PickSimpleReplacement(SelectedFilter.QuantityInPlace);
        if (picked is null) return;

        var tracked = App.Db.OpacimetricFilters.Include(f => f.Replacements).First(f => f.Id == SelectedFilter.Id);
        tracked.Replacements.Add(new OpacimetricReplacement
        {
            QuantityChanged = picked.Value.Quantity,
            DateChanged = picked.Value.Date
        });
        App.Db.SaveChanges();
        App.YearContext.EnsureYear(picked.Value.Date.Year);
        Load();
    }

    [RelayCommand]
    private void Print()
    {
        var headers = new[] { "Nom de la centrale d'air", "Dimension", "Type", "Qté en place", $"Dernier changement ({YearContext.Year})", $"Nb remplacements ({YearContext.Year})" };
        // Une ligne titre avant chaque famille, à la place d'une colonne Famille (lignes déjà triées).
        var rows = new List<string[]>();
        string? currentGroup = null;
        foreach (var f in Filters)
        {
            if (f.FamilyGroupLabel != currentGroup)
            {
                currentGroup = f.FamilyGroupLabel;
                var title = new string[headers.Length];
                Array.Fill(title, "");
                title[0] = "— " + currentGroup.ToUpperInvariant() + " —";
                rows.Add(title);
            }
            rows.Add(new[]
            {
                f.Location, f.Dimension, f.FilterType ?? "", f.QuantityInPlace.ToString(),
                f.LastChangedDateInYear?.ToString("dd/MM/yyyy") ?? "-", f.ReplacementCountInYear.ToString()
            });
        }
        var documentTitle = SelectedFamily is null || SelectedFamily.IsAll ? Title : $"{Title} - {SelectedFamily.Label}";
        App.Printer.PrintTable(documentTitle, headers, rows);
    }

    [RelayCommand]
    private void ExportExcelYear()
    {
        var path = App.ExcelExport.ExportYear(App.Db, App.Settings.ResolvedPdfExportPath, YearContext.Year);
        App.Dialogs.ShowMessage("Export Excel",
            $"Export de l'année {YearContext.Year} généré avec succès (toutes catégories, une feuille par catégorie).\n\nIl est stocké dans :\n{path}");
    }

    /// <summary>"Consulter l'historique..." (menu contextuel de la grille) : ouvre une fenêtre de lecture
    /// seule listant, pour l'année choisie parmi celles où ce filtre précis a de l'historique, la liste
    /// chronologique des remplacements (qté changée + date). Cette feuille n'a pas de mois fixe (voir
    /// Views.Dialogs.OpacimetricHistoryWindow).</summary>
    public void ShowHistory(OpacimetricFilter filter)
    {
        var replacements = App.Db.OpacimetricReplacements
            .AsNoTracking()
            .Where(r => r.OpacimetricFilterId == filter.Id)
            .ToList();
        App.Dialogs.ShowOpacimetricHistory(filter.Location, filter.Dimension, replacements);
    }
}
