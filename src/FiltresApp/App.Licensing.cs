using FiltresApp.Core.Services.Licensing;
using FiltresApp.Views.Dialogs;

namespace FiltresApp;

/// <summary>Contrôle de licence au démarrage (voir LicenseManager pour l'orchestration complète :
/// bypass "licence gratuite", activation/validation auprès du serveur, grâce hors-ligne).</summary>
public partial class App
{
    /// <summary>Appelé avant tout autre accès (base de données comprise) : si aucune licence utilisable
    /// n'est disponible, affiche l'écran de saisie de clé en boucle jusqu'à activation réussie ou fermeture
    /// demandée par l'utilisateur - seul point d'entrée possible dans ce cas. Retourne false si
    /// l'application doit se fermer.</summary>
    private static bool EnsureLicensedAtStartup()
    {
        var outcome = LicenseManager.CheckAtStartupAsync().GetAwaiter().GetResult();
        if (outcome.Mode != LicenseMode.Blocked) return true;

        var window = new LicenseActivationWindow(outcome.BlockedMessage);
        return window.ShowDialog() == true && window.ActivationSucceeded;
    }

    /// <summary>Pied de page de l'application (voir MainWindow.xaml) : état de licence en permanence.</summary>
    public static string LicenseFooterText => LicenseManager.FooterText;
    public static bool IsLicenseOfflineGrace => LicenseManager.IsOfflineGrace;
}
