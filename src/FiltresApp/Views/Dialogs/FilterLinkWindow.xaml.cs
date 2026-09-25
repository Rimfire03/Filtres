using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FiltresApp.Core.Models;
using FiltresApp.ViewModels;

namespace FiltresApp.Views.Dialogs;

/// <summary>Sélecteur de rattachement manuel : par défaut, liste uniquement les filtres périodiques (G4
/// plissé, G4 plan, G3, Charbon) dont la dimension correspond à celle de la ligne de commande en cours
/// (voir <see cref="Core.Services.DimensionMatchService"/>), avec une case à cocher, pour choisir
/// explicitement ceux à rattacher à une ligne de "Commande chmy" / "pour devis" (voir
/// <see cref="ViewModels.OrderListViewModel"/>). Une case "Afficher tous les filtres" permet de voir
/// toutes les dimensions en secours. Les filtres déjà rattachés à une autre ligne sont signalés en
/// rouge.</summary>
public partial class FilterLinkWindow : Window
{
    private readonly ObservableCollection<FilterPickItem> _allItems;
    private readonly ICollectionView _view;
    private readonly bool _hasAnyDimensionMatch;

    public List<FilterRef> SelectedFilters { get; private set; } = new();

    public FilterLinkWindow(List<FilterPickItem> items)
    {
        InitializeComponent();
        _allItems = new ObservableCollection<FilterPickItem>(items);
        Grid.ItemsSource = _allItems;
        _view = System.Windows.Data.CollectionViewSource.GetDefaultView(_allItems);

        _hasAnyDimensionMatch = _allItems.Any(i => i.DimensionMatches);

        DimensionInfoText.Text = !App.Settings.LinkDimensionFilterEnabled
            ? "Filtre par dimension désactivé dans Paramètres : tous les filtres sont affichés."
            : _hasAnyDimensionMatch
                ? $"Seuls les filtres dont la dimension correspond à celle de cette ligne de commande (à ±{Core.Services.DimensionMatchService.ToleranceMm} mm près, largeur et hauteur interchangeables) sont affichés par défaut."
                : "Dimension non renseignée, ou aucun filtre de dimension correspondante trouvé pour cette ligne.";

        // Déclenche ApplyFilter via l'événement Checked : à faire après l'initialisation de _view.
        if (!App.Settings.LinkDimensionFilterEnabled) ShowAllCheckBox.IsChecked = true;

        ApplyFilter();
        UpdateSelectionCount();
        foreach (var item in _allItems) item.PropertyChanged += (_, _) => UpdateSelectionCount();
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ShowAllCheckBox_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var text = SearchBox.Text.Trim().ToLowerInvariant();
        var showAll = ShowAllCheckBox.IsChecked == true;

        _view.Filter = o =>
        {
            if (o is not FilterPickItem item) return false;
            // Un filtre déjà rattaché à CETTE ligne reste toujours visible (même si sa dimension ne
            // correspond plus, ex. dimension modifiée après coup), pour ne jamais masquer un rattachement
            // existant à l'utilisateur au risque qu'il le décoche par erreur sans le voir.
            if (!showAll && !item.DimensionMatches && !item.IsSelected) return false;
            return string.IsNullOrEmpty(text) || item.SearchText.Contains(text);
        };

        NoMatchText.Visibility = !showAll && !_hasAnyDimensionMatch ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSelectionCount()
    {
        var count = _allItems.Count(i => i.IsSelected);
        SelectionCountText.Text = $"{count} filtre(s) sélectionné(s)";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        SelectedFilters = _allItems.Where(i => i.IsSelected).Select(i => i.Ref).ToList();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
