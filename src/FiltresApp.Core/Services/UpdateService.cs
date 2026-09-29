using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace FiltresApp.Core.Services;

/// <summary>Informations sur une nouvelle version disponible, extraites de la release GitHub.</summary>
public record UpdateInfo(string Version, string ReleaseUrl, string ReleaseNotes, string AssetUrl, string AssetName);

/// <summary>Vérifie et applique les mises à jour de l'application en s'appuyant sur les releases
/// GitHub du dépôt (exécutable portable publié en asset .zip sur chaque release).</summary>
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

    /// <summary>Télécharge l'archive .zip de la nouvelle version, la décompresse intégralement, puis
    /// prépare et lance un script qui attend la fermeture du processus courant pour copier TOUT le
    /// contenu décompressé (l'exécutable, mais aussi tout autre fichier requis à côté - ex. FiltreData\
    /// LatoFont) par-dessus le dossier d'installation, avant de relancer l'application. Seuls les
    /// fichiers présents dans l'archive sont écrasés/ajoutés : les données propres à l'utilisateur qui
    /// n'y figurent jamais (FiltreData\filtres.db, settings.json, exports...) ne sont jamais touchées.
    /// L'appelant doit fermer l'application juste après (le remplacement de l'exécutable ne peut se
    /// faire tant qu'il est verrouillé).</summary>
    public async Task DownloadAndApplyAsync(UpdateInfo info, IProgress<double>? progress = null)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "FiltresApp-Update-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var zipPath = Path.Combine(tempDir, info.AssetName);

        using (var response = await Http.GetAsync(info.AssetUrl, HttpCompletionOption.ResponseHeadersRead))
        {
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
        }

        var extractDir = Path.Combine(tempDir, "extracted");
        ZipFile.ExtractToDirectory(zipPath, extractDir);

        // L'archive publiée place toujours FiltresApp.exe directement à sa racine (voir "Publier
        // l'exécutable portable" dans le README) : vérifie que la structure est bien celle attendue
        // avant de lancer le remplacement, plutôt que d'échouer silencieusement plus tard.
        if (!File.Exists(Path.Combine(extractDir, "FiltresApp.exe")))
            throw new InvalidOperationException("FiltresApp.exe introuvable à la racine de l'archive téléchargée.");

        var currentExePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Impossible de déterminer l'exécutable en cours d'exécution.");
        var installDir = Path.GetDirectoryName(currentExePath)
            ?? throw new InvalidOperationException("Impossible de déterminer le dossier d'installation.");
        var currentPid = Environment.ProcessId;

        var scriptPath = Path.Combine(tempDir, "update.bat");
        File.WriteAllText(scriptPath, BuildUpdateScript(currentPid, extractDir, installDir, currentExePath, tempDir));

        Process.Start(new ProcessStartInfo
        {
            FileName = scriptPath,
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            CreateNoWindow = true
        });
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

    /// <summary>robocopy (toujours présent sur Windows) copie tout le contenu décompressé par-dessus le
    /// dossier d'installation : sans /MIR, il n'efface jamais un fichier absent de la source (les
    /// données de l'utilisateur dans FiltreData\ restent donc intactes), et ses tentatives intégrées
    /// (/R /W) couvrent l'attente de la libération de l'exécutable par l'ancien processus, déjà
    /// garantie une première fois par la boucle tasklist ci-dessous.
    /// <para>Important : ce script ne supprime JAMAIS <paramref name="tempDir"/> (qui le contient lui-
    /// même) pendant qu'il s'exécute encore - cmd.exe peut interrompre le traitement du fichier .bat en
    /// cours dès que son dossier disparaît, empêchant alors "start" (relance de l'application) de
    /// s'exécuter. Le nettoyage est donc délégué à un second processus cmd totalement détaché, qui
    /// démarre après un court délai (le temps que ce script-ci ait fini de s'exécuter).</para></summary>
    private static string BuildUpdateScript(int pid, string extractDir, string installDir, string targetExePath, string tempDir) => $"""
        @echo off
        setlocal

        :waitloop
        tasklist /FI "PID eq {pid}" 2>NUL | find /I "{pid}" >NUL
        if "%ERRORLEVEL%"=="0" (
            timeout /t 1 /nobreak >nul
            goto waitloop
        )

        robocopy "{extractDir}" "{installDir}" /E /R:5 /W:1 /NFL /NDL /NJH /NJS

        start "" "{targetExePath}"
        start "" cmd /c "timeout /t 3 /nobreak >nul & rd /s /q ""{tempDir}"" >nul 2>&1"
        """;
}
