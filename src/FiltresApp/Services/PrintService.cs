using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace FiltresApp.Services;

public class PrintService
{
    /// <summary>Gris moyen dédié aux lignes de grille des documents imprimés, cohérent avec
    /// BrushGridLine (Styles/Colors.xaml) utilisé pour les DataGrid à l'écran : bordures nettes autour
    /// de chaque cellule, ni trop claires (peu visibles une fois imprimées) ni trop agressives.</summary>
    private static readonly Brush GridLineBrush = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));

    /// <param name="includeCheckboxColumn">Ajoute une dernière colonne "Fait" avec une grande case à cocher
    /// vierge par ligne, à cocher à la main sur le terrain.</param>
    public void PrintTable(string documentTitle, string[] headers, IReadOnlyList<string[]> rows, bool includeCheckboxColumn = false)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true) return;

        if (includeCheckboxColumn) headers = headers.Append("Fait").ToArray();
        var doc = BuildFlowDocument(documentTitle, headers, rows, includeCheckboxColumn);
        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        printDialog.PrintDocument(paginator, documentTitle);
    }

    private static FlowDocument BuildFlowDocument(string title, string[] headers, IReadOnlyList<string[]> rows, bool includeCheckboxColumn)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(30),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            ColumnWidth = double.PositiveInfinity
        };

        doc.Blocks.Add(new Paragraph(new Run(title)) { FontSize = 18, FontWeight = FontWeights.Bold });
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

    /// <summary>Grande case à cocher vierge (carré d'environ 1,8 cm, ~68 unités WPF à 96 DPI), cochée à
    /// la main au marqueur une fois le changement physique effectué.</summary>
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
        return new TableCell(new BlockUIContainer(box) { Margin = new Thickness(0) })
        {
            Padding = new Thickness(4),
            BorderBrush = GridLineBrush,
            BorderThickness = new Thickness(0.75),
            TextAlignment = TextAlignment.Center
        };
    }
}
