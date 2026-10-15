using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace FiltresApp.Core.Services;

/// <summary>Une valeur reconnue dans un rapport : désignation, résultat (numérique ou texte), unité et spécification.</summary>
public sealed record ImportedMeasurement(string Name, decimal? Value, string? TextValue, string? Unit, string? Specification);

/// <summary>Résultat de la lecture d'un rapport de qualification.</summary>
public sealed record ReportImportResult(
    List<ImportedMeasurement> Measurements, DateOnly? InterventionDate, string? RiskClassObtained, string? Conditions, int? Page);

/// <summary>
/// Lecture d'un rapport de qualification des installations de traitement d'air (modèle APAVE, NF S 90-351) : repère la
/// page « TABLEAU DE SYNTHESE » (annexe « Détail des mesures ») et en relève les paramètres, leur spécification et le
/// résultat obtenu, pour préremplir les valeurs d'un contrôle. Lit la couche texte du PDF ; un PDF scanné (image) n'a pas
/// de texte à lire : il faudrait alors une reconnaissance de caractères.
/// </summary>
public static class ZacReportImporter
{
    private enum Kind { Flow, IsoClass, Minutes, Ufc, VolumesPerHour, Velocity, Pressure, Temperature }

    private sealed record Param(string Name, Regex Label, Kind Kind);

