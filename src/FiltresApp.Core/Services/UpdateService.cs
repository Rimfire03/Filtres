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
    /// récente que <paramref name="currentVersion"/> (format "MAJOR.MINOR.PATCH"). Retourne null en
    /// l'absence de mise à jour ou en cas d'erreur (réseau, API indisponible, etc.) : la vérification
    /// ne doit jamais faire échouer l'appelant.</summary>
    public async Task<UpdateInfo?> CheckForUpdateAsync(string currentVersion)
    {
        try
        {
            using var response = await Http.GetAsync(
                $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
            if (!response.IsSuccessStatusCode) return null;

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
            if (assetUrl == null || assetName == null) return null;

            var notes = root.TryGetProperty("body", out var bodyEl) ? bodyEl.GetString() ?? "" : "";
            var htmlUrl = root.TryGetProperty("html_url", out var urlEl) ? urlEl.GetString() ?? "" : "";

            return new UpdateInfo(versionText, htmlUrl, notes, assetUrl, assetName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Télécharge l'archive de la nouvelle version, en extrait le nouvel exécutable, puis
    /// prépare et lance un script qui attend la fermeture du processus courant pour remplacer
    /// l'exécutable en place et relancer l'application. L'appelant doit fermer l'application
    /// juste après (le remplacement du fichier ne peut se faire tant qu'il est verrouillé).</summary>
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

        var newExePath = Directory.GetFiles(extractDir, "FiltresApp.exe", SearchOption.AllDirectories).FirstOrDefault()
            ?? throw new InvalidOperationException("FiltresApp.exe introuvable dans l'archive téléchargée.");

        var currentExePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Impossible de déterminer l'exécutable en cours d'exécution.");
        var currentPid = Environment.ProcessId;

        var scriptPath = Path.Combine(tempDir, "update.bat");
        File.WriteAllText(scriptPath, BuildUpdateScript(currentPid, newExePath, currentExePath, extractDir));

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

    private static string BuildUpdateScript(int pid, string newExePath, string targetExePath, string extractDir) => $"""
        @echo off
        setlocal

        :waitloop
        tasklist /FI "PID eq {pid}" 2>NUL | find /I "{pid}" >NUL
        if "%ERRORLEVEL%"=="0" (
            timeout /t 1 /nobreak >nul
            goto waitloop
        )

        :copyloop
        copy /y "{newExePath}" "{targetExePath}" >nul 2>&1
        if errorlevel 1 (
            timeout /t 1 /nobreak >nul
            goto copyloop
        )

        rd /s /q "{extractDir}" >nul 2>&1
        start "" "{targetExePath}"
        del "%~f0" >nul 2>&1
        """;
}
