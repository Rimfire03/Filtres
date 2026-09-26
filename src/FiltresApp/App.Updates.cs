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

    /// <summary>Vérification silencieuse au démarrage : ne bloque jamais le lancement de l'application
    /// et n'interrompt l'utilisateur que si une mise à jour est réellement disponible.</summary>
    private static async Task CheckForUpdateOnStartupAsync()
    {
        try
        {
            var info = await Updater.CheckForUpdateAsync(CurrentVersion);
            if (info is null) return;

            var proceed = Dialogs.ShowConfirm("Mise à jour disponible",
                $"Une nouvelle version {info.Version} est disponible (version actuelle : {CurrentVersion}).\n\n" +
                "Voulez-vous la télécharger et l'installer maintenant ? L'application va se fermer puis redémarrer automatiquement.\n\n" +
                "Vous pouvez désactiver cette vérification automatique dans Paramètres.");
            if (!proceed) return;

            await Updater.DownloadAndApplyAsync(info);
            Current.Shutdown();
        }
        catch (Exception ex)
        {
            // La vérification/installation de mise à jour ne doit jamais faire planter l'application.
            Dialogs.ShowMessage("Mise à jour", $"La mise à jour automatique a échoué : {ex.Message}");
        }
    }
}
