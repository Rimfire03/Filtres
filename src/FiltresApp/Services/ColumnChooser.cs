using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace FiltresApp.Services;

/// <summary>
/// Clic droit sur un en-tête de colonne d'une grille portant <c>ColumnChooser.Key</c> : menu pour masquer
/// ou réafficher des colonnes. Le choix est mémorisé sur l'ordinateur (voir <see cref="ColumnPreferences"/>),
/// sous la clé de la grille.
/// </summary>
public static class ColumnChooser
{
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(ColumnChooser), new PropertyMetadata(null, OnKeyChanged));

    public static string? GetKey(DependencyObject obj) => (string?)obj.GetValue(KeyProperty);
    public static void SetKey(DependencyObject obj, string? value) => obj.SetValue(KeyProperty, value);

    private static void OnKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid) return;
        grid.Loaded -= OnLoaded;
        grid.Loaded += OnLoaded;
        grid.PreviewMouseRightButtonUp -= OnPreviewRightClick;
        grid.PreviewMouseRightButtonUp += OnPreviewRightClick;
        if (grid.IsLoaded) Apply(grid);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Apply((DataGrid)sender);

    private static void Apply(DataGrid grid)
    {
        var key = GetKey(grid);
        if (string.IsNullOrEmpty(key)) return;

        foreach (var column in grid.Columns)
            column.Visibility = ColumnPreferences.IsHidden(key, ColumnKey(grid, column)) ? Visibility.Collapsed : Visibility.Visible;

        if (grid.Columns.Count > 0 && grid.Columns.All(c => c.Visibility != Visibility.Visible))
            grid.Columns[0].Visibility = Visibility.Visible;
    }

    private static void OnPreviewRightClick(object sender, MouseButtonEventArgs e)
    {
        var grid = (DataGrid)sender;
        var key = GetKey(grid);
        if (string.IsNullOrEmpty(key) || FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject) is null) return;

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Colonnes affichées sur cet ordinateur :", IsEnabled = false });

        foreach (var column in grid.Columns)
        {
            var item = new MenuItem
            {
                Header = DisplayName(grid, column),
                IsCheckable = true,
                IsChecked = column.Visibility == Visibility.Visible,
                StaysOpenOnClick = true
            };
            item.Click += (_, _) =>
            {
                // Au moins une colonne doit rester visible.
                if (!item.IsChecked && grid.Columns.Count(c => c.Visibility == Visibility.Visible) <= 1)
                {
                    item.IsChecked = true;
                    return;
                }
                column.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                ColumnPreferences.SetHidden(key, ColumnKey(grid, column), !item.IsChecked);
            };
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var showAll = new MenuItem { Header = "Afficher toutes les colonnes" };
        showAll.Click += (_, _) =>
        {
            ColumnPreferences.ShowAll(key);
            foreach (var column in grid.Columns) column.Visibility = Visibility.Visible;
        };
        menu.Items.Add(showAll);

        menu.PlacementTarget = grid;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Identifiant stable de la colonne : son titre, ou sa position si le titre n'est pas un texte.</summary>
    private static string ColumnKey(DataGrid grid, DataGridColumn column) =>
        column.Header is string header && header.Length > 0 ? header : "#" + grid.Columns.IndexOf(column);

    private static string DisplayName(DataGrid grid, DataGridColumn column) =>
        column.Header is string header && header.Length > 0 ? header : $"Colonne {grid.Columns.IndexOf(column) + 1}";

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null and not T)
            current = current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        return current as T;
    }
}
