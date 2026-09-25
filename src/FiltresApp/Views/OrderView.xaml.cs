using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
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
            item.Click += (_, _) => vm.SetQuickLink(line, option.FilterId, item.IsChecked);
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
