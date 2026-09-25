using System.Text.RegularExpressions;
using FiltresApp.Core.Data;
using FiltresApp.Core.Models;

namespace FiltresApp.Core.Services;

/// <summary>Reconstitue les familles K7 (<see cref="K7Family"/>) à partir du texte libre de la colonne
/// LIEU déjà présent en base, utilisée par <see cref="DbContextFactory"/> (migration ponctuelle de la base
/// existante, voir README).
/// <para>Dans le classeur Excel d'origine, une ligne "titre de famille" ressemble à
/// "AP  RDC periodicités  1/4/7/10" (nom + mot "periodicité(s)" + liste de mois séparés par "/") et
/// précède les lignes de détail de cette famille jusqu'à la prochaine ligne de titre. Les lignes de
/// titre elles-mêmes ne sont pas supprimées (aucune perte de donnée) : elles sont marquées
/// <see cref="K7Location.IsFamilyHeader"/> et masquées de la liste détaillée de leur famille dans
/// l'interface.</para></summary>
public static class K7FamilyReconstructionService
{
    private static readonly Regex HeaderRegex = new(
        @"^(?<name>.*?)\s*periodicit[ée]s?\s*(?<months>[\d/]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>N'agit que si aucune <see cref="K7Family"/> n'existe encore en base (idempotent) :
    /// n'écrase donc jamais un rattachement déjà fait/corrigé manuellement par l'utilisateur.</summary>
    public static void ReconstructIfNeeded(FiltresDbContext ctx)
    {
        if (ctx.K7Families.Local.Concat(ctx.K7Families).Any()) return;

        var locations = ctx.K7Locations.Local.Concat(ctx.K7Locations).Distinct().OrderBy(l => l.Id).ToList();
        if (locations.Count == 0) return;

        K7Family? currentFamily = null;
        K7Family? uncategorized = null;

        foreach (var loc in locations)
        {
            var match = HeaderRegex.Match(loc.Lieu ?? string.Empty);
            if (match.Success)
            {
                var months = Regex.Matches(match.Groups["months"].Value, @"\d+")
                    .Select(m => int.Parse(m.Value))
                    .Where(m => m is >= 1 and <= 12)
                    .Distinct();

                currentFamily = new K7Family
                {
                    Nom = match.Groups["name"].Value.Trim(),
                    Periodicite = K7Family.FormatPeriodicityMonths(months)
                };
                ctx.K7Families.Add(currentFamily);

                loc.Family = currentFamily;
                loc.IsFamilyHeader = true;
            }
            else if (currentFamily != null)
            {
                loc.Family = currentFamily;
            }
            else
            {
                // Aucun en-tête de famille rencontré avant ce lieu (ne devrait pas arriver avec les
                // données réelles, où la première ligne est déjà un en-tête) : famille générique de repli.
                uncategorized ??= new K7Family { Nom = "Non classé", Periodicite = "" };
                if (uncategorized.Id == 0 && !ctx.K7Families.Local.Contains(uncategorized)) ctx.K7Families.Add(uncategorized);
                loc.Family = uncategorized;
            }
        }

        ctx.SaveChanges();
    }
}
