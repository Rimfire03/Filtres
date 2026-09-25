using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FiltresApp.Core.Services;

/// <summary>Génère un export PDF simple (titre + tableau), en remplacement de
/// ActiveSheet.ExportAsFixedFormat / exportcmd du classeur Excel. Le dossier de
/// destination est configurable (voir AppSettings.PdfExportPath) au lieu d'être
/// codé en dur comme dans la macro VBA d'origine.</summary>
public class PdfExportService
{
    static PdfExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public string ExportTable(string exportFolder, string documentTitle, string[] headers, IReadOnlyList<string[]> rows)
    {
        Directory.CreateDirectory(exportFolder);
        var now = DateTime.Now;
        var fileName = $"{SanitizeFileName(documentTitle)} du {now:dd.MM.yyyy} à {now.Hour}.{now.Minute}.pdf";
        var fullPath = Path.Combine(exportFolder, fileName);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Text(documentTitle).FontSize(16).Bold();

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        foreach (var _ in headers) columns.RelativeColumn();
                    });

                    table.Header(header =>
                    {
                        foreach (var h in headers)
                        {
                            header.Cell().Border(1).Padding(3).Background(Colors.Grey.Lighten2).Text(h).Bold();
                        }
                    });

                    foreach (var row in rows)
                    {
                        foreach (var cell in row)
                        {
                            table.Cell().Border(1).Padding(3).Text(cell ?? string.Empty);
                        }
                    }
                });

                page.Footer().AlignRight().Text(t =>
                {
                    t.Span("Généré le ").FontSize(8);
                    t.Span(now.ToString("dd/MM/yyyy HH:mm")).FontSize(8);
                });
            });
        }).GeneratePdf(fullPath);

        return fullPath;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray());
    }
}
