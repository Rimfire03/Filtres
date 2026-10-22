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

    /// <summary>Charge pondérée de chaque mois (index 0 = janvier), toutes vues confondues. <paramref name="excludedFilterId"/>
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
        return load;
    }
}
