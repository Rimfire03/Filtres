using ClosedXML.Excel;
using FiltresApp.Core.Data;
using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Services;

/// <summary>Export Excel de l'année (écran Paramètres) : une feuille par onglet de filtres du logiciel
/// (G4 plissés, G4 plan, G3, Charbon), avec une ligne titre par famille comme à l'écran.
/// Liste K7, Inventaire et Commande ne sont pas exportés. Le fichier est déposé dans le dossier des
/// exports PDF (<see cref="AppSettings.PdfExportPath"/>).</summary>
public class ExcelExportService
{
    private static readonly string[] MonthShortNames =
        { "Jan", "Fév", "Mar", "Avr", "Mai", "Jun", "Jul", "Aoû", "Sep", "Oct", "Nov", "Déc" };

    private static readonly XLColor FamilyFill = XLColor.FromHtml("#1F2937");
    private static readonly XLColor HeaderFill = XLColor.FromHtml("#E5E7EB");

    public string ExportYear(FiltresDbContext ctx, string exportFolder, int year)
    {
        Directory.CreateDirectory(exportFolder);

        using var workbook = new XLWorkbook();

        foreach (var category in new[] { FilterCategory.G4Plisse, FilterCategory.G4Plan, FilterCategory.G3, FilterCategory.Charbon })
            AddPeriodicSheet(workbook, ctx, category, year, groupByFamily: category == FilterCategory.G3);

        foreach (var variety in ctx.FilterVarieties.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList())
            AddDynamicSheet(workbook, ctx, variety);

        var now = DateTime.Now;
        var fileName = $"Suivi filtres {year} du {now:dd.MM.yyyy} a {now.Hour}.{now.Minute}.xlsx";
        var fullPath = Path.Combine(exportFolder, fileName);
        workbook.SaveAs(fullPath);
        return fullPath;
    }

    // ---- Filtres à périodicité (G4 plissés, G4 plan, G3, Charbon) ----

    private static void AddPeriodicSheet(XLWorkbook workbook, FiltresDbContext ctx, FilterCategory category,
        int year, bool groupByFamily)
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
        if (category == FilterCategory.Charbon) headers.Add("Compteur d'heures");
        var firstMonthCol = headers.Count + 1;
        for (var m = 1; m <= 12; m++)
        {
            headers.Add($"{MonthShortNames[m - 1]} {year} réalisé");
            headers.Add($"{MonthShortNames[m - 1]} {year} date");
        }

        var ws = AddSheet(workbook, OrderLine.FamilyLabelFor(category));
        WriteHeader(ws, headers);

        // G3 : familles à remplacer / à laver / sans dimension, comme à l'écran.
        var groups = groupByFamily
            ? filters.GroupBy(f => f.DimensionFamilyLabel).OrderBy(g => g.First().DimensionFamilyRank).Select(g => (Title: (string?)g.Key, Items: g.ToList()))
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

    // ---- Variétés du menu dépliant "Filtres F7 à H14" (pas de périodicité fixe : dates des derniers
    // remplacements plutôt qu'un suivi mensuel de l'année) ----

    private const int DynamicHistoryColumnCount = 10;

    private static void AddDynamicSheet(XLWorkbook workbook, FiltresDbContext ctx, FilterVariety variety)
    {
        var filters = ctx.DynamicFilters.AsNoTracking()
            .Include(f => f.Family)
            .Include(f => f.Replacements)
            .Where(f => f.VarietyId == variety.Id)
            .OrderBy(f => f.DynamicFilterFamilyId == null)
            .ThenBy(f => f.Family!.Nom)
            .ThenBy(f => f.Location)
            .ToList();

        var headers = new List<string> { "Nom de la centrale d'air", "Dimension", "Type", "Qté en place" };
        for (var i = 1; i <= DynamicHistoryColumnCount; i++) headers.Add($"Changement -{i}");

        var ws = AddSheet(workbook, variety.Nom);
        WriteHeader(ws, headers);

        var groups = filters.GroupBy(f => f.FamilyGroupLabel);
        var row = 2;
        foreach (var group in groups)
        {
            WriteFamilyRow(ws, row++, group.Key, headers.Count);
            foreach (var f in group)
            {
                var col = 1;
                ws.Cell(row, col++).Value = f.Location;
                ws.Cell(row, col++).Value = f.Dimension;
                ws.Cell(row, col++).Value = f.FilterType ?? "";
                ws.Cell(row, col++).Value = f.QuantityInPlace;

                var dates = f.Replacements
                    .Where(r => r.DateChanged.HasValue)
                    .Select(r => r.DateChanged!.Value)
                    .OrderByDescending(d => d)
                    .Take(DynamicHistoryColumnCount)
                    .ToList();
                foreach (var d in dates) WriteDate(ws.Cell(row, col++), d);
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

    /// <summary>Nouvelle feuille nommée d'après <paramref name="name"/>, ramené aux règles d'Excel (31
    /// caractères, sans \ / ? * [ ] :, non vide) et rendu unique dans le classeur : deux variétés au nom
    /// proche ne doivent pas faire échouer l'export.</summary>
    private static IXLWorksheet AddSheet(XLWorkbook workbook, string name)
    {
        var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        if (clean.Length == 0) clean = "Feuille";
        if (clean.Length > 31) clean = clean[..31].TrimEnd();

        var unique = clean;
        for (var i = 2; workbook.Worksheets.Contains(unique); i++)
        {
            var suffix = $" ({i})";
            unique = (clean.Length + suffix.Length > 31 ? clean[..(31 - suffix.Length)].TrimEnd() : clean) + suffix;
        }
        return workbook.Worksheets.Add(unique);
    }
}
