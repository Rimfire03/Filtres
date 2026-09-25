using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiltresApp.Core.Services;

public class AppSettings
{
    public string DatabasePath { get; set; } = Path.Combine("data", "filtres.db");
    public string PdfExportPath { get; set; } = Path.Combine("data", "exports");
    public bool AutoUpdateEnabled { get; set; } = true;

    /// <summary>Fenêtre "Rattacher des filtres..." : n'afficher par défaut que les filtres dont la dimension
    /// correspond (approximativement) à celle de la ligne. Désactivé : tous les filtres sont affichés.</summary>
    public bool LinkDimensionFilterEnabled { get; set; } = true;

    private static string SettingsFilePath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                var json = File.ReadAllText(SettingsFilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // fichier corrompu ou illisible : on repart sur les valeurs par défaut
        }

        var defaults = new AppSettings();
        defaults.Save();
        return defaults;
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFilePath, json);
    }

    /// <summary>Chemin absolu de la base de données, résolu par rapport au dossier de l'exécutable
    /// si un chemin relatif a été configuré (mode portable).</summary>
    [JsonIgnore]
    public string ResolvedDatabasePath =>
        Path.IsPathRooted(DatabasePath) ? DatabasePath : Path.Combine(AppContext.BaseDirectory, DatabasePath);

    [JsonIgnore]
    public string ResolvedPdfExportPath =>
        Path.IsPathRooted(PdfExportPath) ? PdfExportPath : Path.Combine(AppContext.BaseDirectory, PdfExportPath);
}
