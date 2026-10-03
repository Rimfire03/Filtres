using FiltresApp.Core.Models;
using FiltresApp.Core.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Data.Migrations;

/// <summary>Étapes de mise à jour du schéma, appliquées par <see cref="DbContextFactory.EnsureDatabaseUpToDate"/>.</summary>
internal static class DatabaseMigrations
{
    /// <summary>Migrations du schéma, dans l'ordre. La version atteinte est enregistrée dans la base
    /// (<c>PRAGMA user_version</c>) : au lancement, le poste rédacteur applique uniquement celles qui
    /// manquent. Règles : ne jamais modifier ni renuméroter une migration publiée, toujours en ajouter
    /// une nouvelle à la fin, et écrire les données en SQL brut (le modèle EF évolue, pas la migration).
    /// Les migrations 1 à 5 reprennent les mises à jour appliquées avant l'existence de ce système ;
    /// elles sont idempotentes car les bases existantes les ont déjà (en partie) reçues.</summary>
    internal static readonly (int Version, string Description, Action<FiltresDbContext> Apply)[] All =
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
        (10, "Colonne Type des filtres F7 à H13", AddOpacimetricFilterTypeColumn),
        (11, "Choix manuel de la famille (Inventaire / Commande)", AddOrderLineFamilyOverrideColumn),
        (12, "Besoin saisi (Commande, familles hors G4 / G3)", AddOrderLineManualNeedColumn),
        (13, "Logo de l'entreprise (stockage dans la base)", AddSharedAssetsTable),
        (14, "Familles des filtres F7 à H13", AddOpacimetricFamilies),
        (15, "Rattachement des filtres F7 à H13 à Commande / Inventaire", AddOrderLineOpacimetricFilters),
        (16, "Familles Commande / Inventaire : Type des filtres F7 à H13 au lieu de « Filtres F7 à H13 »", AddOrderLineFamilyOverrideTypeColumn),
        (17, "Suppression de la fonctionnalité Filtres F7 à H13", RemoveOpacimetricFeature),
        (18, "Menu dépliant « Filtres F7 à H14 » : variétés de filtres créées librement", AddDynamicFilterSchema),
        (19, "Colonne Commentaire (écrans de filtres)", AddFilterCommentaireColumns),
        (20, "Colorisation des lignes (filtres, K7, Commande, Inventaire)", AddRowColorSchema),
        (21, "Suppression du champ Notes (écrans de filtres, inutilisé hors édition)", RemoveFilterNotesColumns),
        (22, "Suppression du champ Référence Liste K7 (écran Filtres G3)", RemoveK7ReferenceColumn),
        (23, "Modules activables « Courroies » et « Roulements » (menu Paramètres)", AddBeltAndBearingSchema),
        (24, "Colonnes Type, Type de courroies et Réf. roulement en majuscules", UppercaseTypeAndReferenceColumns),
    };

    /// <summary>Le contenu des colonnes "Type" (filtres G4 / G3 / Charbon et F7 à H14), "Type de courroies"
    /// et "Réf. roulement" (avant / arrière / volute) est désormais toujours en majuscules (saisie convertie,
    /// voir EditField côté WPF) : les valeurs existantes sont converties ici. <c>UPPER()</c> de SQLite ne
    /// convertit que l'ASCII (un « é » resterait en minuscule) : conversion .NET via une fonction SQL
    /// enregistrée sur la connexion, ouverte pendant toute la migration (transaction).</summary>
    private static void UppercaseTypeAndReferenceColumns(FiltresDbContext ctx)
    {
        var connection = (SqliteConnection)ctx.Database.GetDbConnection();
        connection.CreateFunction("dotnet_upper", (string? value) => value?.ToUpperInvariant(), isDeterministic: true);

        ctx.Database.ExecuteSqlRaw("""UPDATE "PeriodicFilters" SET "MediaType" = dotnet_upper("MediaType");""");
        ctx.Database.ExecuteSqlRaw("""UPDATE "DynamicFilters" SET "FilterType" = dotnet_upper("FilterType") WHERE "FilterType" IS NOT NULL;""");
        ctx.Database.ExecuteSqlRaw("""UPDATE "Belts" SET "BeltType" = dotnet_upper("BeltType") WHERE "BeltType" IS NOT NULL;""");
        ctx.Database.ExecuteSqlRaw(
            """
            UPDATE "BearingUnits" SET
                "RefAvant" = dotnet_upper("RefAvant"),
                "RefArriere" = dotnet_upper("RefArriere"),
                "RefVolute" = dotnet_upper("RefVolute");
            """);
    }

    /// <summary>Modules "Courroies" et "Roulements" (menu Paramètres, activables indépendamment) : même
    /// principe que <see cref="DynamicFilter"/> (menu "Filtres F7 à H14" - familles créées à la main,
    /// historique de remplacements ponctuels sans périodicité mensuelle fixe), mais chacun est un module
    /// unique (pas de variétés multiples créées par l'utilisateur), donc sans la table "FilterVarieties"
    /// intermédiaire. "Roulements" a trois positions (avant/arrière/volute) : chaque enregistrement de
    /// remplacement précise lesquelles ont été changées ce jour-là (voir <see cref="BearingReplacement"/>).</summary>
    private static void AddBeltAndBearingSchema(FiltresDbContext ctx)
    {
        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "BeltFamilies" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BeltFamilies" PRIMARY KEY AUTOINCREMENT,
                "Nom" TEXT NOT NULL
            );
            """);

        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "Belts" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Belts" PRIMARY KEY AUTOINCREMENT,
                "Location" TEXT NOT NULL DEFAULT '',
                "BeltType" TEXT NULL,
                "QuantityInPlace" INTEGER NOT NULL DEFAULT 0,
                "Commentaire" TEXT NULL,
                "RowColorId" INTEGER NULL,
                "BeltFamilyId" INTEGER NULL,
                CONSTRAINT "FK_Belts_BeltFamilies_BeltFamilyId" FOREIGN KEY ("BeltFamilyId") REFERENCES "BeltFamilies" ("Id") ON DELETE SET NULL
            );
            """);
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_Belts_BeltFamilyId" ON "Belts" ("BeltFamilyId");""");

        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "BeltReplacements" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BeltReplacements" PRIMARY KEY AUTOINCREMENT,
                "BeltId" INTEGER NOT NULL,
                "QuantityChanged" INTEGER NOT NULL DEFAULT 0,
                "DateChanged" TEXT NULL,
                CONSTRAINT "FK_BeltReplacements_Belts_BeltId" FOREIGN KEY ("BeltId") REFERENCES "Belts" ("Id") ON DELETE CASCADE
            );
            """);
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_BeltReplacements_BeltId" ON "BeltReplacements" ("BeltId");""");

        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "BearingFamilies" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BearingFamilies" PRIMARY KEY AUTOINCREMENT,
                "Nom" TEXT NOT NULL
            );
            """);

        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "BearingUnits" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BearingUnits" PRIMARY KEY AUTOINCREMENT,
                "Location" TEXT NOT NULL DEFAULT '',
                "CentraleType" TEXT NOT NULL DEFAULT 'Courroies',
                "RefAvant" TEXT NULL,
                "RefArriere" TEXT NULL,
                "RefVolute" TEXT NULL,
                "Commentaire" TEXT NULL,
                "RowColorId" INTEGER NULL,
                "BearingFamilyId" INTEGER NULL,
                CONSTRAINT "FK_BearingUnits_BearingFamilies_BearingFamilyId" FOREIGN KEY ("BearingFamilyId") REFERENCES "BearingFamilies" ("Id") ON DELETE SET NULL
            );
            """);
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_BearingUnits_BearingFamilyId" ON "BearingUnits" ("BearingFamilyId");""");

        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "BearingReplacements" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_BearingReplacements" PRIMARY KEY AUTOINCREMENT,
                "BearingUnitId" INTEGER NOT NULL,
                "DateChanged" TEXT NULL,
                "ChangedAvant" INTEGER NOT NULL DEFAULT 0,
                "ChangedArriere" INTEGER NOT NULL DEFAULT 0,
                "ChangedVolute" INTEGER NOT NULL DEFAULT 0,
                CONSTRAINT "FK_BearingReplacements_BearingUnits_BearingUnitId" FOREIGN KEY ("BearingUnitId") REFERENCES "BearingUnits" ("Id") ON DELETE CASCADE
            );
            """);
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_BearingReplacements_BearingUnitId" ON "BearingReplacements" ("BearingUnitId");""");
    }

    /// <summary>Le champ "Référence Liste K7" (écran Filtres G3) est retiré à la demande de l'utilisateur :
    /// édition, export Excel et modèle.</summary>
    private static void RemoveK7ReferenceColumn(FiltresDbContext ctx)
    {
        if (SchemaInspector.GetColumns(ctx, "PeriodicFilters").Contains("K7Reference"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "PeriodicFilters" DROP COLUMN "K7Reference";""");
    }

    /// <summary>Le champ "Notes" des filtres (PeriodicFilter/DynamicFilter) n'était accessible que depuis
    /// la fenêtre Ajouter/Modifier - jamais affiché en grille ni à l'impression - retiré à la demande de
    /// l'utilisateur. Sans rapport avec OrderLine.Notes ("Référence fournisseur", Commande/Inventaire),
    /// activement affiché, qui n'est pas touché.</summary>
    private static void RemoveFilterNotesColumns(FiltresDbContext ctx)
    {
        if (SchemaInspector.GetColumns(ctx, "PeriodicFilters").Contains("Notes"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "PeriodicFilters" DROP COLUMN "Notes";""");
        if (SchemaInspector.GetColumns(ctx, "DynamicFilters").Contains("Notes"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "DynamicFilters" DROP COLUMN "Notes";""");
    }

    /// <summary>Colorisation de ligne (clic droit) : palette de couleurs nommées réglée dans Paramètres
    /// (table "RowColors", partagée en base comme les familles K7), et colonne "RowColorId" sur chaque
    /// table dont les lignes peuvent être colorées.</summary>
    private static void AddRowColorSchema(FiltresDbContext ctx)
    {
        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "RowColors" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_RowColors" PRIMARY KEY AUTOINCREMENT,
                "Nom" TEXT NOT NULL,
                "Hex" TEXT NOT NULL DEFAULT '#FFFFFF',
                "Ordre" INTEGER NOT NULL DEFAULT 0
            );
            """);

        foreach (var table in new[] { "PeriodicFilters", "DynamicFilters", "K7Locations", "OrderLines" })
        {
            // Un nom de table ne peut pas être passé en paramètre SQL ; "table" vient uniquement de la
            // liste en dur ci-dessus (jamais d'une saisie), donc aucun risque d'injection.
#pragma warning disable EF1002
            if (!SchemaInspector.GetColumns(ctx, table).Contains("RowColorId"))
                ctx.Database.ExecuteSqlRaw($"""ALTER TABLE "{table}" ADD COLUMN "RowColorId" INTEGER NULL;""");
#pragma warning restore EF1002
        }
    }

    /// <summary>Colonne "Commentaire" affichée en dernière position sur tous les écrans de filtres (G4
    /// plissés, G4 plan, G3, Charbon, et chaque variété "Filtres F7 à H14") - pas sur Commande ni
    /// Inventaire.</summary>
    private static void AddFilterCommentaireColumns(FiltresDbContext ctx)
    {
        if (!SchemaInspector.GetColumns(ctx, "PeriodicFilters").Contains("Commentaire"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "PeriodicFilters" ADD COLUMN "Commentaire" TEXT NULL;""");
        if (!SchemaInspector.GetColumns(ctx, "DynamicFilters").Contains("Commentaire"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "DynamicFilters" ADD COLUMN "Commentaire" TEXT NULL;""");
    }

    /// <summary>Suppression définitive de la fonctionnalité "Filtres F7 à H13" (demandée explicitement par
    /// l'utilisateur), données ET structure : table des filtres, de leurs remplacements, de leurs familles,
    /// et de leur rattachement à Commande / Inventaire. Les lignes de Commande / Inventaire dont la famille
    /// était un Type F7 à H13 (FamilyOverrideType) repassent en famille automatique.</summary>
    private static void RemoveOpacimetricFeature(FiltresDbContext ctx)
    {
        if (SchemaInspector.GetColumns(ctx, "OrderLines").Contains("FamilyOverrideType"))
        {
            ctx.Database.ExecuteSqlRaw("""UPDATE "OrderLines" SET "FamilyOverride" = NULL WHERE "FamilyOverrideType" IS NOT NULL;""");
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "OrderLines" DROP COLUMN "FamilyOverrideType";""");
        }
        ctx.Database.ExecuteSqlRaw("""DROP TABLE IF EXISTS "OrderLineOpacimetricFilters";""");
        ctx.Database.ExecuteSqlRaw("""DROP TABLE IF EXISTS "OpacimetricReplacements";""");
        ctx.Database.ExecuteSqlRaw("""DROP TABLE IF EXISTS "OpacimetricFilters";""");
        ctx.Database.ExecuteSqlRaw("""DROP TABLE IF EXISTS "OpacimetricFamilies";""");
    }

    /// <summary>Menu dépliant "Filtres F7 à H14" (barre latérale) : variétés de filtres créées librement
    /// par l'utilisateur (<see cref="FilterVariety"/>), chacune avec ses propres filtres
    /// (<see cref="DynamicFilter"/>), familles (<see cref="DynamicFilterFamily"/>) et rattachement à
    /// Commande / Inventaire (<see cref="OrderLineDynamicFilter"/>). Généralisation de l'ancienne
    /// fonctionnalité "Filtres F7 à H13" (une seule variété codée en dur, supprimée en v17) à un nombre
    /// quelconque de variétés créées à la main. Réutilise le champ "FamilyOverrideType" (colonne texte
    /// libre) pour le choix manuel de famille par Type, comme l'ancienne fonctionnalité : cette colonne
    /// avait été supprimée en v17, elle est recréée ici avec le même rôle.</summary>
    private static void AddDynamicFilterSchema(FiltresDbContext ctx)
    {
        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "FilterVarieties" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_FilterVarieties" PRIMARY KEY AUTOINCREMENT,
                "Nom" TEXT NOT NULL,
                "Ordre" INTEGER NOT NULL DEFAULT 0
            );
            """);

        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "DynamicFilterFamilies" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_DynamicFilterFamilies" PRIMARY KEY AUTOINCREMENT,
                "VarietyId" INTEGER NOT NULL,
                "Nom" TEXT NOT NULL,
                CONSTRAINT "FK_DynamicFilterFamilies_FilterVarieties_VarietyId" FOREIGN KEY ("VarietyId") REFERENCES "FilterVarieties" ("Id") ON DELETE CASCADE
            );
            """);
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_DynamicFilterFamilies_VarietyId" ON "DynamicFilterFamilies" ("VarietyId");""");

        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "DynamicFilters" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_DynamicFilters" PRIMARY KEY AUTOINCREMENT,
                "VarietyId" INTEGER NOT NULL,
                "Location" TEXT NOT NULL DEFAULT '',
                "Dimension" TEXT NOT NULL DEFAULT '',
                "FilterType" TEXT NULL,
                "QuantityInPlace" INTEGER NOT NULL DEFAULT 0,
                "Notes" TEXT NULL,
                "DynamicFilterFamilyId" INTEGER NULL,
                CONSTRAINT "FK_DynamicFilters_FilterVarieties_VarietyId" FOREIGN KEY ("VarietyId") REFERENCES "FilterVarieties" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_DynamicFilters_DynamicFilterFamilies_DynamicFilterFamilyId" FOREIGN KEY ("DynamicFilterFamilyId") REFERENCES "DynamicFilterFamilies" ("Id") ON DELETE SET NULL
            );
            """);
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_DynamicFilters_VarietyId" ON "DynamicFilters" ("VarietyId");""");
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_DynamicFilters_DynamicFilterFamilyId" ON "DynamicFilters" ("DynamicFilterFamilyId");""");

        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "DynamicFilterReplacements" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_DynamicFilterReplacements" PRIMARY KEY AUTOINCREMENT,
                "DynamicFilterId" INTEGER NOT NULL,
                "QuantityChanged" INTEGER NOT NULL DEFAULT 0,
                "DateChanged" TEXT NULL,
                CONSTRAINT "FK_DynamicFilterReplacements_DynamicFilters_DynamicFilterId" FOREIGN KEY ("DynamicFilterId") REFERENCES "DynamicFilters" ("Id") ON DELETE CASCADE
            );
            """);
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_DynamicFilterReplacements_DynamicFilterId" ON "DynamicFilterReplacements" ("DynamicFilterId");""");

        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "OrderLineDynamicFilters" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_OrderLineDynamicFilters" PRIMARY KEY AUTOINCREMENT,
                "OrderLineId" INTEGER NOT NULL,
                "DynamicFilterId" INTEGER NOT NULL,
                CONSTRAINT "FK_OrderLineDynamicFilters_OrderLines_OrderLineId" FOREIGN KEY ("OrderLineId") REFERENCES "OrderLines" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_OrderLineDynamicFilters_DynamicFilters_DynamicFilterId" FOREIGN KEY ("DynamicFilterId") REFERENCES "DynamicFilters" ("Id") ON DELETE CASCADE
            );
            """);
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_OrderLineDynamicFilters_OrderLineId" ON "OrderLineDynamicFilters" ("OrderLineId");""");
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_OrderLineDynamicFilters_DynamicFilterId" ON "OrderLineDynamicFilters" ("DynamicFilterId");""");

        if (!SchemaInspector.GetColumns(ctx, "OrderLines").Contains("FamilyOverrideType"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "OrderLines" ADD COLUMN "FamilyOverrideType" TEXT NULL;""");
    }

    /// <summary>La famille "Filtres F7 à H13" (FamilyOverride = 100) n'existe plus : les lignes qui l'avaient
    /// repassent en automatique ; 100 désigne désormais un Type F7 à H13 choisi (FamilyOverrideType).</summary>
    private static void AddOrderLineFamilyOverrideTypeColumn(FiltresDbContext ctx)
    {
        if (!SchemaInspector.GetColumns(ctx, "OrderLines").Contains("FamilyOverrideType"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "OrderLines" ADD COLUMN "FamilyOverrideType" TEXT NULL;""");
        ctx.Database.ExecuteSqlRaw("""UPDATE "OrderLines" SET "FamilyOverride" = NULL WHERE "FamilyOverride" = 100 AND "FamilyOverrideType" IS NULL;""");
    }

    private static void AddOrderLineOpacimetricFilters(FiltresDbContext ctx)
    {
        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "OrderLineOpacimetricFilters" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_OrderLineOpacimetricFilters" PRIMARY KEY AUTOINCREMENT,
                "OrderLineId" INTEGER NOT NULL,
                "OpacimetricFilterId" INTEGER NOT NULL,
                CONSTRAINT "FK_OrderLineOpacimetricFilters_OrderLines_OrderLineId" FOREIGN KEY ("OrderLineId") REFERENCES "OrderLines" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_OrderLineOpacimetricFilters_OpacimetricFilters_OpacimetricFilterId" FOREIGN KEY ("OpacimetricFilterId") REFERENCES "OpacimetricFilters" ("Id") ON DELETE CASCADE
            );
            """);
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_OrderLineOpacimetricFilters_OrderLineId" ON "OrderLineOpacimetricFilters" ("OrderLineId");""");
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_OrderLineOpacimetricFilters_OpacimetricFilterId" ON "OrderLineOpacimetricFilters" ("OpacimetricFilterId");""");
    }

    private static void AddOpacimetricFamilies(FiltresDbContext ctx)
    {
        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "OpacimetricFamilies" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_OpacimetricFamilies" PRIMARY KEY AUTOINCREMENT,
                "Nom" TEXT NOT NULL
            );
            """);
        if (!SchemaInspector.GetColumns(ctx, "OpacimetricFilters").Contains("OpacimetricFamilyId"))
            ctx.Database.ExecuteSqlRaw(
                """ALTER TABLE "OpacimetricFilters" ADD COLUMN "OpacimetricFamilyId" INTEGER NULL REFERENCES "OpacimetricFamilies" ("Id") ON DELETE SET NULL;""");
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_OpacimetricFilters_OpacimetricFamilyId" ON "OpacimetricFilters" ("OpacimetricFamilyId");""");
    }

    private static void AddSharedAssetsTable(FiltresDbContext ctx) =>
        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "SharedAssets" (
                "Key" TEXT NOT NULL CONSTRAINT "PK_SharedAssets" PRIMARY KEY,
                "Data" BLOB NOT NULL
            );
            """);

    private static void AddOrderLineManualNeedColumn(FiltresDbContext ctx)
    {
        if (!SchemaInspector.GetColumns(ctx, "OrderLines").Contains("ManualNeed"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "OrderLines" ADD COLUMN "ManualNeed" INTEGER NULL;""");
    }

    private static void AddOrderLineFamilyOverrideColumn(FiltresDbContext ctx)
    {
        if (!SchemaInspector.GetColumns(ctx, "OrderLines").Contains("FamilyOverride"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "OrderLines" ADD COLUMN "FamilyOverride" INTEGER NULL;""");
    }

    private static void AddOpacimetricFilterTypeColumn(FiltresDbContext ctx)
    {
        if (!SchemaInspector.GetColumns(ctx, "OpacimetricFilters").Contains("FilterType"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "OpacimetricFilters" ADD COLUMN "FilterType" TEXT NULL;""");
    }

    /// <summary>Familles saisies manuellement, remplacées depuis par des familles déduites des filtres
    /// rattachés : la table et la colonne restent en base mais ne sont plus utilisées.</summary>
    private static void AddOrderFamilies(FiltresDbContext ctx)
    {
        ctx.Database.ExecuteSqlRaw(
            """
            CREATE TABLE IF NOT EXISTS "OrderFamilies" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_OrderFamilies" PRIMARY KEY AUTOINCREMENT,
                "Nom" TEXT NOT NULL
            );
            """);
        if (!SchemaInspector.GetColumns(ctx, "OrderLines").Contains("OrderFamilyId"))
            ctx.Database.ExecuteSqlRaw(
                """ALTER TABLE "OrderLines" ADD COLUMN "OrderFamilyId" INTEGER NULL REFERENCES "OrderFamilies" ("Id") ON DELETE SET NULL;""");
        ctx.Database.ExecuteSqlRaw(
            """CREATE INDEX IF NOT EXISTS "IX_OrderLines_OrderFamilyId" ON "OrderLines" ("OrderFamilyId");""");
    }

    private static void AddOrderLineInventaireColumn(FiltresDbContext ctx)
    {
        if (!SchemaInspector.GetColumns(ctx, "OrderLines").Contains("Inventaire"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "OrderLines" ADD COLUMN "Inventaire" INTEGER NULL;""");
    }

    private static void DropOrderLineUniteColumn(FiltresDbContext ctx)
    {
        if (SchemaInspector.GetColumns(ctx, "OrderLines").Contains("Unite"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "OrderLines" DROP COLUMN "Unite";""");
    }

    /// <summary>Colonne "Destination" des écrans Commande et Inventaire, vide pour les lignes existantes.</summary>
    private static void EnsureOrderLineDestinationColumn(FiltresDbContext ctx)
    {
        var cols = SchemaInspector.GetColumns(ctx, "OrderLines");
        if (!cols.Contains("Destination"))
            ctx.Database.ExecuteSqlRaw("""ALTER TABLE "OrderLines" ADD COLUMN "Destination" TEXT NULL;""");
    }

    /// <summary>Option "Changé tous les 15 jours" (G4 plissé, voir README) : ajoute à "PeriodicFilters"
    /// la colonne booléenne correspondante, à 0 (false) par défaut pour toutes les lignes existantes,
    /// sans toucher aux données déjà en base.</summary>
    private static void EnsureChangedEvery15DaysColumn(FiltresDbContext ctx)
    {
        var cols = SchemaInspector.GetColumns(ctx, "PeriodicFilters");
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
        var cols = SchemaInspector.GetColumns(ctx, "OrderLines");
        if (cols.Count > 0)
        {
            // OrderDocumentType.PourDevis valait 1 (CommandeChmy = 0) avant sa suppression de l'enum.
            ctx.Database.ExecuteSqlRaw("""DELETE FROM "OrderLinePeriodicFilters" WHERE "OrderLineId" IN (SELECT "Id" FROM "OrderLines" WHERE "DocumentType" = 1);""");
            ctx.Database.ExecuteSqlRaw("""DELETE FROM "OrderLines" WHERE "DocumentType" = 1;""");
        }
        ctx.Database.ExecuteSqlRaw("""DROP TABLE IF EXISTS "RefacturingLines";""");
    }

    /// <summary>Fusion Inventaire / Commande chmy (voir README) : ajoute à "OrderLines" les colonnes
    /// nécessaires à l'accueil des anciennes lignes "InventoryLine" ("Unite" et le marqueur technique
    /// "MigratedFromInventoryLineId"), sans toucher aux données existantes.</summary>
    private static void EnsureInventoryOrderMergeSchema(FiltresDbContext ctx)
    {
        var cols = SchemaInspector.GetColumns(ctx, "OrderLines");
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

        var cols = SchemaInspector.GetColumns(ctx, "K7Locations");
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
