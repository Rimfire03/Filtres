using System.Globalization;
using System.Text.RegularExpressions;

namespace FiltresApp.Core.Services;

/// <summary>Résultat du calcul de conformité d'une valeur mesurée.</summary>
public enum ZacConformity { Unknown, Conform, NonConform }

/// <summary>
/// Limites de concentration particulaire de l'ISO 14644-1 (particules par m³ d'air, tableau des classes de propreté) et
/// calcul de conformité d'un comptage. Seuls les comptages particulaires ont une limite dans cette norme : pression,
/// température, hygrométrie... n'en ont pas (la conformité reste alors à valider manuellement).
/// </summary>
public static class ZacIsoLimits
{
    // classe ISO -> (taille de particule en µm -> concentration maximale par m³). ISO 14644-1:2015, tableau 1.
    private static readonly Dictionary<int, Dictionary<double, long>> Table = new()
    {
        [1] = new() { [0.1] = 10, [0.2] = 2 },
        [2] = new() { [0.1] = 100, [0.2] = 24, [0.3] = 10, [0.5] = 4 },
        [3] = new() { [0.1] = 1_000, [0.2] = 237, [0.3] = 102, [0.5] = 35, [1] = 8 },
        [4] = new() { [0.1] = 10_000, [0.2] = 2_370, [0.3] = 1_020, [0.5] = 352, [1] = 83 },
        [5] = new() { [0.1] = 100_000, [0.2] = 23_700, [0.3] = 10_200, [0.5] = 3_520, [1] = 832, [5] = 29 },
        [6] = new() { [0.1] = 1_000_000, [0.2] = 237_000, [0.3] = 102_000, [0.5] = 35_200, [1] = 8_320, [5] = 293 },
        [7] = new() { [0.5] = 352_000, [1] = 83_200, [5] = 2_930 },
        [8] = new() { [0.5] = 3_520_000, [1] = 832_000, [5] = 29_300 },
        [9] = new() { [0.5] = 35_200_000, [1] = 8_320_000, [5] = 293_000 },
    };

    private static readonly Regex ClassPattern = new(@"ISO\s*([1-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SizePattern = new(@"[≥>]=?\s*([0-9]+(?:[.,][0-9]+)?)\s*[µμu]m", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Classe ISO d'après le nom du type de ZAC (« ISO 7 » donne 7), ou null.</summary>
    public static int? ClassFromTypeName(string? name) =>
        name is not null && ClassPattern.Match(name) is { Success: true } m ? int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : null;

    /// <summary>Taille de particule en µm d'après la désignation (« Comptage particulaire ≥ 0,5 µm » donne 0,5), ou null.</summary>
    public static double? SizeFromMeasurementName(string? name) =>
        name is not null && SizePattern.Match(name) is { Success: true } m
            ? double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture)
            : null;

    /// <summary>Concentration maximale (particules/m³) pour la classe et la taille, ou null si la norme n'en définit pas.</summary>
    public static long? Limit(int? isoClass, double? sizeMicrons)
    {
        if (isoClass is not { } c || sizeMicrons is not { } s) return null;
        return Table.TryGetValue(c, out var sizes) && sizes.TryGetValue(s, out var limit) ? limit : null;
    }

    /// <summary>Limite applicable à une valeur mesurée d'une pièce de la classe donnée (via sa désignation).</summary>
    public static long? LimitFor(string? typeName, string? measurementName) =>
        Limit(ClassFromTypeName(typeName), SizeFromMeasurementName(measurementName));

    public static string LimitText(long? limit) =>
        limit is { } l ? "≤ " + l.ToString("N0", CultureInfo.CurrentCulture) + " /m³" : "-";

    /// <summary>Conformité calculée d'une valeur : d'après la spécification relevée dans le rapport si elle existe, sinon
    /// d'après le seuil ISO 14644-1 des comptages particulaires de la classe du type de ZAC.</summary>
    public static ZacConformity Auto(string? typeName, string? measurementName, decimal? value, string? textValue, string? specification) =>
        !string.IsNullOrWhiteSpace(specification)
            // Pression différentielle « positive ou négative » : c'est la valeur absolue qui est comparée à la spécification.
            ? ZacSpecification.Evaluate(specification,
                measurementName is not null && measurementName.StartsWith("Pression diff", StringComparison.OrdinalIgnoreCase) && value is { } v ? Math.Abs(v) : value,
                textValue)
            : Evaluate(LimitFor(typeName, measurementName), value);

    public static ZacConformity Evaluate(long? limit, decimal? value)
    {
        if (limit is not { } l || value is not { } v) return ZacConformity.Unknown;
        return v <= l ? ZacConformity.Conform : ZacConformity.NonConform;
    }

    /// <summary>Statut final : la validation manuelle l'emporte sur le calcul automatique.</summary>
    public static string StatusText(ZacConformity auto, bool? manualValidation) =>
        manualValidation == true ? "Validé manuellement"
        : auto == ZacConformity.Conform ? "Conforme"
        : auto == ZacConformity.NonConform ? "Non conforme"
        : "-";
}
