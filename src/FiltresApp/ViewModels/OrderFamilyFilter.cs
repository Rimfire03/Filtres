using CommunityToolkit.Mvvm.ComponentModel;
using FiltresApp.Core.Models;

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
