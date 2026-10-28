using System.Globalization;
using FiltresApp.Core.Data;

namespace FiltresApp.Core.Services;

public enum OrderNeedMode
{
    /// <summary>Deux commandes par an, livrées aux mois choisis : le besoin couvre jusqu'à la livraison suivante + une marge en mois.</summary>
    Delivery,

    /// <summary>Inventaire à chaque changement : le besoin couvre un changement + une marge en % d'un changement.</summary>
    EveryChange
}

/// <summary>Mode de calcul du besoin de commande, propre à chaque site (Paramètres, onglet « Commande »). Stocké dans la table
/// DbInfo sous une clé par site, sans changement de schéma. Les valeurs par défaut reproduisent le calcul d'origine (livraisons
/// en mars et septembre, 2 mois de marge).</summary>
public sealed record OrderNeedSettings(OrderNeedMode Mode, int Delivery1, int Delivery2, int SafetyMonths, int SafetyPercent)
{
    public static readonly OrderNeedSettings Default = new(OrderNeedMode.Delivery, 3, 9, 2, 20);

    /// <summary>Réglage du site courant, rechargé à l'ouverture de la base et à chaque changement de site (<see cref="Load"/>).</summary>
    public static OrderNeedSettings Current { get; private set; } = Default;

    private static string Key(FiltresDbContext ctx) => $"OrderNeed.{ctx.CurrentSiteId}";

    public static void Load(FiltresDbContext ctx)
    {
        Current = Default;
        try
        {
            var p = ServerSql.GetInfo(ctx, Key(ctx))?.Split(';');
            if (p is { Length: 5 }
                && Enum.TryParse<OrderNeedMode>(p[0], out var mode)
                && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var d1)
                && int.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var d2)
                && int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var months)
                && int.TryParse(p[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out var percent))
                Current = new OrderNeedSettings(mode, Math.Clamp(d1, 1, 12), Math.Clamp(d2, 1, 12), Math.Clamp(months, 0, 6), Math.Clamp(percent, 0, 200));
        }
        catch
        {
            // Réglage illisible ou table absente : calcul d'origine.
        }
    }

    public static void Save(FiltresDbContext ctx, OrderNeedSettings value)
    {
        ServerSql.EnsureDbInfo(ctx);
        ServerSql.SetInfo(ctx, Key(ctx), string.Join(';', value.Mode, value.Delivery1, value.Delivery2, value.SafetyMonths, value.SafetyPercent));
        Current = value;
    }

    /// <summary>Mois (1-12) comptés pour la commande livrée en <paramref name="delivery"/> : du mois qui précède la livraison
    /// jusqu'à la livraison suivante (<paramref name="other"/>), plus la marge en mois. Avec les valeurs d'origine : août à mars
    /// pour septembre, février à septembre pour mars.</summary>
    public int[] Window(int delivery, int other)
    {
        var gap = (other - delivery + 12) % 12;
        if (gap == 0) gap = 12;
        var length = Math.Min(12, gap + SafetyMonths);
        return Enumerable.Range(0, length).Select(i => (delivery - 2 + i + 12) % 12 + 1).ToArray();
    }
}
