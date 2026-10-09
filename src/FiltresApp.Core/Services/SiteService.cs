using FiltresApp.Core.Data;
using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Services;

/// <summary>Gestion des sites du module MultiSite : création (avec les quatre vues d'origine, vides), renommage et
/// suppression avec nettoyage complet des données du site.</summary>
public static class SiteService
{
    public static List<Site> GetSites(FiltresDbContext db) =>
        db.Sites.AsNoTracking().OrderBy(s => s.Ordre).ThenBy(s => s.Id).ToList();

    /// <summary>Le nom est déjà pris par un autre site (sans tenir compte de la casse).</summary>
    public static bool NameExists(FiltresDbContext db, string name, int? excludedId = null)
    {
        var lower = name.Trim().ToLower();
        return db.Sites.AsNoTracking().Any(s => s.Id != (excludedId ?? -1) && s.Nom.ToLower() == lower);
    }

    /// <summary>Crée un site vide : aucune donnée, mais les quatre vues d'origine du menu « Changement filtre
    /// périodique » (G4 plissés, G4 plan, G3, Charbon), comme sur une installation existante.</summary>
    public static Site Create(FiltresDbContext db, string name)
    {
        var site = new Site { Nom = name.Trim(), Ordre = (db.Sites.Max(s => (int?)s.Ordre) ?? -1) + 1 };
        db.Sites.Add(site);
        db.SaveChanges();

        var builtIns = new (FilterCategory Category, string Nom, string Icon, string Label)[]
        {
            (FilterCategory.G4Plisse, "Filtres G4 plissés", "🟦", "Nom de la centrale d'air"),
            (FilterCategory.G4Plan, "Filtres G4 plan", "🟦", "Emplacement de l'appareil"),
            (FilterCategory.G3, "Filtres G3", "🟩", "Emplacement de l'appareil"),
            (FilterCategory.Charbon, "Charbon", "⬛", "Emplacement de l'appareil")
        };
        for (var i = 0; i < builtIns.Length; i++)
        {
            var b = builtIns[i];
            db.PeriodicViews.Add(new PeriodicView
            {
                SiteId = site.Id, Nom = b.Nom, Icon = b.Icon, Ordre = i, LocationLabel = b.Label, Category = b.Category
            });
        }
        db.SaveChanges();
        db.ChangeTracker.Clear();
        return site;
    }

    public static void Rename(FiltresDbContext db, int siteId, string name)
    {
        var site = db.Sites.First(s => s.Id == siteId);
        site.Nom = name.Trim();
        db.SaveChanges();
    }

    /// <summary>Supprime le site et TOUTES ses données (vues, filtres, historique, Liste K7, Inventaire, Commande,
    /// courroies, roulements...), dans une seule transaction : en cas d'erreur rien n'est supprimé. Les autres sites
    /// et les Paramètres ne sont pas touchés. Retourne le nombre de lignes supprimées.</summary>
    public static int Delete(FiltresDbContext db, int siteId)
    {
        if (db.Sites.Count() <= 1) throw new InvalidOperationException("Le dernier site ne peut pas être supprimé.");

        var scoped = db.Model.GetEntityTypes()
            .Where(e => typeof(ISiteScoped).IsAssignableFrom(e.ClrType))
            .ToList();
        // Enfants d'abord : l'inverse de l'ordre de copie (voir DatabaseCopier).
        var ordered = DatabaseCopier.OrderForCopy(scoped);
        ordered.Reverse();

        var removed = 0;
        using var tx = db.Database.BeginTransaction();
        foreach (var type in ordered)
        {
            var method = typeof(SiteService).GetMethod(nameof(DeleteRows), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                .MakeGenericMethod(type.ClrType);
            removed += (int)method.Invoke(null, new object[] { db, siteId })!;
        }
        db.Sites.Where(s => s.Id == siteId).ExecuteDelete();
        tx.Commit();
        db.ChangeTracker.Clear();

        // SQLite : rend l'espace libéré au fichier (hors transaction).
        if (db.Provider == DatabaseProvider.Sqlite)
        {
            try { db.Database.ExecuteSqlRaw("VACUUM"); } catch { /* optionnel */ }
        }
        return removed;
    }

    private static int DeleteRows<T>(FiltresDbContext db, int siteId) where T : class =>
        db.Set<T>().IgnoreQueryFilters().Where(e => EF.Property<int>(e, nameof(ISiteScoped.SiteId)) == siteId).ExecuteDelete();
}
