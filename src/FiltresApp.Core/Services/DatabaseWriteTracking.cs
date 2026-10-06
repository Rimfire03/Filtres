using FiltresApp.Core.Data;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Services;

/// <summary>Date de la dernière saisie enregistrée dans la base (affichée dans Paramètres, « Base de données »).
/// Chaque table de données porte trois déclencheurs SQLite (ajout, modification, suppression) qui inscrivent
/// l'heure locale du poste dans <c>DbInfo</c> (clé <see cref="LastWriteKey"/>) : quel que soit le chemin d'écriture
/// (EF Core, ExecuteUpdate / ExecuteDelete, SQL brut), et pour tous les postes puisque l'information est dans la
/// base partagée. Les tables techniques (<c>DbInfo</c>, tables internes de SQLite) sont exclues.</summary>
public static class DatabaseWriteTracking
{
    public const string LastWriteKey = "LastWriteAt";

    private const string Stamp = "strftime('%Y-%m-%dT%H:%M:%S','now','localtime')";

    /// <summary>Crée les déclencheurs manquants (idempotent, aucune écriture si tout existe déjà) : appelé à
    /// chaque ouverture par le poste rédacteur, donc aussi pour les tables ajoutées par une nouvelle version.</summary>
    public static void EnsureTriggers(FiltresDbContext ctx)
    {
        ctx.Database.ExecuteSqlRaw("""CREATE TABLE IF NOT EXISTS "DbInfo" ("Key" TEXT NOT NULL PRIMARY KEY, "Value" TEXT NOT NULL);""");

        var tables = ctx.Database.SqlQueryRaw<string>(
            """
            SELECT name AS "Value" FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite\_%' ESCAPE '\' AND name <> 'DbInfo' AND name NOT LIKE '\_\_%' ESCAPE '\';
            """).ToList();

        foreach (var table in tables)
        {
            foreach (var (suffix, action) in new[] { ("ins", "INSERT"), ("upd", "UPDATE"), ("del", "DELETE") })
            {
                // Noms de table issus de sqlite_master (jamais saisis par l'utilisateur) : entre guillemets doublés.
                var safe = table.Replace("\"", "\"\"");
                ctx.Database.ExecuteSqlRaw(
                    $"""
                    CREATE TRIGGER IF NOT EXISTS "trg_lastwrite_{safe}_{suffix}" AFTER {action} ON "{safe}"
                    BEGIN
                        INSERT OR REPLACE INTO "DbInfo" ("Key", "Value") VALUES ('{LastWriteKey}', {Stamp});
                    END;
                    """);
            }
        }
    }

    /// <summary>Heure locale de la dernière saisie enregistrée, ou null si aucune n'a encore été relevée
    /// (base antérieure à ce suivi, sans écriture depuis).</summary>
    public static DateTime? ReadLastWrite(FiltresDbContext ctx)
    {
        try
        {
            var value = ctx.Database.SqlQueryRaw<string>(
                $"""SELECT "Value" AS "Value" FROM "DbInfo" WHERE "Key" = '{LastWriteKey}'""").FirstOrDefault();
            return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var at) ? at : null;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            return null;
        }
    }
}
