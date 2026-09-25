using System.Text.RegularExpressions;
using FiltresApp.Core.Models;

namespace FiltresApp.Core.Services;

/// <summary>
/// Comparaison tolérante entre la dimension d'une ligne de commande (<see cref="OrderLine"/>) et celle
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
/// une ligne future l'utilise correctement).</para>
/// </summary>
public static class DimensionMatchService
{
    /// <summary>Motif "NNNxNNN" ou "NNNxNNNxNN", séparateurs x/X/×/* tolérés, avec espaces optionnels
    /// autour, décimales avec virgule ou point tolérées (ex. "66,5").</summary>
    private static readonly Regex DimensionPattern = new(
        @"(?<n1>\d+(?:[.,]\d+)?)\s*[xX×\*]\s*(?<n2>\d+(?:[.,]\d+)?)(?:\s*[xX×\*]\s*(?<n3>\d+(?:[.,]\d+)?))?",
        RegexOptions.Compiled);

    /// <summary>Extrait le premier motif de dimension trouvé dans un texte libre (ex. début de
    /// <see cref="OrderLine.Designation"/> ou de <see cref="PeriodicFilter.Dimension"/> lui-même, qui
    /// contient parfois du texte parasite après les chiffres, ex. "535x345x20   4poches de 300").</summary>
    public static string? ExtractDimensionToken(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = DimensionPattern.Match(text);
        if (!m.Success) return null;
        var parts = new List<string> { m.Groups["n1"].Value, m.Groups["n2"].Value };
        if (m.Groups["n3"].Success && m.Groups["n3"].Value.Length > 0) parts.Add(m.Groups["n3"].Value);
        return string.Join("x", parts);
    }

    /// <summary>Normalise un motif de dimension pour la comparaison : minuscules, sans espaces, virgule
    /// décimale ramenée au point.</summary>
    public static string Normalize(string token)
    {
        var noSpace = new string(token.Where(c => !char.IsWhiteSpace(c)).ToArray());
        return noSpace.ToLowerInvariant().Replace(",", ".");
    }

    /// <summary>Clé de dimension normalisée d'une ligne de commande, ou <c>null</c> si aucune dimension
    /// n'a pu être identifiée. Voir la remarque de classe : c'est le préfixe numérique de
    /// <see cref="OrderLine.Designation"/> qui est utilisé (pas le champ <c>Dimension</c>, qui contient en
    /// réalité un code catégorie/média sur les données réelles).</summary>
    public static string? GetOrderLineDimensionKey(OrderLine line)
    {
        var token = ExtractDimensionToken(line.Designation) ?? ExtractDimensionToken(line.Dimension);
        return token == null ? null : Normalize(token);
    }

    /// <summary>Clé de dimension normalisée d'un filtre périodique, ou <c>null</c> si son champ
    /// <c>Dimension</c> est vide.</summary>
    public static string? GetFilterDimensionKey(PeriodicFilter filter)
    {
        var token = ExtractDimensionToken(filter.Dimension) ?? filter.Dimension;
        return string.IsNullOrWhiteSpace(token) ? null : Normalize(token);
    }

    /// <summary>Compare la dimension d'une ligne de commande et d'un filtre périodique, avec tolérance
    /// aux espaces/casse/séparateurs (voir <see cref="Normalize"/>). Retourne <c>false</c> si l'une des
    /// deux dimensions n'a pas pu être identifiée (pas de correspondance possible).</summary>
    public static bool Matches(OrderLine line, PeriodicFilter filter)
    {
        var a = GetOrderLineDimensionKey(line);
        var b = GetFilterDimensionKey(filter);
        return a != null && b != null && a == b;
    }
}