    private static Regex R(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Paramètres du tableau de synthèse, dans l'ordre du rapport.
    private static readonly Param[] Params =
    {
        new("Régime d'écoulement de l'air", R(@"R[ée]gime d.[ée]coulement de l.air de la zone [àa] prot[ée]ger"), Kind.Flow),
        new("Classe de propreté particulaire (sous flux)", R(@"Classe de propret[ée] particulaire \(Sous Flux\)"), Kind.IsoClass),
        new("Classe de propreté particulaire (hors flux)", R(@"Classe de propret[ée] particulaire \(Hors Flux\)"), Kind.IsoClass),
        new("Cinétique d'élimination des particules", R(@"Cin[ée]tique d.[ée]limination des particules"), Kind.Minutes),
        new("Classe de propreté microbiologique (sous flux)", R(@"Classe de propret[ée] microbiologique Sous Flux"), Kind.Ufc),
        new("Taux de brassage", R(@"Taux de brassage attendu \(Classe \d\) \([^)]*\)"), Kind.VolumesPerHour),
        new("Vitesse sous flux unidirectionnel", R(@"Vitesses sous flux d.air unidirectionnel \(Classe \d\) \(m/s\)"), Kind.Velocity),
        new("Taux d'air neuf", R(@"Taux d.air neuf \(Classe \d\) \([^)]*\)"), Kind.VolumesPerHour),
        new("Pression différentielle", R(@"Pression diff[ée]rentielle\s*\(positive ou n[ée]gative\)"), Kind.Pressure),
        new("Température", R(@"Temp[ée]rature"), Kind.Temperature),
    };

    private static readonly Regex EndMarker = R(@"Synth[èe]se et commentaires");

    private const string Num = @"[-+−]?\d+(?:[.,]\d+)?";

    /// <summary>Texte de chaque page du PDF (vide pour une page sans couche texte).</summary>
    public static List<string> ReadPages(string pdfPath)
    {
        using var document = PdfDocument.Open(pdfPath);
        return document.GetPages().Select(p => p.Text).ToList();
    }

    /// <summary>Lit le rapport : trouve la page du tableau de synthèse et en extrait les valeurs. Retourne null si aucune
    /// page ne ressemble à un tableau de synthèse.</summary>
    public static ReportImportResult? Import(string pdfPath)
    {
        var pages = ReadPages(pdfPath);
        for (var i = 0; i < pages.Count; i++)
        {
            var text = Clean(pages[i]);
            if (!text.Contains("TABLEAU DE SYNTHESE", StringComparison.OrdinalIgnoreCase)
                && !(text.Contains("Valeurs guides", StringComparison.OrdinalIgnoreCase) && text.Contains("Résultats obtenus", StringComparison.OrdinalIgnoreCase)))
                continue;

            var result = ParseSynthesis(text);
            if (result.Measurements.Count > 0) return result with { Page = i + 1 };
        }
        return null;
    }

    private static string Clean(string text) =>
        Regex.Replace(text.Replace('’', '\'').Replace(' ', ' ').Replace(' ', ' '), @"\s+", " ").Trim();

    /// <summary>Analyse du texte de la page de synthèse (séparée de la lecture du PDF pour pouvoir être testée).</summary>
    public static ReportImportResult ParseSynthesis(string rawText)
    {
        var text = Clean(rawText);
        var list = new List<ImportedMeasurement>();

        // Positions des intitulés, dans l'ordre.
        var found = new List<(Param Param, int Start, int End)>();
        var from = 0;
        foreach (var p in Params)
        {
            var m = p.Label.Match(text, from);
            if (!m.Success) continue;
            found.Add((p, m.Index, m.Index + m.Length));
            from = m.Index + m.Length;
        }

        var endIndex = EndMarker.Match(text, found.Count > 0 ? found[^1].End : 0) is { Success: true } end ? end.Index : text.Length;

        for (var i = 0; i < found.Count; i++)
        {
            var segmentEnd = i + 1 < found.Count ? found[i + 1].Start : endIndex;
            var segment = text[found[i].End..Math.Max(found[i].End, segmentEnd)];
            if (Extract(found[i].Param, segment) is { } item) list.Add(item);
        }

        // Informations générales.
        DateOnly? date = null;
        if (Regex.Match(text, @"Date d.intervention\s*:\s*(\d{2})/(\d{2})/(\d{2,4})") is { Success: true } dm)
        {
            var year = int.Parse(dm.Groups[3].Value);
            if (year < 100) year += 2000;
            try { date = new DateOnly(year, int.Parse(dm.Groups[2].Value), int.Parse(dm.Groups[1].Value)); } catch { /* date illisible */ }
        }

        var risk = Regex.Match(text, @"Classe de risque obtenue\s*(Classe [^C]*?(?:/\s*Classe \d\s*\w*)?)\s*Commentaires", RegexOptions.IgnoreCase) is { Success: true } rm
            ? rm.Groups[1].Value.Trim() : null;
        var conditions = Regex.Match(text, @"Conditions d.occupation du local/zone\s*:\s*([A-ZÉÈa-zéè]+)") is { Success: true } cm
            ? cm.Groups[1].Value.Trim() : null;

        return new ReportImportResult(list, date, risk, conditions, null);
    }

    private static ImportedMeasurement? Extract(Param p, string seg)
    {
        switch (p.Kind)
        {
            case Kind.Flow:
            {
                var ms = Regex.Matches(seg, @"Flux d'air (?:non )?unidirectionnel|Flux d'air (?:turbulent|mixte)|Flux (?:turbulent|mixte)", RegexOptions.IgnoreCase);
                if (ms.Count < 2) return null;
                return new ImportedMeasurement(p.Name, null, ms[1].Value.Trim(), null, ms[0].Value.Trim());
            }
            case Kind.IsoClass:
            {
                var ms = Regex.Matches(seg, @"ISO\s*(\d)", RegexOptions.IgnoreCase);
                if (ms.Count < 2) return null;
                return new ImportedMeasurement(p.Name, null, "ISO " + ms[1].Groups[1].Value, null, "ISO " + ms[0].Groups[1].Value);
            }
            case Kind.Minutes:
            {
                var ms = Regex.Matches(seg, @"(" + Num + @")\s*min", RegexOptions.IgnoreCase);
                if (ms.Count < 2) return null;
                return Numeric(p.Name, ms[1].Groups[1].Value, "min", ZacSpecification.Max(Dec(ms[0].Groups[1].Value)));
            }
            case Kind.Ufc:
            {
                var ms = Regex.Matches(seg, @"(" + Num + @")\s*UFC", RegexOptions.IgnoreCase);
                if (ms.Count < 2) return null;
                return Numeric(p.Name, ms[1].Groups[1].Value, "UFC/m³", ZacSpecification.Max(Dec(ms[0].Groups[1].Value)));
            }
            case Kind.VolumesPerHour:
            {
                var spec = Regex.Match(seg, @"([≥>]=?)\s*(" + Num + @")\s*Volumes");
                if (!spec.Success) return null;
                var result = Regex.Matches(seg[(spec.Index + spec.Length)..], @"(" + Num + @")\s*Volumes", RegexOptions.IgnoreCase);
                // Le premier « Volumes » après la spécification fait partie de « Volumes / heure » de la spécification
                // elle-même ; le résultat est le nombre suivi de « Volumes ».
                var m = result.Count > 0 ? result[0] : null;
                if (m is null) return null;
                var strict = spec.Groups[1].Value == ">";
                return Numeric(p.Name, m.Groups[1].Value, "vol/h", ZacSpecification.Min(Dec(spec.Groups[2].Value), strict));
            }
            case Kind.Velocity:
            {
                var spec = Regex.Match(seg, @"(" + Num + @")\s*≤\s*V[^≤]*≤\s*(" + Num + @")");
                if (!spec.Success) return null;
                var result = Regex.Matches(seg[(spec.Index + spec.Length)..], @"(" + Num + @")\s*m/s", RegexOptions.IgnoreCase);
                if (result.Count == 0) return null;
                return Numeric(p.Name, result[^1].Groups[1].Value, "m/s",
                    ZacSpecification.Range(Dec(spec.Groups[1].Value), Dec(spec.Groups[2].Value)));
            }
            case Kind.Pressure:
            {
                var spec = Regex.Match(seg, @"(" + Num + @")\s*Pa\s*±\s*(" + Num + @")\s*Pa", RegexOptions.IgnoreCase);
                if (!spec.Success) return null;
                var result = Regex.Matches(seg[(spec.Index + spec.Length)..], @"(" + Num + @")\s*Pa", RegexOptions.IgnoreCase);
                if (result.Count == 0) return null;
                return Numeric(p.Name, result[0].Groups[1].Value, "Pa",
                    ZacSpecification.Tolerance(Dec(spec.Groups[1].Value), Dec(spec.Groups[2].Value)));
            }
            case Kind.Temperature:
            {
                var spec = Regex.Match(seg, @"(" + Num + @")\s*°\s*C\s*≤\s*T°?\s*≤\s*(" + Num + @")\s*°\s*C", RegexOptions.IgnoreCase);
                if (!spec.Success) return null;
                var result = Regex.Matches(seg[(spec.Index + spec.Length)..], @"(" + Num + @")\s*°\s*C", RegexOptions.IgnoreCase);
                if (result.Count == 0) return null;
                return Numeric(p.Name, result[0].Groups[1].Value, "°C",
                    ZacSpecification.Range(Dec(spec.Groups[1].Value), Dec(spec.Groups[2].Value)));
            }
        }
        return null;
    }

    private static decimal Dec(string s) =>
        decimal.Parse(s.Replace('−', '-').Replace(',', '.').TrimStart('+'), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static ImportedMeasurement Numeric(string name, string result, string unit, string spec) =>
        new(name, Dec(result), null, unit, spec);
}
