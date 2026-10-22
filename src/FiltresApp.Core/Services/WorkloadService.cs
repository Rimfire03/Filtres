using FiltresApp.Core.Data;
using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Services;

/// <summary>
/// Charge de travail mensuelle des filtres périodiques : chaque filtre à changer un mois compte pour le coefficient de
/// sa vue (<see cref="PeriodicView.LoadCoefficient"/>, Paramètres, « Calcul de charge »). Toutes les vues du site sont
/// cumulées : le coefficient sert à pondérer des vues de poids différent (un filtre G3 est plus long à changer qu'un G4).
/// </summary>
public static class WorkloadService
{
    public const double MinCoefficient = 0.1;
    public const double MaxCoefficient = 10.0;
    public const double Step = 0.1;

    /// <summary>Coefficient ramené à un multiple de 0,1 entre 0,1 et 10 (évite les écarts d'arrondi de la virgule flottante).</summary>
    public static double Clamp(double value) => Math.Round(Math.Clamp(value, MinCoefficient, MaxCoefficient), 1);

    // ---- Coefficient de correction de chaque mois (en %) ----

    public const double MinMonthPercent = 10;
    public const double MaxMonthPercent = 200;
    public const double MonthPercentStep = 5;
    private const string MonthPercentKey = "WorkloadMonthPercent";

    public static double ClampMonthPercent(double value) => Math.Round(Math.Clamp(value, MinMonthPercent, MaxMonthPercent));

    /// <summary>Coefficient de correction de chaque mois en pourcentage (index 0 = janvier), 100 par défaut. Un mois à 80 %
    /// a une capacité réduite : une charge égale à 80 % de la normale y est considérée comme une charge de 100 %. Stocké dans
    /// la table DbInfo (commun à tous les postes, sans changement de schéma).</summary>
    public static double[] MonthPercents(FiltresDbContext ctx)
    {
        var result = Enumerable.Repeat(100.0, 12).ToArray();
        try
        {
            var text = ServerSql.GetInfo(ctx, MonthPercentKey);
            if (string.IsNullOrWhiteSpace(text)) return result;
            var parts = text.Split(';');
            for (var i = 0; i < 12 && i < parts.Length; i++)
                if (double.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    result[i] = ClampMonthPercent(v);
        }
        catch
        {
            // Réglage illisible ou table absente : valeurs de base (100 %).
        }
        return result;
    }

    public static void SaveMonthPercents(FiltresDbContext ctx, IReadOnlyList<double> percents)
    {
        ServerSql.EnsureDbInfo(ctx);
        ServerSql.SetInfo(ctx, MonthPercentKey, string.Join(';', percents.Select(p => ClampMonthPercent(p).ToString(System.Globalization.CultureInfo.InvariantCulture))));
    }

    /// <summary>Charge d'un mois ramenée à sa capacité : charge / (coefficient du mois / 100).</summary>
    public static double Adjust(double load, double monthPercent) => monthPercent <= 0 ? load : load / (monthPercent / 100.0);

    /// <summary>Charge pondérée de chaque mois (index 0 = janvier), toutes vues confondues, corrigée du coefficient de chaque
    /// mois. <paramref name="excludedFilterId"/>
    /// écarte le filtre en cours d'édition (0 : aucun).</summary>
    public static double[] MonthlyLoad(FiltresDbContext ctx, int excludedFilterId = 0)
    {
        var coefficients = ctx.PeriodicViews.AsNoTracking().ToDictionary(v => v.Id, v => v.LoadCoefficient);
        var filters = ctx.PeriodicFilters.AsNoTracking()
            .Where(f => excludedFilterId == 0 || f.Id != excludedFilterId)
            .ToList();

        var load = new double[12];
        foreach (var f in filters)
        {
            var coefficient = f.PeriodicViewId is int id && coefficients.TryGetValue(id, out var c) ? c : 1.0;
            foreach (var month in f.GetPeriodicityMonths())
                if (month is >= 1 and <= 12) load[month - 1] += coefficient;
        }

        var percents = MonthPercents(ctx);
        for (var i = 0; i < 12; i++) load[i] = Adjust(load[i], percents[i]);
        return load;
    }
}
