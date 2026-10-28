using System.Globalization;
using FiltresApp.Core.Data;

namespace FiltresApp.Core.Services;

public enum OrderNeedMode
{
    /// <summary>Une commande par livraison choisie : chacune couvre jusqu'à la livraison suivante + une marge en mois.</summary>
    Delivery,

    /// <summary>Inventaire à chaque changement : le besoin couvre un changement + une marge en % d'un changement.</summary>
    EveryChange
}

/// <summary>Mode de calcul du besoin de commande, propre à chaque site (Paramètres, onglet « Commande »). Stocké dans la table
/// DbInfo sous une clé par site, sans changement de schéma. Les valeurs par défaut reproduisent le calcul d'origine (livraisons
/// en mars et septembre, 2 mois de marge). <see cref="Deliveries"/> : mois (1-12) de livraison, triés, sans doublon, au moins un.</summary>
public sealed record OrderNeedSettings(OrderNeedMode Mode, int[] Deliveries, int SafetyMonths, int SafetyPercent)
{
    public static readonly OrderNeedSettings Default = new(OrderNeedMode.Delivery, [3, 9], 2, 20);

    /// <summary>Réglage du site courant, rechargé à l'ouverture de la base et à chaque changement de site (<see cref="Load"/>).</summary>
    public static OrderNeedSettings Current { get; private set; } = Default;

    private static string Key(FiltresDbContext ctx) => $"OrderNeed.{ctx.CurrentSiteId}";

    public static int[] NormalizeDeliveries(IEnumerable<int> months)
    {
        var result = months.Where(m => m is >= 1 and <= 12).Distinct().Order().ToArray();
        return result.Length == 0 ? Default.Deliveries : result;
    }

    public static void Load(FiltresDbContext ctx)
    {
        Current = Default;
        try
        {
            var p = ServerSql.GetInfo(ctx, Key(ctx))?.Split(';');
            if (p is { Length: 4 }
                && Enum.TryParse<OrderNeedMode>(p[0], out var mode)
                && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var months)
                && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
                Current = new OrderNeedSettings(mode,
                    NormalizeDeliveries(p[1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => int.TryParse(s, out var m) ? m : 0)),
                    Math.Clamp(months, 0, 6), Math.Clamp(percent, 0, 200));
        }
        catch
        {
            // Réglage illisible ou table absente : calcul d'origine.
        }
    }

    public static void Save(FiltresDbContext ctx, OrderNeedSettings value)
    {
        value = value with { Deliveries = NormalizeDeliveries(value.Deliveries) };
        ServerSql.EnsureDbInfo(ctx);
        ServerSql.SetInfo(ctx, Key(ctx), string.Join(';', value.Mode, string.Join(',', value.Deliveries), value.SafetyMonths, value.SafetyPercent));
        Current = value;
    }

    /// <summary>Mois (1-12) comptés pour la commande livrée en <paramref name="delivery"/> : du mois qui précède la livraison
    /// jusqu'à la livraison suivante, plus la marge en mois (12 mois au plus). Avec les valeurs d'origine : août à mars
    /// pour septembre, février à septembre pour mars.</summary>
    public int[] Window(int delivery)
    {
        var gaps = Deliveries.Where(d => d != delivery).Select(d => (d - delivery + 12) % 12).ToList();
        var gap = gaps.Count == 0 ? 12 : gaps.Min();
        var length = Math.Min(12, gap + SafetyMonths);
        return Enumerable.Range(0, length).Select(i => (delivery - 2 + i + 12) % 12 + 1).ToArray();
    }

    /// <summary>Un en-tête de colonne « Besoin » par besoin calculé : un par livraison, ou un seul en mode inventaire à chaque changement.</summary>
    public string[] NeedHeaders() => Mode == OrderNeedMode.EveryChange
        ? ["Besoin (calculé)"]
        : Deliveries.Select(d => $"Besoin {MonthNames[d - 1]} (calculé)").ToArray();

    private static readonly string[] MonthNames =
        ["janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"];
}
