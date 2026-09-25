using ClosedXML.Excel;
using FiltresApp.Core.Data;
using FiltresApp.Core.Models;

namespace FiltresApp.Core.Services;

/// <summary>Génère un export .xlsx de l'année sélectionnée (une feuille par catégorie de filtre),
/// via ClosedXML (déjà utilisé par <see cref="ExcelImportService"/> pour la lecture). Le dossier de
/// destination réutilise <see cref="AppSettings.PdfExportPath"/> : il s'agit du même dossier "exports"
/// déjà configurable dans les Paramètres, pas besoin d'un réglage séparé pour un simple changement de
/// format de fichier.</summary>
public class ExcelExportService
{
    private static readonly string[] MonthShortNames =
        { "Jan", "Fév", "Mar", "Avr", "Mai", "Jun", "Jul", "Aoû", "Sep", "Oct", "Nov", "Déc" };

    public string ExportYear(FiltresDbContext ctx, string exportFolder, int year)
    {
        Directory.CreateDirectory(exportFolder);

        using var workbook = new XLWorkbook();

        AddPeriodicSheet(workbook, ctx, FilterCategory.G4Plisse, "G4 plissés", "Nom de la centrale d'air", year);
        AddPeriodicSheet(workbook, ctx, FilterCategory.G4Plan, "G4 plan", "Emplacement de l'appareil", year);
        AddPeriodicSheet(workbook, ctx, FilterCategory.G3, "G3", "Emplacement de l'appareil", year);
        AddOpacimetricSheet(workbook, ctx, year);
        AddPeriodicSheet(workbook, ctx, FilterCategory.Charbon, "Charbon", "Emplacement de l'appareil", year);

        var now = DateTime.Now;
        var fileName = $"Suivi filtres {year} du {now:dd.MM.yyyy} a {now.Hour}.{now.Minute}.xlsx";
        var fullPath = Path.Combine(exportFolder, fileName);
        workbook.SaveAs(fullPath);
        return fullPath;
    }

    private static void AddPeriodicSheet(XLWorkbook workbook, FiltresDbContext ctx, FilterCategory category,
        string sheetName, string locationLabel, int year)
    {
        var filters = ctx.PeriodicFilters
            .Where(f => f.Category == category)
            .OrderBy(f => f.Location)
            .Select(f => new { f.Id, f.Location, f.Dimension, f.MediaType, f.QuantityInPlace, f.Periodicity, f.HourCounter })
            .ToList();

        var replacements = ctx.FilterReplacements
            .Where(r => r.Year == year && filters.Select(f => f.Id).Contains(r.PeriodicFilterId))
            .ToList()
            .ToLookup(r => r.PeriodicFilterId);

        var ws = workbook.Worksheets.Add(SanitizeSheetName(sheetName));

        var col = 1;
        ws.Cell(1, col).Value = locationLabel; col++;
        ws.Cell(1, col).Value = "Dimension"; col++;
        ws.Cell(1, col).Value = "Type"; col++;
        ws.Cell(1, col).Value = "Qté en place"; col++;
        ws.Cell(1, col).Value = "Périodicité"; col++;
        if (category == FilterCategory.Charbon) { ws.Cell(1, col).Value = "Compteur d'heures"; col++; }

        var firstMonthCol = col;
        for (var m = 1; m <= 12; m++)
        {
            ws.Cell(1, firstMonthCol + (m - 1) * 2).Value = $"{MonthShortNames[m - 1]} réalisé";
            ws.Cell(1, firstMonthCol + (m - 1) * 2 + 1).Value = $"{MonthShortNames[m - 1]} date";
        }
        ws.Row(1).Style.Font.Bold = true;

        var row = 2;
        foreach (var f in filters)
        {
            col = 1;
            ws.Cell(row, col).Value = f.Location; col++;
            ws.Cell(row, col).Value = f.Dimension; col++;
            ws.Cell(row, col).Value = f.MediaType; col++;
            ws.Cell(row, col).Value = f.QuantityInPlace; col++;
            ws.Cell(row, col).Value = f.Periodicity; col++;
            if (category == FilterCategory.Charbon) { ws.Cell(row, col).Value = f.HourCounter; col++; }

            var repsForFilter = replacements[f.Id];
            for (var m = 1; m <= 12; m++)
            {
                var rep = repsForFilter.FirstOrDefault(r => r.Month == m);
                var realizedCol = firstMonthCol + (m - 1) * 2;
                var dateCol = realizedCol + 1;
                ws.Cell(row, realizedCol).Value = rep is { DateDone: not null } ? "Oui" : "Non";
                if (rep?.DateDone is { } d)
                {
                    ws.Cell(row, dateCol).Value = d.ToDateTime(TimeOnly.MinValue);
                    ws.Cell(row, dateCol).Style.DateFormat.Format = "dd/MM/yyyy";
                }
            }
            row++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns(1, firstMonthCol - 1).AdjustToContents();
    }

    private static void AddOpacimetricSheet(XLWorkbook workbook, FiltresDbContext ctx, int year)
    {
        var ws = workbook.Worksheets.Add("F7-H13");
        ws.Cell(1, 1).Value = "Nom de la centrale d'air";
        ws.Cell(1, 2).Value = "Dimension";
        ws.Cell(1, 3).Value = "Qté en place";
        ws.Cell(1, 4).Value = "Date de changement";
        ws.Cell(1, 5).Value = "Qté changée";
        ws.Row(1).Style.Font.Bold = true;

        var filters = ctx.OpacimetricFilters
            .OrderBy(f => f.Location)
            .Select(f => new { f.Id, f.Location, f.Dimension, f.QuantityInPlace })
            .ToList();

        var replacements = ctx.OpacimetricReplacements
            .Where(r => r.DateChanged != null && r.DateChanged.Value.Year == year)
            .ToList()
            .ToLookup(r => r.OpacimetricFilterId);

        var row = 2;
        foreach (var f in filters)
        {
            var repsForFilter = replacements[f.Id].OrderBy(r => r.DateChanged).ToList();
            if (repsForFilter.Count == 0)
            {
                ws.Cell(row, 1).Value = f.Location;
                ws.Cell(row, 2).Value = f.Dimension;
                ws.Cell(row, 3).Value = f.QuantityInPlace;
                row++;
                continue;
            }

            foreach (var rep in repsForFilter)
            {
                ws.Cell(row, 1).Value = f.Location;
                ws.Cell(row, 2).Value = f.Dimension;
                ws.Cell(row, 3).Value = f.QuantityInPlace;
                if (rep.DateChanged.HasValue)
                {
                    ws.Cell(row, 4).Value = rep.DateChanged.Value.ToDateTime(TimeOnly.MinValue);
                    ws.Cell(row, 4).Style.DateFormat.Format = "dd/MM/yyyy";
                }
                ws.Cell(row, 5).Value = rep.QuantityChanged;
                row++;
            }
        }

        ws.SheetView.FreezeRows(1);
        ws.Columns(1, 5).AdjustToContents();
    }

    private static string SanitizeSheetName(string name)
    {
        var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var clean = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return clean.Length > 31 ? clean[..31] : clean;
    }
}
