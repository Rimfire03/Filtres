using System.Globalization;
using System.Text.RegularExpressions;

namespace FiltresApp.Core.Services;

/// <summary>Harmonise le champ Dimension des filtres (G4 plissé/plan, G3, Charbon, F7 à H13) selon la
/// norme : "aaaa x bbbb x cccc", où aaaa est toujours le plus grand des deux premiers nombres (bbbb le
/// plus petit) et cccc (épaisseur) n'est jamais réordonné. Un indice de classe entre parenthèses
/// ("(A+)", "( C )", "(Classe B)"...) est retiré. Tout texte libre restant après les nombres est déplacé
/// sur une seconde ligne, sous la dimension. Ne propose rien (retourne null) si le texte ne commence pas
/// par un motif de dimension reconnaissable ("NxN" ou "NxNxN") : ces valeurs (ex. "A laver") restent
/// inchangées, aucune règle ne s'y applique.</summary>
public static class DimensionFormatService
{
    private static readonly Regex LeadingDimensionPattern = new(
        @"^\s*(?<n1>\d+(?:[.,]\d+)?)\s*[xX×\*]\s*(?<n2>\d+(?:[.,]\d+)?)(?:\s*[xX×\*]\s*(?<n3>\d+(?:[.,]\d+)?))?",
        RegexOptions.Compiled);

    /// <summary>Indice de classe entre parenthèses (ex. "(A+)", "( C )", "(Classe B )", "( classe A+)") :
    /// uniquement une lettre A à D, "+" optionnel, mot "classe" optionnel devant. D'autres parenthèses
    /// (ex. "(9poches)", "(diam)", "(à découper)") ne correspondent pas et sont laissées telles quelles,
    /// traitées comme texte libre.</summary>
    private static readonly Regex ClassSuffixPattern = new(
        @"^\(\s*(?:classe\s+)?[A-Da-d]\+?\s*\)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Reformate <paramref name="raw"/> selon la norme, ou retourne null si aucun motif de
    /// dimension reconnaissable n'a été trouvé en début de texte.</summary>
    public static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var match = LeadingDimensionPattern.Match(raw);
        if (!match.Success) return null;

        var n1 = match.Groups["n1"].Value;
        var n2 = match.Groups["n2"].Value;
        var n3 = match.Groups["n3"].Success ? match.Groups["n3"].Value : null;

        var (first, second) = NumericValue(n1) >= NumericValue(n2) ? (n1, n2) : (n2, n1);

        var result = $"{first} x {second}";
        if (n3 is not null) result += $" x {n3}";

        var rest = raw[match.Length..].Trim();
        var classMatch = ClassSuffixPattern.Match(rest);
        if (classMatch.Success) rest = rest[classMatch.Length..].Trim();

        if (rest.Length > 0) result += "\n" + rest;

        return result;
    }

    private static double NumericValue(string token) =>
        double.TryParse(token.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
