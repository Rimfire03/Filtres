using CommunityToolkit.Mvvm.ComponentModel;
using FiltresApp.Core.Models;
using FiltresApp.Services;

namespace FiltresApp.ViewModels;

/// <summary>Filtre par famille des écrans Inventaire et Commande. Les familles ne sont pas saisies : elles
/// découlent de la catégorie des filtres rattachés à chaque ligne (voir <see cref="OrderLine.FamilyGroupLabel"/>).
/// Chaque écran garde son propre choix de filtre.</summary>
public partial class OrderFamilyFilter : ObservableObject
{
    private const string AllLabel = "Toutes les familles";

    private readonly Action _onFilterChanged;

    /// <summary>Ordre d'affichage des groupes dans les grilles et de la liste du filtre.</summary>
    public static readonly string[] FamilyOrder =
    {
        OrderLine.FamilyLabelFor(FilterCategory.G4Plisse),
        OrderLine.FamilyLabelFor(FilterCategory.G4Plan),
        OrderLine.FamilyLabelFor(FilterCategory.G3),
        OrderLine.FamilyLabelFor(FilterCategory.Charbon),
        OrderLine.OpacimetricFamilyLabel,
        OrderLine.MultipleFamiliesLabel,
        OrderLine.NoFamilyLabel
    };

    public List<string> Options { get; } = new[] { AllLabel }.Concat(FamilyOrder).ToList();

    [ObservableProperty] private string _selected = AllLabel;

    public OrderFamilyFilter(Action onFilterChanged) => _onFilterChanged = onFilterChanged;

    partial void OnSelectedChanged(string value) => _onFilterChanged();

    /// <summary>Lignes (filtres rattachés chargés) filtrées puis triées par famille, puis par ordre.</summary>
    public List<OrderLine> Apply(IEnumerable<OrderLine> lines) =>
        lines.Where(l => Selected == AllLabel || l.FamilyGroupLabel == Selected)
            .OrderBy(l => Array.IndexOf(FamilyOrder, l.FamilyGroupLabel))
            .ThenBy(l => l.Ordre)
            .ToList();

    /// <summary>Choix de famille manuel pour une nouvelle ligne : celle du filtre en cours si c'est une famille
    /// de filtres, sinon automatique.</summary>
    public int? DefaultFamilyOverride
    {
        get
        {
            var choice = ManualChoices.FirstOrDefault(c => c.Label == Selected);
            return choice.Label is null ? null : choice.Override;
        }
    }

    /// <summary>Familles proposées au choix manuel, avec la valeur de <see cref="OrderLine.FamilyOverride"/>
    /// correspondante.</summary>
    private static readonly (string Label, int Override)[] ManualChoices =
        Enum.GetValues<FilterCategory>().Select(c => (OrderLine.FamilyLabelFor(c), (int)c))
            .Append((OrderLine.OpacimetricFamilyLabel, OrderLine.OpacimetricFamilyOverride))
            .Append((OrderLine.NoFamilyLabel, OrderLine.NoFamilyOverride))
            .ToArray();

    /// <summary>Choix proposés par la colonne "Famille" de l'écran Commande : "Automatique" puis les
    /// familles au choix manuel.</summary>
    public static IReadOnlyList<string> QuickChoices { get; } =
        new[] { OrderLine.AutomaticFamilyChoice }.Concat(ManualChoices.Select(c => c.Label)).ToList();

    /// <summary>Valeur de <see cref="OrderLine.FamilyOverride"/> correspondant à un choix de
    /// <see cref="QuickChoices"/> (null = automatique).</summary>
    public static int? OverrideForChoice(string choice)
    {
        var match = ManualChoices.FirstOrDefault(c => c.Label == choice);
        return match.Label is null ? null : match.Override;
    }

    /// <summary>Champ "Famille" de la fenêtre Ajouter / Modifier : "Automatique" (famille déduite des filtres
    /// rattachés, rappelée entre parenthèses) ou une famille choisie manuellement.</summary>
    public static EditField CreateEditField(OrderLine entity, string automaticLabel)
    {
        var names = new List<string> { $"Automatique (d'après les filtres rattachés : {automaticLabel})" };
        names.AddRange(ManualChoices.Select(c => c.Label));
        var currentIndex = entity.FamilyOverride is int value
            ? Array.FindIndex(ManualChoices, c => c.Override == value) + 1
            : 0;

        return EditField.ComboField("Famille", names, () => currentIndex, v => entity.FamilyOverride =
            v >= 1 && v <= ManualChoices.Length ? ManualChoices[v - 1].Override : null);
    }

    /// <summary>Ajouté au titre des impressions / exports quand un filtre est actif.</summary>
    public string TitleSuffix => Selected == AllLabel ? "" : " - " + Selected;

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
}
