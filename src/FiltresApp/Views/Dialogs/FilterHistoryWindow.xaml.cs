using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FiltresApp.Core.Models;
using FiltresApp.Services;
using FiltresApp.ViewModels;
using FiltresApp.ViewModels.Filtres;

namespace FiltresApp.Views.Dialogs;

/// <summary>Fenêtre de consultation "Consulter l'historique..." (menu contextuel de
/// <see cref="PeriodicFilterView"/>, toutes catégories à périodicité mensuelle : G4 plissé, G4 plan, G3,
/// Charbon). Affiche la liste chronologique (le plus récent en premier) de tous les mois pour lesquels CE
/// filtre précis a effectivement un changement enregistré, toutes années confondues, sans sélecteur
/// d'année à changer : il suffit de dérouler la liste pour remonter jusqu'au plus ancien enregistrement.
/// Clic droit sur une ligne : permet de supprimer cet enregistrement (seule modification possible depuis
/// cette fenêtre, l'édition normale restant réservée à la case à cocher de la grille principale, voir
/// <see cref="PeriodicFilterListViewModel.SetReplacementDone"/>).</summary>
public partial class FilterHistoryWindow : Window
{
    private readonly List<MonthHistoryRow> _rows;

    public FilterHistoryWindow(string locationLabel, string location, string dimension, List<FilterReplacement> replacements)
    {
        InitializeComponent();

        var dimensionText = string.IsNullOrWhiteSpace(dimension) ? "-" : dimension;
        HeaderText.Text = $"{locationLabel} : {location}    —    Dimension : {dimensionText}";

        _rows = replacements
            .Where(r => r.DateDone.HasValue)
            .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
            .Select(r => new MonthHistoryRow
            {
                Id = r.Id,
                MonthLabel = $"{ConsultedMonthOption.MonthLabels[r.Month - 1]} {r.Year}",
                DateLabel = r.DateDone!.Value.ToString("dd/MM/yyyy")
            })
            .ToList();

        if (_rows.Count == 0)
        {
            NoHistoryText.Visibility = Visibility.Visible;
            MonthsGrid.Visibility = Visibility.Collapsed;
            return;
        }

        MonthsGrid.ItemsSource = _rows;
    }

    /// <summary>Clic droit sur une ligne : affiche "Supprimer cet enregistrement" (construit ici plutôt
    /// qu'en XAML, voir le commentaire sur MonthsGrid dans le .xaml).</summary>
    private void MonthsGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject).FindAncestor<DataGridRow>() is not { DataContext: MonthHistoryRow row } dgRow) return;
        MonthsGrid.SelectedItem = row;

        var menu = new ContextMenu { PlacementTarget = dgRow, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        var delete = new MenuItem { Header = "Supprimer cet enregistrement", IsEnabled = App.IsWritable };
        delete.Click += (_, _) => DeleteReplacement(row);
        menu.Items.Add(delete);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void DeleteReplacement(MonthHistoryRow row)
    {
        if (!App.GuardWritable()) return;
        if (!App.Dialogs.ShowConfirm("Supprimer", $"Supprimer l'enregistrement « {row.MonthLabel} » (changement du {row.DateLabel}) ?")) return;

        var tracked = App.Db.FilterReplacements.FirstOrDefault(r => r.Id == row.Id);
        if (tracked is not null)
        {
            App.Db.FilterReplacements.Remove(tracked);
            App.Db.SaveChanges();
        }

        _rows.Remove(row);
        MonthsGrid.Items.Refresh();
        if (_rows.Count == 0)
        {
            NoHistoryText.Visibility = Visibility.Visible;
            MonthsGrid.Visibility = Visibility.Collapsed;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>Ligne d'affichage d'un mois dans <see cref="FilterHistoryWindow"/>.</summary>
public class MonthHistoryRow
{
    public int Id { get; set; }
    public string MonthLabel { get; set; } = string.Empty;
    public string DateLabel { get; set; } = string.Empty;
}
