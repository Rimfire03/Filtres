using System.IO.Compression;
using FiltresApp.Core.Data;
using FiltresApp.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace FiltresApp.Core.Services;

/// <summary>Niveau d'alerte du prochain contrôle d'une pièce.</summary>
public enum ZacAlertLevel { None, Approaching, Overdue }

/// <summary>Pièce en alerte (fenêtre de démarrage et colonne « État »).</summary>
public sealed record ZacAlertRow(string Item, string ZacType, DateOnly NextControl, int DaysLeft, ZacAlertLevel Level)
{
    public string NextText => NextControl.ToString("dd/MM/yyyy");
    public string DelayText => Level == ZacAlertLevel.Overdue ? $"dépassé de {-DaysLeft} j" : DaysLeft == 0 ? "aujourd'hui" : $"dans {DaysLeft} j";
}

/// <summary>Règles du module « Suivi ZAC » : niveau d'alerte, liste des alertes et stockage des fichiers joints
/// (compression GZip quand elle fait gagner de la place).</summary>
public static class ZacService
{
    /// <summary>Extensions déjà compressées : inutile de les recompresser.</summary>
    private static readonly HashSet<string> AlreadyCompressed = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".gz", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf", ".docx", ".xlsx", ".pptx", ".mp3", ".mp4", ".mkv"
    };

    public static ZacAlertLevel Level(DateOnly? next, DateOnly today, int alertDays)
    {
        if (next is not { } n) return ZacAlertLevel.None;
        if (n < today) return ZacAlertLevel.Overdue;
        return n.DayNumber - today.DayNumber <= alertDays ? ZacAlertLevel.Approaching : ZacAlertLevel.None;
    }

    /// <summary>Pièces à contrôler bientôt ou en retard, les plus urgentes d'abord.</summary>
    public static List<ZacAlertRow> Alerts(FiltresDbContext ctx, DateOnly today, int alertDays)
    {
        var items = ctx.Set<ZacItem>().AsNoTracking().Include(i => i.Family).Where(i => i.NextControlDate != null).ToList();
        return items
            .Select(i => (Item: i, Level: Level(i.NextControlDate, today, alertDays)))
            .Where(x => x.Level != ZacAlertLevel.None)
            .Select(x => new ZacAlertRow(x.Item.Location, x.Item.FamilyGroupLabel, x.Item.NextControlDate!.Value,
                x.Item.NextControlDate!.Value.DayNumber - today.DayNumber, x.Level))
            .OrderBy(a => a.NextControl)
            .ToList();
    }

    /// <summary>Prochain contrôle après un contrôle à <paramref name="date"/> : date + intervalle.</summary>
    public static DateOnly NextAfter(DateOnly date, int intervalMonths) => date.AddMonths(Math.Max(1, intervalMonths));

    // ---- Fichiers joints ----

    public static ZacAttachment CreateAttachment(string fileName, byte[] content)
    {
        var attachment = new ZacAttachment { FileName = fileName, OriginalSize = content.LongLength, Data = content };
        if (AlreadyCompressed.Contains(Path.GetExtension(fileName)) || content.Length < 512) return attachment;

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(content, 0, content.Length);
        if (output.Length < content.Length)
        {
            attachment.Data = output.ToArray();
            attachment.IsCompressed = true;
        }
        return attachment;
    }

    public static byte[] ReadContent(ZacAttachment attachment)
    {
        if (!attachment.IsCompressed) return attachment.Data;
        using var input = new MemoryStream(attachment.Data);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }

    public static string FormatSize(long bytes) =>
        bytes < 1024 ? $"{bytes} o" : bytes < 1024 * 1024 ? $"{bytes / 1024.0:0.0} Ko" : $"{bytes / 1024.0 / 1024.0:0.0} Mo";
}
