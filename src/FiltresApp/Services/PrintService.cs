using System.Globalization;
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

    /// <summary>Largeur réelle (mesurée, pas approximée par un nombre de caractères) de la plus longue
    /// ligne d'un texte pouvant contenir des retours à la ligne (ex. "CUISINE -1 CTA Cuisines\n(12 poches
    /// de 200)") : compter la ligne entière concaténée gonflerait à tort le poids de la colonne (voir le
    /// calcul des largeurs de colonnes ci-dessous), au détriment des autres colonnes.</summary>
    private static double LongestLineWidth(string text, Typeface typeface, double fontSize)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var max = 0.0;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;
            var formatted = new FormattedText(line, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                typeface, fontSize, Brushes.Black, 1.0);
            if (formatted.Width > max) max = formatted.Width;
        }
        return max;
    }

    /// <summary>Hauteur réellement occupée par <paramref name="text"/> une fois affiché dans une cellule de
    /// largeur <paramref name="maxWidth"/> (retours à la ligne automatiques inclus, comme dans la cellule
    /// imprimée réelle) : sert à estimer combien de lignes tiennent sur une page pour répéter l'en-tête de
    /// colonnes sur chaque page (voir la pagination manuelle dans BuildFlowDocument).</summary>
    private static double MeasureWrappedHeight(string text, Typeface typeface, double fontSize, double maxWidth)
    {
        var formatted = new FormattedText(string.IsNullOrEmpty(text) ? " " : text, CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, typeface, fontSize, Brushes.Black, 1.0)
        {
            MaxTextWidth = Math.Max(1, maxWidth)
        };
        return formatted.Height;
    }

    /// <param name="includeCheckboxColumn">Ajoute une colonne "Fait" avec une grande case à cocher vierge
    /// par ligne, à cocher à la main sur le terrain - toujours en toute première position (demandé
    /// explicitement), quelles que soient les autres colonnes conservées.</param>
    /// <param name="printColumnsKey">Clé du réglage "Colonnes imprimées" (Paramètres) : colonnes de
    /// <paramref name="headers"/> exclues par l'utilisateur pour cet écran (voir
    /// <see cref="PrintableColumnsRegistry"/>). <paramref name="headers"/> sert alors aussi de clé de
    /// colonne - null pour ne rien filtrer (écrans sans colonnes désactivables).</param>
    /// <param name="rowColors">Couleur de ligne (clic droit sur la grille, voir RowColorPalette), même
    /// index/ordre que <paramref name="rows"/> - null pour aucune ligne colorée. Le filtrage/déplacement de
    /// colonnes ci-dessous ne touche jamais à l'ordre des lignes, cet alignement par index reste donc valide.</param>
    public void PrintTable(string documentTitle, string[] headers, IReadOnlyList<string[]> rows, bool includeCheckboxColumn = false,
        string? printColumnsKey = null, IReadOnlyList<Color?>? rowColors = null)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true) return;

        var rowList = rows as List<string[]> ?? rows.ToList();
        if (printColumnsKey is not null) (headers, rowList) = FilterByPrintKeys(printColumnsKey, headers, headers, rowList);
        // Toujours en dernière position à l'impression, quel que soit son ordre dans l'écran d'origine
        // (voir aussi les colonnes de grille elles-mêmes, PeriodicFilterView.xaml / DynamicFilterView.xaml).
        (headers, rowList) = MoveColumnLast("Commentaire", headers, rowList);

        if (includeCheckboxColumn) headers = headers.Append("Fait").ToArray();
        // Largeur imprimable réellement disponible sur le papier choisi (l'orientation/taille du
        // PrintDialog) : les colonnes du tableau ci-dessous sont proportionnelles (Star), donc leur somme
        // ne dépasse jamais cette largeur - une impression ne fait donc jamais plus d'une feuille de large,
        // quel que soit le nombre de colonnes conservées.
        var pageWidth = printDialog.PrintableAreaWidth;
        var pageHeight = printDialog.PrintableAreaHeight;
        var doc = BuildFlowDocument(documentTitle, headers, rowList, includeCheckboxColumn, pageWidth, pageHeight, rowColors);
        var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
        printDialog.PrintDocument(paginator, documentTitle);
    }

    /// <summary>Retire les colonnes exclues à l'impression (Paramètres, "Colonnes imprimées"), au besoin
    /// avec une clé de colonne stable distincte du texte affiché (ex. en-têtes datés d'une année,
    /// <see cref="DynamicFilterListViewModel.Print"/>). Même index retiré dans <paramref name="columnKeys"/>,
    /// <paramref name="headers"/> et chaque ligne. Garde toujours au moins une colonne, même si
    /// l'utilisateur les a toutes décochées.</summary>
    public static (string[] Headers, List<string[]> Rows) FilterByPrintKeys(string screenKey, string[] columnKeys, string[] headers, IReadOnlyList<string[]> rows)
    {
        var keep = Enumerable.Range(0, columnKeys.Length).Where(i => !ColumnPreferences.IsPrintHidden(screenKey, columnKeys[i])).ToList();
        if (keep.Count == 0 || keep.Count == columnKeys.Length) return (headers, rows as List<string[]> ?? rows.ToList());

        var filteredHeaders = keep.Select(i => headers[i]).ToArray();
        var filteredRows = rows.Select(r =>
        {
            var filtered = keep.Select(i => i < r.Length ? r[i] : "").ToArray();
            // Ligne titre de groupe (voir BuildGroupedRows) : son texte est en colonne 0 ; si celle-ci est
            // décochée, le titre serait perdu - on le replace dans la première colonne conservée.
            if (!keep.Contains(0) && IsGroupTitleRow(r)) filtered[0] = r[0];
            return filtered;
        }).ToList();
        return (filteredHeaders, filteredRows);
    }

    private static bool IsGroupTitleRow(string[] row) =>
        row.Length > 0 && row[0].StartsWith("— ") && row[0].EndsWith(" —") && row.Skip(1).All(string.IsNullOrEmpty);

    /// <summary>Déplace la colonne <paramref name="columnName"/> (si présente) en toute dernière position,
    /// sans effet si elle est déjà là ou absente.</summary>
    private static (string[] Headers, List<string[]> Rows) MoveColumnLast(string columnName, string[] headers, List<string[]> rows)
    {
        var idx = Array.IndexOf(headers, columnName);
        if (idx < 0 || idx == headers.Length - 1) return (headers, rows);

        var newHeaders = headers.Where((_, i) => i != idx).Append(headers[idx]).ToArray();
        var newRows = rows.Select(r =>
        {
            if (idx >= r.Length) return r;
            var value = r[idx];
            return r.Where((_, i) => i != idx).Append(value).ToArray();
        }).ToList();
        return (newHeaders, newRows);
    }

    /// <summary>Lignes d'impression / PDF avec, à la place d'une colonne de groupe, une ligne titre
    /// « — GROUPE — » avant chaque groupe (<paramref name="items"/> déjà triés par groupe).</summary>
    public static List<string[]> BuildGroupedRows<T>(IEnumerable<T> items, Func<T, string> groupOf, Func<T, string[]> buildRow, int columnCount)
    {
        var rows = new List<string[]>();
        string? currentGroup = null;
        foreach (var item in items)
        {
            var group = groupOf(item);
            if (group != currentGroup)
            {
                currentGroup = group;
                var title = new string[columnCount];
                Array.Fill(title, "");
                title[0] = "— " + group.ToUpperInvariant() + " —";
                rows.Add(title);
            }
            rows.Add(buildRow(item));
        }
        return rows;
    }

    /// <summary>Couleurs de ligne alignées sur <see cref="BuildGroupedRows{T}"/> (même ordre, avec une
    /// entrée null pour chaque ligne titre de groupe insérée) : à passer telle quelle à
    /// <see cref="PrintTable"/> quand les lignes imprimées viennent de BuildGroupedRows.</summary>
    public static List<Color?> BuildGroupedRowColors<T>(IEnumerable<T> items, Func<T, string> groupOf, Func<T, Color?> colorOf)
    {
        var colors = new List<Color?>();
        string? currentGroup = null;
        foreach (var item in items)
        {
            var group = groupOf(item);
            if (group != currentGroup)
            {
                currentGroup = group;
                colors.Add(null);
            }
            colors.Add(colorOf(item));
        }
        return colors;
    }

    private static FlowDocument BuildFlowDocument(string title, string[] headers, IReadOnlyList<string[]> rows, bool includeCheckboxColumn,
        double pageWidth, double pageHeight, IReadOnlyList<Color?>? rowColors = null)
    {
        var doc = new FlowDocument
        {
            PagePadding = new Thickness(30),
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            ColumnWidth = double.PositiveInfinity,
            // Fixe la page à la taille imprimable réellement choisie dans le PrintDialog (papier +
            // orientation) : nécessaire pour que les colonnes Star ci-dessous (table.Columns) se répartissent
            // cette largeur exacte plutôt que de s'étendre à l'infini (ColumnWidth ci-dessus).
            PageWidth = pageWidth > 0 ? pageWidth : 793,
            PageHeight = pageHeight > 0 ? pageHeight : 1122
        };

        if (App.CompanyLogoImage is not null)
        {
            var logo = new Image { Source = App.CompanyLogoImage, MaxHeight = 50, MaxWidth = 200, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
            doc.Blocks.Add(new BlockUIContainer(logo) { Margin = new Thickness(0, 0, 0, 6) });
        }
        // Titre centré, uniquement sur la première page (voir pagination manuelle ci-dessous : un seul bloc
        // de titre est ajouté au document, avant le premier tableau).
        doc.Blocks.Add(new Paragraph(new Run(title)) { FontSize = 18, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center });
        doc.Blocks.Add(new Paragraph(new Run($"Imprimé le {DateTime.Now:dd/MM/yyyy HH:mm}"))
        {
            FontSize = 9, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 12)
        });

        // Largeurs en pixels précalculées ici (pas de colonnes Star) : les TableColumn en largeur Star
        // déclenchent un bug connu de Table FlowDocument où la hauteur de ligne calculée devient
        // incohérente d'une ligne à l'autre sans rapport avec le contenu. On calcule donc nous-mêmes des
        // largeurs fixes, mesurées sur le texte réel (pas approximées par un nombre de caractères, trop
        // imprécis pour garantir qu'une colonne tienne sur une seule ligne) : chaque colonne reçoit
        // exactement la largeur dont son contenu le plus large a besoin, l'espace restant éventuel étant
        // redistribué à la colonne "Filtres"/nom (la plus longue, qui en profite le plus) ; si la somme
        // dépasse quand même la page (texte très long partout), tout est réduit au prorata - même garantie
        // qu'avec Star ("jamais plus d'une feuille de large"), sans le bug.
        const double checkboxColumnWidth = 34;
        var contentWidth = (pageWidth > 0 ? pageWidth : 793) - 60 /* PagePadding gauche+droite */
            - (includeCheckboxColumn ? checkboxColumnWidth : 0);

        var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var boldTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
        const double horizontalPadding = 8; // 4 de chaque côté, voir NewCell
        const double buffer = 6; // marge de sécurité (bordures, arrondi de mesure)

        var textColumnCount = includeCheckboxColumn ? headers.Length - 1 : headers.Length;
        var naturalWidths = new double[textColumnCount];
        for (var i = 0; i < textColumnCount; i++)
        {
            var width = LongestLineWidth(headers[i], boldTypeface, 11);
            foreach (var row in rows)
                if (i < row.Length && row[i] is { } cell) width = Math.Max(width, LongestLineWidth(cell, typeface, 11));
            naturalWidths[i] = width + horizontalPadding + buffer;
        }

        // "Qté en place" : demandé volontairement réduite à la largeur de son seul titre de colonne
        // (toujours plus large que son contenu, 1-2 chiffres), pour libérer de la place pour les colonnes
        // à contenu plus long (Dimension, Périodicité...).
        var qtyIndex = Array.IndexOf(headers, "Qté en place");
        if (qtyIndex >= 0 && qtyIndex < textColumnCount)
            naturalWidths[qtyIndex] = LongestLineWidth(headers[qtyIndex], boldTypeface, 11) + horizontalPadding + buffer;

        // "Commentaire" : largeur minimale = celle de son seul titre (comme "Qté en place" ci-dessus), mais
        // reçoit en priorité tout l'espace libre restant une fois les autres colonnes satisfaites (voir plus
        // bas) - c'est la colonne destinée à absorber la place disponible.
        var commentIndex = Array.IndexOf(headers, "Commentaire");
        var commentMinWidth = commentIndex >= 0 && commentIndex < textColumnCount
            ? LongestLineWidth(headers[commentIndex], boldTypeface, 11) + horizontalPadding + buffer
            : 0;

        // "Dimension" : doit obligatoirement tenir sur une seule ligne, jamais réduite même si les colonnes
        // flexibles doivent être compressées pour tout faire tenir sur la page.
        var dimensionIndex = Array.IndexOf(headers, "Dimension");

        // Contenu toujours centré pour ces colonnes (même convention que les grilles à l'écran, voir
        // CenteredCellStyle dans les XAML).
        var periodicityIndex = Array.IndexOf(headers, "Périodicité");
        var centeredIndices = new HashSet<int> { dimensionIndex, qtyIndex, periodicityIndex };
        TextAlignment AlignmentFor(int i) => centeredIndices.Contains(i) ? TextAlignment.Center : TextAlignment.Left;

        var isFixed = new bool[textColumnCount];
        var fixedWidths = new double[textColumnCount];
        if (qtyIndex >= 0 && qtyIndex < textColumnCount) { isFixed[qtyIndex] = true; fixedWidths[qtyIndex] = naturalWidths[qtyIndex]; }
        if (dimensionIndex >= 0 && dimensionIndex < textColumnCount) { isFixed[dimensionIndex] = true; fixedWidths[dimensionIndex] = naturalWidths[dimensionIndex]; }
        if (commentIndex >= 0 && commentIndex < textColumnCount) { isFixed[commentIndex] = true; fixedWidths[commentIndex] = commentMinWidth; }

        var fixedTotal = 0.0;
        for (var i = 0; i < textColumnCount; i++) if (isFixed[i]) fixedTotal += fixedWidths[i];

        var flexIndices = Enumerable.Range(0, textColumnCount).Where(i => !isFixed[i]).ToArray();
        var flexNaturalTotal = flexIndices.Sum(i => naturalWidths[i]);
        var availableForFlex = contentWidth - fixedTotal;

        var finalWidths = new double[textColumnCount];
        for (var i = 0; i < textColumnCount; i++) if (isFixed[i]) finalWidths[i] = fixedWidths[i];

        if (availableForFlex >= flexNaturalTotal)
        {
            // Tout tient : chaque colonne flexible reçoit sa largeur naturelle, l'espace libre restant est
            // donné en priorité à "Commentaire" si elle est imprimée, sinon à la colonne "Filtres"/nom.
            foreach (var i in flexIndices) finalWidths[i] = naturalWidths[i];
            var leftover = availableForFlex - flexNaturalTotal;
            if (commentIndex >= 0 && commentIndex < textColumnCount) finalWidths[commentIndex] += leftover;
            else finalWidths[0] += leftover;
        }
        else if (flexIndices.Length > 0)
        {
            // Espace flexible insuffisant : seules les colonnes flexibles sont réduites au prorata
            // ("Dimension", "Qté en place" et "Commentaire" gardent leur largeur garantie).
            foreach (var i in flexIndices)
                finalWidths[i] = flexNaturalTotal > 0 ? availableForFlex * naturalWidths[i] / flexNaturalTotal : 0;
        }
        else if (fixedTotal > 0)
        {
            // Cas extrême : aucune colonne flexible et les colonnes fixes elles-mêmes dépassent la page,
            // réduites au prorata en dernier recours.
            for (var i = 0; i < textColumnCount; i++)
                finalWidths[i] = contentWidth * fixedWidths[i] / fixedTotal;
        }

        // Pagination manuelle : le titre des colonnes doit être répété en haut de chaque page imprimée (pas
        // seulement au tout début du document, comme le ferait un unique grand Table FlowDocument - WPF
        // n'a pas de notion native d'en-tête de tableau répété). On découpe donc les lignes en un tableau
        // séparé par page, chacun avec sa propre ligne d'en-tête, avec un saut de page forcé avant chaque
        // tableau suivant (Table.BreakPageBefore). La hauteur de chaque ligne (avec ses retours à la ligne
        // éventuels) est estimée via MeasureWrappedHeight, avec les largeurs de colonnes finales ci-dessus,
        // pour savoir combien de lignes tiennent sur une page.
        const double cellVerticalPadding = 2; // haut + bas, voir NewCell Padding (4,1,4,1)
        const double cellBorder = 1.5; // haut + bas, BorderThickness 0.75 x2
        // Notre estimation (FormattedText) ne correspond pas exactement au rendu réel du Table
        // FlowDocument (interlignage par défaut, marges entre blocs...) : sous-estimer la hauteur ferait
        // déborder une page avant notre saut de page manuel, et le nouveau tableau (avec son en-tête)
        // démarrerait alors au milieu d'une page suivante au lieu du haut. On majore donc légèrement chaque
        // mesure (la vraie cause du débordement était l'oubli de la case à cocher ci-dessous, désormais
        // corrigé - une marge trop généreuse ici laisse sinon un blanc inutile en bas de chaque page).
        const double heightSafetyFactor = 1.08;
        // Hauteur de la case à cocher "Fait" (NewCheckboxCell : Border 22 + sa propre Margin de 2 de chaque
        // côté) : pour une ligne au texte court (ex. "1"), c'est ELLE qui impose la vraie hauteur minimale
        // de la ligne, pas le texte - un oubli ici sous-estimait systématiquement la hauteur des lignes
        // courtes et provoquait un débordement interne du tableau (sans en-tête répété) avant notre saut de
        // page manuel.
        const double checkboxCellContentHeight = 22 + 4;

        double RowHeightFor(string[] row)
        {
            var maxCellHeight = 0.0;
            for (var i = 0; i < textColumnCount; i++)
            {
                var cellText = i < row.Length ? row[i] : "";
                var h = MeasureWrappedHeight(cellText, typeface, 11, finalWidths[i] - horizontalPadding);
                if (h > maxCellHeight) maxCellHeight = h;
            }
            if (includeCheckboxColumn && checkboxCellContentHeight > maxCellHeight) maxCellHeight = checkboxCellContentHeight;
            return (maxCellHeight * heightSafetyFactor) + cellVerticalPadding + cellBorder;
        }

        var headerRowHeight = 0.0;
        for (var i = 0; i < textColumnCount; i++)
        {
            var h = MeasureWrappedHeight(headers[i], boldTypeface, 11, finalWidths[i] - horizontalPadding);
            if (h > headerRowHeight) headerRowHeight = h;
        }
        headerRowHeight = (headerRowHeight * heightSafetyFactor) + cellVerticalPadding + cellBorder;

        // Place réservée sur la première page pour le logo, le titre (centré) et la ligne "Imprimé le ..."
        // au-dessus du tableau (leur hauteur réelle dépend de marges par défaut du FlowDocument non
        // exposées précisément - légère marge de sécurité incluse).
        var firstPageReserved = (App.CompanyLogoImage is not null ? 56.0 : 0.0) + 42.0 + 28.0;
        // Marge de sécurité supplémentaire sur chaque page (espacement entre blocs consécutifs du document,
        // arrondi de mesure) : même logique que heightSafetyFactor ci-dessus.
        const double pageSafetyMargin = 8;
        var pageContentHeight = (pageHeight > 0 ? pageHeight : 1122) - 60 /* PagePadding haut+bas */ - pageSafetyMargin;

        var rowList = rows as IReadOnlyList<string[]> ?? rows.ToList();
        var pageChunks = new List<List<(string[] Row, Color? Color)>>();
        var current = new List<(string[] Row, Color? Color)>();
        var used = 0.0;
        var isFirstPage = true;
        var capacity = pageContentHeight - headerRowHeight - firstPageReserved;

        for (var rowIndex = 0; rowIndex < rowList.Count; rowIndex++)
        {
            var row = rowList[rowIndex];
            var color = rowColors is not null && rowIndex < rowColors.Count ? rowColors[rowIndex] : null;
            var rh = RowHeightFor(row);
            if (current.Count > 0 && used + rh > capacity)
            {
                pageChunks.Add(current);
                current = new List<(string[] Row, Color? Color)>();
                used = 0;
                isFirstPage = false;
                capacity = pageContentHeight - headerRowHeight - (isFirstPage ? firstPageReserved : 0);
            }
            current.Add((row, color));
            used += rh;
        }
        if (current.Count > 0 || pageChunks.Count == 0) pageChunks.Add(current);

        var isFirstTable = true;
        foreach (var chunk in pageChunks)
        {
            var table = new Table
            {
                BorderBrush = GridLineBrush,
                BorderThickness = new Thickness(1),
                CellSpacing = 0
            };
            var isBreakBefore = !isFirstTable;
            isFirstTable = false;

            // "Fait" toujours en toute première colonne (demandé explicitement), quelle que soit la
            // position des autres colonnes (ex. "Commentaire", toujours en dernière position - voir
            // MoveColumnLast - ne dépend donc jamais de la case à cocher).
            if (includeCheckboxColumn) table.Columns.Add(new TableColumn { Width = new GridLength(checkboxColumnWidth) });
            for (var i = 0; i < textColumnCount; i++)
                table.Columns.Add(new TableColumn { Width = new GridLength(finalWidths[i], GridUnitType.Pixel) });

            table.RowGroups.Add(new TableRowGroup());
            var headerRow = new TableRow { Background = Brushes.LightGray, FontWeight = FontWeights.Bold };
            if (includeCheckboxColumn) headerRow.Cells.Add(NewCell(headers[textColumnCount], TextAlignment.Center));
            for (var i = 0; i < textColumnCount; i++)
                headerRow.Cells.Add(NewCell(headers[i], AlignmentFor(i)));
            table.RowGroups[0].Rows.Add(headerRow);

            foreach (var (row, color) in chunk)
            {
                var tr = new TableRow();
                if (color.HasValue) tr.Background = new SolidColorBrush(color.Value);
                if (includeCheckboxColumn) tr.Cells.Add(NewCheckboxCell());
                for (var i = 0; i < row.Length; i++)
                    tr.Cells.Add(NewCell(row[i], AlignmentFor(i)));
                table.RowGroups[0].Rows.Add(tr);
            }

            // BUG WPF connu : Table.BreakPageBefore n'a aucun effet (le saut de page est silencieusement
            // ignoré) - il faut l'appliquer sur une Section englobante, seul Block qui le respecte
            // correctement pour forcer le tableau suivant en haut de la page suivante.
            var section = new Section { BreakPageBefore = isBreakBefore };
            section.Blocks.Add(table);
            doc.Blocks.Add(section);
        }

        return doc;
    }

    /// <summary>Cellule d'une ligne de filtre imprimée.</summary>
    private static TableCell NewCell(string text, TextAlignment alignment = TextAlignment.Left) => new(new Paragraph(new Run(text ?? string.Empty))
    {
        Margin = new Thickness(0),
        TextAlignment = alignment
    })
    {
        Padding = new Thickness(4, 1, 4, 1),
        BorderBrush = GridLineBrush,
        BorderThickness = new Thickness(0.75)
    };

    /// <summary>Case à cocher vierge (~22 unités WPF à 96 DPI, environ la hauteur d'une ligne de texte à
    /// cette taille de police, pour ne jamais imposer une hauteur de ligne supérieure à celle du texte),
    /// cochée à la main au marqueur une fois le changement physique effectué.</summary>
    private static TableCell NewCheckboxCell()
    {
        var box = new Border
        {
            Width = 22,
            Height = 22,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(1.5),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2)
        };
        return new TableCell(new BlockUIContainer(box) { Margin = new Thickness(0) })
        {
            Padding = new Thickness(4, 1, 4, 1),
            BorderBrush = GridLineBrush,
            BorderThickness = new Thickness(0.75),
            TextAlignment = TextAlignment.Center
        };
    }
}
