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
        if (o.Mode is not (LicenseMode.Blocked or LicenseMode.Expired)) return true;

        var w = new LicenseActivationWindow(o.BlockedMessage, o.Mode);
        return w.ShowDialog() == true && w.ActivationSucceeded;
    }

    /// <summary>"Changer de licence" depuis Paramètres : la nouvelle clé est activée avant que l'ancienne soit
    /// désactivée ; annulation ou échec = l'ancienne licence reste en place.</summary>
    public static bool ChangeLicense()
    {
        var w = new LicenseActivationWindow(null, LicenseManager.Mode, changeMode: true) { Owner = Current.MainWindow };
        return w.ShowDialog() == true && w.ActivationSucceeded;
    }

    private static System.Windows.Threading.DispatcherTimer? _licenseTimer;

    /// <summary>Revalidation périodique : prolongation ou changement de modules côté serveur pris en compte
    /// sans relancer l'application (les abonnés de LicenseManager.Changed recalculent l'état des modules).</summary>
    public static void StartLicenseRevalidation()
    {
        if (_licenseTimer is not null || LicenseManager.IsFreeLicense) return;
        _licenseTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(15) };
        _licenseTimer.Tick += async (_, _) =>
        {
            try { await LicenseManager.RevalidateAsync(); } catch { }
        };
        _licenseTimer.Start();
    }

    public static string LicenseFooterText => LicenseManager.FooterText;
    public static bool IsLicenseOfflineGrace => LicenseManager.IsOfflineGrace;
}
