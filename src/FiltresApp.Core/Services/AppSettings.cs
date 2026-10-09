using System.Text.Json;
using System.Text.Json.Serialization;

namespace FiltresApp.Core.Services;

public class AppSettings
{
    // Chemins par défaut relatifs à FiltreData (voir DataDirectory) : seul l'exécutable doit rester à la
    // racine du dossier publié, tout le reste (base de données, exports, réglages) va dans FiltreData.
    public string DatabasePath { get; set; } = "filtres.db";
    public string PdfExportPath { get; set; } = "exports";

    /// <summary>Textes de la page de garde de l'export PDF Commande / Inventaire (voir <see cref="CoverPageInfo"/>),
    /// modifiables dans Paramètres. Une ligne vide n'est pas imprimée.</summary>
    public CoverPageInfo CoverPage { get; set; } = new();
    public bool AutoUpdateEnabled { get; set; } = true;

    /// <summary>Canal de mise à jour : "Main" (dernière release stable) ou "Dev" (dernière release, pré-versions
    /// comprises).</summary>
    public string UpdateChannel { get; set; } = UpdateChannels.Main;

    /// <summary>Version du logiciel au dernier démarrage : si elle change, la base est sauvegardée (Save DB) avant\r\n    /// d'être ouverte par la nouvelle version.</summary>
    public string? LastRunVersion { get; set; }

    // Sauvegarde automatique planifiée (voir BackupSchedule) et nombre de sauvegardes conservées (0 = illimité).
    public bool AutoBackupEnabled { get; set; }
    public string AutoBackupFrequency { get; set; } = BackupSchedule.Daily;
    public string AutoBackupTime { get; set; } = "02:00";
    public int AutoBackupDayOfWeek { get; set; } = (int)DayOfWeek.Monday;
    public int AutoBackupDayOfMonth { get; set; } = 1;
    public int MaxBackupsToKeep { get; set; } = 30;
    public DateTime? AutoBackupLastRun { get; set; }

    /// <summary>Noms des sauvegardes (Save DB) protégées contre la suppression automatique.</summary>
    public List<string> ProtectedBackups { get; set; } = new();

    /// <summary>Fenêtre "Rattacher des filtres..." : n'afficher par défaut que les filtres dont la dimension
    /// correspond (approximativement) à celle de la ligne. Désactivé : tous les filtres sont affichés.</summary>
    public bool LinkDimensionFilterEnabled { get; set; } = true;

    /// <summary>Menu dépliant "Filtres F7 à H14" : autorise la création de nouvelles variétés (chacune
    /// devenant un sous-menu permanent). Non persisté ([JsonIgnore]) : toujours désactivé au lancement de
    /// l'application, à réactiver manuellement dans Paramètres à chaque session pour éviter la création
    /// accidentelle de nouveaux sous-menus ; l'ajout, la modification et la suppression des filtres d'une
    /// variété déjà créée restent soumis uniquement au verrou d'écriture habituel (App.IsWritable).</summary>
    [JsonIgnore]
    public bool AllowFilterVarietyCreation { get; set; } = false;

    /// <summary>Même principe pour le menu dépliant « Changement filtre périodique » : autorise la création, le
    /// renommage et la suppression de ses vues (non persisté, désactivé à chaque lancement).</summary>
    [JsonIgnore]
    public bool AllowPeriodicViewCreation { get; set; } = false;

    /// <summary>Modules activables depuis Paramètres (menu dédié dans la barre latérale si activé) :
    /// "Courroies" et "Roulements". Persistés (contrairement à <see cref="AllowFilterVarietyCreation"/>
    /// ci-dessus) : une fois activé, un module le reste d'une session à l'autre.</summary>
    public bool ShowBeltsModule { get; set; } = false;
    public bool ShowBearingsModule { get; set; } = false;

    /// <summary>Module « MultiSite » (nécessite la fonctionnalité de licence « multisite ») : plusieurs sites aux données séparées, seuls les Paramètres sont communs.</summary>
    public bool MultiSiteEnabled { get; set; } = false;

    /// <summary>Site ouvert automatiquement au démarrage (null = demander le site à chaque ouverture).</summary>
    public int? DefaultSiteId { get; set; }

    /// <summary>Dernier site ouvert : sert de site actif quand le module n'est plus utilisable (désactivé ou hors licence).</summary>
    public int? LastSiteId { get; set; }

    /// <summary>Module "Filtre" (G4 plissés, G4 plan, G3, Charbon, F7 à H14, Liste K7, Inventaire,
    /// Commande - considérés comme un seul module) : activé par défaut (fonctionnalité d'origine de
    /// l'application, contrairement aux modules Courroies / Roulements ci-dessus).</summary>
    public bool ShowFiltresModule { get; set; } = true;

