using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using FiltresApp.Core.Data;
using FiltresApp.Core.Models;

namespace FiltresApp.Core.Services;

public class ImportResult
{
    public int PeriodicFiltersImported { get; set; }
    public int OpacimetricFiltersImported { get; set; }
    public int K7LocationsImported { get; set; }
    public int InventoryLinesImported { get; set; }
    public int OrderLinesImported { get; set; }
    public List<string> Warnings { get; } = new();

    public int Total => PeriodicFiltersImported + OpacimetricFiltersImported + K7LocationsImported
        + InventoryLinesImported + OrderLinesImported;
}

/// <summary>Importe les données du classeur Excel original (.xlsm) vers la base SQLite.
/// Lit uniquement les valeurs (pas les macros/ActiveX). Les colonnes "changement prévu"
/// ne sont pas importées : elles sont recalculées par MaintenanceScheduleService.</summary>
public class ExcelImportService
{
    private static readonly string[] MonthNames =
    {
        "janvier", "fevrier", "mars", "avril", "mai", "juin",
        "juillet", "aout", "septembre", "octobre", "novembre", "decembre"
    };

    public ImportResult Import(string xlsmPath, string dbPath, bool wipeExisting = true)
    {
        if (!File.Exists(xlsmPath))
            throw new FileNotFoundException("Fichier Excel introuvable", xlsmPath);

        var result = new ImportResult();
        using var workbook = new XLWorkbook(xlsmPath);

        var factory = new DbContextFactory(dbPath);
        factory.EnsureDatabaseCreated();
        using var ctx = factory.Create();

        if (wipeExisting)
        {
            ctx.FilterReplacements.RemoveRange(ctx.FilterReplacements);
            ctx.PeriodicFilters.RemoveRange(ctx.PeriodicFilters);
            ctx.OpacimetricReplacements.RemoveRange(ctx.OpacimetricReplacements);
            ctx.OpacimetricFilters.RemoveRange(ctx.OpacimetricFilters);
            ctx.K7Locations.RemoveRange(ctx.K7Locations);
            ctx.K7Families.RemoveRange(ctx.K7Families);
            ctx.InventoryLines.RemoveRange(ctx.InventoryLines);
            ctx.OrderLines.RemoveRange(ctx.OrderLines);
            ctx.SaveChanges();
        }

        ImportPeriodicSheet(workbook, "Filtres G4 plissés", FilterCategory.G4Plisse, ctx, result);
        ImportPeriodicSheet(workbook, "Filtres G4 plan", FilterCategory.G4Plan, ctx, result);
        ImportPeriodicSheet(workbook, "Filtres G3", FilterCategory.G3, ctx, result);
        ImportPeriodicSheet(workbook, "Charbon", FilterCategory.Charbon, ctx, result);
        ImportOpacimetricSheet(workbook, "Filtres F7 a H13", ctx, result);
        ImportK7Sheet(workbook, "Liste K7", ctx, result);
        // Reconstitue les familles K7 à partir du texte libre de "Lieu" (voir README) : les nouvelles
        // K7Location viennent d'être ajoutées au contexte (pas encore SaveChanges), la méthode gère aussi
        // bien des entités tracked non enregistrées que déjà en base.
        K7FamilyReconstructionService.ReconstructIfNeeded(ctx);
        ImportInventorySheet(workbook, "inventaire", ctx, result);
        ImportOrderSheet(workbook, "Commande chmy", OrderDocumentType.CommandeChmy, ctx, result);
        // Fusion Inventaire / Commande chmy (voir README) : les lignes de la feuille "inventaire" sont
        // désormais dupliquées vers OrderLines (type CommandeChmy) en plus d'InventoryLines (conservée
        // telle quelle comme copie de sauvegarde historique), pour qu'un futur réimport garde les deux
        // écrans synchronisés sur la même liste, comme c'est le cas de la base déjà en production
        // (migrée une fois par DbContextFactory.EnsureDatabaseCreated).
        MergeInventoryIntoOrderLines(ctx);
        // "pour devis" et "filtres a refacturer" ne sont plus importés depuis le 25/09/2026 (écrans et
        // données supprimés définitivement, voir README) : ces deux feuilles Excel, si présentes dans un
        // futur classeur source, seraient désormais ignorées silencieusement par un réimport.

        ctx.SaveChanges();
        return result;
    }

