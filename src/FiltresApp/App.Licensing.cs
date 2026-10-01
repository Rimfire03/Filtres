using FiltresApp.Core.Services.Licensing;
using FiltresApp.Views.Dialogs;

namespace FiltresApp;

public partial class App
{
    private static bool Qw9()
    {
        // Task.Run (pas un .GetAwaiter().GetResult() direct) : CheckAtStartupAsync peut faire un vrai
        // appel réseau (voir LicenseManager.Vs) dont la suite voudrait reprendre sur le thread
        // d'interface via son SynchronizationContext - ici bloqué en attente synchrone, avant même que
        // la boucle de messages ne tourne (OnStartup). Sans ce Task.Run, l'application se fige
        // définitivement dès qu'une licence est déjà stockée (donc dès le second lancement).
        var o = System.Threading.Tasks.Task.Run(() => LicenseManager.CheckAtStartupAsync()).GetAwaiter().GetResult();
        if (o.Mode != LicenseMode.Blocked) return true;

        var w = new LicenseActivationWindow(o.BlockedMessage);
        return w.ShowDialog() == true && w.ActivationSucceeded;
    }

    public static string LicenseFooterText => LicenseManager.FooterText;
    public static bool IsLicenseOfflineGrace => LicenseManager.IsOfflineGrace;
}
