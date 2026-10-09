using System.Windows;
using FiltresApp.Core.Services;

namespace FiltresApp;

/// <summary>Version du logiciel et mise à jour automatique.</summary>
public partial class App
{
    public static UpdateService Updater { get; } = new();

    /// <summary>Version courante de l'application (définie par &lt;Version&gt; dans le .csproj),
    /// comparée à la dernière release GitHub par <see cref="Updater"/>.</summary>
    public static string CurrentVersion
    {
        get
        {
            var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>Version affichée à l'utilisateur : <see cref="CurrentVersion"/> suivie de « -dev » pour une build
    /// publiée sur le canal Dev (métadonnée ReleaseChannel posée par tools\Release.ps1).</summary>
    public static string DisplayVersion
    {
        get
        {
            var channel = System.Reflection.Assembly.GetExecutingAssembly()
                .GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
                .OfType<System.Reflection.AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "ReleaseChannel")?.Value;
            return string.IsNullOrWhiteSpace(channel) ? CurrentVersion : $"{CurrentVersion}-{channel}";
        }
    }

    /// <summary>Base de données plus récente que ce logiciel : cherche la dernière version et propose de
    /// l'installer (la base n'est pas touchée, donc pas de sauvegarde). Renvoie true si l'installation a été
    /// lancée (l'appelant quitte alors sans message d'erreur), false s'il faut afficher l'erreur habituelle
    /// (pas de mise à jour trouvée, réseau indisponible, refus ou échec).</summary>
    private static bool OfferUpdateForNewerDatabase()
    {
        UpdateInfo? info;
        try
        {
            info = Task.Run(() => Updater.CheckForUpdateAsync(CurrentVersion, Settings.UpdateChannel)).GetAwaiter().GetResult();
        }
        catch
        {
            return false;
        }
        if (info is null) return false;

        var accepted = MessageBox.Show(
            $"La base de données a été mise à jour par une version plus récente du logiciel : votre version ({CurrentVersion}) ne peut pas l'ouvrir.\n\n" +
            $"La version {info.Version} est disponible. La télécharger et l'installer maintenant ? " +
            "La base de données ne sera pas modifiée. L'application va se fermer puis redémarrer automatiquement.",
            "Mise à jour du logiciel nécessaire", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes);
        if (accepted != MessageBoxResult.Yes) return false;

        try
        {
            Task.Run(() => Updater.DownloadAndApplyAsync(info)).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"La mise à jour a échoué : {ex.Message}", "Mise à jour", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Vérification silencieuse au démarrage : ne bloque jamais le lancement de l'application
    /// et n'interrompt l'utilisateur que si une mise à jour est réellement disponible. Un échec de la
    /// vérification elle-même (réseau, API GitHub indisponible...) reste silencieux ici - ce n'est pas
    /// le rôle d'un contrôle en arrière-plan au démarrage de le signaler à chaque lancement ; utiliser
    /// "Vérifier maintenant" dans Paramètres pour voir l'erreur réelle le cas échéant (voir
    /// UpdateService.CheckForUpdateAsync).</summary>
    private static async Task CheckForUpdateOnStartupAsync()
    {
        UpdateInfo? info;
        try
        {
            info = await Updater.CheckForUpdateAsync(CurrentVersion, Settings.UpdateChannel);
        }
        catch
        {
            return;
        }
        if (info is null) return;

        var proceed = Dialogs.ShowConfirm("Mise à jour disponible",
            $"Une nouvelle version {info.Version} est disponible (version actuelle : {DisplayVersion}).\n\n" +
            "Voulez-vous la télécharger et l'installer maintenant ? Une copie de sauvegarde de la base de données sera faite avant toute chose. L'application va se fermer puis redémarrer automatiquement.\n\n" +
            "Vous pouvez désactiver cette vérification automatique dans Paramètres.");
        if (!proceed) return;

        try
        {
            DbFactory.CreateBackup("avant-mise-a-jour");
            await Updater.DownloadAndApplyAsync(info);
            Current.Shutdown();
        }
        catch (Exception ex)
        {
            Dialogs.ShowMessage("Mise à jour", $"La mise à jour a échoué : {ex.Message}");
        }
    }
}
