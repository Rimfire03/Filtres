using System.Globalization;
using System.Text.RegularExpressions;

namespace FiltresApp.Core.Services;

/// <summary>
/// Spécification (valeur attendue) d'une valeur mesurée, relevée dans un rapport de qualification, et calcul de
/// conformité. Formes canoniques (stockées dans <c>ZacMeasurement.Specification</c>) :
/// « ≥ 15 », « &gt; 6 », « ≤ 5 », « &lt; 3 », « 0,25 – 0,35 » (intervalle), « 15 ± 5 » (tolérance), « ISO 5 » (classe : un
/// résultat de classe inférieure ou égale convient) ou un texte (égalité).
/// </summary>
public static class ZacSpecification
{
    private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

    private static readonly Regex NumberPattern = new(@"[-+−]?\d+(?:[.,]\d+)?", RegexOptions.Compiled);
    private static readonly Regex ClassPattern = new(@"^\s*ISO\s*(\d)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Format(decimal value) => value.ToString("0.###", Fr);

    public static string Min(decimal v, bool strict = false) => (strict ? "> " : "≥ ") + Format(v);
    public static string Max(decimal v, bool strict = false) => (strict ? "< " : "≤ ") + Format(v);
    public static string Range(decimal min, decimal max) => $"{Format(min)} – {Format(max)}";
    public static string Tolerance(decimal center, decimal delta) => $"{Format(center)} ± {Format(delta)}";

    /// <summary>Lit un nombre au format français ou anglais (« 52,4 », « +23 », « 0.25 »), ou null.</summary>
    public static decimal? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = NumberPattern.Match(text);
        if (!m.Success) return null;
        var normalized = m.Value.Replace('−', '-').Replace(',', '.').TrimStart('+');
        return decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Conformité d'un résultat (numérique ou texte) par rapport à une spécification canonique. Inconnue si la
    /// spécification ou le résultat est absent ou illisible.</summary>
    public static ZacConformity Evaluate(string? specification, decimal? value, string? textValue)
    {
        if (string.IsNullOrWhiteSpace(specification)) return ZacConformity.Unknown;
        var spec = specification.Trim();

        // Classe ISO : « ISO 5 » attendu, un résultat de classe inférieure ou égale (plus propre) convient.
        if (ClassPattern.Match(spec) is { Success: true } specClass)
        {
            if (textValue is null || ClassPattern.Match(textValue) is not { Success: true } resultClass) return ZacConformity.Unknown;
            return int.Parse(resultClass.Groups[1].Value) <= int.Parse(specClass.Groups[1].Value)
                ? ZacConformity.Conform : ZacConformity.NonConform;
        }

        // Spécification numérique.
        if (TryParseNumeric(spec, out var min, out var max, out var minStrict, out var maxStrict))
        {
            var number = value ?? ParseNumber(textValue);
            if (number is not { } n) return ZacConformity.Unknown;
            if (min is { } lo && (minStrict ? n <= lo : n < lo)) return ZacConformity.NonConform;
            if (max is { } hi && (maxStrict ? n >= hi : n > hi)) return ZacConformity.NonConform;
            return ZacConformity.Conform;
        }

        // Texte : égalité (sans tenir compte de la casse).
        if (string.IsNullOrWhiteSpace(textValue)) return ZacConformity.Unknown;
        return string.Equals(Normalize(spec), Normalize(textValue), StringComparison.OrdinalIgnoreCase)
            ? ZacConformity.Conform : ZacConformity.NonConform;
    }

    private static string Normalize(string s) => Regex.Replace(s.Replace('’', '\''), @"\s+", " ").Trim();

    private static bool TryParseNumeric(string spec, out decimal? min, out decimal? max, out bool minStrict, out bool maxStrict)
    {
        min = max = null;
        minStrict = maxStrict = false;

        // Intervalle « a – b ».
        var range = Regex.Match(spec, @"^\s*(?<a>[-+]?\d+(?:[.,]\d+)?)\s*[–-]\s*(?<b>[-+]?\d+(?:[.,]\d+)?)\s*$");
        if (range.Success)
        {
            min = ParseNumber(range.Groups["a"].Value);
            max = ParseNumber(range.Groups["b"].Value);
            return min is not null && max is not null;
        }

        // Tolérance « c ± d ».
        var tol = Regex.Match(spec, @"^\s*(?<c>[-+]?\d+(?:[.,]\d+)?)\s*±\s*(?<d>\d+(?:[.,]\d+)?)\s*$");
        if (tol.Success && ParseNumber(tol.Groups["c"].Value) is { } c && ParseNumber(tol.Groups["d"].Value) is { } d)
        {
            min = c - d;
            max = c + d;
            return true;
        }

        // Borne simple.
        var bound = Regex.Match(spec, @"^\s*(?<op>≥|>=|>|≤|<=|<)\s*(?<v>[-+]?\d+(?:[.,]\d+)?)\s*$");
        if (!bound.Success || ParseNumber(bound.Groups["v"].Value) is not { } v) return false;
        switch (bound.Groups["op"].Value)
        {
            case "≥" or ">=": min = v; break;
            case ">": min = v; minStrict = true; break;
            case "≤" or "<=": max = v; break;
            default: max = v; maxStrict = true; break;
        }
        return true;
    }
}
