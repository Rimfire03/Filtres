using System.Windows;
using FiltresApp.Core.Models;
using FiltresApp.ViewModels;

namespace FiltresApp.Views.Dialogs;

/// <summary>Fenêtre de lecture seule "Consulter l'historique..." (menu contextuel de
/// <see cref="PeriodicFilterView"/>, toutes catégories à périodicité mensuelle : G4 plissé, G4 plan, G3,
/// Charbon). Affiche, pour l'année choisie parmi celles où CE filtre précis a réellement de l'historique
/// en base, les 12 mois avec statut réalisé/date. Ne modifie jamais rien : l'édition reste réservée à la
/// case à cocher de la grille principale (voir <see cref="PeriodicFilterListViewModel.SetReplacementDone"/>).</summary>
public partial class FilterHistoryWindow : Window
{
    private readonly List<FilterReplacement> _replacements;

    public FilterHistoryWindow(string locationLabel, string location, string dimension, List<FilterReplacement> replacements)
    {
        InitializeComponent();
        _replacements = replacements;

        var dimensionText = string.IsNullOrWhiteSpace(dimension) ? "-" : dimension;
        HeaderText.Text = $"{locationLabel} : {location}    —    Dimension : {dimensionText}";

        // Années où CE filtre a AU MOINS UN changement effectif (DateDone renseignée), pas une liste fixe
        // 2014-2026 codée en dur, et pas non plus une année dont les lignes existent en base sans qu'aucun
        // changement n'y soit réellement enregistré (ex: ligne importée sans date).
        var years = _replacements.Where(r => r.DateDone.HasValue).Select(r => r.Year).Distinct().OrderByDescending(y => y).ToList();

        if (years.Count == 0)
        {
            NoHistoryText.Visibility = Visibility.Visible;
            YearCombo.Visibility = Visibility.Collapsed;
            MonthsGrid.Visibility = Visibility.Collapsed;
            return;
        }

        YearCombo.ItemsSource = years;
        YearCombo.SelectedItem = years.Contains(App.YearContext.Year) ? App.YearContext.Year : years[0];
        RefreshMonths();
    }

    private void YearCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => RefreshMonths();

    private void RefreshMonths()
    {
        if (YearCombo.SelectedItem is not int year) return;

        var rows = Enumerable.Range(1, 12).Select(month =>
        {
            var replacement = _replacements.FirstOrDefault(r => r.Month == month && r.Year == year && r.DateDone.HasValue);
            return new MonthHistoryRow
            {
                MonthLabel = ConsultedMonthOption.MonthLabels[month - 1],
                IsDone = replacement != null,
                DateLabel = replacement?.DateDone?.ToString("dd/MM/yyyy") ?? "-"
            };
        }).ToList();

        MonthsGrid.ItemsSource = rows;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>Ligne d'affichage (lecture seule) d'un mois dans <see cref="FilterHistoryWindow"/>.</summary>
public class MonthHistoryRow
{
    public string MonthLabel { get; set; } = string.Empty;
    public bool IsDone { get; set; }
    public string DateLabel { get; set; } = string.Empty;
}
