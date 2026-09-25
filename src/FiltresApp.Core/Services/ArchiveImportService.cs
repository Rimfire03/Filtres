using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using FiltresApp.Core.Data;
using FiltresApp.Core.Models;

namespace FiltresApp.Core.Services;

/// <summary>Une ligne de classeur d'archive qui n'a pas pu être rattachée avec confiance à un filtre déjà
/// existant en base (voir README, section "Import des archives 2014-2025") : consultable/à vérifier
/// manuellement par l'utilisateur dans le rapport texte produit par l'import.</summary>
public class ArchiveUnmatchedLine
{
    public string FileName { get; set; } = string.Empty;
    public int Year { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Dimension { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Résultat de l'import d'un classeur d'archive (une année).</summary>
public class ArchiveFileResult
{
    public string FileName { get; set; } = string.Empty;
    public int Year { get; set; }
    public int PeriodicRowsMatched { get; set; }
    public int PeriodicRowsUnmatched { get; set; }
    public int FilterReplacementsImported { get; set; }
    public int FilterReplacementsAlreadyPresent { get; set; }
    public int OpacimetricRowsMatched { get; set; }
    public int OpacimetricRowsUnmatched { get; set; }
    public int OpacimetricReplacementsImported { get; set; }
    public int OpacimetricReplacementsAlreadyPresent { get; set; }
    public int K7RowsMatched { get; set; }
    public int K7RowsUnmatched { get; set; }
    public List<string> Warnings { get; } = new();
    public bool Skipped { get; set; }
}

public class ArchiveImportReport
{
    public List<ArchiveFileResult> Files { get; } = new();
    public List<ArchiveUnmatchedLine> Unmatched { get; } = new();

    public int TotalFilterReplacementsImported => Files.Sum(f => f.FilterReplacementsImported);
    public int TotalOpacimetricReplacementsImported => Files.Sum(f => f.OpacimetricReplacementsImported);
}

/// <summary>
/// Import de l'historique des 12 classeurs d'archives (2014-2025) vers la base SQLite déjà en production.
/// Contrairement à <see cref="ExcelImportService"/> (import complet qui remplace toute la base), ce service
/// n'ajoute QUE de l'historique de remplacement (<see cref="FilterReplacement"/> / <see cref="OpacimetricReplacement"/>),
/// rattaché à des filtres <b>déjà existants</b> en base, et ne crée JAMAIS de nouveau <see cref="PeriodicFilter"/>
/// ni <see cref="OpacimetricFilter"/> : voir README, section "Import des archives 2014-2025", pour le détail de
/// cette consigne (imposée explicitement par l'utilisateur) et de la démarche de matching tolérant réutilisée
/// de <see cref="DimensionMatchService"/>.
///
/// <para>Une ligne d'archive qui ne peut pas être rattachée avec confiance à un filtre existant n'est jamais
/// utilisée pour créer un nouveau filtre : elle est simplement ignorée et consignée dans
/// <see cref="ArchiveImportReport.Unmatched"/> (voir <c>import-archives-non-rattaches.txt</c> à la racine du
/// projet, généré par <c>FiltresApp.ImportCli</c>, sous-commande <c>archive</c>).</para>
/// </summary>
public class ArchiveImportService
{
    private static readonly string[] PeriodicSheetNames =
    {
        "Filtres G4 plissés", "Filtres G4 plan", "Filtres G3", "Charbon"
    };

    private static readonly Dictionary<string, FilterCategory> PeriodicSheetCategories = new()
    {
        ["Filtres G4 plissés"] = FilterCategory.G4Plisse,
        ["Filtres G4 plan"] = FilterCategory.G4Plan,
        ["Filtres G3"] = FilterCategory.G3,
        ["Charbon"] = FilterCategory.Charbon,
    };

    /// <summary>Extrait l'année représentée par un fichier archive à partir de son nom (ex.
    /// "FILTRES  2014.xlsm" -> 2014, "Filtres 2025.xlsm" -> 2025). Les noms de fichiers ont des variations
    /// d'espacement/casse (voir README) mais contiennent tous un nombre à 4 chiffres identifiant l'année.</summary>
    public static int? ExtractYearFromFileName(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var m = Regex.Match(name, @"(20\d{2})");
        return m.Success ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    /// <summary>Importe l'historique d'une liste de classeurs d'archives dans la base <paramref name="dbPath"/>
    /// (déjà existante, jamais recréée/vidée). Chaque fichier est traité indépendamment : une erreur ou une
    /// structure trop différente sur un fichier n'empêche pas le traitement des suivants (voir
    /// <see cref="ArchiveFileResult.Skipped"/> / <see cref="ArchiveFileResult.Warnings"/>).</summary>
    public ArchiveImportReport ImportAll(string dbPath, IEnumerable<(string Path, int Year)> files)
    {
        var report = new ArchiveImportReport();
        var factory = new DbContextFactory(dbPath);
        factory.EnsureDatabaseCreated();
        using var ctx = factory.Create();

        // Charge une seule fois tous les filtres déjà en base (jamais recréés) avec leur historique existant,
        // pour matcher chaque ligne d'archive dessus et détecter les doublons éventuels.
        var periodicFilters = ctx.PeriodicFilters
            .Select(f => f)
            .ToList();
        foreach (var f in periodicFilters) ctx.Entry(f).Collection(x => x.Replacements).Load();

        var opacimetricFilters = ctx.OpacimetricFilters.ToList();
        foreach (var f in opacimetricFilters) ctx.Entry(f).Collection(x => x.Replacements).Load();

        var k7Locations = ctx.K7Locations.ToList();

        var periodicByCategory = periodicFilters
            .GroupBy(f => f.Category)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var (path, year) in files.OrderBy(f => f.Year))
        {
            var fileName = Path.GetFileName(path);
            var fileResult = new ArchiveFileResult { FileName = fileName, Year = year };
            report.Files.Add(fileResult);

            try
            {
                ImportOneFile(path, year, ctx, periodicByCategory, opacimetricFilters, k7Locations, fileResult, report.Unmatched);
                ctx.SaveChanges();
            }
            catch (Exception ex)
            {
                fileResult.Skipped = true;
                fileResult.Warnings.Add($"Fichier ignoré suite à une erreur : {ex.Message}");
            }
        }

        return report;
    }

    private void ImportOneFile(
        string path, int year, FiltresDbContext ctx,
        Dictionary<FilterCategory, List<PeriodicFilter>> periodicByCategory,
        List<OpacimetricFilter> opacimetricFilters,
        List<K7Location> k7Locations,
        ArchiveFileResult fileResult, List<ArchiveUnmatchedLine> unmatched)
    {
        if (!File.Exists(path))
        {
            fileResult.Skipped = true;
            fileResult.Warnings.Add("Fichier introuvable.");
            return;
        }

        using var wb = new XLWorkbook(path);

        foreach (var sheetName in PeriodicSheetNames)
        {
            if (!wb.Worksheets.TryGetWorksheet(sheetName, out var ws))
            {
                fileResult.Warnings.Add($"Feuille '{sheetName}' introuvable dans {fileResult.FileName}.");
                continue;
            }
            var category = PeriodicSheetCategories[sheetName];
            if (!periodicByCategory.TryGetValue(category, out var candidates)) candidates = new List<PeriodicFilter>();
            ImportPeriodicArchiveSheet(ws, category, candidates, year, fileResult, unmatched);
        }

        var opaSheet = wb.Worksheets.FirstOrDefault(w =>
            ExcelImportService.NormalizeHeader(w.Name).StartsWith("filtres f7"));
        if (opaSheet != null)
        {
            ImportOpacimetricArchiveSheet(opaSheet, opacimetricFilters, year, fileResult, unmatched);
        }
        else
        {
            fileResult.Warnings.Add($"Aucune feuille 'Filtres F7...' trouvée dans {fileResult.FileName}.");
        }

        if (wb.Worksheets.TryGetWorksheet("Liste K7", out var k7Ws))
        {
            MatchK7SheetInfoOnly(k7Ws, k7Locations, year, fileResult, unmatched);
        }
        else
        {
            fileResult.Warnings.Add($"Feuille 'Liste K7' introuvable dans {fileResult.FileName}.");
        }
    }

    // ---- Normalisation / matching ----

    private static string NormalizeLocation(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var normalized = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        var collapsed = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        return collapsed.ToLowerInvariant();
    }

    private static string? DimensionKey(string? dimension)
    {
        var token = DimensionMatchService.ExtractDimensionToken(dimension);
        return token == null ? null : DimensionMatchService.Normalize(token);
    }

    /// <summary>Tente de rattacher une ligne d'archive (emplacement + dimension) à un unique filtre déjà en
    /// base parmi <paramref name="candidates"/> : correspondance exacte (tolérante casse/espaces/accents) sur
    /// l'emplacement d'abord ; si plusieurs filtres partagent le même emplacement normalisé (arrive sur G3 où
    /// plusieurs filtres sont installés au même endroit), on affine avec la dimension. Ne retourne un match que
    /// s'il est unique et non ambigu : jamais de "au hasard parmi plusieurs candidats".</summary>
    private static (object? Match, string? Reason) TryMatch<T>(
        string archiveLocation, string? archiveDimension, IEnumerable<T> candidates,
        Func<T, string> getLocation, Func<T, string> getDimension)
    {
        var normLoc = NormalizeLocation(archiveLocation);
        if (normLoc.Length == 0) return (null, "Emplacement vide dans l'archive.");

        var sameLocation = candidates.Where(c => NormalizeLocation(getLocation(c)) == normLoc).ToList();
        if (sameLocation.Count == 0) return (null, "Aucun filtre existant avec cet emplacement.");
        if (sameLocation.Count == 1) return (sameLocation[0], null);

        var archiveDimKey = DimensionKey(archiveDimension);
        if (archiveDimKey != null)
        {
            var sameDim = sameLocation.Where(c => DimensionKey(getDimension(c)) == archiveDimKey).ToList();
            if (sameDim.Count == 1) return (sameDim[0], null);
        }

        return (null, $"Emplacement ambigu : {sameLocation.Count} filtres existants partagent cet emplacement, dimension insuffisante pour départager.");
    }

    // ---- Feuilles périodiques (G4 plissé / G4 plan / G3 / Charbon) ----

    private void ImportPeriodicArchiveSheet(
        IXLWorksheet ws, FilterCategory category, List<PeriodicFilter> candidates, int year,
        ArchiveFileResult fileResult, List<ArchiveUnmatchedLine> unmatched)
    {
        const int headerRow = 3;
        const int firstDataRow = 4;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? firstDataRow;

        var realizedCols = new Dictionary<int, int>();
        var dateCols = new Dictionary<int, int>();

        for (var c = 1; c <= lastCol; c++)
        {
            var header = ExcelImportService.NormalizeHeader(ws.Cell(headerRow, c).GetString());
            if (header.Contains("realise") || header.Contains("réalisé"))
            {
                var month = ExcelImportService.FindMonthInHeader(header);
                if (month.HasValue) realizedCols[month.Value] = c;
            }
            else if (header.Contains("date"))
            {
                var month = ExcelImportService.FindMonthInHeader(header);
                if (month.HasValue) dateCols[month.Value] = c;
            }
        }

        for (var r = firstDataRow; r <= lastRow; r++)
        {
            var location = ws.Cell(r, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(location)) continue;
            var dimension = ws.Cell(r, 3).GetString().Trim();

            var (matchObj, reason) = TryMatch(location, dimension, candidates,
                f => f.Location, f => f.Dimension);
            var match = matchObj as PeriodicFilter;

            if (match == null)
            {
                fileResult.PeriodicRowsUnmatched++;
                unmatched.Add(new ArchiveUnmatchedLine
                {
                    FileName = fileResult.FileName,
                    Year = year,
                    Category = category.ToString(),
                    Location = location,
                    Dimension = dimension,
                    Reason = reason ?? "Non rattaché."
                });
                continue;
            }

            fileResult.PeriodicRowsMatched++;

            foreach (var month in Enumerable.Range(1, 12))
            {
                int? realizedQty = null;
                if (realizedCols.TryGetValue(month, out var rc))
                {
                    var cell = ws.Cell(r, rc);
                    if (cell.TryGetValue(out double rv)) realizedQty = (int)Math.Round(rv);
                }

                DateOnly? dateDone = null;
                if (dateCols.TryGetValue(month, out var dc))
                {
                    var cell = ws.Cell(r, dc);
                    if (cell.TryGetValue(out DateTime dv)) dateDone = DateOnly.FromDateTime(dv);
                }

                if (!realizedQty.HasValue && !dateDone.HasValue) continue;

                var already = match.Replacements.Any(rep => rep.Month == month && rep.Year == year);
                if (already)
                {
                    fileResult.FilterReplacementsAlreadyPresent++;
                    continue;
                }

                var newRep = new FilterReplacement
                {
                    PeriodicFilterId = match.Id,
                    PeriodicFilter = match,
                    Month = month,
                    Year = year,
                    QuantityDone = realizedQty ?? 0,
                    DateDone = dateDone
                };
                match.Replacements.Add(newRep);
                fileResult.FilterReplacementsImported++;
            }
        }
    }

    // ---- Feuille F7-H10 / F7 a H13 (opacimétrique) ----

    private void ImportOpacimetricArchiveSheet(
        IXLWorksheet ws, List<OpacimetricFilter> candidates, int year,
        ArchiveFileResult fileResult, List<ArchiveUnmatchedLine> unmatched)
    {
        const int firstDataRow = 4;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? firstDataRow;
        var lastCol = Math.Min(ws.LastColumnUsed()?.ColumnNumber() ?? 4, 60);

        var inStockTable = false;

        for (var r = firstDataRow; r <= lastRow; r++)
        {
            var location = ws.Cell(r, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(location)) continue;

            var normLoc = ExcelImportService.NormalizeHeader(location);
            var col2 = ws.Cell(r, 2).GetString().Trim();

            if (normLoc.Contains("nom de la centrale")) { inStockTable = false; continue; }
            if (normLoc.StartsWith("maintenance filtres")) { inStockTable = false; continue; }
            if (normLoc.StartsWith("total poche")) continue;
            if (normLoc.StartsWith("dimensions") && ExcelImportService.NormalizeHeader(col2).StartsWith("stock reel"))
            {
                inStockTable = true;
                continue;
            }
            if (inStockTable) continue;

            var dimension = col2;

            var (matchObj, reason) = TryMatch(location, dimension, candidates,
                f => f.Location, f => f.Dimension);
            var match = matchObj as OpacimetricFilter;

            if (match == null)
            {
                fileResult.OpacimetricRowsUnmatched++;
                unmatched.Add(new ArchiveUnmatchedLine
                {
                    FileName = fileResult.FileName,
                    Year = year,
                    Category = "F7-H13 (opacimétrique)",
                    Location = location,
                    Dimension = dimension,
                    Reason = reason ?? "Non rattaché."
                });
                continue;
            }

            fileResult.OpacimetricRowsMatched++;

            for (var c = 4; c + 1 <= lastCol; c += 2)
            {
                var qtyCell = ws.Cell(r, c);
                var dateCell = ws.Cell(r, c + 1);
                var hasQty = qtyCell.TryGetValue(out double changedQty);
                DateOnly? dateChanged = null;
                if (dateCell.TryGetValue(out DateTime dv)) dateChanged = DateOnly.FromDateTime(dv);

                if (!hasQty && !dateChanged.HasValue) continue;

                var qty = hasQty ? (int)Math.Round(changedQty) : 0;
                var already = match.Replacements.Any(rep => rep.QuantityChanged == qty && rep.DateChanged == dateChanged);
                if (already)
                {
                    fileResult.OpacimetricReplacementsAlreadyPresent++;
                    continue;
                }

                var newRep = new OpacimetricReplacement
                {
                    OpacimetricFilterId = match.Id,
                    OpacimetricFilter = match,
                    QuantityChanged = qty,
                    DateChanged = dateChanged
                };
                match.Replacements.Add(newRep);
                fileResult.OpacimetricReplacementsImported++;
            }
        }
    }

    // ---- Feuille "Liste K7" : information seulement, aucune écriture ----

    /// <summary>La feuille "Liste K7" n'a pas de table d'historique en base (<see cref="K7Location"/> ne porte
    /// qu'une seule date "ChangementRealise", pas un historique par année comme <see cref="FilterReplacement"/>
    /// / <see cref="OpacimetricReplacement"/>) : le modèle de données actuel ne permet donc pas d'importer un
    /// historique multi-année pour cette feuille sans risquer d'écraser une donnée existante (interdit par la
    /// consigne). On se contente ici de compter les correspondances trouvées à titre informatif (voir README) ;
    /// aucune ligne K7 n'est donc jamais comptée comme "non rattachée" au sens d'un échec d'import de données.</summary>
    private void MatchK7SheetInfoOnly(
        IXLWorksheet ws, List<K7Location> candidates, int year,
        ArchiveFileResult fileResult, List<ArchiveUnmatchedLine> unmatched)
    {
        const int firstDataRow = 4;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? firstDataRow;

        for (var r = firstDataRow; r <= lastRow; r++)
        {
            var lieu = ws.Cell(r, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(lieu)) continue;

            var normLoc = NormalizeLocation(lieu);
            var match = candidates.FirstOrDefault(l => NormalizeLocation(l.Lieu) == normLoc);
            if (match != null) fileResult.K7RowsMatched++;
            else fileResult.K7RowsUnmatched++;
        }
    }
}
