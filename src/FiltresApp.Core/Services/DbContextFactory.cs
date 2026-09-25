using FiltresApp.Core.Data;
using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Services;

public class DbContextFactory
{
    private readonly string _dbPath;
    private readonly bool _readOnly;

    public DbContextFactory(string dbPath, bool readOnly = false)
    {
        _dbPath = dbPath;
        _readOnly = readOnly;
        var dir = Path.GetDirectoryName(dbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    }

    public FiltresDbContext Create() => new(_dbPath, _readOnly);

    public void EnsureDatabaseCreated()
    {
        // Mise à jour du schéma réservée au poste rédacteur (voir DbWriteLock).
        if (_readOnly) return;

        using var ctx = Create();
        // EnsureCreated() ne crée le schéma complet qu'au tout premier lancement (fichier SQLite
        // inexistant) : sur une base déjà existante, il ne fait rien, même si le modèle EF Core a
        // évolué depuis (l'application n'utilise pas `dotnet ef migrations`, voir README). La table de
        // liaison "OrderLinePeriodicFilters" (rattachement manuel filtre <-> ligne de commande) a été
        // ajoutée après la mise en production initiale : on la crée donc explicitement ici si elle
        // n'existe pas encore, par un simple `CREATE TABLE IF NOT EXISTS`, qui ne touche à aucune des
        // tables/données déjà en base.
        ctx.Database.EnsureCreated();
        EnsureOrderLinePeriodicFilterTable(ctx);
        EnsureInventoryOrderMergeSchema(ctx);
        EnsureOrderLineDestinationColumn(ctx);
        MigrateInventoryLinesIntoOrderLines(ctx);
        EnsureK7FamilySchema(ctx);
        K7FamilyReconstructionService.ReconstructIfNeeded(ctx);
        RemovePourDevisAndRefacturingData(ctx);
        EnsureChangedEvery15DaysColumn(ctx);
    }

    /// <summary>Colonne "Destination" des écrans Commande et Inventaire, vide pour les lignes existantes.</summary>
    private static void EnsureOrderLineDestinationColumn(FiltresDbContext ctx)
    {
        var cols = GetColumns(ctx, "OrderLines");
        if (!cols.Contains("Destination"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "OrderLines" ADD COLUMN "Destination" TEXT NULL;""");
    }

    /// <summary>Option "Changé tous les 15 jours" (G4 plissé, voir README) : ajoute à "PeriodicFilters"
    /// la colonne booléenne correspondante, à 0 (false) par défaut pour toutes les lignes existantes,
    /// sans toucher aux données déjà en base.</summary>
    private static void EnsureChangedEvery15DaysColumn(FiltresDbContext ctx)
    {
        var cols = GetColumns(ctx, "PeriodicFilters");
        if (!cols.Contains("ChangedEvery15Days"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "PeriodicFilters" ADD COLUMN "ChangedEvery15Days" INTEGER NOT NULL DEFAULT 0;""");
    }

    /// <summary>Suppression définitive de "pour devis" et "filtres à refacturer" (voir README, section
    /// "Suppression définitive de « pour devis » et « filtres à refacturer »"), demandée explicitement
    /// par l'utilisateur le 25/09/2026 : données ET structure. Déjà exécutée une fois sur la base de
    /// production réelle via un script ponctuel ; ce nettoyage est reproduit ici (idempotent, ne fait
    /// rien si déjà appliqué) uniquement en filet de sécurité pour toute copie de la base antérieure à
    /// cette date (ex. une sauvegarde restaurée) qui contiendrait encore ces données/cette table.
    /// L'entité et le DbSet <c>RefacturingLine</c> n'existent plus dans le code : on ne peut donc plus
    /// utiliser EF Core pour cette table, uniquement du SQL brut.</summary>
    private static void RemovePourDevisAndRefacturingData(FiltresDbContext ctx)
    {
        var cols = GetColumns(ctx, "OrderLines");
        if (cols.Count > 0)
        {
            // OrderDocumentType.PourDevis valait 1 (CommandeChmy = 0) avant sa suppression de l'enum.
            ctx.Database.ExecuteSqlRaw("""DELETE FROM "OrderLinePeriodicFilters" WHERE "OrderLineId" IN (SELECT "Id" FROM "OrderLines" WHERE "DocumentType" = 1);""");
            ctx.Database.ExecuteSqlRaw("""DELETE FROM "OrderLines" WHERE "DocumentType" = 1;""");
        }
        ctx.Database.ExecuteSqlRaw("""DROP TABLE IF EXISTS "RefacturingLines";""");
    }

    private static HashSet<string> GetColumns(FiltresDbContext ctx, string table)
    {
        var cols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var conn = ctx.Database.GetDbConnection();
        var mustClose = conn.State != System.Data.ConnectionState.Open;
        if (mustClose) conn.Open();
        try
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info(\"{table}\")";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) cols.Add(reader.GetString(1));
        }
        finally
        {
            if (mustClose) conn.Close();
        }
        return cols;
    }

    /// <summary>Fusion Inventaire / Commande chmy (voir README) : ajoute à "OrderLines" les colonnes
    /// nécessaires à l'accueil des anciennes lignes "InventoryLine" ("Unite" et le marqueur technique
    /// "MigratedFromInventoryLineId"), sans toucher aux données existantes.</summary>
    private static void EnsureInventoryOrderMergeSchema(FiltresDbContext ctx)
    {
        var cols = GetColumns(ctx, "OrderLines");
        if (!cols.Contains("Unite"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "OrderLines" ADD COLUMN "Unite" TEXT NULL;""");
        if (!cols.Contains("MigratedFromInventoryLineId"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "OrderLines" ADD COLUMN "MigratedFromInventoryLineId" INTEGER NULL;""");
    }

    /// <summary>Migration de données (une fois, idempotente via "MigratedFromInventoryLineId") : copie
    /// chaque ligne "InventoryLine" pas encore migrée vers "OrderLines" (type CommandeChmy), pour que les
    /// écrans "Inventaire" et "Commande chmy" partagent désormais les mêmes lignes. La table
    /// "InventoryLines" d'origine n'est ni vidée ni modifiée : elle reste une copie de sauvegarde inerte.</summary>
    private static void MigrateInventoryLinesIntoOrderLines(FiltresDbContext ctx)
    {
        var alreadyMigrated = ctx.OrderLines
            .Where(o => o.MigratedFromInventoryLineId != null)
            .Select(o => o.MigratedFromInventoryLineId!.Value)
            .ToHashSet();

        var toMigrate = ctx.InventoryLines
            .Where(i => !alreadyMigrated.Contains(i.Id))
            .OrderBy(i => i.Ordre)
            .ToList();

        if (toMigrate.Count == 0) return;

        var nextOrdre = (ctx.OrderLines.Where(o => o.DocumentType == OrderDocumentType.CommandeChmy)
            .Select(o => (int?)o.Ordre).Max() ?? 0) + 1;

        foreach (var inv in toMigrate)
        {
            ctx.OrderLines.Add(new OrderLine
            {
                DocumentType = OrderDocumentType.CommandeChmy,
                Ordre = nextOrdre++,
                Designation = inv.Designation,
                Dimension = inv.Dimension,
                Quantite = inv.Quantite,
                Unite = inv.Unite,
                Notes = inv.Notes,
                MigratedFromInventoryLineId = inv.Id
            });
        }

        ctx.SaveChanges();
    }

    /// <summary>Familles K7 (voir README) : crée la table "K7Families" et ajoute à "K7Locations" les
    /// colonnes de rattachement, sans toucher aux données existantes.</summary>
    private static void EnsureK7FamilySchema(FiltresDbContext ctx)
    {
        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "K7Families" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_K7Families" PRIMARY KEY AUTOINCREMENT,
                "Nom" TEXT NOT NULL,
                "Periodicite" TEXT NOT NULL DEFAULT ''
            );
            """);

        var cols = GetColumns(ctx, "K7Locations");
        if (!cols.Contains("K7FamilyId"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "K7Locations" ADD COLUMN "K7FamilyId" INTEGER NULL;""");
        if (!cols.Contains("IsFamilyHeader"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "K7Locations" ADD COLUMN "IsFamilyHeader" INTEGER NOT NULL DEFAULT 0;""");

        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_K7Locations_K7FamilyId" ON "K7Locations" ("K7FamilyId");""");
    }

    private static void EnsureOrderLinePeriodicFilterTable(FiltresDbContext ctx)
    {
        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "OrderLinePeriodicFilters" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_OrderLinePeriodicFilters" PRIMARY KEY AUTOINCREMENT,
                "OrderLineId" INTEGER NOT NULL,
                "PeriodicFilterId" INTEGER NOT NULL,
                CONSTRAINT "FK_OrderLinePeriodicFilters_OrderLines_OrderLineId" FOREIGN KEY ("OrderLineId") REFERENCES "OrderLines" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_OrderLinePeriodicFilters_PeriodicFilters_PeriodicFilterId" FOREIGN KEY ("PeriodicFilterId") REFERENCES "PeriodicFilters" ("Id") ON DELETE CASCADE
            );
            """);
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_OrderLinePeriodicFilters_OrderLineId" ON "OrderLinePeriodicFilters" ("OrderLineId");""");
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_OrderLinePeriodicFilters_PeriodicFilterId" ON "OrderLinePeriodicFilters" ("PeriodicFilterId");""");
    }
}
