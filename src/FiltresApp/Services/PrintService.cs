using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FiltresApp.Services;

public class PrintService
{
    private static readonly string[] MonthNames =
    {
        "Janvier", "Février", "Mars", "Avril", "Mai", "Juin",
        "Juillet", "Août", "Septembre", "Octobre", "Novembre", "Décembre"
    };

    /// <summary>Gris moyen dédié aux lignes de grille des documents imprimés, cohérent avec
    /// BrushGridLine (Styles/Colors.xaml) utilisé pour les DataGrid à l'écran : bordures nettes autour
    /// de chaque cellule, ni trop claires (peu visibles une fois imprimées) ni trop agressives.</summary>
    private static readonly Brush GridLineBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));

    public void PrintTable(string documentTitle, string[] headers, IReadOnlyList<string[]> rows)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true) return;

        var doc = BuildFlowDocument(documentTitle, null, headers, rows);
        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        printDialog.PrintDocument(paginator, documentTitle);
    }

    /// <summary>Impression dédiée au mois consulté d'une catégorie de filtre : une feuille de terrain
    /// simple (pas un rapport de données) avec uniquement l'emplacement, la dimension, la quantité en
    /// place et une grande case à cocher vierge à cocher à la main au marqueur une fois le changement
    /// physique effectué. Un en-tête rappelant catégorie/mois/année est conservé pour identifier la
    /// feuille (voir PrintService.BuildFlowDocument), via FlowDocument (mise en page propre, contrairement
    /// à une impression brute de DataGrid).</summary>
    /// <param name="locationLabel">Libellé de la colonne d'emplacement (déjà adapté à la catégorie,
    /// voir PeriodicFilterListViewModel.LocationColumnLabel).</param>
    /// <param name="rows">Une ligne par filtre dû ce mois-là : (emplacement, dimension, qté en place).</param>
    public void PrintMonth(string categoryTitle, int month, int year, string locationLabel,
        IReadOnlyList<(string Location, string Dimension, string Quantity)> rows)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true) return;

        var subtitle = $"{categoryTitle} — {MonthNames[month - 1]} {year}";
        var headers = new[] { locationLabel, "Dimension", "Qté en place", "Fait" };
        var doc = BuildFlowDocument(categoryTitle, subtitle, headers, rows.Select(r => new[] { r.Location, r.Dimension, r.Quantity }).ToList(),
            includeCheckboxColumn: true);
        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        printDialog.PrintDocument(paginator, subtitle);
    }

    private static FlowDocument BuildFlowDocument(string title, string? subtitle, string[] headers, IReadOnlyList<string[]> rows,
        bool includeCheckboxColumn = false)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(30),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            ColumnWidth = double.PositiveInfinity
        };

        doc.Blocks.Add(new Paragraph(new Run(title)) { FontSize = 18, FontWeight = FontWeights.Bold });
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            doc.Blocks.Add(new Paragraph(new Run(subtitle))
            {
                FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DimGray, Margin = new Thickness(0, 2, 0, 0)
            });
        }
        doc.Blocks.Add(new Paragraph(new Run($"Imprimé le {DateTime.Now:dd/MM/yyyy HH:mm}"))
        {
            FontSize = 9, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 12)
        });

        var table = new Table
        {
            BorderBrush = GridLineBrush,
            BorderThickness = new Thickness(1),
            CellSpacing = 0
        };
        for (var i = 0; i < headers.Length; i++)
            table.Columns.Add(new TableColumn { Width = includeCheckboxColumn && i == headers.Length - 1 ? new GridLength(70) : GridLength.Auto });

        table.RowGroups.Add(new TableRowGroup());
        var headerRow = new TableRow { Background = Brushes.LightGray, FontWeight = FontWeights.Bold };
        foreach (var h in headers)
            headerRow.Cells.Add(NewCell(h));
        table.RowGroups[0].Rows.Add(headerRow);

        foreach (var row in rows)
        {
            var tr = new TableRow();
            foreach (var cell in row) tr.Cells.Add(NewCell(cell));
            if (includeCheckboxColumn) tr.Cells.Add(NewCheckboxCell());
            table.RowGroups[0].Rows.Add(tr);
        }

        doc.Blocks.Add(table);
        return doc;
    }

    private static TableCell NewCell(string text) => new(new Paragraph(new Run(text ?? string.Empty)))
    {
        Padding = new Thickness(4),
        BorderBrush = GridLineBrush,
        BorderThickness = new Thickness(0.75)
    };

    /// <summary>Cellule contenant une grande case à cocher vierge (carré d'environ 1.8 cm de côté,
    /// ~68 unités WPF à 96 DPI) que l'utilisateur coche à la main au marqueur/feutre une fois le
    /// changement physique effectué sur le terrain — volontairement bien plus grande qu'une case à
    /// cocher standard, imprimée sans être remplie.</summary>
    private static TableCell NewCheckboxCell()
    {
        var box = new Border
        {
            Width = 68,
            Height = 68,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4)
        };
        var container = new BlockUIContainer(box) { Margin = new Thickness(0) };
        var cell = new TableCell(container)
        {
            Padding = new Thickness(4),
            BorderBrush = GridLineBrush,
            BorderThickness = new Thickness(0.75),
            TextAlignment = TextAlignment.Center
        };
        return cell;
    }
}
