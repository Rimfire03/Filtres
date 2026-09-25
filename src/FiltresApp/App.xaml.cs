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
