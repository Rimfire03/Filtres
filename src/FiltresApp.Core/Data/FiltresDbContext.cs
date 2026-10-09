using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Data;

public class FiltresDbContext : DbContext
{
    private readonly DbTarget _target;
    private readonly bool _readOnly;

    // Version du serveur MariaDB/MySQL détectée une fois par serveur (la détection ouvre une connexion).
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ServerVersion> MySqlVersions = new();

    public FiltresDbContext(string dbPath, bool readOnly = false) : this(DbTarget.ForFile(dbPath), readOnly) { }

    public FiltresDbContext(DbTarget target, bool readOnly = false)
    {
        _target = target;
        _readOnly = readOnly;
    }

    public DatabaseProvider Provider => _target.Provider;

    /// <summary>Site affiché (module MultiSite) : toutes les lectures, modifications et suppressions des tables propres à un site sont limitées à ce site (filtre de requête global), et les lignes ajoutées lui sont rattachées. Modifiable à tout moment ; 1 = site principal.</summary>
    public int CurrentSiteId { get; set; } = 1;

    public DbSet<Site> Sites => Set<Site>();
    public DbSet<PeriodicFilter> PeriodicFilters => Set<PeriodicFilter>();
    public DbSet<FilterReplacement> FilterReplacements => Set<FilterReplacement>();
    public DbSet<K7Location> K7Locations => Set<K7Location>();
    public DbSet<K7Family> K7Families => Set<K7Family>();
    public DbSet<InventoryLine> InventoryLines => Set<InventoryLine>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderLinePeriodicFilter> OrderLinePeriodicFilters => Set<OrderLinePeriodicFilter>();
    public DbSet<SharedAsset> SharedAssets => Set<SharedAsset>();
    public DbSet<FilterVariety> FilterVarieties => Set<FilterVariety>();
    public DbSet<PeriodicView> PeriodicViews => Set<PeriodicView>();
    public DbSet<MenuEntry> MenuEntries => Set<MenuEntry>();
    public DbSet<DynamicFilter> DynamicFilters => Set<DynamicFilter>();
    public DbSet<DynamicFilterReplacement> DynamicFilterReplacements => Set<DynamicFilterReplacement>();
    public DbSet<DynamicFilterFamily> DynamicFilterFamilies => Set<DynamicFilterFamily>();
    public DbSet<OrderLineDynamicFilter> OrderLineDynamicFilters => Set<OrderLineDynamicFilter>();
    public DbSet<RowColor> RowColors => Set<RowColor>();
    public DbSet<Belt> Belts => Set<Belt>();
    public DbSet<BeltReplacement> BeltReplacements => Set<BeltReplacement>();
    public DbSet<BeltFamily> BeltFamilies => Set<BeltFamily>();
    public DbSet<BearingUnit> BearingUnits => Set<BearingUnit>();
    public DbSet<BearingReplacement> BearingReplacements => Set<BearingReplacement>();
    public DbSet<BearingFamily> BearingFamilies => Set<BearingFamily>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Journal SQLite par défaut (DELETE) volontairement conservé : le mode WAL ne fonctionne pas sur
        // un disque réseau. Le délai d'attente couvre les lectures qui tombent pendant une écriture.
        switch (_target.Provider)
        {
            case DatabaseProvider.Sqlite:
                var connectionString = new SqliteConnectionStringBuilder
                {
                    DataSource = _target.FilePath,
                    Mode = _readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
                    DefaultTimeout = 30
                }.ToString();
                optionsBuilder.UseSqlite(connectionString);
                break;

            case DatabaseProvider.PostgreSql:
                optionsBuilder.UseNpgsql(_target.Server!.BuildConnectionString());
                break;

            case DatabaseProvider.MariaDb:
                var mysql = _target.Server!.BuildConnectionString();
                optionsBuilder.UseMySql(mysql, MySqlVersions.GetOrAdd(mysql, ServerVersion.AutoDetect));
                break;

            case DatabaseProvider.SqlServer:
                optionsBuilder.UseSqlServer(_target.Server!.BuildConnectionString());
                break;
        }

