using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;

namespace FiltresApp.Views.Dialogs;

/// <summary>Fenêtre de lecture seule "Consulter l'historique..." pour l'écran d'une variété du menu
/// "Filtres F7 à H14" (<see cref="DynamicFilterView"/>), qui n'a pas de notion de mois fixe (historique de
/// remplacements ponctuels, voir <see cref="DynamicFilter"/>) : affiche donc, pour l'année choisie parmi
/// celles où CE filtre précis a de l'historique en base, la liste chronologique des remplacements
/// (quantité changée + date) plutôt qu'une grille à 12 mois. Clic droit sur une ligne : permet de supprimer
/// cet enregistrement.</summary>
public partial class DynamicFilterHistoryWindow : Window
{
    private readonly List<DynamicFilterReplacement> _replacements;
    private List<DynamicReplacementHistoryRow> _rows = new();

    public DynamicFilterHistoryWindow(string location, string dimension, List<DynamicFilterReplacement> replacements)
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

        _rows = _replacements
            .Where(r => r.DateChanged.HasValue && r.DateChanged.Value.Year == year)
            .OrderBy(r => r.DateChanged)
            .Select(r => new DynamicReplacementHistoryRow
            {
                Id = r.Id,
                DateLabel = r.DateChanged!.Value.ToString("dd/MM/yyyy"),
                Quantity = r.QuantityChanged
            })
            .ToList();

        ReplacementsGrid.ItemsSource = _rows;
    }

    /// <summary>Clic droit sur une ligne : affiche "Supprimer cet enregistrement" (construit ici plutôt
    /// qu'en XAML, voir le commentaire sur ReplacementsGrid dans le .xaml).</summary>
    private void ReplacementsGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject).FindAncestor<DataGridRow>() is not { DataContext: DynamicReplacementHistoryRow row } dgRow) return;
        ReplacementsGrid.SelectedItem = row;

        var menu = new ContextMenu { PlacementTarget = dgRow, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        var delete = new MenuItem { Header = "Supprimer cet enregistrement", IsEnabled = App.IsWritable };
        delete.Click += (_, _) => DeleteReplacement(row);
        menu.Items.Add(delete);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void DeleteReplacement(DynamicReplacementHistoryRow row)
    {
        if (!App.GuardWritable()) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer l'enregistrement du {row.DateLabel} (quantité {row.Quantity}) ?")) return;

        var tracked = App.Db.DynamicFilterReplacements.FirstOrDefault(r => r.Id == row.Id);
        if (tracked is not null)
        {
            App.Db.DynamicFilterReplacements.Remove(tracked);
            App.Db.SaveChanges();
        }

        _replacements.RemoveAll(r => r.Id == row.Id);
        RefreshReplacements();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>Ligne d'affichage d'un remplacement dans <see cref="DynamicFilterHistoryWindow"/>.</summary>
public class DynamicReplacementHistoryRow
{
    public int Id { get; set; }
    public string DateLabel { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
