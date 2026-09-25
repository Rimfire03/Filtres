using FiltresApp.Core.Services;

if (args.Length < 1)
{
    Console.WriteLine("Usage: FiltresApp.ImportCli <chemin-vers-fichier.xlsm> [chemin-vers-base.db]");
    Console.WriteLine("       FiltresApp.ImportCli archive <chemin-base.db> <chemin-rapport.txt> <fichier1.xlsm> [fichier2.xlsm ...]");
    return 1;
}

if (string.Equals(args[0], "archive", StringComparison.OrdinalIgnoreCase))
{
    return RunArchiveImport(args.Skip(1).ToArray());
}

var xlsmPath = args[0];
var dbPath = args.Length > 1 ? args[1] : Path.Combine(AppContext.BaseDirectory, "data", "filtres.db");

Console.WriteLine($"Import depuis : {xlsmPath}");
Console.WriteLine($"Base de données cible : {dbPath}");

using var writeLock = AcquireWriteLockOrReport(dbPath);
if (writeLock is null) return 1;

try
{
    var importer = new ExcelImportService();
    var result = importer.Import(xlsmPath, dbPath);

    Console.WriteLine();
    Console.WriteLine("Import terminé.");
    Console.WriteLine($"  Filtres périodiques (G4/G3/Charbon) : {result.PeriodicFiltersImported}");
    Console.WriteLine($"  Filtres F7-H13                      : {result.OpacimetricFiltersImported}");
    Console.WriteLine($"  Lieux K7                             : {result.K7LocationsImported}");
    Console.WriteLine($"  Lignes inventaire                    : {result.InventoryLinesImported}");
    Console.WriteLine($"  Lignes commande                      : {result.OrderLinesImported}");
    Console.WriteLine($"  TOTAL                                : {result.Total}");

    if (result.Warnings.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("Avertissements :");
        foreach (var w in result.Warnings) Console.WriteLine($"  - {w}");
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Erreur lors de l'import : {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 1;
}

static int RunArchiveImport(string[] args)
{
    if (args.Length < 3)
    {
        Console.WriteLine("Usage: FiltresApp.ImportCli archive <chemin-base.db> <chemin-rapport.txt> <fichier1.xlsm> [fichier2.xlsm ...]");
        return 1;
    }

    var dbPath = args[0];
    var reportPath = args[1];
    var xlsmPaths = args.Skip(2).ToArray();

    var files = new List<(string Path, int Year)>();
    foreach (var p in xlsmPaths)
    {
        var year = ArchiveImportService.ExtractYearFromFileName(p);
        if (year == null)
        {
            Console.Error.WriteLine($"Impossible de déduire l'année depuis le nom du fichier : {p} (ignoré).");
            continue;
        }
        files.Add((p, year.Value));
    }

    Console.WriteLine($"Base de données cible : {dbPath}");
    Console.WriteLine($"Fichiers à importer : {files.Count}");
    foreach (var (path, year) in files.OrderBy(f => f.Year))
        Console.WriteLine($"  {year} : {path}");
    Console.WriteLine();

    using var writeLock = AcquireWriteLockOrReport(dbPath);
    if (writeLock is null) return 1;

    try
    {
        var importer = new ArchiveImportService();
        var report = importer.ImportAll(dbPath, files);

        Console.WriteLine("Import des archives terminé.");
        Console.WriteLine();
        Console.WriteLine($"{"Année",-6} {"Fichier",-30} {"Périodique OK/KO",-18} {"F.Repl +/=",-12} {"Opa OK/KO",-14} {"O.Repl +/=",-12} {"K7 OK/KO",-10}");
        foreach (var f in report.Files.OrderBy(f => f.Year))
        {
            var status = f.Skipped ? " [IGNORÉ]" : "";
            Console.WriteLine($"{f.Year,-6} {f.FileName,-30} {f.PeriodicRowsMatched}/{f.PeriodicRowsUnmatched,-14} {f.FilterReplacementsImported}/{f.FilterReplacementsAlreadyPresent,-9} {f.OpacimetricRowsMatched}/{f.OpacimetricRowsUnmatched,-11} {f.OpacimetricReplacementsImported}/{f.OpacimetricReplacementsAlreadyPresent,-9} {f.K7RowsMatched}/{f.K7RowsUnmatched}{status}");
            foreach (var w in f.Warnings) Console.WriteLine($"    ! {w}");
        }

        Console.WriteLine();
        Console.WriteLine($"TOTAL FilterReplacement importés : {report.TotalFilterReplacementsImported}");
        Console.WriteLine($"TOTAL OpacimetricReplacement importés : {report.TotalOpacimetricReplacementsImported}");
        Console.WriteLine($"TOTAL lignes non rattachées : {report.Unmatched.Count} (voir {reportPath})");

        WriteUnmatchedReport(reportPath, report);

        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Erreur lors de l'import des archives : {ex.Message}");
        Console.Error.WriteLine(ex.StackTrace);
        return 1;
    }
}

static DbWriteLock? AcquireWriteLockOrReport(string dbPath)
{
    var writeLock = DbWriteLock.TryAcquire(dbPath);
    if (writeLock is null)
    {
        var owner = DbWriteLock.ReadOwner(dbPath);
        Console.Error.WriteLine("Import annulé : la base est ouverte en écriture par l'application" +
            (owner is null ? "." : $" par @{owner}.") + " Fermez l'application sur ce poste puis relancez l'import.");
    }
    return writeLock;
}

static void WriteUnmatchedReport(string reportPath, ArchiveImportReport report)
{
    using var w = new StreamWriter(reportPath, false, System.Text.Encoding.UTF8);
    w.WriteLine("Rapport des lignes d'archives non rattachées à un filtre existant");
    w.WriteLine("====================================================================");
    w.WriteLine($"Généré le {DateTime.Now:dd/MM/yyyy HH:mm}");
    w.WriteLine();
    w.WriteLine("Ces lignes n'ont PAS créé de nouveau filtre en base (consigne explicite : ne jamais créer");
    w.WriteLine("de filtre qui n'existe pas déjà). Vérifiez-les manuellement : filtre renommé/déplacé,");
    w.WriteLine("filtre supprimé depuis, ou emplacement ambigu (plusieurs filtres existants au même endroit).");
    w.WriteLine();

    foreach (var group in report.Unmatched.GroupBy(u => u.Year).OrderBy(g => g.Key))
    {
        w.WriteLine($"--- Année {group.Key} ---");
        foreach (var byFile in group.GroupBy(u => u.FileName))
        {
            w.WriteLine($"  Fichier : {byFile.Key}");
            foreach (var line in byFile.OrderBy(l => l.Category).ThenBy(l => l.Location))
            {
                w.WriteLine($"    [{line.Category}] Emplacement=\"{line.Location}\" Dimension=\"{line.Dimension}\" -> {line.Reason}");
            }
        }
        w.WriteLine();
    }

    if (report.Unmatched.Count == 0)
        w.WriteLine("(Aucune ligne non rattachée : toutes les lignes des archives ont été rattachées à un filtre existant.)");

    w.WriteLine();
    w.WriteLine("Note sur la feuille \"Liste K7\" : le modèle de données actuel (K7Location) ne porte qu'une");
    w.WriteLine("seule date de dernier changement (pas d'historique multi-année comme FilterReplacement /");
    w.WriteLine("OpacimetricReplacement) : aucune donnée n'a donc été importée pour cette feuille (uniquement");
    w.WriteLine("un comptage informatif des correspondances trouvées, affiché dans la sortie console de");
    w.WriteLine("l'import). Voir README pour plus de détails.");
}
