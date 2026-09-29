using System.Windows.Media;
using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Services;

/// <summary>Palette de couleurs de ligne (Paramètres, "Couleurs de ligne"), mise en cache en mémoire et
/// rechargée à la demande après une modification (ajout/suppression/renommage/recoloriage) : évite une
/// requête en base à chaque ligne de grille affichée.</summary>
public static class RowColorPalette
{
    public static event Action? Changed;

    private static List<(int Id, string Nom, Color Color)>? _cache;

    public static IReadOnlyList<(int Id, string Nom, Color Color)> Colors => _cache ??= Load();

    /// <summary>À appeler après toute modification de la palette (Paramètres) pour que les grilles déjà
    /// ouvertes reflètent le changement.</summary>
    public static void Invalidate()
    {
        _cache = null;
        Changed?.Invoke();
    }

    public static Brush? BrushFor(int? colorId)
    {
        var color = ColorFor(colorId);
        return color is null ? null : new SolidColorBrush(color.Value);
    }

    /// <summary>Utilisé par l'impression (PrintService) : même couleur que celle affichée à l'écran.</summary>
    public static Color? ColorFor(int? colorId)
    {
        if (colorId is null) return null;
        foreach (var (id, _, color) in Colors)
            if (id == colorId)
                return color;
        return null;
    }

    private static List<(int, string, Color)> Load() =>
        App.Db.RowColors.AsNoTracking().OrderBy(c => c.Ordre).ThenBy(c => c.Nom)
            .ToList()
            .Select(c => (c.Id, c.Nom, ParseColor(c.Hex)))
            .ToList();

    private static Color ParseColor(string hex)
    {
        try
        {
            return (Color)ColorConverter.ConvertFromString(hex)!;
        }
        catch
        {
            return Colors_White;
        }
    }

    private static readonly Color Colors_White = Color.FromRgb(0xFF, 0xFF, 0xFF);
}
