using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using FiltresApp.Core.Models;
using FiltresApp.ViewModels;

namespace FiltresApp.Views;

public partial class OrderView : UserControl
{
    private readonly InlineIntCell _needCell;

    public OrderView()
    {
        InitializeComponent();
        _needCell = new InlineIntCell(
            box => box.DataContext is OrderLine line && DataContext is OrderListViewModel vm && vm.SetManualNeed(line, box.Text),
            QuantityColumn);
    }

    /// <summary>Colonne "Famille" : le choix est enregistré dès la sélection. Les changements dus au
    /// chargement de la cellule (valeur déjà enregistrée) sont ignorés par SetFamilyChoice.
    /// SetFamilyChoice recharge entièrement la grille (nouvelle famille = changement possible de groupe
    /// et de position de la ligne), ce qui remonte sinon la grille tout en haut : on mémorise le défilement
    /// avant, et on le restaure une fois la nouvelle liste posée (différé pour laisser le temps au DataGrid
    /// de se reconstruire).</summary>
    private void FamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { IsLoaded: true, SelectedItem: string choice } combo
            || combo.DataContext is not OrderLine line || DataContext is not OrderListViewModel vm) return;

        var offset = FindVisualChild<ScrollViewer>(LinesGrid)?.VerticalOffset;
        vm.SetFamilyChoice(line, choice);

        if (offset is double savedOffset)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                FindVisualChild<ScrollViewer>(LinesGrid)?.ScrollToVerticalOffset(savedOffset)));
        }
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent is null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            var descendant = FindVisualChild<T>(child);
            if (descendant is not null) return descendant;
        }
        return null;
    }

    private void NeedBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _needCell.GotFocus((TextBox)sender);
    private void NeedBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => _needCell.LostFocus((TextBox)sender);
    private void NeedBox_PreviewKeyDown(object sender, KeyEventArgs e) => _needCell.PreviewKeyDown((TextBox)sender, e);

    /// <summary>Clic droit sur une cellule "Filtres liés" : menu rapide pour cocher / décocher les filtres
    /// rattachés et les filtres de dimension correspondante, ou ouvrir la fenêtre complète.</summary>
    private void Grid_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        var cell = FindAncestor<DataGridCell>(e.OriginalSource as DependencyObject);
        if (cell?.Column != LinkedFiltersColumn || cell.DataContext is not OrderLine line || DataContext is not OrderListViewModel vm) return;

        vm.SelectedLine = line;
        var menu = new ContextMenu { PlacementTarget = cell, Placement = PlacementMode.MousePoint };
        var (options, omitted) = vm.GetQuickLinkOptions(line);

        menu.Items.Add(new MenuItem { Header = "Cocher pour rattacher, décocher pour retirer :", IsEnabled = false });
        if (options.Count == 0)
            menu.Items.Add(new MenuItem { Header = "Aucun filtre rattaché ni de dimension correspondante", IsEnabled = false });

        foreach (var option in options)
        {
            // TextBlock : un "_" dans un emplacement ne doit pas devenir un raccourci clavier.
            var item = new MenuItem { Header = new TextBlock { Text = option.Label }, IsCheckable = true, IsChecked = option.IsLinked, IsEnabled = App.IsWritable };
            item.Click += (_, _) => vm.SetQuickLink(line, option.Ref, item.IsChecked);
            menu.Items.Add(item);
        }

        if (omitted > 0)
            menu.Items.Add(new MenuItem { Header = $"… et {omitted} autre(s) filtre(s) de dimension correspondante", IsEnabled = false });

        menu.Items.Add(new Separator());
        var openWindow = new MenuItem { Header = "Rattacher des filtres... (tous les filtres)", IsEnabled = App.IsWritable };
        openWindow.Click += (_, _) => vm.OpenLinkWindow(line);
        menu.Items.Add(openWindow);

        menu.IsOpen = true;
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null and not T)
            current = current is Visual or Visual3D ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current);
        return current as T;
    }
}
