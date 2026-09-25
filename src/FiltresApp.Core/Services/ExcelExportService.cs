using ClosedXML.Excel;
using FiltresApp.Core.Data;
using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Services;

/// <summary>Export Excel de l'année (écran Paramètres) : une feuille par onglet de filtres du logiciel
/// (G4 plissés, G4 plan, G3, F7 à H13, Charbon), avec une ligne titre par famille comme à l'écran.
/// Liste K7, Inventaire et Commande ne sont pas exportés. Le fichier est déposé dans le dossier des
/// exports PDF (<see cref="AppSettings.PdfExportPath"/>).</summary>
public class ExcelExportService
{
    /// <summary>Nombre de dates de changement exportées pour chaque filtre F7 à H13.</summary>
    public const int OpacimetricLastChanges = 10;

    private static readonly string[] MonthShortNames =
        { "Jan", "Fév", "Mar", "Avr", "Mai", "Jun", "Jul", "Aoû", "Sep", "Oct", "Nov", "Déc" };

    private static readonly XLColor FamilyFill = XLColor.FromHtml("#1F2937");
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#E5E7EB");

    public string ExportYear(FiltresDbContext ctx, string exportFolder, int year)
    {
        Directory.CreateDirectory(exportFolder);

        using var workbook = new XLWorkbook();

        AddPeriodicSheet(workbook, ctx, FilterCategory.G4Plisse, "Filtres G4 plissés", year, groupByFamily: false);
        AddPeriodicSheet(workbook, ctx, FilterCategory.G4Plan, "Filtres G4 plan", year, groupByFamily: false);
        AddPeriodicSheet(workbook, ctx, FilterCategory.G3, "Filtres G3", year, groupByFamily: true);
        AddOpacimetricSheet(workbook, ctx, year);
        AddPeriodicSheet(workbook, ctx, FilterCategory.Charbon, "Charbon", year, groupByFamily: false);

        var now = DateTime.Now;
        var fileName = $"Suivi filtres {year} du {now:dd.MM.yyyy} a {now.Hour}.{now.Minute}.xlsx";
        var fullPath = Path.Combine(exportFolder, fileName);
        workbook.SaveAs(fullPath);
        return fullPath;
    }

    // ---- Filtres à périodicité (G4 plissés, G4 plan, G3, Charbon) ----

    private static void AddPeriodicSheet(XLWorkbook workbook, FiltresDbContext ctx, FilterCategory category,
        string sheetName, int year, bool groupByFamily)
    {
        var filters = ctx.PeriodicFilters.AsNoTracking()
            .Where(f => f.Category == category)
            .OrderBy(f => f.Location)
            .ToList();

        var ids = filters.Select(f => f.Id).ToList();
        var replacements = ctx.FilterReplacements.AsNoTracking()
            .Where(r => r.Year == year && ids.Contains(r.PeriodicFilterId))
            .ToList()
            .ToLookup(r => r.PeriodicFilterId);

        var headers = new List<string> { "Filtres", "Dimension", "Type", "Qté en place", "Périodicité" };
        if (category == FilterCategory.G3) headers.Add("Réf. K7");
        if (category == FilterCategory.Charbon) headers.Add("Compteur d'heures");
        var firstMonthCol = headers.Count + 1;
        for (var m = 1; m <= 12; m++)
        {
            headers.Add($"{MonthShortNames[m - 1]} {year} réalisé");
            headers.Add($"{MonthShortNames[m - 1]} {year} date");
        }

        var ws = workbook.Worksheets.Add(SanitizeSheetName(sheetName));
        WriteHeader(ws, headers);

        // G3 : familles à remplacer / à laver / sans dimension, comme à l'écran.
        var groups = groupByFamily
            ? filters.GroupBy(f => f.DimensionFamilyLabel).OrderBy(g => FamilyRank(g.Key)).Select(g => (Title: (string?)g.Key, Items: g.ToList()))
            : new[] { (Title: (string?)null, Items: filters) };

        var row = 2;
        foreach (var (title, items) in groups)
        {
            if (title is not null) WriteFamilyRow(ws, row++, title, headers.Count);
            foreach (var f in items)
            {
                var col = 1;
                ws.Cell(row, col++).Value = f.Location;
                ws.Cell(row, col++).Value = f.Dimension;
                ws.Cell(row, col++).Value = f.MediaType;
                ws.Cell(row, col++).Value = f.QuantityInPlace;
                ws.Cell(row, col++).Value = f.PeriodicityDisplay;
                if (category == FilterCategory.G3) ws.Cell(row, col++).Value = f.K7Reference ?? "";
                if (category == FilterCategory.Charbon) ws.Cell(row, col++).Value = f.HourCounter;

                var repsForFilter = replacements[f.Id];
                for (var m = 1; m <= 12; m++)
                {
                    var rep = repsForFilter.FirstOrDefault(r => r.Month == m);
                    var realizedCol = firstMonthCol + (m - 1) * 2;
                    ws.Cell(row, realizedCol).Value = rep is { DateDone: not null } ? "Oui" : "";
                    if (rep?.DateDone is { } d) WriteDate(ws.Cell(row, realizedCol + 1), d);
                }
                row++;
            }
        }

        Finish(ws, headers.Count);
    }

