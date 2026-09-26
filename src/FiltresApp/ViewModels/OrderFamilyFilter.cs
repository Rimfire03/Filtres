using CommunityToolkit.Mvvm.ComponentModel;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.ViewModels;

/// <summary>Familles des écrans Inventaire et Commande : filtre d'affichage (chaque écran garde son propre
/// choix), ordre des groupes, et choix manuel de famille. Les familles sont celles des filtres à
/// périodicité (G4 plissés, G4 plan, G3, Charbon) et les Types saisis sur les écrans "Filtres F7 à H14"
/// (voir <see cref="OrderLine.FamilyGroupLabel"/>).</summary>
public partial class OrderFamilyFilter : ObservableObject
{
    private const string AllLabel = "Toutes les familles";

    private static readonly string[] CategoryFamilies =
        Enum.GetValues<FilterCategory>().Select(OrderLine.FamilyLabelFor).ToArray();

    private readonly Action _onFilterChanged;
    private bool _updatingOptions;

    [ObservableProperty] private List<string> _options = new() { AllLabel };
    [ObservableProperty] private string _selected = AllLabel;

    public OrderFamilyFilter(Action onFilterChanged) => _onFilterChanged = onFilterChanged;

    partial void OnSelectedChanged(string value)
    {
        if (!_updatingOptions) _onFilterChanged();
    }

    /// <summary>Types saisis sur les écrans "Filtres F7 à H14" (familles possibles dans Commande /
    /// Inventaire), toutes variétés confondues.</summary>
    public static List<string> LoadDynamicTypes() =>
        App.Db.DynamicFilters.AsNoTracking()
            .Select(f => f.FilterType)
            .ToList()
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Lignes (filtres rattachés chargés) filtrées puis triées par famille, puis par ordre. Met aussi
    /// à jour la liste du filtre avec les familles connues.</summary>
    public List<OrderLine> Apply(IEnumerable<OrderLine> lines)
    {
        var list = lines.ToList();
        var order = BuildOrder(list);
        UpdateOptions(order);
        return list.Where(Matches)
            .OrderBy(l => order.IndexOf(l.FamilyGroupLabel))
            .ThenBy(l => l.Ordre)
            .ToList();
    }

    /// <summary>Ordre des familles : catégories, Types "Filtres F7 à H14" (plus d'éventuels types choisis
    /// manuellement qui n'existent plus), "Plusieurs familles", "Sans famille".</summary>
    private static List<string> BuildOrder(IEnumerable<OrderLine> lines)
    {
        var order = CategoryFamilies.ToList();
        var types = LoadDynamicTypes()
            .Concat(lines.Select(l => l.FamilyGroupLabel))
            .Where(f => !CategoryFamilies.Contains(f) && f != OrderLine.MultipleFamiliesLabel && f != OrderLine.NoFamilyLabel)
            .Distinct()
            .OrderBy(t => t, StringComparer.CurrentCultureIgnoreCase);
        order.AddRange(types);
        order.Add(OrderLine.MultipleFamiliesLabel);
        order.Add(OrderLine.NoFamilyLabel);
        return order;
    }

    private void UpdateOptions(List<string> order)
    {
        var options = new[] { AllLabel }.Concat(order).ToList();
        if (options.SequenceEqual(Options)) return;

        var keep = Selected;
        _updatingOptions = true;
        try
        {
            Options = options;
            Selected = options.Contains(keep) ? keep : AllLabel;
        }
        finally
        {
            _updatingOptions = false;
        }
    }

    /// <summary>True si la ligne correspond au filtre de famille actuellement choisi (toujours vrai si
    /// "Toutes les familles" est sélectionné).</summary>
    public bool Matches(OrderLine line) => Selected == AllLabel || line.FamilyGroupLabel == Selected;

    // ---- Choix manuel de la famille ----

    private sealed record ManualChoice(string Label, int Override, string? Type);

    /// <summary>Familles au choix manuel : catégories, Types "Filtres F7 à H14", "Sans famille".</summary>
    private static List<ManualChoice> ManualChoices() =>
        Enum.GetValues<FilterCategory>().Select(c => new ManualChoice(OrderLine.FamilyLabelFor(c), (int)c, null))
            .Concat(LoadDynamicTypes().Select(t => new ManualChoice(t, OrderLine.DynamicTypeOverride, t)))
            .Append(new ManualChoice(OrderLine.NoFamilyLabel, OrderLine.NoFamilyOverride, null))
            .ToList();

    private static void ApplyChoice(OrderLine entity, ManualChoice? choice)
    {
        entity.FamilyOverride = choice?.Override;
        entity.FamilyOverrideType = choice?.Type;
    }

    /// <summary>Nouvelle ligne : famille du filtre en cours si c'en est une, sinon automatique.</summary>
    public void ApplyDefaultFamily(OrderLine entity) =>
        ApplyChoice(entity, ManualChoices().FirstOrDefault(c => c.Label == Selected));

    /// <summary>Choix proposés par la colonne "Famille" de l'écran Commande : "Automatique" puis les
    /// familles au choix manuel.</summary>
    public static List<string> QuickChoices() =>
        new[] { OrderLine.AutomaticFamilyChoice }.Concat(ManualChoices().Select(c => c.Label)).ToList();

    /// <summary>Applique un choix de <see cref="QuickChoices"/> ("Automatique" = famille automatique).</summary>
    public static void ApplyQuickChoice(OrderLine entity, string choice) =>
        ApplyChoice(entity, ManualChoices().FirstOrDefault(c => c.Label == choice));

    /// <summary>Champ "Famille" de la fenêtre Ajouter / Modifier : "Automatique" (famille déduite des filtres
    /// rattachés, rappelée entre parenthèses) ou une famille choisie manuellement.</summary>
    public static EditField CreateEditField(OrderLine entity, string automaticLabel)
    {
        var choices = ManualChoices();
        var names = new List<string> { $"Automatique (d'après les filtres rattachés : {automaticLabel})" };
        names.AddRange(choices.Select(c => c.Label));

        var currentIndex = entity.FamilyOverride is null
            ? 0
            : choices.FindIndex(c => c.Override == entity.FamilyOverride
                && (c.Type is null || string.Equals(c.Type, entity.FamilyOverrideType?.Trim(), StringComparison.CurrentCultureIgnoreCase))) + 1;

        return EditField.ComboField("Famille", names, () => currentIndex,
            v => ApplyChoice(entity, v >= 1 && v <= choices.Count ? choices[v - 1] : null));
    }

    /// <summary>Ajouté au titre des impressions / exports quand un filtre est actif.</summary>
    public string TitleSuffix => Selected == AllLabel ? "" : " - " + Selected;
}
