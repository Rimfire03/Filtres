using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace FiltresApp.Services;

/// <summary>
/// Largeur des colonnes d'un tableau vide. WPF calcule la largeur des colonnes « étoile » à partir du panneau des
/// cellules, qui n'existe pas tant qu'aucune ligne n'est affichée : un tableau vide montre alors toutes ses colonnes à
/// leur largeur minimale (titres écrasés lettre par lettre). Tant que le tableau est vide, chaque colonne étoile reçoit
/// donc une largeur minimale proportionnelle à son poids (la première colonne, plus lourde, reste la plus large) et
/// l'ensemble occupe toute la place disponible ; dès qu'une ligne apparaît, les largeurs d'origine sont rétablies et
/// WPF reprend le calcul. Appliqué à tous les tableaux de l'application (voir <see cref="Register"/>).
/// </summary>
public static class EmptyDataGridLayout
{
    private sealed class State
    {
        public readonly Dictionary<DataGridColumn, double> OriginalMinWidths = new();
    }

    private static readonly ConditionalWeakTable<DataGrid, State> States = new();

    /// <summary>À appeler une fois au démarrage : suit chaque DataGrid de l'application. L'événement Loaded n'est pas relayé
    /// aux gestionnaires de classe par WPF : on s'appuie sur SizeChanged, déclenché à la première mise en page.</summary>
    public static void Register() =>
        EventManager.RegisterClassHandler(typeof(DataGrid), FrameworkElement.SizeChangedEvent, new RoutedEventHandler(OnSizeChanged));

    private static void OnSizeChanged(object sender, RoutedEventArgs e)
    {
        var grid = (DataGrid)sender;
        if (!States.TryGetValue(grid, out _))
        {
            States.Add(grid, new State());
            if (grid.Items is INotifyCollectionChanged items)
                items.CollectionChanged += (_, _) => Schedule(grid);
            DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty, typeof(DataGrid))
                .AddValueChanged(grid, (_, _) => Schedule(grid));
        }
        // Après le chargement complet (les préférences de colonnes sont appliquées au chargement, voir ColumnChooser).
        Schedule(grid);
    }

    private static void Schedule(DataGrid grid) =>
        grid.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () => Update(grid));

    private static void Update(DataGrid grid)
    {
        if (!States.TryGetValue(grid, out var state)) return;

        if (grid.Items.Count > 0)
        {
            Restore(state);
            return;
        }

        if (!grid.IsLoaded || grid.ActualWidth <= 0) return;

        var visible = grid.Columns.Where(c => c.Visibility == Visibility.Visible).ToList();
        var stars = visible.Where(c => c.Width.IsStar).ToList();
        if (stars.Count == 0) return;

        var fixedWidth = visible.Where(c => !c.Width.IsStar)
            .Sum(c => c.Width.IsAbsolute ? c.Width.Value : Math.Max(c.ActualWidth, c.MinWidth));
        var available = grid.ActualWidth - grid.RowHeaderActualWidth - 8 - fixedWidth;
        if (available <= 0) return;

        var totalWeight = stars.Sum(c => c.Width.Value);
        if (totalWeight <= 0) return;

        foreach (var column in stars)
        {
            if (!state.OriginalMinWidths.ContainsKey(column)) state.OriginalMinWidths[column] = column.MinWidth;
            var share = Math.Floor(available * column.Width.Value / totalWeight);
            column.MinWidth = Math.Max(state.OriginalMinWidths[column], share);
        }
    }

    private static void Restore(State state)
    {
        if (state.OriginalMinWidths.Count == 0) return;
        foreach (var (column, minWidth) in state.OriginalMinWidths) column.MinWidth = minWidth;
        state.OriginalMinWidths.Clear();
    }
}
