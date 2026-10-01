using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;

namespace FiltresApp.Views.Dialogs;

/// <summary>Fenêtre "Consulter l'historique..." pour le module "Courroies" : liste chronologique des
/// remplacements (qté changée + date) de l'année choisie. Clic droit sur une ligne : supprimer.</summary>
public partial class BeltHistoryWindow : Window
{
    private readonly List<BeltReplacement> _replacements;
    private List<BeltReplacementHistoryRow> _rows = new();

    public BeltHistoryWindow(string location, List<BeltReplacement> replacements)
    {
        InitializeComponent();
        _replacements = replacements;
        HeaderText.Text = $"Nom de la centrale : {location}";

        var years = _replacements.Where(r => r.DateChanged.HasValue).Select(r => r.DateChanged!.Value.Year).Distinct().OrderByDescending(y => y).ToList();
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

    private void YearCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshReplacements();

    private void RefreshReplacements()
    {
        if (YearCombo.SelectedItem is not int year) return;

        _rows = _replacements
            .Where(r => r.DateChanged.HasValue && r.DateChanged.Value.Year == year)
            .OrderBy(r => r.DateChanged)
            .Select(r => new BeltReplacementHistoryRow { Id = r.Id, DateLabel = r.DateChanged!.Value.ToString("dd/MM/yyyy"), Quantity = r.QuantityChanged })
            .ToList();

        ReplacementsGrid.ItemsSource = _rows;
    }

    private void ReplacementsGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject).FindAncestor<DataGridRow>() is not { DataContext: BeltReplacementHistoryRow row } dgRow) return;
        ReplacementsGrid.SelectedItem = row;

        var menu = new ContextMenu { PlacementTarget = dgRow, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        var delete = new MenuItem { Header = "Supprimer cet enregistrement", IsEnabled = App.IsWritable };
        delete.Click += (_, _) => DeleteReplacement(row);
        menu.Items.Add(delete);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void DeleteReplacement(BeltReplacementHistoryRow row)
    {
        if (!App.GuardWritable()) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer l'enregistrement du {row.DateLabel} (quantité {row.Quantity}) ?")) return;

        var tracked = App.Db.BeltReplacements.FirstOrDefault(r => r.Id == row.Id);
        if (tracked is not null)
        {
            App.Db.BeltReplacements.Remove(tracked);
            App.Db.SaveChanges();
        }

        _replacements.RemoveAll(r => r.Id == row.Id);
        RefreshReplacements();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public class BeltReplacementHistoryRow
{
    public int Id { get; set; }
    public string DateLabel { get; set; } = string.Empty;
    public int Quantity { get; set; }
}
