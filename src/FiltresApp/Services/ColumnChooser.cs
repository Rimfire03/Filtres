using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace FiltresApp.Services;

/// <summary>
/// Grilles portant <c>ColumnChooser.Key</c> : clic droit sur un en-tête de colonne pour masquer ou
/// réafficher des colonnes, et mémorisation des largeurs redimensionnées à la souris. Tout est enregistré
/// sur l'ordinateur (voir <see cref="ColumnPreferences"/>), sous la clé de la grille.
/// </summary>
public static class ColumnChooser
{
    private sealed class OriginalWidth(DataGridLength width)
    {
        public DataGridLength Width { get; } = width;
    }

    /// <summary>Largeur définie dans le XAML, pour "Réinitialiser les largeurs" et pour ne mémoriser que
    /// les colonnes réellement modifiées.</summary>
    private static readonly ConditionalWeakTable<DataGridColumn, OriginalWidth> OriginalWidths = new();

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
        grid.RemoveHandler(Thumb.DragCompletedEvent, (DragCompletedEventHandler)OnResizeCompleted);
        grid.AddHandler(Thumb.DragCompletedEvent, (DragCompletedEventHandler)OnResizeCompleted, handledEventsToo: true);
        if (grid.IsLoaded) Apply(grid);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Apply((DataGrid)sender);

    private static void Apply(DataGrid grid)
    {
        var key = GetKey(grid);
        if (string.IsNullOrEmpty(key)) return;

        foreach (var column in grid.Columns)
        {
            var columnKey = ColumnKey(grid, column);
            column.Visibility = ColumnPreferences.IsHidden(key, columnKey) ? Visibility.Collapsed : Visibility.Visible;

            var original = OriginalWidths.GetValue(column, c => new OriginalWidth(c.Width)).Width;
            var saved = ColumnPreferences.GetWidth(key, columnKey);
            column.Width = saved is null ? original : new DataGridLength(saved.Value, saved.Star ? DataGridLengthUnitType.Star : DataGridLengthUnitType.Pixel);
        }

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

        var resetWidths = new MenuItem { Header = "Réinitialiser les largeurs" };
        resetWidths.Click += (_, _) =>
        {
            ColumnPreferences.SetWidths(key, new Dictionary<string, ColumnWidth>());
            foreach (var column in grid.Columns)
                if (OriginalWidths.TryGetValue(column, out var original)) column.Width = original.Width;
        };
        menu.Items.Add(resetWidths);

        menu.PlacementTarget = grid;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Fin d'un redimensionnement à la souris (poignée d'un en-tête) : mémorise la largeur de toutes
    /// les colonnes qui diffèrent de leur largeur d'origine (en mode proportionnel, WPF peut aussi ajuster
    /// les colonnes voisines).</summary>
    private static void OnResizeCompleted(object sender, DragCompletedEventArgs e)
    {
        var grid = (DataGrid)sender;
        var key = GetKey(grid);
        if (string.IsNullOrEmpty(key) || FindAncestor<DataGridColumnHeader>(e.OriginalSource as DependencyObject) is null) return;

        var widths = new Dictionary<string, ColumnWidth>();
        foreach (var column in grid.Columns)
        {
            if (!OriginalWidths.TryGetValue(column, out var original)) continue;
            var width = column.Width;
            if (width.UnitType == original.Width.UnitType && width.Value.Equals(original.Width.Value)) continue;
            widths[ColumnKey(grid, column)] = width.IsStar
                ? new ColumnWidth(width.Value, Star: true)
                : new ColumnWidth(width.IsAbsolute ? width.Value : column.ActualWidth, Star: false);
        }
        ColumnPreferences.SetWidths(key, widths);
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
