using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FiltresApp.Services;

namespace FiltresApp.Views.Dialogs;

/// <summary>Fenêtre "Consulter l'historique..." commune aux écrans "Filtres F7 à H14", "Courroies" et
/// "Roulements" : pour l'année choisie parmi celles où CET élément précis a de l'historique en base, liste
/// chronologique des remplacements. Clic droit sur une ligne : supprimer cet enregistrement. Ouverte via
/// <see cref="IDialogService.ShowReplacementHistory{TReplacement}"/>.</summary>
public partial class ReplacementHistoryWindow : Window
{
    private readonly List<ReplacementHistoryRow> _allRows;
    private readonly Action<int> _deleteFromDb;

    public ReplacementHistoryWindow(string header, string noHistoryText, string detailHeader,
        double dateColumnWidth, double detailColumnWidth, List<ReplacementHistoryRow> rows, Action<int> deleteFromDb)
    {
        InitializeComponent();
        _allRows = rows;
        _deleteFromDb = deleteFromDb;

        HeaderText.Text = header;
        NoHistoryText.Text = noHistoryText;
        DetailColumn.Header = detailHeader;
        DateColumn.Width = new DataGridLength(dateColumnWidth, DataGridLengthUnitType.Star);
        DetailColumn.Width = new DataGridLength(detailColumnWidth, DataGridLengthUnitType.Star);

        var years = _allRows.Select(r => r.Date.Year).Distinct().OrderByDescending(y => y).ToList();
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
        ReplacementsGrid.ItemsSource = _allRows.Where(r => r.Date.Year == year).OrderBy(r => r.Date).ToList();
    }

    /// <summary>Clic droit sur une ligne : affiche "Supprimer cet enregistrement" (construit ici plutôt
    /// qu'en XAML, voir le commentaire sur ReplacementsGrid dans le .xaml).</summary>
    private void ReplacementsGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject).FindAncestor<DataGridRow>() is not { DataContext: ReplacementHistoryRow row } dgRow) return;
        ReplacementsGrid.SelectedItem = row;

        var menu = new ContextMenu { PlacementTarget = dgRow, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        var delete = new MenuItem { Header = "Supprimer cet enregistrement", IsEnabled = App.IsWritable };
        delete.Click += (_, _) => DeleteReplacement(row);
        menu.Items.Add(delete);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void DeleteReplacement(ReplacementHistoryRow row)
    {
        if (!App.GuardWritable()) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer l'enregistrement du {row.DateLabel} ({row.ConfirmDetail}) ?")) return;

        _deleteFromDb(row.Id);
        _allRows.Remove(row);
        RefreshReplacements();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>Ligne d'affichage d'un remplacement dans <see cref="ReplacementHistoryWindow"/>.</summary>
public class ReplacementHistoryRow
{
    public int Id { get; init; }
    public DateOnly Date { get; init; }
    public string DateLabel => Date.ToString("dd/MM/yyyy");
    public string Detail { get; init; } = string.Empty;
    public string ConfirmDetail { get; init; } = string.Empty;
}
