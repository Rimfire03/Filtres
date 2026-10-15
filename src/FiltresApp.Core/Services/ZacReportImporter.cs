using System.Globalization;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace FiltresApp.Core.Services;

/// <summary>Une valeur reconnue dans un rapport : désignation, résultat (numérique ou texte), unité et spécification.</summary>
public sealed record ImportedMeasurement(string Name, decimal? Value, string? TextValue, string? Unit, string? Specification);

/// <summary>Résultat de la lecture d'un rapport de qualification.</summary>
/// <param name="InterventionDate">Date d'intervention du tableau de synthèse (date des mesures).</param>
/// <param name="QualificationDate">Date de la qualification : fin de la période d'intervention de la page de garde
/// (« du 24/08/2026 au 24/08/2026 »), à défaut la date d'intervention du tableau de synthèse.</param>
/// <param name="ReportDate">Date d'émission du rapport (page de garde).</param>
public sealed record ReportImportResult(
    List<ImportedMeasurement> Measurements, DateOnly? InterventionDate, string? RiskClassObtained, string? Conditions, int? Page,
    DateOnly? QualificationDate = null, DateOnly? ReportDate = null, string? ReportNumber = null);

/// <summary>
/// Lecture d'un rapport de qualification des installations de traitement d'air (modèle APAVE, NF S 90-351) : repère la
/// page « TABLEAU DE SYNTHESE » (annexe « Détail des mesures ») et en relève les paramètres, leur spécification et le
/// résultat obtenu, pour préremplir les valeurs d'un contrôle. Lit la couche texte du PDF ; un PDF scanné (image) n'a pas
/// de texte à lire : il faudrait alors une reconnaissance de caractères.
/// Trois présentations du tableau sont reconnues : salle d'opération (flux unidirectionnel, classes sous/hors flux),
/// salle à flux non unidirectionnel (classe 3) et local de classe ISO 8 (laboratoire, UPA : taux de brassage indicatif,
/// humidité relative).
/// </summary>
public static class ZacReportImporter
{
    private enum Kind { Flow, IsoClass, Minutes, Ufc, VolumesPerHour, Velocity, Pressure, Temperature, Humidity }

    private sealed record Param(string Name, Regex Label, Kind Kind);

    private static Regex R(string pattern) => new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Paramètres du tableau de synthèse, dans l'ordre d'apparition (un paramètre absent du rapport est simplement sauté).
    private static readonly Param[] Params =
    {
        new("Régime d'écoulement de l'air", R(@"R[ée]gime d.[ée]coulement de l.air de la zone [àa] prot[ée]ger"), Kind.Flow),
        new("Classe de propreté particulaire (sous flux)", R(@"Classe de propret[ée] particulaire \(Sous Flux\)"), Kind.IsoClass),
        new("Classe de propreté particulaire (hors flux)", R(@"Classe de propret[ée] particulaire \(Hors Flux\)"), Kind.IsoClass),
        new("Classe de propreté particulaire", R(@"Classe de propret[ée] particulaire(?!\s*\()"), Kind.IsoClass),
        new("Cinétique d'élimination des particules", R(@"Cin[ée]tique d.[ée]limination des particules(?:\s*\([^)]*\))?"), Kind.Minutes),
        new("Classe de propreté microbiologique (sous flux)", R(@"Classe de propret[ée] microbiologique Sous Flux"), Kind.Ufc),
        new("Classe de propreté microbiologique", R(@"Classe de propret[ée] microbiologique(?!\s*Sous Flux)"), Kind.Ufc),
        new("Taux de brassage", R(@"Taux de brassage attendu(?:\s*\([^)]*\))+"), Kind.VolumesPerHour),
        new("Vitesse sous flux unidirectionnel", R(@"Vitesses? sous flux d.air unidirectionnel(?:\s*\([^)]*\))+"), Kind.Velocity),
        new("Taux d'air neuf", R(@"Taux d.air neuf(?:\s*\([^)]*\))+"), Kind.VolumesPerHour),
        new("Pression différentielle", R(@"Pression diff[ée]rentielle\s*\(positive ou n[ée]gative\)"), Kind.Pressure),
        new("Température", R(@"Temp[ée]rature"), Kind.Temperature),
        new("Humidité relative", R(@"Humidit[ée] [Rr]elative"), Kind.Humidity),
    };

