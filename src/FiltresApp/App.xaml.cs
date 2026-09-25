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
    public static ExcelImportService Importer { get; private set; } = null!;
    public static YearContext YearContext { get; private set; } = null!;
    public static UpdateService Updater { get; } = new();

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

            DbFactory = new DbContextFactory(Settings.ResolvedDatabasePath);
            DbFactory.EnsureDatabaseCreated();
            Db = DbFactory.Create();
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
        Importer = new ExcelImportService();
        YearContext = CreateYearContext();

        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();

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
                    return true; // EnsureDatabaseCreated() créera le fichier vide juste après

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
        base.OnExit(e);
    }

    /// <summary>Recrée le contexte de base de données courant (après changement de chemin
    /// dans les paramètres, ou après un import qui a remplacé le fichier).</summary>
    public static void ReloadDatabase(string newPath)
    {
        Db?.Dispose();
        DbFactory = new DbContextFactory(newPath);
        DbFactory.EnsureDatabaseCreated();
        Db = DbFactory.Create();
        YearContext = CreateYearContext();
    }

    /// <summary>Construit le contexte d'année partagé par les écrans de suivi : année courante par
    /// défaut, plus toutes les années déjà présentes dans l'historique de remplacements en base.</summary>
    private static YearContext CreateYearContext()
    {
        var yearsInData = Db.FilterReplacements.Select(r => r.Year).Distinct().ToList();
        return new YearContext(DateTime.Today.Year, yearsInData);
    }
}
