using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Data;

public class FiltresDbContext : DbContext
{
    private readonly string _dbPath;

    public FiltresDbContext(string dbPath)
    {
        _dbPath = dbPath;
    }

    public DbSet<PeriodicFilter> PeriodicFilters => Set<PeriodicFilter>();
    public DbSet<FilterReplacement> FilterReplacements => Set<FilterReplacement>();
    public DbSet<OpacimetricFilter> OpacimetricFilters => Set<OpacimetricFilter>();
    public DbSet<OpacimetricReplacement> OpacimetricReplacements => Set<OpacimetricReplacement>();
    public DbSet<K7Location> K7Locations => Set<K7Location>();
    public DbSet<K7Family> K7Families => Set<K7Family>();
    public DbSet<InventoryLine> InventoryLines => Set<InventoryLine>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();
    public DbSet<OrderLinePeriodicFilter> OrderLinePeriodicFilters => Set<OrderLinePeriodicFilter>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseSqlite($"Data Source={_dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PeriodicFilter>()
            .HasMany(f => f.Replacements)
            .WithOne(r => r.PeriodicFilter)
            .HasForeignKey(r => r.PeriodicFilterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OpacimetricFilter>()
            .HasMany(f => f.Replacements)
            .WithOne(r => r.OpacimetricFilter)
            .HasForeignKey(r => r.OpacimetricFilterId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrderLinePeriodicFilter>()
            .HasOne(l => l.OrderLine)
            .WithMany(o => o.FilterLinks)
            .HasForeignKey(l => l.OrderLineId)
            .OnDelete(DeleteBehavior.Cascade);

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
    }
}
