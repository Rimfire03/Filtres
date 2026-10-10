using ClosedXML.Excel;

namespace FiltresApp.Core.Services;

/// <summary>Une feuille du classeur de statistiques : titre, en-têtes et lignes.</summary>
public sealed record StatisticsSheet(string Name, string[] Headers, List<object?[]> Rows);

/// <summary>Export Excel des tableaux de statistiques affichés (un classeur, une feuille par tableau).</summary>
public static class StatisticsExcelExport
{
    public static void Export(string path, IEnumerable<StatisticsSheet> sheets)
    {
        using var workbook = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in sheets)
        {
            var name = new string(sheet.Name.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
            if (name.Length > 31) name = name[..31];
            var unique = name;
            for (var i = 2; !used.Add(unique); i++) unique = name[..Math.Min(name.Length, 28)] + " " + i;

            var ws = workbook.Worksheets.Add(unique);
            for (var c = 0; c < sheet.Headers.Length; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = sheet.Headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#E6F1FB");
            }
            for (var r = 0; r < sheet.Rows.Count; r++)
                for (var c = 0; c < sheet.Rows[r].Length; c++)
                {
                    var v = sheet.Rows[r][c];
                    var cell = ws.Cell(r + 2, c + 1);
                    switch (v)
                    {
                        case null: break;
                        case int i: cell.Value = i; break;
                        case double d: cell.Value = d; break;
                        case DateOnly date: cell.Value = date.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "dd/MM/yyyy"; break;
                        default: cell.Value = v.ToString() ?? ""; break;
                    }
                }
            ws.Columns().AdjustToContents();
            ws.SheetView.FreezeRows(1);
        }
        workbook.SaveAs(path);
    }
}
