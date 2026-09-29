using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace FiltresApp.ViewModels.Filtres;

/// <summary>Filtres placés sous les titres de colonnes des écrans à périodicité et F7 à H13 : "contient" sur
/// le nom (sans tenir compte des majuscules ni des accents) et liste des dimensions présentes (modèles
/// <c>NameFilterHeaderTemplate</c> / <c>DimensionFilterHeaderTemplate</c> de Styles/Controls.xaml).</summary>
public partial class NameDimensionFilter : ObservableObject
{
    public const string AllDimensions = "Toutes";

    private readonly Action _onFilterChanged;
    private bool _refreshingDimensions;

    [ObservableProperty] private string _nameFilter = string.Empty;

    /// <summary>"Toutes", puis les dimensions présentes sur l'écran.</summary>
    [ObservableProperty] private List<string> _dimensionOptions = new() { AllDimensions };
    [ObservableProperty] private string _selectedDimension = AllDimensions;

    public NameDimensionFilter(Action onFilterChanged) => _onFilterChanged = onFilterChanged;

    partial void OnNameFilterChanged(string value) => _onFilterChanged();

    partial void OnSelectedDimensionChanged(string value)
    {
        if (!_refreshingDimensions) _onFilterChanged();
    }

    /// <summary>Met à jour la liste des dimensions en conservant le choix courant s'il existe encore (sans
    /// déclencher de nouveau filtrage : l'appelant filtre ensuite).</summary>
    public void RefreshDimensions(IEnumerable<string> dimensions)
    {
        var options = new List<string> { AllDimensions };
        options.AddRange(dimensions.Select(d => d.Trim()).Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d, StringComparer.CurrentCultureIgnoreCase));

        _refreshingDimensions = true;
        try
        {
            var previous = SelectedDimension;
            DimensionOptions = options;
            SelectedDimension = options.FirstOrDefault(o => string.Equals(o, previous, StringComparison.OrdinalIgnoreCase)) ?? AllDimensions;
        }
        finally
        {
            _refreshingDimensions = false;
        }
    }

    public bool Matches(string name, string dimension)
    {
        var text = NameFilter.Trim();
        const CompareOptions ignore = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        return (text.Length == 0 || CultureInfo.CurrentCulture.CompareInfo.IndexOf(name, text, ignore) >= 0)
            && (SelectedDimension == AllDimensions || string.Equals(dimension.Trim(), SelectedDimension, StringComparison.OrdinalIgnoreCase));
    }

    public void Reset()
    {
        NameFilter = string.Empty;
        SelectedDimension = AllDimensions;
    }

    /// <summary>Ajouté au titre des impressions quand un filtre est actif.</summary>
    public string TitleSuffix
    {
        get
        {
            var suffix = SelectedDimension != AllDimensions ? $" - Dimension {SelectedDimension}" : "";
            if (NameFilter.Trim().Length > 0) suffix += $" - Nom contenant « {NameFilter.Trim()} »";
            return suffix;
        }
    }
}
