using System.Diagnostics;
using System.IO;
using System.Windows;
using FiltresApp.Core.Services;
using FiltresApp.Services;

namespace FiltresApp;

/// <summary>Ouverture de la base : choix du fichier, accès en écriture (un seul poste), contrôle de version.</summary>
public partial class App
{
    private static DbWriteLock? _writeLock;
    private static string? _writeLockPath;

    /// <summary>Version du fichier de base ouvert (voir DbContextFactory.LatestVersion).</summary>
    public static int DatabaseVersion { get; private set; }

    /// <summary>Tant qu'aucun fichier de base de données n'existe à l'emplacement configuré (ex. tout
    /// premier lancement après un clone du dépôt, qui ne contient volontairement aucune DB), propose à
    /// l'utilisateur d'en créer une nouvelle (vide) ou de choisir un fichier .db existant (ex. une
    /// sauvegarde). Retourne false si l'utilisateur annule, auquel cas l'application doit se fermer.</summary>
    private static bool EnsureDatabaseSelected()
    {
        while (!File.Exists(Settings.ResolvedDatabasePath))
        {
            var choice = MessageBox.Show(
                $"Aucune base de données trouvée à l'emplacement :\n{Settings.ResolvedDatabasePath}\n\n" +
                "Oui : créer une nouvelle base de données vide à cet emplacement.\n" +
                "Non : choisir un fichier de base de données existant (.db).\n" +
                "Annuler : fermer l'application.",
                "Base de données introuvable", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            switch (choice)
            {
                case MessageBoxResult.Yes:
                    return true; // EnsureDatabaseUpToDate() créera la base juste après

                case MessageBoxResult.No:
                    var dialog = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Choisir une base de données existante",
                        Filter = "Base de données SQLite (*.db)|*.db|Tous les fichiers (*.*)|*.*"
                    };
                    if (dialog.ShowDialog() == true)
                    {
                        Settings.DatabasePath = dialog.FileName;
                        Settings.Save();
                    }
                    break; // reboucle : re-teste File.Exists avec le chemin (éventuellement) mis à jour

                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>Prend l'accès en écriture si aucun autre poste ne l'a déjà, sinon ouvre la base en
    /// lecture seule.</summary>
    private static void OpenDatabase(string path)
    {
        // Rechargement du même fichier par le rédacteur : on garde le verrou pour ne pas le céder.
        if (_writeLock is null || !string.Equals(_writeLockPath, path, StringComparison.OrdinalIgnoreCase))
        {
            _writeLock?.Dispose();
            _writeLock = DbWriteLock.TryAcquire(path);
            _writeLockPath = path;
        }
        IsReadOnly = _writeLock is null;
        WriteLockOwner = IsReadOnly ? DbWriteLock.ReadOwner(path) : null;

        DbFactory = new DbContextFactory(path, IsReadOnly);
        CheckDatabaseVersion(path);
        DbFactory.EnsureDatabaseUpToDate(CurrentVersion);
        DatabaseVersion = DbFactory.GetDatabaseVersion();
        Db = DbFactory.Create();
        PeriodicViewRegistry.Load(Db);
        LoadCompanyLogo();
    }

    /// <summary>Bloque l'ouverture si ce logiciel et la base ne sont pas à la même version : logiciel trop
    /// ancien pour une base déjà mise à jour, ou poste en lecture seule qui ne peut pas mettre la base à
    /// jour lui-même.</summary>
    private static void CheckDatabaseVersion(string path)
    {
        if (!File.Exists(path)) return; // base neuve, créée directement à la dernière version

        var dbVersion = DbFactory.GetDatabaseVersion();
        var expected = DbContextFactory.LatestVersion;

        if (dbVersion > expected)
        {
            var by = DbFactory.GetLastMigratedByAppVersion();
            throw new DatabaseVersionException(
                $"Cette version du logiciel ({CurrentVersion}) est trop ancienne pour ouvrir la base de données.\n\n" +
                $"Version de la base : {dbVersion}" + (by is null ? "" : $" (mise à jour par le logiciel version {by})") + "\n" +
                $"Version de base gérée par ce logiciel : {expected}\n\n" +
                "Installez la dernière version du logiciel sur ce poste" + (by is null ? "" : $" ({by} ou plus récente)") +
                ", puis relancez-le. Rien n'a été modifié dans la base.");
        }

        if (dbVersion < expected && !IsReadOnly)
        {
            var changes = string.Join("\n", DbContextFactory.PendingMigrations(dbVersion).Select(c => "  • " + c));
            var accepted = MessageBox.Show(
                $"La base de données est en version {dbVersion} ; cette version du logiciel ({CurrentVersion}) a besoin de la version {expected}.\n\n" +
                $"Modifications à appliquer :\n{changes}\n\n" +
                "Une copie de sauvegarde complète de la base sera faite à côté du fichier avant la mise à jour. " +
                "Après la mise à jour, les postes équipés d'une version plus ancienne du logiciel ne pourront plus l'ouvrir.\n\n" +
                "Mettre à jour la base maintenant ?",
                "Mise à jour de la base de données", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
            if (accepted != MessageBoxResult.Yes)
                throw new DatabaseVersionException(
                    $"La base de données n'a pas été mise à jour : elle reste en version {dbVersion}, et cette version du logiciel ({CurrentVersion}) " +
                    $"ne peut pas l'ouvrir sans la mettre en version {expected}.\n\n" +
                    "Relancez le logiciel et acceptez la mise à jour, ou utilisez sur ce poste la version du logiciel qui correspond à la base.");
        }

        if (dbVersion < expected && IsReadOnly)
        {
            var owner = WriteLockOwner is null ? "un autre utilisateur" : "@" + WriteLockOwner;
            throw new DatabaseVersionException(
                $"La base de données doit être mise à jour (version {dbVersion} → {expected}) pour cette version du logiciel ({CurrentVersion}), " +
                $"mais elle est actuellement ouverte en écriture par {owner}, qui utilise une version plus ancienne du logiciel.\n\n" +
                "La mise à jour de la base se fera automatiquement au prochain lancement du logiciel à jour sur un poste " +
                $"ayant l'accès en écriture. Demandez à {owner} de fermer le logiciel (et de le mettre à jour), puis relancez-le ici.");
        }
    }

    /// <summary>Remplace le fichier de base courant par une sauvegarde choisie (bouton "Importer une
    /// sauvegarde", écran Paramètres), puis relance l'application : c'est ce redémarrage qui déclenche,
    /// via <see cref="OpenDatabase"/> → <see cref="CheckDatabaseVersion"/> → <see cref="DbContextFactory.EnsureDatabaseUpToDate"/>,
    /// la vérification de la version de la sauvegarde importée et sa mise à jour automatique si elle est
    /// antérieure (avec la même confirmation et la même sauvegarde préalable que pour toute autre mise à
    /// jour de base). Réservé au poste rédacteur (voir <see cref="GuardWritable"/>, appelé par l'appelant).</summary>
    public static void ImportDatabaseBackup(string backupPath)
    {
        Db.Dispose();
        // Les connexions Sqlite peuvent rester mises en pool après Dispose() et garder le fichier
        // verrouillé : on les libère explicitement avant d'écraser le fichier.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Copy(backupPath, Settings.ResolvedDatabasePath, overwrite: true);
        Restart();
    }

    /// <summary>Relance l'application dans un nouveau processus puis ferme celui-ci. Utilisé après un
    /// changement de chemin de base de données (Paramètres) : un simple rechargement du contexte ne
    /// suffirait pas, tous les écrans déjà ouverts (barre latérale, année consultée...) gardant sinon des
    /// données de l'ancienne base en mémoire.</summary>
    public static void Restart()
    {
        var exePath = Environment.ProcessPath;
        if (exePath is not null)
            Process.Start(new ProcessStartInfo { FileName = exePath, UseShellExecute = true });
        Current.Shutdown();
    }

    /// <summary>Construit le contexte d'année partagé par les écrans de suivi : année courante par
    /// défaut, plus toutes les années déjà présentes dans l'historique de remplacements en base.</summary>
    private static YearContext CreateYearContext()
    {
        var yearsInData = Db.FilterReplacements.Select(r => r.Year).Distinct().ToList();
        return new YearContext(DateTime.Today.Year, yearsInData);
    }
}