    /// <summary>Rendu accessible (internal) pour être réutilisé tel quel par <see cref="ArchiveImportService"/>
    /// (import de l'historique des classeurs d'archives 2014-2025, voir README), qui a besoin de la même
    /// détection de colonnes mensuelles par en-tête que l'import principal.</summary>
    internal static string NormalizeHeader(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Replace("\n", " ").Replace("\r", " ").ToLowerInvariant();
    }

    internal static int? FindMonthInHeader(string normalizedHeader)
    {
        for (var i = 0; i < MonthNames.Length; i++)
        {
            if (normalizedHeader.Contains(MonthNames[i])) return i + 1;
        }
        return null;
    }

    private void ImportPeriodicSheet(XLWorkbook wb, string sheetName, FilterCategory category,
        FiltresDbContext ctx, ImportResult result)
    {
        if (!wb.Worksheets.TryGetWorksheet(sheetName, out var ws))
        {
            result.Warnings.Add($"Feuille '{sheetName}' introuvable.");
            return;
        }

        const int headerRow = 3;
        const int firstDataRow = 4;
        var lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 1;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? firstDataRow;

        var realizedCols = new Dictionary<int, int>();
        var dateCols = new Dictionary<int, int>();
        var counterCols = new List<int>();

        for (var c = 1; c <= lastCol; c++)
        {
            var header = NormalizeHeader(ws.Cell(headerRow, c).GetString());
            if (header.Contains("realise") || header.Contains("réalisé") || header.Contains("changement realise") || header.Contains("changement  realise"))
            {
                var month = FindMonthInHeader(header);
                if (month.HasValue) realizedCols[month.Value] = c;
            }
            else if (header.Contains("date"))
            {
                var month = FindMonthInHeader(header);
                if (month.HasValue) dateCols[month.Value] = c;
            }
            else if (header.Contains("compteur"))
            {
                counterCols.Add(c);
            }
        }

        for (var r = firstDataRow; r <= lastRow; r++)
        {
            var location = ws.Cell(r, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(location)) continue;

            var dimension = ws.Cell(r, 3).GetString().Trim();
            var mediaHeader = NormalizeHeader(ws.Cell(headerRow, 3).GetString());
            var qtyCell = ws.Cell(r, 4);
            var qty = qtyCell.TryGetValue(out double qtyVal) ? (int)Math.Round(qtyVal) : 0;
            var periodicityRaw = ws.Cell(r, 5).GetString().Trim();
            var k7Ref = category == FilterCategory.G3 ? ws.Cell(r, 2).GetString().Trim() : null;

            int? hourCounter = null;
            foreach (var cc in counterCols)
            {
                var cell = ws.Cell(r, cc);
                if (cell.TryGetValue(out double hv)) hourCounter = (int)Math.Round(hv);
            }

            var filter = new PeriodicFilter
            {
                Category = category,
                Location = location,
                Dimension = dimension,
                MediaType = CleanMedia(mediaHeader, dimension),
                QuantityInPlace = qty,
                Periodicity = NormalizePeriodicity(periodicityRaw),
                HourCounter = hourCounter,
                K7Reference = string.IsNullOrWhiteSpace(k7Ref) ? null : k7Ref
            };

            foreach (var month in Enumerable.Range(1, 12))
            {
                int? realizedQty = null;
                if (realizedCols.TryGetValue(month, out var rc))
                {
                    var cell = ws.Cell(r, rc);
                    if (cell.TryGetValue(out double rv)) realizedQty = (int)Math.Round(rv);
                }

                DateOnly? dateDone = null;
                string? dateRaw = null;
                if (dateCols.TryGetValue(month, out var dc))
                {
                    var cell = ws.Cell(r, dc);
                    if (cell.TryGetValue(out DateTime dv)) dateDone = DateOnly.FromDateTime(dv);
                    else dateRaw = cell.GetString().Trim();
                }

                if (realizedQty.HasValue || dateDone.HasValue || !string.IsNullOrWhiteSpace(dateRaw))
                {
                    filter.Replacements.Add(new FilterReplacement
                    {
                        Month = month,
                        Year = DateTime.Now.Year,
                        QuantityDone = realizedQty ?? 0,
                        DateDone = dateDone
                    });
                }
            }

            ctx.PeriodicFilters.Add(filter);
            result.PeriodicFiltersImported++;
        }
    }

