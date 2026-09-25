using System.IO;
using System.Text.Json;

namespace FiltresApp.Services;

/// <summary>Colonnes masquées par grille, propres à chaque ordinateur : stockées dans le profil Windows
/// local (%LocalAppData%\FiltresApp\colonnes.json) et non à côté de l'exécutable, qui peut être partagé
/// sur le disque réseau par tous les postes.</summary>
public static class ColumnPreferences
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FiltresApp", "colonnes.json");

    private static Dictionary<string, HashSet<string>>? _hidden;

    private static Dictionary<string, HashSet<string>> Hidden => _hidden ??= Load();

    public static bool IsHidden(string gridKey, string columnKey) =>
        Hidden.TryGetValue(gridKey, out var columns) && columns.Contains(columnKey);

    public static void SetHidden(string gridKey, string columnKey, bool hidden)
    {
        if (!Hidden.TryGetValue(gridKey, out var columns)) Hidden[gridKey] = columns = new HashSet<string>();
        if (hidden) columns.Add(columnKey);
        else columns.Remove(columnKey);
        Save();
    }

    public static void ShowAll(string gridKey)
    {
        if (Hidden.Remove(gridKey)) Save();
    }

    private static Dictionary<string, HashSet<string>> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Dictionary<string, HashSet<string>>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Fichier illisible ou corrompu : on repart simplement de toutes les colonnes affichées.
        }
        return new();
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Hidden, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Préférence d'affichage seulement : le choix reste appliqué jusqu'à la fermeture.
        }
    }
}
