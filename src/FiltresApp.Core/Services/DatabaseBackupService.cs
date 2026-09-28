using Microsoft.Data.Sqlite;

namespace FiltresApp.Core.Services;

public record DatabaseImportResult(int PreviousSchemaVersion, int NewSchemaVersion)
{
    public bool WasUpgraded => NewSchemaVersion > PreviousSchemaVersion;
}

/// <summary>Sauvegarde/restauration complète du fichier de base SQLite (bouton "Exporter la base de
/// données" / "Importer une sauvegarde" de l'écran Paramètres), indépendamment de l'import/export Excel
/// existant. Un import réutilise le mécanisme de migration idempotent de
/// <see cref="DbContextFactory.EnsureDatabaseCreated"/> (basé sur la présence des colonnes/tables), la
/// version de schéma SQLite (PRAGMA user_version, voir <see cref="DbContextFactory.CurrentSchemaVersion"/>)
/// ne servant qu'à détecter et afficher qu'une mise à jour a bien eu lieu.</summary>
public static class DatabaseBackupService
{
    public static string BuildExportFileName(string appVersion, DateTime? at = null) =>
        $"save_db_filtre_{(at ?? DateTime.Now):yyyyMMdd_HHmmss}_{appVersion}.db";

    /// <summary>Copie cohérente du fichier de base vers <paramref name="destinationPath"/> via l'API de
    /// sauvegarde SQLite : fonctionne même pendant que l'application a la base source ouverte.</summary>
    public static void Export(string sourceDbPath, string destinationPath)
    {
        using var source = new SqliteConnection($"Data Source={sourceDbPath};Mode=ReadOnly");
        using var destination = new SqliteConnection($"Data Source={destinationPath}");
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    public static int ReadSchemaVersion(string dbPath)
    {
        using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>Remplace le fichier de base courant par <paramref name="backupPath"/> (et ses éventuels
    /// fichiers annexes -wal/-shm, pour ne pas mélanger d'anciennes pages en attente avec le nouveau
    /// fichier principal). L'appelant doit avoir fermé toute connexion vers <paramref name="targetDbPath"/>
    /// avant d'appeler cette méthode, puis recharger la base ensuite (voir App.ReloadDatabase) : c'est ce
    /// rechargement qui déclenche la mise à jour du schéma si la sauvegarde est antérieure à la version
    /// courante.</summary>
    public static int ReplaceDatabaseFile(string backupPath, string targetDbPath)
    {
        var previousVersion = ReadSchemaVersion(backupPath);

        File.Copy(backupPath, targetDbPath, overwrite: true);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sourceSidecar = backupPath + suffix;
            var targetSidecar = targetDbPath + suffix;
            if (File.Exists(sourceSidecar)) File.Copy(sourceSidecar, targetSidecar, overwrite: true);
            else if (File.Exists(targetSidecar)) File.Delete(targetSidecar);
        }

        return previousVersion;
    }
}