    private static string CleanMedia(string mediaHeader, string dimension) =>
        mediaHeader.Length > 0 && mediaHeader.Length < 60 ? mediaHeader.Trim() : dimension;

    private static string NormalizePeriodicity(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var months = raw.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s.Trim(), out var m) ? m : 0)
            .Where(m => m is >= 1 and <= 12)
            .Distinct()
            .OrderBy(m => m);
        return PeriodicFilter.FormatPeriodicityMonths(months);
    }

    /// <summary>La feuille "Filtres F7 a H13" n'est pas une table unique : elle enchaîne
    /// plusieurs sous-sections (une par classe/type de filtre), chacune précédée d'un titre
    /// "MAINTENANCE FILTRES ..." et/ou d'une ligne d'en-tête répétée "NOM DE LA CENTRALE D'AIR |
    /// ...", et se terminant souvent par une mini-table de stock "Dimensions ... | Stock réel |
    /// Stock Mini | Qté à commander" (dimension/stock, sans rapport avec une centrale) et/ou une
    /// ligne "TOTAL POCHE F7". Un import naïf ligne à ligne de la colonne A traite ces lignes
    /// d'en-tête/titre/total/stock comme si c'étaient des centrales, ce qui produit des lignes
    /// parasites - notamment les lignes de la mini-table de stock, où la colonne A contient en
    /// réalité un code de DIMENSION (ex. "592 x 592 x 292") et non un nom de centrale. On détecte
    /// et on ignore ces lignes non-données pour ne garder que les véritables lignes centrale.</summary>
    private void ImportOpacimetricSheet(XLWorkbook wb, string sheetName, FiltresDbContext ctx, ImportResult result)
    {
        if (!wb.Worksheets.TryGetWorksheet(sheetName, out var ws))
        {
            result.Warnings.Add($"Feuille '{sheetName}' introuvable.");
            return;
        }

        const int firstDataRow = 4;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? firstDataRow;
        var lastCol = Math.Min(ws.LastColumnUsed()?.ColumnNumber() ?? 4, 60);

        var inStockTable = false;

        for (var r = firstDataRow; r <= lastRow; r++)
        {
            var location = ws.Cell(r, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(location)) continue;

            var normLoc = NormalizeHeader(location);
            var col2 = ws.Cell(r, 2).GetString().Trim();

            if (normLoc.Contains("nom de la centrale"))
            {
                // Ligne d'en-tête de sous-section répétée ("NOM DE LA CENTRALE D'AIR | ...").
                inStockTable = false;
                continue;
            }
            if (normLoc.StartsWith("maintenance filtres"))
            {
                // Ligne de titre de sous-section ("MAINTENANCE FILTRES F9", "... BLOCS MAXILAM", ...).
                inStockTable = false;
                continue;
            }
            if (normLoc.StartsWith("total poche"))
            {
                // Ligne de total ("TOTAL POCHE F7").
                continue;
            }
            if (normLoc.StartsWith("dimensions") && NormalizeHeader(col2).StartsWith("stock reel"))
            {
                // Titre de la mini-table de stock ("Dimensions ... | Stock réel | Stock Mini | Qté à commander").
                inStockTable = true;
                continue;
            }
            if (inStockTable)
            {
                // Lignes de la mini-table de stock : colonne A = code dimension, pas une centrale.
                continue;
            }

            var dimension = col2;
            var qty = ws.Cell(r, 3).TryGetValue(out double qv) ? (int)Math.Round(qv) : 0;

            var filter = new OpacimetricFilter
            {
                Location = location,
                Dimension = dimension,
                QuantityInPlace = qty
            };

            for (var c = 4; c + 1 <= lastCol; c += 2)
            {
                var qtyCell = ws.Cell(r, c);
                var dateCell = ws.Cell(r, c + 1);
                var hasQty = qtyCell.TryGetValue(out double changedQty);
                DateOnly? dateChanged = null;
                if (dateCell.TryGetValue(out DateTime dv)) dateChanged = DateOnly.FromDateTime(dv);

                if (hasQty || dateChanged.HasValue)
                {
                    filter.Replacements.Add(new OpacimetricReplacement
                    {
                        QuantityChanged = hasQty ? (int)Math.Round(changedQty) : 0,
                        DateChanged = dateChanged
                    });
                }
            }

            ctx.OpacimetricFilters.Add(filter);
            result.OpacimetricFiltersImported++;
        }
    }

    private void ImportK7Sheet(XLWorkbook wb, string sheetName, FiltresDbContext ctx, ImportResult result)
    {
        if (!wb.Worksheets.TryGetWorksheet(sheetName, out var ws))
        {
            result.Warnings.Add($"Feuille '{sheetName}' introuvable.");
            return;
        }

        const int firstDataRow = 4;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? firstDataRow;

        for (var r = firstDataRow; r <= lastRow; r++)
        {
            var lieu = ws.Cell(r, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(lieu)) continue;

            DateOnly? changeDate = null;
            var dateCell = ws.Cell(r, 3);
            if (dateCell.TryGetValue(out DateTime dv)) changeDate = DateOnly.FromDateTime(dv);

            ctx.K7Locations.Add(new K7Location
            {
                Lieu = lieu,
                NumeroPorte = ws.Cell(r, 2).GetString().Trim() is { Length: > 0 } np ? np : null,
                ChangementRealise = changeDate,
                NbFiltres = ws.Cell(r, 4).TryGetValue(out double nb) ? (int)Math.Round(nb) : 0
            });
            result.K7LocationsImported++;
        }
    }

    private void ImportInventorySheet(XLWorkbook wb, string sheetName, FiltresDbContext ctx, ImportResult result)
    {
        if (!wb.Worksheets.TryGetWorksheet(sheetName, out var ws))
        {
            result.Warnings.Add($"Feuille '{sheetName}' introuvable.");
            return;
        }

        const int firstDataRow = 18;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? firstDataRow;
        var ordre = 0;

        for (var r = firstDataRow; r <= lastRow; r++)
        {
            var designation = ws.Cell(r, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(designation)) continue;

            ctx.InventoryLines.Add(new InventoryLine
            {
                Ordre = ++ordre,
                Designation = designation,
                Dimension = ws.Cell(r, 2).GetString().Trim() is { Length: > 0 } d ? d : null,
                Quantite = ws.Cell(r, 6).TryGetValue(out double q) ? (int)Math.Round(q) : null,
                Unite = null,
                Notes = ws.Cell(r, 3).GetString().Trim() is { Length: > 0 } n ? n : null
            });
            result.InventoryLinesImported++;
        }
    }

    private void MergeInventoryIntoOrderLines(FiltresDbContext ctx)
    {
        var maxOrdre = ctx.OrderLines.Local
            .Where(o => o.DocumentType == OrderDocumentType.CommandeChmy)
            .Select(o => (int?)o.Ordre)
            .DefaultIfEmpty(0)
            .Max() ?? 0;

        foreach (var inv in ctx.InventoryLines.Local.OrderBy(i => i.Ordre))
        {
            maxOrdre++;
            ctx.OrderLines.Add(new OrderLine
            {
                DocumentType = OrderDocumentType.CommandeChmy,
                Ordre = maxOrdre,
                Designation = inv.Designation,
                Dimension = inv.Dimension,
                Quantite = inv.Quantite,
                Unite = inv.Unite,
                Notes = inv.Notes
            });
        }
    }

    private void ImportOrderSheet(XLWorkbook wb, string sheetName, OrderDocumentType type, FiltresDbContext ctx, ImportResult result)
    {
        if (!wb.Worksheets.TryGetWorksheet(sheetName, out var ws))
        {
            result.Warnings.Add($"Feuille '{sheetName}' introuvable.");
            return;
        }

        const int firstDataRow = 18;
        var lastRow = ws.LastRowUsed()?.RowNumber() ?? firstDataRow;
        var ordre = 0;

        for (var r = firstDataRow; r <= lastRow; r++)
        {
            var designation = ws.Cell(r, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(designation)) continue;

            ctx.OrderLines.Add(new OrderLine
            {
                DocumentType = type,
                Ordre = ++ordre,
                Designation = designation,
                Dimension = ws.Cell(r, 2).GetString().Trim() is { Length: > 0 } d ? d : null,
                Quantite = ws.Cell(r, 5).TryGetValue(out double q) ? (int)Math.Round(q) : null,
                Notes = ws.Cell(r, 3).GetString().Trim() is { Length: > 0 } n ? n : null
            });
            result.OrderLinesImported++;
        }
    }

}