    private static int FamilyRank(string label) => label switch
    {
        "Filtres à remplacer" => 0,
        "Filtres à laver" => 1,
        _ => 2
    };

    // ---- Filtres F7 à H13 : 10 derniers changements ----

    private static void AddOpacimetricSheet(XLWorkbook workbook, FiltresDbContext ctx, int year)
    {
        var filters = ctx.OpacimetricFilters.AsNoTracking()
            .Include(f => f.Family)
            .Include(f => f.Replacements)
            .ToList()
            .OrderBy(f => f.Family is null)
            .ThenBy(f => f.Family?.Nom, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(f => f.Location, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var headers = new List<string> { "Filtres", "Dimension", "Type", "Qté en place" };
        var firstChangeCol = headers.Count + 1;
        headers.Add("Dernier changement");
        for (var i = 2; i <= OpacimetricLastChanges; i++) headers.Add($"Changement n-{i - 1}");

        var ws = workbook.Worksheets.Add(SanitizeSheetName("Filtres F7 à H13"));
        WriteHeader(ws, headers);

        // Changements jusqu'à la fin de l'année exportée, du plus récent au plus ancien.
        var endOfYear = new DateOnly(year, 12, 31);
        var row = 2;
        foreach (var group in filters.GroupBy(f => f.FamilyGroupLabel))
        {
            WriteFamilyRow(ws, row++, group.Key, headers.Count);
            foreach (var f in group)
            {
                ws.Cell(row, 1).Value = f.Location;
                ws.Cell(row, 2).Value = f.Dimension;
                ws.Cell(row, 3).Value = f.FilterType ?? "";
                ws.Cell(row, 4).Value = f.QuantityInPlace;

                var dates = f.Replacements
                    .Where(r => r.DateChanged is { } d && d <= endOfYear)
                    .Select(r => r.DateChanged!.Value)
                    .OrderByDescending(d => d)
                    .Take(OpacimetricLastChanges)
                    .ToList();
                for (var i = 0; i < dates.Count; i++) WriteDate(ws.Cell(row, firstChangeCol + i), dates[i]);
                row++;
            }
        }

        Finish(ws, headers.Count);
    }

    // ---- Mise en forme commune ----

    private static void WriteHeader(IXLWorksheet ws, IReadOnlyList<string> headers)
    {
        for (var i = 0; i < headers.Count; i++) ws.Cell(1, i + 1).Value = headers[i];
        var header = ws.Range(1, 1, 1, headers.Count);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = HeaderFill;
    }

    /// <summary>Ligne titre de famille, fusionnée sur toute la largeur, même style que les bandeaux de
    /// l'application.</summary>
    private static void WriteFamilyRow(IXLWorksheet ws, int row, string title, int columnCount)
    {
        ws.Cell(row, 1).Value = title;
        var range = ws.Range(row, 1, row, columnCount);
        range.Merge();
        range.Style.Font.Bold = true;
        range.Style.Font.FontColor = XLColor.White;
        range.Style.Fill.BackgroundColor = FamilyFill;
    }

    private static void WriteDate(IXLCell cell, DateOnly date)
    {
        cell.Value = date.ToDateTime(TimeOnly.MinValue);
        cell.Style.DateFormat.Format = "dd/MM/yyyy";
    }

    private static void Finish(IXLWorksheet ws, int columnCount)
    {
        ws.SheetView.FreezeRows(1);
        ws.Columns(1, columnCount).AdjustToContents();
    }

    private static string SanitizeSheetName(string name)
    {
        var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return clean.Length > 31 ? clean[..31] : clean;
    }
}
