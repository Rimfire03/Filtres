using System.Windows;
using FiltresApp.Core.Models;

namespace FiltresApp.Views.Dialogs;

/// <summary>Fenêtre de lecture seule "Consulter l'historique..." pour l'écran "Filtres F7 à H13"
/// (<see cref="OpacimetricFilterView"/>), qui n'a pas de notion de mois fixe (historique de
/// remplacements ponctuels, voir <see cref="OpacimetricFilter"/>) : affiche donc, pour l'année choisie
/// parmi celles où CE filtre précis a de l'historique en base, la liste chronologique des remplacements
/// (quantité changée + date) plutôt qu'une grille à 12 mois.</summary>
public partial class OpacimetricHistoryWindow : Window
{
    private readonly List<OpacimetricReplacement> _replacements;

    public OpacimetricHistoryWindow(string location, string dimension, List<OpacimetricReplacement> replacements)
    {
        InitializeComponent();
        _replacements = replacements;

        var dimensionText = string.IsNullOrWhiteSpace(dimension) ? "-" : dimension;
        HeaderText.Text = $"Nom de la centrale d'air : {location}    —    Dimension : {dimensionText}";

        var years = _replacements
            .Where(r => r.DateChanged.HasValue)
            .Select(r => r.DateChanged!.Value.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        if (years.Count == 0)
        {
            NoHistoryText.Visibility = Visibility.Visible;
            YearCombo.Visibility = Visibility.Collapsed;
            ReplacementsGrid.Visibility = Visibility.Collapsed;
            return;
        }

        YearCombo.ItemsSource = years;
        YearCombo.SelectedItem = years.Contains(App.YearContext.Year) ? App.YearContext.Year : years[0];
        RefreshReplacements();
    }

    private void YearCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => RefreshReplacements();

    private void RefreshReplacements()
    {
        if (YearCombo.SelectedItem is not int year) return;

        var rows = _replacements
            .Where(r => r.DateChanged.HasValue && r.DateChanged.Value.Year == year)
            .OrderBy(r => r.DateChanged)
            .Select(r => new ReplacementHistoryRow
            {
                DateLabel = r.DateChanged!.Value.ToString("dd/MM/yyyy"),
                Quantity = r.QuantityChanged
            })
            .ToList();

        ReplacementsGrid.ItemsSource = rows;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>Ligne d'affichage (lecture seule) d'un remplacement dans <see cref="OpacimetricHistoryWindow"/>.</summary>
public class ReplacementHistoryRow
{
    public string DateLabel { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