    /// <summary>Dossier contenant tout ce qui n'est pas l'exécutable lui-même (base de données, exports,
    /// réglages) : seul l'exécutable doit rester à la racine du dossier publié.</summary>
    private static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "FiltreData");

    /// <summary>Dossier des données locales (réglages, sauvegardes de serveur).</summary>
    public static string DataDirectoryPath => DataDirectory;

    /// <summary>Utiliser un serveur de base de données (réglages dans <see cref="DatabaseServer"/>) au lieu du
    /// fichier SQLite <see cref="DatabasePath"/>. Le fichier n'est jamais supprimé en passant sur un serveur.</summary>
    public bool UseDatabaseServer { get; set; }

    /// <summary>Dernier réglage de serveur saisi (conservé même si on revient au fichier).</summary>
    public DbConnectionInfo DatabaseServer { get; set; } = new();

    /// <summary>Base à ouvrir selon les réglages.</summary>
    [JsonIgnore]
    public DbTarget ResolvedTarget =>
        UseDatabaseServer ? DbTarget.ForServer(DatabaseServer) : DbTarget.ForFile(ResolvedDatabasePath);

    private static string SettingsFilePath => Path.Combine(DataDirectory, "settings.json");

    public static AppSettings Load()
    {
        MigrateLegacyLayoutIfNeeded();

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
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsFilePath, json);
    }

    /// <summary>Chemin absolu de la base de données, résolu par rapport à <see cref="DataDirectory"/>
    /// si un chemin relatif a été configuré (mode portable) - inchangé si un chemin absolu a été choisi
    /// (ex. réseau, via "Parcourir...").</summary>
    [JsonIgnore]
    public string ResolvedDatabasePath =>
        Path.IsPathRooted(DatabasePath) ? DatabasePath : Path.Combine(DataDirectory, DatabasePath);

    [JsonIgnore]
    public string ResolvedPdfExportPath =>
        Path.IsPathRooted(PdfExportPath) ? PdfExportPath : Path.Combine(DataDirectory, PdfExportPath);

    /// <summary>Migration ponctuelle depuis l'ancienne disposition (settings.json, data\filtres.db,
    /// data\exports à côté de l'exécutable) vers FiltreData : ne fait rien si FiltreData existe déjà
    /// (déjà migré, ou installation neuve qui le créera directement) ou si aucun settings.json
    /// n'existe à l'ancien emplacement (rien à migrer). Ne déplace que les chemins relatifs (mode
    /// portable) : un chemin absolu choisi par l'utilisateur (ex. réseau) n'est jamais touché, ni le
    /// fichier qu'il désigne. Ne doit jamais empêcher le démarrage : toute erreur est avalée, au pire
    /// l'ancienne disposition reste utilisée et de nouvelles valeurs par défaut repartent à zéro.</summary>
    private static void MigrateLegacyLayoutIfNeeded()
    {
        var dataDir = DataDirectory;
        if (Directory.Exists(dataDir)) return;

        var legacySettingsPath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        if (!File.Exists(legacySettingsPath)) return;

        try
        {
            Directory.CreateDirectory(dataDir);

            AppSettings legacy;
            try { legacy = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(legacySettingsPath)) ?? new AppSettings(); }
            catch { legacy = new AppSettings(); }

            legacy.DatabasePath = MigrateRelativeFile(legacy.DatabasePath, dataDir);
            legacy.PdfExportPath = MigrateRelativeDirectory(legacy.PdfExportPath, dataDir);

            var json = JsonSerializer.Serialize(legacy, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(dataDir, "settings.json"), json);
            File.Delete(legacySettingsPath);
        }
        catch
        {
            // Migration best-effort : voir le résumé ci-dessus.
        }
    }

    /// <summary>Déplace le fichier désigné par un chemin relatif (ex. "data\filtres.db") dans
    /// <paramref name="dataDir"/>, avec ses éventuelles sauvegardes ".bak" à côté, et retourne le
    /// nouveau chemin relatif (juste le nom de fichier, FiltreData servant déjà de dossier dédié).
    /// Ne touche à rien pour un chemin déjà absolu.</summary>
    private static string MigrateRelativeFile(string configuredPath, string dataDir)
    {
        if (string.IsNullOrWhiteSpace(configuredPath) || Path.IsPathRooted(configuredPath)) return configuredPath;

        var oldFullPath = Path.Combine(AppContext.BaseDirectory, configuredPath);
        var fileName = Path.GetFileName(configuredPath);
        if (string.IsNullOrEmpty(fileName)) return configuredPath;
        var newFullPath = Path.Combine(dataDir, fileName);

        if (File.Exists(oldFullPath) && !File.Exists(newFullPath))
        {
            File.Move(oldFullPath, newFullPath);
            var oldDir = Path.GetDirectoryName(oldFullPath);
            if (oldDir is not null && Directory.Exists(oldDir))
            {
                foreach (var bak in Directory.GetFiles(oldDir, fileName + ".*.bak"))
                    File.Move(bak, Path.Combine(dataDir, Path.GetFileName(bak)));
            }
        }
        return fileName;
    }

    /// <summary>Même principe que <see cref="MigrateRelativeFile"/> pour un dossier (ex. "data\exports").</summary>
    private static string MigrateRelativeDirectory(string configuredPath, string dataDir)
    {
        if (string.IsNullOrWhiteSpace(configuredPath) || Path.IsPathRooted(configuredPath)) return configuredPath;

        var oldFullPath = Path.Combine(AppContext.BaseDirectory, configuredPath);
        var name = new DirectoryInfo(configuredPath).Name;
        if (string.IsNullOrEmpty(name)) return configuredPath;
        var newFullPath = Path.Combine(dataDir, name);

        if (Directory.Exists(oldFullPath) && !Directory.Exists(newFullPath))
            Directory.Move(oldFullPath, newFullPath);
        return name;
    }
}
