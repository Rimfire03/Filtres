using System.Text.RegularExpressions;
using FiltresApp.Core.Models;

namespace FiltresApp.Core.Services;

/// <summary>
/// Comparaison approximative entre la dimension d'une ligne de commande (<see cref="OrderLine"/>) et celle
/// d'un filtre périodique (<see cref="PeriodicFilter"/>), utilisée par le sélecteur de rattachement
/// (<see cref="Views.Dialogs.FilterLinkWindow"/> côté WPF) pour ne proposer que les filtres de dimension
/// correspondante.
///
/// <para><b>Constat sur les données réelles (important, contre-intuitif)</b> : le champ
/// <see cref="OrderLine.Dimension"/> ne contient en pratique PAS une dimension physique mais un code de
/// catégorie/média hérité de la feuille Excel d'origine (ex. "PLG G4", "PJG G4", "MCF G3", "MTPS F7") -
/// vérifié sur les 461 lignes réelles en base, 0 valeur ne correspondant au format de
/// <see cref="PeriodicFilter.Dimension"/> ("1035x698x45", etc.). La dimension physique réelle de la ligne
/// de commande est en fait le préfixe numérique de son champ <see cref="OrderLine.Designation"/> (ex.
/// "630X325x47*  MAGNOLIAS", "415x385x45  CTA DE CLERAMBAULT") : sur les données réelles, extraire ce
/// préfixe et le comparer (normalisé) à <see cref="PeriodicFilter.Dimension"/> donne 261 correspondances
/// sur 396 lignes où un motif de dimension a pu être extrait, contre 0 en comparant le champ
/// <see cref="OrderLine.Dimension"/> tel quel. C'est donc ce préfixe numérique de <c>Designation</c> qui
/// est utilisé ici comme "dimension" de la ligne de commande (avec repli sur <c>Dimension</c> si jamais
/// une ligne future l'utilise correctement). À l'écran, <c>Designation</c> est affiché dans la colonne
/// "Dimension" et <c>Dimension</c> dans la colonne "Type".</para>
/// </summary>
public static class DimensionMatchService
{
    /// <summary>Écart toléré sur chaque valeur de dimension, en millimètres.</summary>
    public const double ToleranceMm = 5;

    /// <summary>Motif "NNNxNNN" ou "NNNxNNNxNN", séparateurs x/X/×/* tolérés, avec espaces optionnels
    /// autour, décimales avec virgule ou point tolérées (ex. "66,5").</summary>
    private static readonly Regex DimensionPattern = new(
        @"(?<n1>\d+(?:[.,]\d+)?)\s*[xX×\*]\s*(?<n2>\d+(?:[.,]\d+)?)(?:\s*[xX×\*]\s*(?<n3>\d+(?:[.,]\d+)?))?",
        RegexOptions.Compiled);

    /// <summary>Valeurs (2 ou 3) du premier motif de dimension trouvé dans un texte libre, ou null. Le texte
    /// autour est ignoré (ex. "630X325x47*  MAGNOLIAS", "535x345x20   4poches de 300").</summary>
    public static double[]? ExtractValues(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = DimensionPattern.Match(text);
        if (!m.Success) return null;

        var values = new List<double> { Parse(m.Groups["n1"].Value), Parse(m.Groups["n2"].Value) };
        if (m.Groups["n3"].Success && m.Groups["n3"].Value.Length > 0) values.Add(Parse(m.Groups["n3"].Value));
        return values.ToArray();
    }

    private static double Parse(string number) =>
        double.Parse(number.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Dimension d'une ligne de commande : colonne "Dimension" (champ Designation), sinon colonne
    /// "Type" (champ Dimension). Voir la remarque de classe.</summary>
    public static double[]? GetOrderLineValues(OrderLine line) =>
        ExtractValues(line.Designation) ?? ExtractValues(line.Dimension);

    /// <summary>Comparaison approximative : chaque valeur à ±<see cref="ToleranceMm"/> mm près, largeur et
    /// hauteur interchangeables, et épaisseur comparée seulement si les deux côtés en ont une. Retourne
    /// false si l'une des deux dimensions n'a pas pu être lue.</summary>
    public static bool Matches(OrderLine line, PeriodicFilter filter) => Matches(line, filter.Dimension);

    /// <summary>Même comparaison, à partir de la colonne Dimension d'un filtre quelconque (ex. F7 à H13).</summary>
    public static bool Matches(OrderLine line, string? filterDimension)
    {
        var a = GetOrderLineValues(line);
        var b = ExtractValues(filterDimension);
        if (a is null || b is null) return false;

        var sameOrder = Close(a[0], b[0]) && Close(a[1], b[1]);
        var swapped = Close(a[0], b[1]) && Close(a[1], b[0]);
        if (!sameOrder && !swapped) return false;

        return a.Length < 3 || b.Length < 3 || Close(a[2], b[2]);
    }

    private static bool Close(double x, double y) => Math.Abs(x - y) <= ToleranceMm;
}
