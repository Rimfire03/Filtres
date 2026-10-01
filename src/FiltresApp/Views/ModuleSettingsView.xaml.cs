using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FiltresApp.ViewModels;
using FiltresApp.Views.Dialogs;

namespace FiltresApp.Views;

public partial class ModuleSettingsView : UserControl
{
    public ModuleSettingsView()
    {
        InitializeComponent();
    }

    /// <summary>Clic sur le carré de couleur ("Couleurs de ligne") : ouvre la roue chromatique plutôt que
    /// de faire saisir un code hexadécimal à la main. Même logique que SettingsView.xaml.cs.</summary>
    private void ColorSwatch_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not RowColorEditViewModel color) return;
        var window = new ColorWheelWindow(color.Hex) { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() == true) color.Hex = window.SelectedHex;
    }
}
