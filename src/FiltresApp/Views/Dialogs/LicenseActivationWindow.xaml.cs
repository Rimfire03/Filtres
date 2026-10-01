using System.Windows;
using System.Windows.Input;
using FiltresApp.Core.Services.Licensing;

namespace FiltresApp.Views.Dialogs;

public partial class LicenseActivationWindow : Window
{
    public bool ActivationSucceeded { get; private set; }

    public LicenseActivationWindow(string? blockedMessage)
    {
        InitializeComponent();
        Qa.Text = string.IsNullOrWhiteSpace(blockedMessage) ? "Saisissez votre clé de licence pour continuer." : blockedMessage;
        Loaded += (_, _) => Qb.Focus();
    }

    private void Qd(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Qc(sender, e);
    }

    private async void Qc(object sender, RoutedEventArgs e)
    {
        var key = Qb.Text.Trim();
        if (key.Length == 0)
        {
            Qf.Text = "Merci de saisir une clé de licence.";
            return;
        }

        Qe.IsEnabled = false;
        Qf.Foreground = (System.Windows.Media.Brush)FindResource("BrushTextSecondary");
        Qf.Text = "Activation en cours...";

        var outcome = await LicenseManager.ActivateAsync(key);
        if (outcome.Mode == LicenseMode.Active)
        {
            ActivationSucceeded = true;
            DialogResult = true;
            Close();
            return;
        }

        Qe.IsEnabled = true;
        Qf.Foreground = (System.Windows.Media.Brush)FindResource("BrushDanger");
        Qf.Text = outcome.BlockedMessage ?? "Activation refusée.";
    }

    private void Qg(object sender, RoutedEventArgs e)
    {
        ActivationSucceeded = false;
        DialogResult = false;
        Close();
    }
}
