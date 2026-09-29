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
            info = await Updater.CheckForUpdateAsync(CurrentVersion);
        }
        catch
        {
            return;
        }
        if (info is null) return;

        var proceed = Dialogs.ShowConfirm("Mise à jour disponible",
            $"Une nouvelle version {info.Version} est disponible (version actuelle : {CurrentVersion}).\n\n" +
            "Voulez-vous la télécharger et l'installer maintenant ? L'application va se fermer puis redémarrer automatiquement.\n\n" +
            "Vous pouvez désactiver cette vérification automatique dans Paramètres.");
        if (!proceed) return;

        try
        {
            await Updater.DownloadAndApplyAsync(info);
            Current.Shutdown();
        }
        catch (Exception ex)
        {
            Dialogs.ShowMessage("Mise à jour", $"L'installation de la mise à jour a échoué : {ex.Message}");
        }
    }
}
