using FiltresApp.Core.Data;
using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

    /// <summary>Migrations du schéma, dans l'ordre. La version atteinte est enregistrée dans la base
    /// (<c>PRAGMA user_version</c>) : au lancement, le poste rédacteur applique uniquement celles qui
    /// manquent. Règles : ne jamais modifier ni renuméroter une migration publiée, toujours en ajouter
    /// une nouvelle à la fin, et écrire les données en SQL brut (le modèle EF évolue, pas la migration).
    /// Les migrations 1 à 5 reprennent les mises à jour appliquées avant l'existence de ce système ;
    /// elles sont idempotentes car les bases existantes les ont déjà (en partie) reçues.</summary>
    private static readonly (int Version, string Description, Action<FiltresDbContext> Apply)[] Migrations =
    {
        (1, "Rattachement filtres / lignes de commande", EnsureOrderLinePeriodicFilterTable),
        (2, "Fusion Inventaire / Commande", ctx =>
        {
            EnsureInventoryOrderMergeSchema(ctx);
            MigrateInventoryLinesIntoOrderLines(ctx);
        }),
        (3, "Familles K7", ctx =>
        {
            EnsureK7FamilySchema(ctx);
            K7FamilyReconstructionService.ReconstructIfNeeded(ctx);
        }),
        (4, "Suppression « pour devis » et « filtres à refacturer »", RemovePourDevisAndRefacturingData),
        (5, "Option « changé tous les 15 jours »", EnsureChangedEvery15DaysColumn),
        (6, "Colonne Destination (Commande / Inventaire)", EnsureOrderLineDestinationColumn),
        (7, "Suppression de la colonne Unité (Inventaire) et de son contenu", DropOrderLineUniteColumn),
        (8, "Colonne Inventaire (écran Inventaire)", AddOrderLineInventaireColumn),
        (9, "Familles des écrans Inventaire et Commande", AddOrderFamilies),
    };

    /// <summary>Version de base attendue par cette version de l'application.</summary>
    public static int LatestVersion => Migrations[^1].Version;

    /// <summary>Libellés des migrations à appliquer depuis <paramref name="fromVersion"/>, pour la demande
    /// de confirmation.</summary>
    public static IReadOnlyList<string> PendingMigrations(int fromVersion) =>
        Migrations.Where(m => m.Version > fromVersion).Select(m => $"v{m.Version} : {m.Description}").ToList();

    /// <summary>Version actuelle du fichier de base (0 = base antérieure au système de version).</summary>
    public int GetDatabaseVersion()
    {
        using var ctx = Create();
        return ReadVersion(ctx);
    }

    /// <summary>Version de l'application qui a mis la base à jour en dernier, pour les messages d'erreur.</summary>
    public string? GetLastMigratedByAppVersion()
    {
        using var ctx = Create();
        if (!GetColumns(ctx, "DbInfo").Contains("Value")) return null;
        return ctx.Database.SqlQueryRaw<string>("""SELECT "Value" AS "Value" FROM "DbInfo" WHERE "Key" = 'AppVersion'""").FirstOrDefault();
    }

    /// <summary>Crée la base si besoin, sinon applique les migrations manquantes (sauvegarde préalable du
    /// fichier). Réservé au poste rédacteur (voir DbWriteLock).</summary>
    public void EnsureDatabaseUpToDate(string appVersion)
    {
        if (_readOnly) return;

        using var ctx = Create();
        if (ctx.Database.EnsureCreated())
        {
            // Base neuve : EnsureCreated vient de créer directement le schéma le plus récent.
            WriteVersion(ctx, LatestVersion, appVersion);
            return;
        }

        var current = ReadVersion(ctx);
        var pending = Migrations.Where(m => m.Version > current).ToList();
        if (pending.Count == 0) return;

        Backup(ctx, current);
        var reached = current;
        foreach (var migration in pending)
        {
            using var transaction = ctx.Database.BeginTransaction();
            try
            {
                migration.Apply(ctx);
                WriteVersion(ctx, migration.Version, appVersion);
                transaction.Commit();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Échec de la mise à jour de la base vers la version {migration.Version} ({migration.Description}) : {ex.Message}\n" +
                    $"La base est restée en version {reached} ; une sauvegarde faite juste avant la mise à jour se trouve à côté du fichier.", ex);
            }
            reached = migration.Version;
            ctx.ChangeTracker.Clear();
        }
    }

    private static int ReadVersion(FiltresDbContext ctx) =>
        ctx.Database.SqlQueryRaw<int>("SELECT user_version AS \"Value\" FROM pragma_user_version").First();

    private static void WriteVersion(FiltresDbContext ctx, int version, string appVersion)
    {
        // PRAGMA n'accepte pas de paramètre ; version est un entier issu du code.
        ctx.Database.ExecuteSqlRaw("PRAGMA user_version = " + version + ";");
        ctx.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "DbInfo" ("Key" TEXT NOT NULL PRIMARY KEY, "Value" TEXT NOT NULL);""");
        ctx.Database.ExecuteSqlRaw("""INSERT OR REPLACE INTO "DbInfo" ("Key", "Value") VALUES ('AppVersion', {0});""", appVersion);
    }

    /// <summary>Copie cohérente de la base (VACUUM INTO) avant toute migration, à côté du fichier.</summary>
    private void Backup(FiltresDbContext ctx, int fromVersion)
    {
        var backupPath = $"{_dbPath}.avant-maj-v{fromVersion}-{DateTime.Now:yyyyMMdd-HHmmss}.bak";
        ctx.Database.ExecuteSqlRaw("VACUUM INTO {0};", backupPath);
    }

    private static void AddOrderFamilies(FiltresDbContext ctx)
    {
        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "OrderFamilies" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_OrderFamilies" PRIMARY KEY AUTOINCREMENT,
                "Nom" TEXT NOT NULL
            );
            """);
        if (!GetColumns(ctx, "OrderLines").Contains("OrderFamilyId"))
            ctx.Database.ExecuteSqlRaw(
                """ALTER TABLE "OrderLines" ADD COLUMN "OrderFamilyId" INTEGER NULL REFERENCES "OrderFamilies" ("Id") ON DELETE SET NULL;""");
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_OrderLines_OrderFamilyId" ON "OrderLines" ("OrderFamilyId");""");
    }

    private static void AddOrderLineInventaireColumn(FiltresDbContext ctx)
    {
        if (!GetColumns(ctx, "OrderLines").Contains("Inventaire"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "OrderLines" ADD COLUMN "Inventaire" INTEGER NULL;""");
    }

    private static void DropOrderLineUniteColumn(FiltresDbContext ctx)
    {
        if (GetColumns(ctx, "OrderLines").Contains("Unite"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "OrderLines" DROP COLUMN "Unite";""");
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
            cmd.Transaction = ctx.Database.CurrentTransaction?.GetDbTransaction();
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

        // SQL brut et non ctx.OrderLines.Add : les colonnes ajoutées par les migrations suivantes
        // n'existent pas encore à ce stade.
        var documentType = (int)OrderDocumentType.CommandeChmy;
        foreach (var inv in toMigrate)
        {
            var ordre = nextOrdre++;
            ctx.Database.ExecuteSqlInterpolated($"""
                INSERT INTO "OrderLines" ("DocumentType", "Ordre", "Designation", "Dimension", "Quantite", "Unite", "Notes", "MigratedFromInventoryLineId")
                VALUES ({documentType}, {ordre}, {inv.Designation}, {inv.Dimension}, {inv.Quantite}, {inv.Unite}, {inv.Notes}, {inv.Id});
                """);
        }
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
