using FiltresApp.Core.Data;
using FiltresApp.Core.Data.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace FiltresApp.Core.Services;

public class DbContextFactory
{
    private readonly DbTarget _target;
    private readonly bool _readOnly;

    public DbContextFactory(string dbPath, bool readOnly = false) : this(DbTarget.ForFile(dbPath), readOnly) { }

    public DbContextFactory(DbTarget target, bool readOnly = false)
    {
        _target = target;
        // Un serveur gère lui-même les accès simultanés : jamais de lecture seule imposée par le logiciel.
        _readOnly = readOnly && !target.IsServer;
        if (!target.IsServer)
        {
            var dir = Path.GetDirectoryName(target.FilePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        }
    }

    public DbTarget Target => _target;
    public bool IsServer => _target.IsServer;

    public FiltresDbContext Create() => new(_target, _readOnly);

    // Migrations du schéma : voir Data/Migrations/DatabaseMigrations.cs (règles de rédaction incluses).

    /// <summary>Version de base attendue par cette version de l'application.</summary>
    public static int LatestVersion => DatabaseMigrations.All[^1].Version;

    /// <summary>Libellés des migrations à appliquer depuis <paramref name="fromVersion"/>, pour la demande
    /// de confirmation.</summary>
    public static IReadOnlyList<string> PendingMigrations(int fromVersion) =>
        DatabaseMigrations.All.Where(m => m.Version > fromVersion).Select(m => $"v{m.Version} : {m.Description}").ToList();

    /// <summary>Libellés des mises à jour à appliquer sur un serveur depuis <paramref name="fromVersion"/>.</summary>
    public static IReadOnlyList<string> PendingServerMigrations(int fromVersion) =>
        DatabaseMigrations.ServerPlan(fromVersion).Select(m => $"v{m.Version} : {m.Description}").ToList();

    /// <summary>La base n'existe pas encore (fichier absent, ou serveur sans les tables de l'application).</summary>
    public bool IsNewDatabase()
    {
        if (!IsServer) return !File.Exists(_target.FilePath);
        using var ctx = Create();
        if (!ctx.Database.CanConnect()) return true;
        return !ServerSql.HasAppTables(ctx);
    }

    /// <summary>Version actuelle de la base (0 = base antérieure au système de version ou vide).</summary>
    public int GetDatabaseVersion()
    {
        using var ctx = Create();
        return ReadVersion(ctx);
    }

    /// <summary>Version de l'application qui a mis la base à jour en dernier, pour les messages d'erreur.</summary>
    public string? GetLastMigratedByAppVersion()
    {
        using var ctx = Create();
        if (IsServer) return ServerSql.GetInfo(ctx, "AppVersion");
        if (!SchemaInspector.GetColumns(ctx, "DbInfo").Contains("Value")) return null;
        return ctx.Database.SqlQueryRaw<string>("""SELECT "Value" AS "Value" FROM "DbInfo" WHERE "Key" = 'AppVersion'""").FirstOrDefault();
    }

    /// <summary>Test de connexion détaillé (écran de réglages) : lève une exception explicite en cas d'échec,
    /// sinon retourne la version du serveur et si la base de l'application y existe déjà.</summary>
    public (string ServerInfo, bool HasAppTables, bool DatabaseExists) TestConnection()
    {
        if (!IsServer) throw new InvalidOperationException("Test réservé aux serveurs.");
        using var ctx = Create();
        var conn = ctx.Database.GetDbConnection();
        try
        {
            conn.Open();
        }
        catch when (_target.Server!.Provider != DatabaseProvider.Sqlite && TryServerLevelConnect(out var info))
        {
            // Serveur joignable mais la base n'existe pas encore : elle sera créée.
            return (info, false, false);
        }
        var serverInfo = $"{conn.ServerVersion}";
        var has = ServerSql.HasAppTables(ctx);
        conn.Close();
        return (serverInfo, has, true);
    }

    private bool TryServerLevelConnect(out string info)
    {
        info = "";
        var server = _target.Server!;
        var adminDb = server.Provider switch
        {
            DatabaseProvider.PostgreSql => "postgres",
            DatabaseProvider.SqlServer => "master",
            _ => null
        };
        try
        {
            using System.Data.Common.DbConnection c = server.Provider switch
            {
                DatabaseProvider.PostgreSql => new Npgsql.NpgsqlConnection(server.BuildConnectionString(adminDb)),
                DatabaseProvider.SqlServer => new Microsoft.Data.SqlClient.SqlConnection(server.BuildConnectionString(adminDb)),
                _ => new MySqlConnector.MySqlConnection(server.BuildConnectionString(""))
            };
            c.Open();
            info = c.ServerVersion;
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Crée la base si besoin, sinon applique les migrations manquantes (sauvegarde préalable du
    /// fichier). Réservé au poste rédacteur (voir DbWriteLock).</summary>
    public void EnsureDatabaseUpToDate(string appVersion)
    {
        if (_readOnly) return;

        if (IsServer)
        {
            EnsureServerUpToDate(appVersion);
            return;
        }

        using var ctx = Create();
        if (ctx.Database.EnsureCreated())
        {
            // Base neuve : EnsureCreated vient de créer directement le schéma le plus récent.
            WriteVersion(ctx, LatestVersion, appVersion);
            DatabaseWriteTracking.EnsureTriggers(ctx);
            return;
        }

        var current = ReadVersion(ctx);
        var pending = DatabaseMigrations.All.Where(m => m.Version > current).ToList();
        if (pending.Count == 0)
        {
            DatabaseWriteTracking.EnsureTriggers(ctx);
            return;
        }

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

        // Déclencheurs du suivi de la dernière saisie (après les migrations, pour que leurs réécritures de données
        // ne comptent pas comme une saisie).
        DatabaseWriteTracking.EnsureTriggers(ctx);
    }

    /// <summary>Serveur : crée la base et les tables au dernier schéma si elles n'existent pas, sinon applique
    /// les mises à jour du schéma propres aux serveurs (voir <see cref="DatabaseMigrations.ServerPlan"/>).</summary>
    private void EnsureServerUpToDate(string appVersion)
    {
        using var ctx = Create();
        var creator = ctx.GetService<IRelationalDatabaseCreator>();
        if (!creator.Exists()) creator.Create();

        if (!ServerSql.HasAppTables(ctx))
        {
            creator.CreateTables();
            ServerSql.EnsureDbInfo(ctx);
            WriteVersion(ctx, LatestVersion, appVersion);
            return;
        }

        ServerSql.EnsureDbInfo(ctx);
        var current = ReadVersion(ctx);
        var reached = current;
        foreach (var migration in DatabaseMigrations.ServerPlan(current))
        {
            try
            {
                migration.Apply(ctx);
                WriteVersion(ctx, migration.Version, appVersion);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Échec de la mise à jour de la base du serveur vers la version {migration.Version} ({migration.Description}) : {ex.Message}\n" +
                    $"La base est restée en version {reached}.", ex);
            }
            reached = migration.Version;
            ctx.ChangeTracker.Clear();
        }
        if (reached < LatestVersion) WriteVersion(ctx, LatestVersion, appVersion);
    }

    private int ReadVersion(FiltresDbContext ctx)
    {
        if (!IsServer)
            return ctx.Database.SqlQueryRaw<int>("SELECT user_version AS \"Value\" FROM pragma_user_version").First();

        try
        {
            if (!ServerSql.HasAppTables(ctx)) return 0;
            var text = ServerSql.GetInfo(ctx, ServerSql.SchemaVersionKey);
            return int.TryParse(text, out var v) ? v : 0;
        }
        catch (System.Data.Common.DbException)
        {
            return 0; // table DbInfo absente
        }
    }

    private void WriteVersion(FiltresDbContext ctx, int version, string appVersion)
    {
        if (IsServer)
        {
            ServerSql.SetInfo(ctx, ServerSql.SchemaVersionKey, version.ToString());
            ServerSql.SetInfo(ctx, "AppVersion", appVersion);
            return;
        }

        // PRAGMA n'accepte pas de paramètre ; version est un entier issu du code.
        ctx.Database.ExecuteSqlRaw("PRAGMA user_version = " + version + ";");
        ctx.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "DbInfo" ("Key" TEXT NOT NULL PRIMARY KEY, "Value" TEXT NOT NULL);""");
        ctx.Database.ExecuteSqlRaw("""INSERT OR REPLACE INTO "DbInfo" ("Key", "Value") VALUES ('AppVersion', {0});""", appVersion);
    }

    /// <summary>Copie cohérente de la base (VACUUM INTO) avant toute migration, à côté du fichier.</summary>
    private void Backup(FiltresDbContext ctx, int fromVersion) => BackupTo(ctx, $"avant-maj-v{fromVersion}");

    /// <summary>Copie cohérente de la base avant une opération destructive ; retourne le chemin de la copie.
    /// Fichier SQLite : à côté de la base. Serveur : fichier SQLite complet dans le dossier des données.</summary>
    public string CreateBackup(string reason)
    {
        if (IsServer)
        {
            var path = Path.Combine(AppSettings.DataDirectoryPath, $"serveur.{reason}-{DateTime.Now:yyyyMMdd-HHmmss}.bak");
            ExportTo(path);
            return path;
        }
        using var ctx = Create();
        return BackupTo(ctx, reason);
    }

    private string BackupTo(FiltresDbContext ctx, string reason)
    {
        var backupPath = $"{_target.FilePath}.{reason}-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        ctx.Database.ExecuteSqlRaw("VACUUM INTO {0};", backupPath);
        return backupPath;
    }

    /// <summary>Copie cohérente de la base vers un fichier SQLite choisi par l'utilisateur (bouton
    /// "Exporter la base de données", écran Paramètres). Sur un serveur, la copie est un fichier SQLite
    /// complet, réimportable par « Importer une sauvegarde ».</summary>
    public void ExportTo(string destinationPath)
    {
        if (IsServer)
        {
            if (File.Exists(destinationPath)) File.Delete(destinationPath);
            DatabaseCopier.Copy(_target, DbTarget.ForFile(destinationPath), appVersion: null, replaceDestination: true);
            return;
        }
        using var ctx = Create();
        ctx.Database.ExecuteSqlRaw("VACUUM INTO {0};", destinationPath);
    }
}
