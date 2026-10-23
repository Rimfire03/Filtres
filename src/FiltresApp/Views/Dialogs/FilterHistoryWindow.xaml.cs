using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
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
    private List<MonthHistoryRow> _rows = new();
    private readonly List<FilterReplacement> _replacements;
    private readonly bool _tracksHours;

    public FilterHistoryWindow(string locationLabel, string location, string dimension, List<FilterReplacement> replacements, bool tracksHours = false)
    {
        InitializeComponent();
        _replacements = replacements;
        _tracksHours = tracksHours;

        var dimensionText = string.IsNullOrWhiteSpace(dimension) ? "-" : dimension;
        HeaderText.Text = $"{locationLabel} : {location}    —    Dimension : {dimensionText}";

        BuildRows();
    }

    /// <summary>Construit les lignes affichées (plus récent en premier) ; rappelée après la modification d'un compteur.</summary>
    private void BuildRows()
    {
        // Durée d'utilisation de l'ancien filtre (celui qu'on retire à ce changement) = compteur relevé à ce changement
        // moins celui du dernier changement relevé avant lui, toutes années confondues (ordre chronologique). Un compteur
        // plus petit que le précédent (compteur remplacé ou remis à zéro) ne donne pas de durée.
        var chronological = _replacements
            .Where(r => r.DateDone.HasValue)
            .OrderBy(r => r.Year).ThenBy(r => r.Month).ThenBy(r => r.DateDone)
            .ToList();
        var lifetimes = new Dictionary<int, int>();
        int? previousHours = null;
        foreach (var r in chronological)
        {
            if (r.OperatingHours is int h)
            {
                if (previousHours is int p && h >= p) lifetimes[r.Id] = h - p;
                previousHours = h;
            }
        }

        _rows = chronological
            .AsEnumerable().Reverse()
            .Select(r => new MonthHistoryRow
            {
                Id = r.Id,
                MonthLabel = $"{ConsultedMonthOption.MonthLabels[r.Month - 1]} {r.Year}",
                DateLabel = r.DateDone!.Value.ToString("dd/MM/yyyy"),
                HoursLabel = r.OperatingHours?.ToString() ?? "-",
                LifetimeLabel = lifetimes.TryGetValue(r.Id, out var life) ? $"{life} h" : "-"
            })
            .ToList();

        var showHours = _tracksHours || chronological.Any(r => r.OperatingHours.HasValue);
        HoursColumn.Visibility = LifetimeColumn.Visibility = showHours ? Visibility.Visible : Visibility.Collapsed;

        if (_rows.Count == 0)
        {
            NoHistoryText.Visibility = Visibility.Visible;
            MonthsGrid.Visibility = Visibility.Collapsed;
            return;
        }

        MonthsGrid.ItemsSource = null;
        MonthsGrid.ItemsSource = _rows;
    }

    /// <summary>Saisie du compteur horaire d'un enregistrement (clic droit sur la ligne). Vide = compteur effacé.</summary>
    private void EditHours(MonthHistoryRow row)
    {
        if (!App.GuardWritable()) return;
        var replacement = _replacements.FirstOrDefault(r => r.Id == row.Id);
        if (replacement is null) return;

        var hours = replacement.OperatingHours;
        var fields = new List<EditField>
        {
            EditField.NullableInt("Compteur d'heures de fonctionnement", () => hours, v => hours = v)
        };
        if (!App.Dialogs.EditFields($"Compteur horaire : {row.MonthLabel}", fields)) return;

        var tracked = App.Db.FilterReplacements.FirstOrDefault(r => r.Id == row.Id);
        if (tracked is null) return;
        tracked.OperatingHours = hours;
        App.Db.SaveChanges();
        replacement.OperatingHours = hours;
        BuildRows();

        if (hours is int h && ReplacementTrackingService.LastReadingBefore(App.Db, tracked.PeriodicFilterId, tracked.Year, tracked.Month) is int last && h < last)
            App.Dialogs.ShowMessage("Compteur horaire à vérifier",
                $"Le compteur saisi ({h}) est inférieur au dernier relevé ({last}). La valeur est enregistrée : vérifiez les valeurs saisies.");
    }

    /// <summary>Clic droit sur une ligne : affiche "Supprimer cet enregistrement" (construit ici plutôt
    /// qu'en XAML, voir le commentaire sur MonthsGrid dans le .xaml).</summary>
    private void MonthsGrid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject).FindAncestor<DataGridRow>() is not { DataContext: MonthHistoryRow row } dgRow) return;
        MonthsGrid.SelectedItem = row;

        var menu = new ContextMenu { PlacementTarget = dgRow, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        if (HoursColumn.Visibility == Visibility.Visible)
        {
            var editHours = new MenuItem { Header = "Modifier le compteur horaire...", IsEnabled = App.IsWritable };
            editHours.Click += (_, _) => EditHours(row);
            menu.Items.Add(editHours);
        }
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

        _replacements.RemoveAll(r => r.Id == row.Id);
        BuildRows();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

/// <summary>Ligne d'affichage d'un mois dans <see cref="FilterHistoryWindow"/>.</summary>
public class MonthHistoryRow
{
    public int Id { get; set; }
    public string MonthLabel { get; set; } = string.Empty;
    public string DateLabel { get; set; } = string.Empty;
    public string HoursLabel { get; set; } = "-";
    public string LifetimeLabel { get; set; } = "-";
}
