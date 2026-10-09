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
    private static readonly XLColor StripeFill = XLColor.FromHtml("#F2F2F2");
    private static readonly XLColor OverdueFill = XLColor.FromHtml("#FFC7CE");

    /// <summary>Mois entièrement écoulé : le mois en cours n'est pas encore en retard.</summary>
    private static bool IsPast(int year, int month)
    {
        var today = DateTime.Today;
        return year < today.Year || (year == today.Year && month < today.Month);
    }

    /// <summary>Exporte l'année <paramref name="year"/> du site courant du contexte (module MultiSite : les données des autres sites ne sont jamais exportées).</summary>
    public string ExportYear(FiltresDbContext ctx, string exportFolder, int year, string? siteName = null)
    {
        Directory.CreateDirectory(exportFolder);

        using var workbook = new XLWorkbook();

        foreach (var view in ctx.PeriodicViews.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList())
            AddPeriodicSheet(workbook, ctx, view, year, groupByFamily: view.Category == FilterCategory.G3);

        foreach (var variety in ctx.FilterVarieties.AsNoTracking().OrderBy(v => v.Ordre).ThenBy(v => v.Nom).ToList())
            AddDynamicSheet(workbook, ctx, variety);

        var now = DateTime.Now;
        var sitePart = string.IsNullOrWhiteSpace(siteName) ? "" : " - " + string.Concat(siteName.Split(Path.GetInvalidFileNameChars())).Trim();
        var fileName = $"Suivi filtres {year}{sitePart} du {now:dd.MM.yyyy} a {now.Hour}.{now.Minute}.xlsx";
        var fullPath = Path.Combine(exportFolder, fileName);
        workbook.SaveAs(fullPath);
        return fullPath;
    }

    // ---- Filtres à périodicité (G4 plissés, G4 plan, G3, Charbon) ----

    private static void AddPeriodicSheet(XLWorkbook workbook, FiltresDbContext ctx, PeriodicView view,
        int year, bool groupByFamily)
    {
        var filters = ctx.PeriodicFilters.AsNoTracking()
            .Where(f => f.PeriodicViewId == view.Id)
            .OrderBy(f => f.Location)
            .ToList();

        var ids = filters.Select(f => f.Id).ToList();
        var replacements = ctx.FilterReplacements.AsNoTracking()
            .Where(r => r.Year == year && ids.Contains(r.PeriodicFilterId))
            .ToList()
            .ToLookup(r => r.PeriodicFilterId);

        var headers = new List<string> { "Filtres", "Dimension", "Type", "Qté en place", "Périodicité" };
        var firstMonthCol = headers.Count + 1;
        var perMonth = view.TracksOperatingHours ? 3 : 2;
        for (var m = 1; m <= 12; m++)
        {
            headers.Add($"{MonthShortNames[m - 1]} {year} réalisé");
            headers.Add($"{MonthShortNames[m - 1]} {year} date");
            if (view.TracksOperatingHours) headers.Add($"{MonthShortNames[m - 1]} {year} heures");
        }

        var ws = AddSheet(workbook, view.Nom);
        WriteHeader(ws, headers);

        // G3 : familles à remplacer / à laver / sans dimension, comme à l'écran.
        var groups = groupByFamily
            ? filters.GroupBy(f => f.DimensionFamilyLabel).OrderBy(g => g.First().DimensionFamilyRank).Select(g => (Title: (string?)g.Key, Items: g.ToList()))
            : new[] { (Title: (string?)null, Items: filters) };

        var row = 2;
        var familyRows = new List<int>();
        foreach (var (title, items) in groups)
        {
            if (title is not null) { familyRows.Add(row); WriteFamilyRow(ws, row++, title, headers.Count); }
            foreach (var f in items)
            {
                var col = 1;
                ws.Cell(row, col++).Value = f.Location;
                ws.Cell(row, col++).Value = f.Dimension;
                ws.Cell(row, col++).Value = f.MediaType;
                ws.Cell(row, col++).Value = f.QuantityInPlace;
                ws.Cell(row, col++).Value = f.PeriodicityDisplay;

                var repsForFilter = replacements[f.Id];
                var plannedMonths = f.GetPeriodicityMonths();
                for (var m = 1; m <= 12; m++)
                {
                    var rep = repsForFilter.FirstOrDefault(r => r.Month == m);
                    var realizedCol = firstMonthCol + (m - 1) * perMonth;
                    ws.Cell(row, realizedCol).Value = rep is { DateDone: not null } ? "Oui" : "";
                    if (rep?.DateDone is { } d) WriteDate(ws.Cell(row, realizedCol + 1), d);
                    if (view.TracksOperatingHours && rep?.OperatingHours is { } h) ws.Cell(row, realizedCol + 2).Value = h;

                    // 1 mois sur 2 en gris léger (janvier, mars...) ; changement prévu, passé et non réalisé : rouge.
                    var block = ws.Range(row, realizedCol, row, realizedCol + perMonth - 1);
                    if (m % 2 == 1) block.Style.Fill.BackgroundColor = StripeFill;
                    if (rep?.DateDone is null && plannedMonths.Contains(m) && IsPast(year, m)) block.Style.Fill.BackgroundColor = OverdueFill;
                }
                row++;
            }
        }

        Finish(ws, headers.Count, row - 1);

        // Séparation épaisse juste avant les colonnes de remplacement (après « Périodicité »), posée après le
        // quadrillage fin ; les bandeaux de famille fusionnés en sont exclus.
        var periodicityCol = firstMonthCol - 1;
        for (var r = 1; r < row; r++)
            if (!familyRows.Contains(r))
                ws.Cell(r, periodicityCol).Style.Border.RightBorder = XLBorderStyleValues.Thick;
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

                // Colonnes « Changement -1, -3... » en gris léger, une sur deux.
                for (var i = 1; i <= DynamicHistoryColumnCount; i += 2)
                    ws.Cell(row, 4 + i).Style.Fill.BackgroundColor = StripeFill;
                row++;
            }
        }

        Finish(ws, headers.Count, row - 1);
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

    private static void Finish(IXLWorksheet ws, int columnCount, int lastRow)
    {
        // Quadrillage fin sur tout le tableau.
        var table = ws.Range(1, 1, Math.Max(lastRow, 1), columnCount);
        table.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        table.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

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
