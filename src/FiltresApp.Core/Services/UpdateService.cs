using System.Net.Http;
using System.Text.Json;

namespace FiltresApp.Core.Services;

/// <summary>Informations sur une nouvelle version disponible, extraites de la release GitHub.</summary>
public record UpdateInfo(string Version, string ReleaseUrl, string ReleaseNotes, string AssetUrl, string AssetName);

/// <summary>Vérifie les mises à jour disponibles en s'appuyant sur les releases GitHub du dépôt
/// (exécutable portable publié en asset .zip sur chaque release) et propose de télécharger l'archive.
/// L'installation elle-même (extraire l'archive, remplacer les fichiers) reste manuelle : un
/// remplacement automatique de l'exécutable en place s'est avéré peu fiable (voir historique des
/// versions 1.2.5/1.2.6 - le processus qui remplaçait l'exe pouvait échouer silencieusement,
/// laissant l'ancienne version tourner après un "redémarrage" apparent).</summary>
public class UpdateService
{
    private const string RepoOwner = "Rimfire03";
    private const string RepoName = "Filtres";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("FiltresApp-UpdateChecker");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    /// <summary>Interroge la dernière release GitHub et retourne ses infos si sa version est plus
    /// récente que <paramref name="currentVersion"/> (format "MAJOR.MINOR.PATCH"). Retourne null
    /// UNIQUEMENT quand la vérification a réussi et qu'il n'y a pas de mise à jour disponible.
    /// Lève une exception en cas d'échec réel (réseau, API indisponible, réponse inattendue) : avaler
    /// ces erreurs en silence (comme avant) rendait impossible de distinguer "à jour" de "la
    /// vérification a échoué", ce qui empêchait de diagnostiquer un vrai problème (ex. limite de débit
    /// de l'API GitHub, pare-feu). L'appelant du démarrage silencieux (voir App.Updates.cs) est
    /// responsable de ne pas déranger l'utilisateur pour ces erreurs ; le bouton "Vérifier maintenant"
    /// (Paramètres) les affiche telles quelles.</summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion)
    {
        using var response = await Http.GetAsync(
            $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"L'API GitHub a répondu {(int)response.StatusCode} {response.ReasonPhrase} : {body}");
        }

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
        var versionText = tag.TrimStart('v', 'V');
        if (!IsNewer(versionText, currentVersion)) return null;

        string? assetUrl = null;
        string? assetName = null;
        if (root.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                assetUrl = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                assetName = name;
                break;
            }
        }
        if (assetUrl == null || assetName == null)
            throw new InvalidOperationException($"La release {tag} sur GitHub n'a pas de fichier .zip en pièce jointe.");

        var notes = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
        var htmlUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() ?? "" : "";

        return new UpdateInfo(versionText, htmlUrl, notes, assetUrl, assetName);
    }

    /// <summary>Télécharge l'archive .zip de la nouvelle version dans le dossier Téléchargements de
    /// l'utilisateur (créé si besoin) et retourne son chemin complet. Ne touche à rien d'autre :
    /// l'installation (extraire l'archive, remplacer le contenu du dossier de l'application) reste à
    /// faire manuellement par l'utilisateur, l'application fermée.</summary>
    public async Task<string> DownloadUpdateAsync(UpdateInfo info, IProgress<double>? progress = null)
    {
        var downloadsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        Directory.CreateDirectory(downloadsDir);
        var zipPath = Path.Combine(downloadsDir, info.AssetName);

        using var response = await Http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1L;
        await using var source = await response.Content.ReadAsStreamAsync();
        await using var destination = File.Create(zipPath);
        var buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read));
            readTotal += read;
            if (total > 0) progress?.Report(readTotal * 100.0 / total);
        }

        return zipPath;
    }

    private static bool IsNewer(string candidate, string current)
    {
        var (cMajor, cMinor, cPatch) = ParseVersion(candidate);
        var (curMajor, curMinor, curPatch) = ParseVersion(current);

        if (cMajor != curMajor) return cMajor > curMajor;
        if (cMinor != curMinor) return cMinor > curMinor;
        return cPatch > curPatch;
    }

    private static (int Major, int Minor, int Patch) ParseVersion(string version)
    {
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
        int Part(int i) => i < parts.Length && int.TryParse(parts[i], out var n) ? n : 0;
        return (Part(0), Part(1), Part(2));
    }
}
