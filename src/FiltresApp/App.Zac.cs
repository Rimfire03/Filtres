using System.Windows;
using FiltresApp.Core.Services;
using FiltresApp.Core.Services.Licensing;
using FiltresApp.ViewModels;
using FiltresApp.Views.Dialogs;

namespace FiltresApp;

/// <summary>Alerte de démarrage du module « Suivi ZAC » : orange si un prochain contrôle approche (délai paramétré dans
/// le module), rouge si une échéance est dépassée.</summary>
public partial class App
{
    /// <summary>Affiche la fenêtre d'alerte si le module est activé, couvert par la licence et qu'au moins une pièce est
    /// à contrôler bientôt ou en retard. Ne bloque jamais le démarrage : toute erreur est ignorée.</summary>
    private static void ShowZacAlertsIfAny()
    {
        try
        {
            if (!Settings.ShowZacModule || !Settings.ZacAlertEnabled) return;
            if (!LicenseManager.HasFeature(LicenseFeatures.SuiviZac)) return;

            // Une seule ouverture par jour et par poste.
            var today = DateOnly.FromDateTime(DateTime.Today);
            var todayText = today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            if (Settings.ZacAlertLastShown == todayText) return;

            var alerts = ZacService.Alerts(Db, today, Math.Max(0, Settings.ZacAlertDays));
            if (alerts.Count == 0) return;

            Settings.ZacAlertLastShown = todayText;
            Settings.Save();

            var window = new ZacAlertWindow(alerts) { Owner = Current.MainWindow };
            window.ShowDialog();
            if (window.OpenModuleRequested && Current.MainWindow?.DataContext is MainViewModel main)
                main.ShowZacModule();
        }
        catch
        {
            // L'alerte est un confort : un incident (base indisponible...) ne doit pas gêner l'ouverture.
        }
    }
}
