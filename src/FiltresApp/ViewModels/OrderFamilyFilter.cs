using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

public enum FamilyFilterKind
{
    All,
    None,
    Family
}

public record FamilyFilterOption(FamilyFilterKind Kind, int? FamilyId, string Label);

/// <summary>Filtre par famille et gestion des familles (création, renommage, suppression), partagés par
/// les écrans Inventaire et Commande, qui affichent les mêmes lignes <see cref="OrderLine"/>. Chaque écran
/// garde son propre choix de filtre.</summary>
public partial class OrderFamilyFilter : ObservableObject
{
    private readonly Action _onFilterChanged;
    private bool _refreshing;

    [ObservableProperty] private List<FamilyFilterOption> _options = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFamilySelected))]
    [NotifyCanExecuteChangedFor(nameof(RenameFamilyCommand), nameof(DeleteFamilyCommand))]
    private FamilyFilterOption? _selected;

    public List<OrderFamily> Families { get; private set; } = new();

    public bool IsFamilySelected => Selected?.Kind == FamilyFilterKind.Family;

    public OrderFamilyFilter(Action onFilterChanged)
    {
        _onFilterChanged = onFilterChanged;
        Refresh();
    }

    partial void OnSelectedChanged(FamilyFilterOption? value)
    {
        if (!_refreshing) _onFilterChanged();
    }

    /// <summary>Relit les familles en base (elles ont pu changer depuis l'autre écran) en conservant le
    /// filtre choisi s'il existe toujours, sans déclencher de rechargement.</summary>
    public void Refresh()
    {
        Families = App.Db.OrderFamilies.AsNoTracking().OrderBy(f => f.Nom).ToList();
        var options = new List<FamilyFilterOption>
        {
            new(FamilyFilterKind.All, null, "Toutes les familles"),
            new(FamilyFilterKind.None, null, "Sans famille")
        };
        options.AddRange(Families.Select(f => new FamilyFilterOption(FamilyFilterKind.Family, f.Id, f.Nom)));

        var previous = Selected;
        _refreshing = true;
        try
        {
            Options = options;
            Selected = options.FirstOrDefault(o => previous is not null && o.Kind == previous.Kind && o.FamilyId == previous.FamilyId)
                       ?? options[0];
        }
        finally
        {
            _refreshing = false;
        }
    }

    public IQueryable<OrderLine> Apply(IQueryable<OrderLine> lines)
    {
        var familyId = Selected?.FamilyId;
        return Selected?.Kind switch
        {
            FamilyFilterKind.None => lines.Where(l => l.OrderFamilyId == null),
            FamilyFilterKind.Family => lines.Where(l => l.OrderFamilyId == familyId),
            _ => lines
        };
    }

    /// <summary>Famille proposée par défaut pour une nouvelle ligne : celle du filtre en cours.</summary>
    public int? DefaultFamilyId => IsFamilySelected ? Selected!.FamilyId : null;

    /// <summary>Ajouté au titre des impressions / exports quand un filtre est actif.</summary>
    public string TitleSuffix => Selected?.Kind switch
    {
        FamilyFilterKind.None => " - Sans famille",
        FamilyFilterKind.Family => " - " + Selected.Label,
        _ => ""
    };

    /// <summary>Tri des lignes pour l'affichage par famille : familles par nom, "Sans famille" en dernier.</summary>
    public static IQueryable<OrderLine> OrderByFamily(IQueryable<OrderLine> lines) =>
        lines.OrderBy(l => l.OrderFamilyId == null).ThenBy(l => l.Family!.Nom).ThenBy(l => l.Ordre);

    /// <summary>Lignes d'impression / PDF avec, à la place d'une colonne Famille, une ligne titre avant
    /// chaque famille (lignes déjà triées par famille).</summary>
    public static List<string[]> BuildGroupedRows(IEnumerable<OrderLine> lines, Func<OrderLine, string[]> buildRow, int columnCount)
    {
        var rows = new List<string[]>();
        string? currentGroup = null;
        foreach (var line in lines)
        {
            if (line.FamilyGroupLabel != currentGroup)
            {
                currentGroup = line.FamilyGroupLabel;
                var title = new string[columnCount];
                Array.Fill(title, "");
                title[0] = "— " + currentGroup.ToUpperInvariant() + " —";
                rows.Add(title);
            }
            rows.Add(buildRow(line));
        }
        return rows;
    }

    public EditField CreateEditField(OrderLine entity)
    {
        var families = Families;
        var names = new List<string> { "(Aucune)" };
        names.AddRange(families.Select(f => f.Nom));
        var currentIndex = entity.OrderFamilyId is int id ? families.FindIndex(f => f.Id == id) + 1 : 0;
        return EditField.ComboField("Famille", names, () => currentIndex,
            v => entity.OrderFamilyId = v >= 1 && v <= families.Count ? families[v - 1].Id : null);
    }

    [RelayCommand]
    private void AddFamily()
    {
        if (!App.GuardWritable()) return;
        var family = new OrderFamily();
        if (!EditName(family, "Nouvelle famille")) return;

        App.Db.OrderFamilies.Add(family);
        App.Db.SaveChanges();
        Refresh();
        Selected = Options.First(o => o.FamilyId == family.Id);
    }

    [RelayCommand(CanExecute = nameof(IsFamilySelected))]
    private void RenameFamily()
    {
        if (!App.GuardWritable() || Selected?.FamilyId is not int id) return;
        var tracked = App.Db.OrderFamilies.First(f => f.Id == id);
        if (!EditName(tracked, "Renommer la famille")) return;

        App.Db.SaveChanges();
        Refresh();
        _onFilterChanged();
    }

    /// <summary>Les lignes de la famille ne sont pas supprimées : elles deviennent "Sans famille".</summary>
    [RelayCommand(CanExecute = nameof(IsFamilySelected))]
    private void DeleteFamily()
    {
        if (!App.GuardWritable() || Selected?.FamilyId is not int id) return;
        var count = App.Db.OrderLines.Count(l => l.OrderFamilyId == id);
        var detail = count == 0 ? "" : $"\n\nSes {count} ligne(s) ne seront pas supprimées : elles passeront en « Sans famille ».";
        if (!App.Dialogs.ShowConfirm("Supprimer la famille", $"Supprimer la famille « {Selected.Label} » ?{detail}")) return;

        App.Db.OrderLines.Where(l => l.OrderFamilyId == id)
            .ExecuteUpdate(s => s.SetProperty(l => l.OrderFamilyId, (int?)null));
        App.Db.OrderFamilies.Where(f => f.Id == id).ExecuteDelete();
        // Les lignes éventuellement suivies par le contexte gardent l'ancienne famille en mémoire.
        App.Db.ChangeTracker.Clear();

        Refresh();
        _onFilterChanged();
    }

    private bool EditName(OrderFamily family, string title)
    {
        var fields = new List<EditField>
        {
            EditField.Text("Nom de la famille", () => family.Nom, v => family.Nom = v.Trim(), required: true)
        };
        if (!App.Dialogs.EditFields(title, fields)) return false;

        var name = family.Nom;
        if (string.Equals(name, OrderLine.NoFamilyLabel, StringComparison.OrdinalIgnoreCase))
        {
            App.Dialogs.ShowMessage("Famille", $"« {OrderLine.NoFamilyLabel} » est réservé au groupe des lignes sans famille : choisissez un autre nom.");
            if (family.Id != 0) App.Db.Entry(family).Reload();
            return false;
        }
        var exists = App.Db.OrderFamilies.AsNoTracking()
            .Any(f => f.Id != family.Id && f.Nom.ToLower() == name.ToLower());
        if (!exists) return true;

        App.Dialogs.ShowMessage("Famille", $"Une famille « {name} » existe déjà.");
        // Le renommage refusé ne doit pas rester en mémoire sur l'entité suivie.
        if (family.Id != 0) App.Db.Entry(family).Reload();
        return false;
    }
}
