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

    /// <param name="logo">Logo de l'entreprise (image), affiché à gauche du titre et sur la page de garde ; null = pas de logo.</param>
    /// <param name="coverPage">Page de garde du bon de commande à placer avant le tableau (textes de Paramètres, en-tête du pôle,
    /// titre, mention de livraison, marché) ; null = pas de page de garde.</param>
    public string ExportTable(string exportFolder, string documentTitle, string[] headers, IReadOnlyList<string[]> rows, byte[]? logo = null, CoverPageInfo? coverPage = null)
    {
        Directory.CreateDirectory(exportFolder);
        var now = DateTime.Now;
        var fileName = $"{SanitizeFileName(documentTitle)} du {now:dd.MM.yyyy} à {now.Hour}.{now.Minute}.pdf";
        var fullPath = Path.Combine(exportFolder, fileName);

        Document.Create(container =>
        {
            if (coverPage is not null) container.Page(page => ComposeCoverPage(page, logo, coverPage));

            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().PaddingBottom(8).Row(row =>
                {
                    if (logo is not null) row.ConstantItem(120).Height(45).Image(logo).FitArea();
                    row.RelativeItem().PaddingLeft(logo is null ? 0 : 12).AlignMiddle().Text(documentTitle).FontSize(16).Bold();
                });

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

    /// <summary>Page de garde du bon de commande : logo à gauche, coordonnées du pôle à droite, titre souligné,
    /// mention de livraison et numéro de marché.</summary>
    private static void ComposeCoverPage(PageDescriptor page, byte[]? logo, CoverPageInfo info)
    {
        page.Size(PageSizes.A4.Landscape());
        page.Margin(20);
        page.DefaultTextStyle(x => x.FontFamily("Arial"));
        page.Content().Column(col =>
        {
            col.Item().Row(row =>
            {
                if (logo is not null) row.ConstantItem(125).Height(90).Image(logo).FitArea();
                else row.ConstantItem(125);
                row.RelativeItem().AlignCenter().Column(c =>
                {
                    if (Has(info.Organisation)) c.Item().AlignCenter().Text(info.Organisation).FontSize(12).Bold();
                    if (Has(info.Direction)) c.Item().PaddingTop(4).AlignCenter().Text(info.Direction).FontSize(9).Bold();
                    if (Has(info.Contact)) c.Item().PaddingTop(10).AlignCenter().Text(info.Contact).FontSize(8);
                });
            });
            if (Has(info.Title)) col.Item().PaddingTop(70).AlignCenter().Text(info.Title).FontSize(15).Bold().Italic().Underline();
            if (Has(info.Delivery)) col.Item().PaddingTop(28).Text(info.Delivery).FontSize(15).Bold();
            if (Has(info.Market)) col.Item().PaddingTop(30).Text(info.Market).FontSize(12).Bold().Italic();
        });
    }

    private static bool Has(string? text) => !string.IsNullOrWhiteSpace(text);

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray());
    }
}