    private static readonly Regex EndMarker = R(@"Synth[èe]se et commentaires");

    private const string Num = @"[-+−]?\s?\d+(?:[.,]\d+)?";

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
            if (result.Measurements.Count == 0) continue;

            // Page de garde : période d'intervention (la qualification est datée de la fin des mesures) et date du rapport.
            var cover = Clean(string.Join(" ", pages.Take(2)));
            var period = Regex.Match(cover, @"Date\(s\) d.intervention\s*:\s*du\s*(\d{2}/\d{2}/\d{4})\s*au\s*(\d{2}/\d{2}/\d{4})", RegexOptions.IgnoreCase);
            var qualification = period.Success ? ParseDate(period.Groups[2].Value) : null;
            var reportDate = Regex.Match(cover, @"N° de rapport[^D]{0,60}?Date\s*:\s*(\d{2}/\d{2}/\d{2,4})", RegexOptions.IgnoreCase) is { Success: true } rd
                ? ParseDate(rd.Groups[1].Value) : null;
            var number = Regex.Match(cover, @"N° de rapport\s*[–-]\s*Version\s*:\s*([0-9]+-[0-9]+-[0-9]+)", RegexOptions.IgnoreCase) is { Success: true } nm
                ? nm.Groups[1].Value : null;

            return result with
            {
                Page = i + 1,
                QualificationDate = qualification ?? result.InterventionDate,
                ReportDate = reportDate,
                ReportNumber = number
            };
        }
        return null;
    }

    private static DateOnly? ParseDate(string text)
    {
        var m = Regex.Match(text, @"(\d{2})/(\d{2})/(\d{2,4})");
        if (!m.Success) return null;
        var year = int.Parse(m.Groups[3].Value);
        if (year < 100) year += 2000;
        try { return new DateOnly(year, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value)); } catch { return null; }
    }

    private static string Clean(string text) =>
        Regex.Replace(text.Replace('’', '\'').Replace(' ', ' ').Replace(' ', ' '), @"\s+", " ").Trim();

    /// <summary>Analyse du texte de la page de synthèse (séparée de la lecture du PDF pour pouvoir être testée).</summary>
    public static ReportImportResult ParseSynthesis(string rawText)
    {
        var text = Clean(rawText);
        var list = new List<ImportedMeasurement>();

        // Les intitulés ne sont cherchés qu'avant « Synthèse et commentaires » : le texte des commentaires peut reprendre des
        // intitulés (« ... la classe de propreté microbiologique ... ») qui ne sont pas des lignes du tableau.
        var endMatch = EndMarker.Match(text);
        var body = endMatch.Success ? text[..endMatch.Index] : text;

        // Positions des intitulés, dans l'ordre.
        var found = new List<(Param Param, int Start, int End)>();
        var from = 0;
        foreach (var p in Params)
        {
            var m = p.Label.Match(body, from);
            if (!m.Success) continue;
            found.Add((p, m.Index, m.Index + m.Length));
            from = m.Index + m.Length;
        }

        var endIndex = body.Length;

        for (var i = 0; i < found.Count; i++)
        {
            var segmentEnd = i + 1 < found.Count ? found[i + 1].Start : endIndex;
            var segment = text[found[i].End..Math.Max(found[i].End, segmentEnd)];
            list.AddRange(Extract(found[i].Param, segment));
        }

        // Informations générales.
        DateOnly? date = null;
        if (Regex.Match(text, @"Date d.intervention\s*:\s*(\d{2})/(\d{2})/(\d{2,4})") is { Success: true } dm)
        {
            var year = int.Parse(dm.Groups[3].Value);
            if (year < 100) year += 2000;
            try { date = new DateOnly(year, int.Parse(dm.Groups[2].Value), int.Parse(dm.Groups[1].Value)); } catch { /* date illisible */ }
        }

        var risk = Regex.Match(text, @"Classe de risque obtenue\s*(.+?)\s*Commentaires?\s*:", RegexOptions.IgnoreCase) is { Success: true } rm
            ? rm.Groups[1].Value.Trim() : null;
        var conditions = Regex.Match(text, @"Conditions d.occupation du local/zone\s*:\s*([A-ZÉÈa-zéè]+)") is { Success: true } cm
            ? cm.Groups[1].Value.Trim() : null;

        return new ReportImportResult(list, date, risk, conditions, null);
    }

    private static IEnumerable<ImportedMeasurement> Extract(Param p, string seg)
    {
        switch (p.Kind)
        {
            case Kind.Flow:
            {
                var ms = Regex.Matches(seg,
                    @"Flux d'air (?:non )?unidirectionnel(?: ou (?:non )?unidirectionnel)?|Flux d'air (?:turbulent|mixte)|Flux (?:turbulent|mixte)",
                    RegexOptions.IgnoreCase);
                if (ms.Count < 2) yield break;
                yield return new ImportedMeasurement(p.Name, null, ms[1].Value.Trim(), null, ms[0].Value.Trim());
                yield break;
            }
            case Kind.IsoClass:
            {
                var ms = Regex.Matches(seg, @"ISO\s*(\d)", RegexOptions.IgnoreCase);
                if (ms.Count < 2) yield break;
                yield return new ImportedMeasurement(p.Name, null, "ISO " + ms[1].Groups[1].Value, null, "ISO " + ms[0].Groups[1].Value);
                yield break;
            }
            case Kind.Minutes:
            {
                var ms = Regex.Matches(seg, @"([<≤]\s*)?(\d+(?:[.,]\d+)?)'?\s*min", RegexOptions.IgnoreCase);
                if (ms.Count < 2) yield break;
                yield return WithPrefix(p.Name, ms[1], "min", ZacSpecification.Max(Dec(ms[0].Groups[2].Value)));
                yield break;
            }
            case Kind.Ufc:
            {
                // « 4+2 UFC / m3 » : somme des deux dénombrements ; « < 1 UFC » : résultat en dessous du seuil de détection.
                var ms = Regex.Matches(seg, @"([<≤]\s*)?(\d+(?:\s*\+\s*\d+)*)\s*UFC", RegexOptions.IgnoreCase);
                if (ms.Count < 2) yield break;
                var spec = ZacSpecification.Max(Sum(ms[0].Groups[2].Value));
                var result = ms[1];
                if (result.Groups[1].Success)
                    yield return new ImportedMeasurement(p.Name, null, "< " + Sum(result.Groups[2].Value).ToString("0.###", CultureInfo.CurrentCulture), "UFC/m³", spec);
                else
                    yield return new ImportedMeasurement(p.Name, Sum(result.Groups[2].Value), null, "UFC/m³", spec);
                yield break;
            }
            case Kind.VolumesPerHour:
            {
                string? specText = null;
                var rest = seg;
                var min = Regex.Match(seg, @"([≥>]=?)\s*(" + Num + @")\s*Volumes");
                var between = Regex.Match(seg, @"Entre\s*(" + Num + @")\s*et\s*(" + Num + @")\s*Volumes", RegexOptions.IgnoreCase);
                if (min.Success)
                {
                    specText = ZacSpecification.Min(Dec(min.Groups[2].Value), min.Groups[1].Value == ">");
                    rest = seg[(min.Index + min.Length)..];
                }
                else if (between.Success)
                {
                    specText = ZacSpecification.Range(Dec(between.Groups[1].Value), Dec(between.Groups[2].Value));
                    rest = seg[(between.Index + between.Length)..];
                }
                if (specText is null) yield break;
                var result = Regex.Match(rest, @"(" + Num + @")\s*Volumes", RegexOptions.IgnoreCase);
                if (!result.Success) yield break;
                yield return Numeric(p.Name, result.Groups[1].Value, "vol/h", specText);
                yield break;
            }
            case Kind.Velocity:
            {
                string? specText = null;
                var rest = seg;
                var range = Regex.Match(seg, @"(" + Num + @")\s*≤\s*V[^≤]*≤\s*(" + Num + @")");
                var order = Regex.Match(seg, @"de l.ordre de\s*(" + Num + @")\s*m/s", RegexOptions.IgnoreCase);
                if (range.Success)
                {
                    specText = ZacSpecification.Range(Dec(range.Groups[1].Value), Dec(range.Groups[2].Value));
                    rest = seg[(range.Index + range.Length)..];
                }
                else if (order.Success)
                {
                    // « de l'ordre de 0,50 m/s » : valeur recherchée sans tolérance chiffrée, à valider manuellement.
                    specText = "≈ " + ZacSpecification.Format(Dec(order.Groups[1].Value));
                    rest = seg[(order.Index + order.Length)..];
                }
                if (specText is null) yield break;
                var result = Regex.Matches(rest, @"(" + Num + @")\s*m/s", RegexOptions.IgnoreCase);
                if (result.Count == 0) yield break;
                yield return Numeric(p.Name, result[^1].Groups[1].Value, "m/s", specText);
                yield break;
            }
            case Kind.Pressure:
            {
                var spec = Regex.Match(seg, @"(" + Num + @")\s*Pa\s*±\s*(" + Num + @")\s*Pa", RegexOptions.IgnoreCase);
                if (!spec.Success) yield break;
                var specText = ZacSpecification.Tolerance(Dec(spec.Groups[1].Value), Dec(spec.Groups[2].Value));
                var rest = seg[(spec.Index + spec.Length)..];

                // Une ou plusieurs valeurs (ex. « -22 -20 Pa Sur SAS Appro Sur SAS Perso »), suivies de la précision du point
                // de mesure. L'unité « Pa » n'est écrite qu'une fois.
                var values = new List<string>();
                var position = 0;
                var number = new Regex(@"^\s*'?\s*([-+−]?\s?\d+(?:[.,]\d+)?)(?:\s*Pa\b)?", RegexOptions.IgnoreCase);
                while (true)
                {
                    var m = number.Match(rest[position..]);
                    if (!m.Success) break;
                    values.Add(m.Groups[1].Value.Replace(" ", ""));
                    position += m.Length;
                }
                if (values.Count == 0) yield break;

                var labels = Regex.Matches(rest[position..], @"\b[Ss]ur\b(?:\s+(?!sur\b|[Ss]ur\b)[A-Za-zÀ-ÿ0-9()]+){0,3}")
                    .Select(m => m.Value.Trim()).ToList();
                for (var i = 0; i < values.Count; i++)
                {
                    var name = values.Count == 1 ? p.Name
                        : labels.Count == values.Count ? $"{p.Name} ({labels[i]})"
                        : $"{p.Name} ({i + 1})";
                    yield return Numeric(name, values[i], "Pa", specText);
                }
                yield break;
            }
            case Kind.Temperature:
            {
                var spec = Regex.Match(seg, @"(" + Num + @")\s*°\s*C\s*≤\s*T°?\s*≤\s*(" + Num + @")\s*°\s*C", RegexOptions.IgnoreCase);
                if (!spec.Success) yield break;
                var result = Regex.Matches(seg[(spec.Index + spec.Length)..], @"(" + Num + @")\s*°\s*C", RegexOptions.IgnoreCase);
                if (result.Count == 0) yield break;
                yield return Numeric(p.Name, result[0].Groups[1].Value, "°C",
                    ZacSpecification.Range(Dec(spec.Groups[1].Value), Dec(spec.Groups[2].Value)));
                yield break;
            }
            case Kind.Humidity:
            {
                var spec = Regex.Match(seg, @"Entre\s*(" + Num + @")\s*et\s*(" + Num + @")\s*%", RegexOptions.IgnoreCase);
                if (!spec.Success) yield break;
                var result = Regex.Match(seg[(spec.Index + spec.Length)..], @"(" + Num + @")\s*%");
                if (!result.Success) yield break;
                yield return Numeric(p.Name, result.Groups[1].Value, "% HR",
                    ZacSpecification.Range(Dec(spec.Groups[1].Value), Dec(spec.Groups[2].Value)));
                yield break;
            }
        }
    }

    private static decimal Dec(string s) =>
        decimal.Parse(s.Replace(" ", "").Replace('−', '-').Replace(',', '.').TrimStart('+'), NumberStyles.Float, CultureInfo.InvariantCulture);

    private static decimal Sum(string s) => s.Split('+').Sum(x => Dec(x.Trim()));

    private static ImportedMeasurement Numeric(string name, string result, string unit, string spec) =>
        new(name, Dec(result), null, unit, spec);

    /// <summary>Résultat éventuellement précédé de « &lt; » (« &lt; 2 min ») : conservé en texte, comparé numériquement.</summary>
    private static ImportedMeasurement WithPrefix(string name, Match result, string unit, string spec) =>
        result.Groups[1].Success
            ? new ImportedMeasurement(name, null, "< " + Dec(result.Groups[2].Value).ToString("0.###", CultureInfo.CurrentCulture), unit, spec)
            : new ImportedMeasurement(name, Dec(result.Groups[2].Value), null, unit, spec);
}