        // Contexte partagé pour toute la session : sans suivi, chaque requête relit la base et voit donc
        // les modifications enregistrées entre-temps par le poste rédacteur.
        if (_readOnly) optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        // Serveur : pas de mode lecture seule natif comme SQLite, donc toute écriture est refusée par un intercepteur.
        if (_readOnly && _target.IsServer) optionsBuilder.AddInterceptors(ReadOnlyGuard.Instance);
    }

    // Suivi de la dernière saisie sur serveur (pas de déclencheurs SQLite) : voir DatabaseWriteTracking.
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (_readOnly && _target.IsServer) throw ReadOnlyGuard.Error();

        foreach (var entry in ChangeTracker.Entries<ISiteScoped>())
            if (entry.State == EntityState.Added && entry.Entity.SiteId == 0) entry.Entity.SiteId = CurrentSiteId;

        var changed = Provider != DatabaseProvider.Sqlite && ChangeTracker.HasChanges();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        if (changed && result > 0) DatabaseWriteTracking.StampServer(this);
        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SharedAsset>().HasKey(a => a.Key);

        modelBuilder.Entity<OrderLineDynamicFilter>()
            .HasOne(l => l.OrderLine)
            .WithMany(o => o.DynamicLinks)
            .HasForeignKey(l => l.OrderLineId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrderLineDynamicFilter>()
            .HasOne(l => l.DynamicFilter)
            .WithMany()
            .HasForeignKey(l => l.DynamicFilterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DynamicFilter>()
            .HasOne(f => f.Variety)
            .WithMany()
            .HasForeignKey(f => f.VarietyId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DynamicFilter>()
            .HasOne(f => f.Family)
            .WithMany()
            .HasForeignKey(f => f.DynamicFilterFamilyId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<DynamicFilter>()
            .HasMany(f => f.Replacements)
            .WithOne(r => r.DynamicFilter)
            .HasForeignKey(r => r.DynamicFilterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DynamicFilterFamily>()
            .HasOne(f => f.Variety)
            .WithMany()
            .HasForeignKey(f => f.VarietyId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PeriodicFilter>()
            .HasMany(f => f.Replacements)
            .WithOne(r => r.PeriodicFilter)
            .HasForeignKey(r => r.PeriodicFilterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrderLinePeriodicFilter>()
            .HasOne(l => l.OrderLine)
            .WithMany(o => o.FilterLinks)
            .HasForeignKey(l => l.OrderLineId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MenuEntry>().HasKey(m => m.Key);

        modelBuilder.Entity<PeriodicFilter>()
            .HasOne(f => f.PeriodicView)
            .WithMany()
            .HasForeignKey(f => f.PeriodicViewId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<OrderLinePeriodicFilter>()
            .HasOne(l => l.PeriodicFilter)
            .WithMany()
            .HasForeignKey(l => l.PeriodicFilterId)
            .OnDelete(DeleteBehavior.Cascade);

        // Rattachement K7Location -> K7Family : Restrict côté base (filet de sécurité), la suppression
        // d'une famille encore utilisée est de toute façon bloquée en amont par K7ListViewModel avec un
        // message clair (voir README, section "Familles K7 et migration").
        modelBuilder.Entity<K7Location>()
            .HasOne(l => l.Family)
            .WithMany(f => f.Locations)
            .HasForeignKey(l => l.K7FamilyId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Belt>()
            .HasOne(b => b.Family)
            .WithMany()
            .HasForeignKey(b => b.BeltFamilyId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Belt>()
            .HasMany(b => b.Replacements)
            .WithOne(r => r.Belt)
            .HasForeignKey(r => r.BeltId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<BearingUnit>()
            .HasOne(b => b.Family)
            .WithMany()
            .HasForeignKey(b => b.BearingFamilyId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<BearingUnit>()
            .HasMany(b => b.Replacements)
            .WithOne(r => r.BearingUnit)
            .HasForeignKey(r => r.BearingUnitId)
            .OnDelete(DeleteBehavior.Cascade);

        // MultiSite : chaque table propre à un site est limitée au site courant du contexte.
        foreach (var type in modelBuilder.Model.GetEntityTypes().Select(e => e.ClrType).Where(t => typeof(ISiteScoped).IsAssignableFrom(t)).ToList())
        {
            var e = System.Linq.Expressions.Expression.Parameter(type, "e");
            var filter = System.Linq.Expressions.Expression.Lambda(
                System.Linq.Expressions.Expression.Equal(
                    System.Linq.Expressions.Expression.Property(e, nameof(ISiteScoped.SiteId)),
                    System.Linq.Expressions.Expression.Property(System.Linq.Expressions.Expression.Constant(this), nameof(CurrentSiteId))),
                e);
            modelBuilder.Entity(type).HasQueryFilter(filter);
        }
    }
}

/// <summary>Refuse toute commande d'écriture sur un poste en lecture seule connecté à un serveur de base de données
/// (voir <see cref="ServerWriteLock"/>).</summary>
internal sealed class ReadOnlyGuard : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
{
    public static readonly ReadOnlyGuard Instance = new();

    public static InvalidOperationException Error() =>
        new("Ce poste est en lecture seule : un autre poste a l'accès en écriture à la base. Aucune modification n'a été enregistrée.");

    public override Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> NonQueryExecuting(
        System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result) => throw Error();

    public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> NonQueryExecutingAsync(
        System.Data.Common.DbCommand command, Microsoft.EntityFrameworkCore.Diagnostics.CommandEventData eventData,
        Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw Error();
}