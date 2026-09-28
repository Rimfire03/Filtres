using FiltresApp.Core.Data;
using FiltresApp.Core.Data.Migrations;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Services;

public class DbContextFactory
{
    private readonly string _dbPath;
    private readonly bool _readOnly;

    public DbContextFactory(string dbPath, bool readOnly = false)
    {
        _dbPath = dbPath;
        _readOnly = readOnly;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    public FiltresDbContext Create() => new(_dbPath, _readOnly);

    // Migrations du schéma : voir Data/Migrations/DatabaseMigrations.cs (règles de rédaction incluses).

    /// <summary>Version de base attendue par cette version de l'application.</summary>
    public static int LatestVersion => DatabaseMigrations.All[^1].Version;

    /// <summary>Libellés des migrations à appliquer depuis <paramref name="fromVersion"/>, pour la demande
    /// de confirmation.</summary>
    public static IReadOnlyList<string> PendingMigrations(int fromVersion) =>
        DatabaseMigrations.All.Where(m => m.Version > fromVersion).Select(m => $"v{m.Version} : {m.Description}").ToList();

    /// <summary>Version actuelle du fichier de base (0 = base antérieure au système de version).</summary>
    public int GetDatabaseVersion()
    {
        using var ctx = Create();
        return ReadVersion(ctx);
    }

    /// <summary>Version de l'application qui a mis la base à jour en dernier, pour les messages d'erreur.</summary>
    public string? GetLastMigratedByAppVersion()
    {
        using var ctx = Create();
        if (!SchemaInspector.GetColumns(ctx, "DbInfo").Contains("Value")) return null;
        return ctx.Database.SqlQueryRaw<string>("""SELECT "Value" AS "Value" FROM "DbInfo" WHERE "Key" = 'AppVersion'""").FirstOrDefault();
    }

    /// <summary>Crée la base si besoin, sinon applique les migrations manquantes (sauvegarde préalable du
    /// fichier). Réservé au poste rédacteur (voir DbWriteLock).</summary>
    public void EnsureDatabaseUpToDate(string appVersion)
    {
        if (_readOnly) return;

        using var ctx = Create();
        if (ctx.Database.EnsureCreated())
        {
            // Base neuve : EnsureCreated vient de créer directement le schéma le plus récent.
            WriteVersion(ctx, LatestVersion, appVersion);
            return;
        }

        var current = ReadVersion(ctx);
        var pending = DatabaseMigrations.All.Where(m => m.Version > current).ToList();
        if (pending.Count == 0) return;

        Backup(ctx, current);
        var reached = current;
        foreach (var migration in pending)
        {
            using var transaction = ctx.Database.BeginTransaction();
            try
            {
                migration.Apply(ctx);
                WriteVersion(ctx, migration.Version, appVersion);
                transaction.Commit();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Échec de la mise à jour de la base vers la version {migration.Version} ({migration.Description}) : {ex.Message}\n" +
                    $"La base est restée en version {reached} ; une sauvegarde faite juste avant la mise à jour se trouve à côté du fichier.", ex);
            }
            reached = migration.Version;
            ctx.ChangeTracker.Clear();
        }
    }

    private static int ReadVersion(FiltresDbContext ctx) =>
        ctx.Database.SqlQueryRaw<int>("SELECT user_version AS \"Value\" FROM pragma_user_version").First();

    private static void WriteVersion(FiltresDbContext ctx, int version, string appVersion)
    {
        // PRAGMA n'accepte pas de paramètre ; version est un entier issu du code.
        ctx.Database.ExecuteSqlRaw("PRAGMA user_version = " + version + ";");
        ctx.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "DbInfo" ("Key" TEXT NOT NULL PRIMARY KEY, "Value" TEXT NOT NULL);""");
        ctx.Database.ExecuteSqlRaw("""INSERT OR REPLACE INTO "DbInfo" ("Key", "Value") VALUES ('AppVersion', {0});""", appVersion);
    }

    /// <summary>Copie cohérente de la base (VACUUM INTO) avant toute migration, à côté du fichier.</summary>
    private void Backup(FiltresDbContext ctx, int fromVersion) => BackupTo(ctx, $"avant-maj-v{fromVersion}");

    /// <summary>Copie cohérente de la base à côté du fichier, avant une opération destructive ; retourne
    /// le chemin de la copie.</summary>
    public string CreateBackup(string reason)
    {
        using var ctx = Create();
        return BackupTo(ctx, reason);
    }

    private string BackupTo(FiltresDbContext ctx, string reason)
    {
        var backupPath = $"{_dbPath}.{reason}-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        ctx.Database.ExecuteSqlRaw("VACUUM INTO {0};", backupPath);
        return backupPath;
    }

    /// <summary>Copie cohérente de la base (VACUUM INTO) vers un emplacement choisi par l'utilisateur
    /// (bouton "Exporter la base de données", écran Paramètres) : contrairement à <see cref="CreateBackup"/>,
    /// qui nomme et place le fichier automatiquement à côté de la base, le chemin de destination est
    /// entièrement choisi par l'appelant.</summary>
    public void ExportTo(string destinationPath)
    {
        using var ctx = Create();
        ctx.Database.ExecuteSqlRaw("VACUUM INTO {0};", destinationPath);
    }
}
