using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FiltresApp.Services;

/// <summary>Construit dynamiquement le contenu du sous-menu "Couleur" du menu contextuel (clic droit) d'une
/// ligne de grille (filtres, K7, Commande, Inventaire) : une entrée par couleur réglée dans Paramètres, plus
/// "Aucune couleur". Seul le nom personnalisé de la couleur apparaît comme texte de l'entrée (aucune autre
/// mention) - un petit carré coloré sert uniquement de repère visuel.</summary>
public static class RowColorMenu
{
    public static void Populate(MenuItem colorMenuItem, int? currentColorId, Action<int?> onSelect)
    {
        colorMenuItem.Items.Clear();

        var none = new MenuItem { Header = "Aucune couleur", IsCheckable = true, IsChecked = currentColorId is null, StaysOpenOnClick = false };
        none.Click += (_, _) => onSelect(null);
        colorMenuItem.Items.Add(none);

        var colors = RowColorPalette.Colors;
        if (colors.Count == 0)
        {
            colorMenuItem.Items.Add(new MenuItem { Header = "Aucune couleur réglée (voir Paramètres)", IsEnabled = false });
            return;
        }

        colorMenuItem.Items.Add(new Separator());
        foreach (var (id, nom, color) in colors)
        {
            var swatch = new Border
            {
                Width = 14,
                Height = 14,
                Background = new SolidColorBrush(color),
                BorderBrush = Brushes.DarkGray,
                BorderThickness = new Thickness(1)
            };
            var item = new MenuItem { Header = nom, Icon = swatch, IsCheckable = true, IsChecked = currentColorId == id, StaysOpenOnClick = false };
            item.Click += (_, _) => onSelect(id);
            colorMenuItem.Items.Add(item);
        }
    }
}
