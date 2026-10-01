using FiltresApp.Core.Services.Licensing;
using FiltresApp.Views.Dialogs;

namespace FiltresApp;

public partial class App
{
    private static bool Qw9()
    {
        var o = LicenseManager.CheckAtStartupAsync().GetAwaiter().GetResult();
        if (o.Mode != LicenseMode.Blocked) return true;

        var w = new LicenseActivationWindow(o.BlockedMessage);
        return w.ShowDialog() == true && w.ActivationSucceeded;
    }

    public static string LicenseFooterText => LicenseManager.FooterText;
    public static bool IsLicenseOfflineGrace => LicenseManager.IsOfflineGrace;
}
