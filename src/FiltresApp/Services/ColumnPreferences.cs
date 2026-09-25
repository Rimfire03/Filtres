using System.IO;
using System.Text.Json;

namespace FiltresApp.Services;

/// <summary>Largeur d'une colonne : en pixels, ou proportionnelle ("*") comme dans le XAML.</summary>
public record ColumnWidth(double Value, bool Star);

/// <summary>Préférences d'affichage d'une grille : colonnes masquées et largeurs modifiées par l'utilisateur.</summary>
public class GridPreferences
{
    public HashSet<string> Hidden { get; set; } = new();
    public Dictionary<string, ColumnWidth> Widths { get; set; } = new();
}

/// <summary>Préférences d'affichage des grilles, propres à chaque ordinateur : stockées dans le profil
/// Windows local (%LocalAppData%\FiltresApp\grilles.json) et non à côté de l'exécutable, qui peut être
/// partagé sur le disque réseau par tous les postes.</summary>
public static class ColumnPreferences
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiltresApp", "grilles.json");

    private static Dictionary<string, GridPreferences>? _grids;

    private static Dictionary<string, GridPreferences> Grids => _grids ??= Load();

    private static GridPreferences For(string gridKey)
    {
        if (!Grids.TryGetValue(gridKey, out var prefs)) Grids[gridKey] = prefs = new GridPreferences();
        return prefs;
    }

    public static bool IsHidden(string gridKey, string columnKey) =>
        Grids.TryGetValue(gridKey, out var prefs) && prefs.Hidden.Contains(columnKey);

    public static void SetHidden(string gridKey, string columnKey, bool hidden)
    {
        var prefs = For(gridKey);
        if (hidden) prefs.Hidden.Add(columnKey);
        else prefs.Hidden.Remove(columnKey);
        Save();
    }

    public static void ShowAll(string gridKey)
    {
        if (!Grids.TryGetValue(gridKey, out var prefs) || prefs.Hidden.Count == 0) return;
        prefs.Hidden.Clear();
        Save();
    }

    public static ColumnWidth? GetWidth(string gridKey, string columnKey) =>
        Grids.TryGetValue(gridKey, out var prefs) && prefs.Widths.TryGetValue(columnKey, out var width) ? width : null;

    /// <summary>Remplace l'ensemble des largeurs mémorisées de la grille (vide = largeurs d'origine).</summary>
    public static void SetWidths(string gridKey, Dictionary<string, ColumnWidth> widths)
    {
        For(gridKey).Widths = widths;
        Save();
    }

    private static Dictionary<string, GridPreferences> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, GridPreferences>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Fichier illisible ou corrompu : on repart de l'affichage par défaut.
        }
        return new();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Grids, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Préférence d'affichage seulement : le choix reste appliqué jusqu'à la fermeture.
        }
    }
}
