using System.IO;
using System.Windows;
using FiltresApp.Core.Data;
using FiltresApp.Core.Services;
using FiltresApp.Services;

namespace FiltresApp;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = null!;
    public static DbContextFactory DbFactory { get; private set; } = null!;
    public static FiltresDbContext Db { get; private set; } = null!;
    public static IDialogService Dialogs { get; private set; } = null!;
    public static PrintService Printer { get; private set; } = null!;
    public static PdfExportService PdfExport { get; private set; } = null!;
    public static ExcelExportService ExcelExport { get; private set; } = null!;
    public static YearContext YearContext { get; private set; } = null!;
    public static UpdateService Updater { get; } = new();

    private static DbWriteLock? _writeLock;
    private static string? _writeLockPath;

    /// <summary>Vrai si un autre poste détenait déjà l'accès en écriture au démarrage (voir
    /// <see cref="DbWriteLock"/>) : ce poste peut consulter les données mais pas les modifier.</summary>
    public static bool IsReadOnly { get; private set; }
    public static bool IsWritable => !IsReadOnly;

    /// <summary>Poste qui détient l'accès en écriture, affiché dans le bandeau "lecture seule".</summary>
    public static string? WriteLockOwner { get; private set; }

    /// <summary>Version du fichier de base ouvert (voir DbContextFactory.LatestVersion).</summary>
    public static int DatabaseVersion { get; private set; }

    /// <summary>Version courante de l'application (définie par &lt;Version&gt; dans le .csproj),
    /// comparée à la dernière release GitHub par <see cref="Updater"/>.</summary>
    public static string CurrentVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            MessageBox.Show(ex.Exception.ToString(), "Erreur non gérée", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        try
        {
            Settings = AppSettings.Load();
            if (!EnsureDatabaseSelected())
            {
                Shutdown(-1);
                return;
            }

            OpenDatabase(Settings.ResolvedDatabasePath);
        }
        catch (DatabaseVersionException ex)
        {
            MessageBox.Show(ex.Message, "Version du logiciel incompatible avec la base", MessageBoxButton.OK, MessageBoxImage.Stop);
            Shutdown(-1);
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Impossible d'ouvrir la base de données :\n{Settings?.ResolvedDatabasePath}\n\n{ex.Message}",
                "Erreur au démarrage", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        Dialogs = new DialogService();
        Printer = new PrintService();
        PdfExport = new PdfExportService();
        ExcelExport = new ExcelExportService();
        YearContext = CreateYearContext();

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

        if (IsReadOnly)
            MessageBox.Show(mainWindow, ReadOnlyMessage, "Données en lecture seule", MessageBoxButton.OK, MessageBoxImage.Information);

        if (Settings.AutoUpdateEnabled) _ = CheckForUpdateOnStartupAsync();
    }

    /// <summary>Vérification silencieuse au démarrage : ne bloque jamais le lancement de l'application
    /// et n'interrompt l'utilisateur que si une mise à jour est réellement disponible.</summary>
    private static async Task CheckForUpdateOnStartupAsync()
    {
        try
        {
            var info = await Updater.CheckForUpdateAsync(CurrentVersion);
            if (info is null) return;

            var proceed = Dialogs.ShowConfirm("Mise à jour disponible",
                $"Une nouvelle version {info.Version} est disponible (version actuelle : {CurrentVersion}).\n\n" +
                "Voulez-vous la télécharger et l'installer maintenant ? L'application va se fermer puis redémarrer automatiquement.\n\n" +
                "Vous pouvez désactiver cette vérification automatique dans Paramètres.");
            if (!proceed) return;

            await Updater.DownloadAndApplyAsync(info);
            Current.Shutdown();
        }
        catch (Exception ex)
        {
            // La vérification/installation de mise à jour ne doit jamais faire planter l'application.
            Dialogs.ShowMessage("Mise à jour", $"La mise à jour automatique a échoué : {ex.Message}");
        }
    }

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

    protected override void OnExit(ExitEventArgs e)
    {
        Db?.Dispose();
        _writeLock?.Dispose();
        base.OnExit(e);
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

    /// <summary>Recrée le contexte de base de données courant (après changement de chemin
    /// dans les paramètres, ou après un import qui a remplacé le fichier).</summary>
    public static void ReloadDatabase(string newPath)
    {
        Db?.Dispose();
        OpenDatabase(newPath);
        YearContext = CreateYearContext();
    }

    /// <summary>À appeler avant toute modification de données : sur un poste en lecture seule, prévient
    /// l'utilisateur et retourne false.</summary>
    public static bool GuardWritable()
    {
        if (IsWritable) return true;
        Dialogs.ShowMessage("Lecture seule", ReadOnlyMessage + "\n\nAucune modification n'a été enregistrée.");
        return false;
    }

    public static string ReadOnlyTitle =>
        "Données en lecture seule : fichier actuellement utilisé par " +
        (WriteLockOwner is null ? "un autre utilisateur" : "@" + WriteLockOwner);

    public static string ReadOnlyMessage =>
        ReadOnlyTitle + ". Fermez puis relancez l'application une fois qu'il l'a quittée pour pouvoir modifier les données.";

    /// <summary>Construit le contexte d'année partagé par les écrans de suivi : année courante par
    /// défaut, plus toutes les années déjà présentes dans l'historique de remplacements en base.</summary>
    private static YearContext CreateYearContext()
    {
        var yearsInData = Db.FilterReplacements.Select(r => r.Year).Distinct().ToList();
        return new YearContext(DateTime.Today.Year, yearsInData);
    }
}

/// <summary>Logiciel et base de données à des versions incompatibles : le démarrage est bloqué.</summary>
public class DatabaseVersionException(string message) : Exception(message);
