using System.Windows;
using System.Windows.Input;
using FiltresApp.Core.Services.Licensing;

namespace FiltresApp.Views.Dialogs;

/// <summary>Écran bloquant affiché au démarrage tant qu'aucune licence valide n'est disponible (voir
/// App.xaml.cs) : seul point d'entrée possible dans ce cas, jusqu'à activation réussie ou fermeture de
/// l'application. "Quitter" ferme l'application entière (pas seulement cette fenêtre).</summary>
public partial class LicenseActivationWindow : Window
{
    /// <summary>Vrai si l'activation a réussi (DialogResult true) - sinon l'appelant doit fermer
    /// l'application. Nom distinct de l'événement Window.Activated (hérité).</summary>
    public bool ActivationSucceeded { get; private set; }

    public LicenseActivationWindow(string? blockedMessage)
    {
        InitializeComponent();
        BlockedMessageText.Text = string.IsNullOrWhiteSpace(blockedMessage)
            ? "Saisissez votre clé de licence pour continuer."
            : blockedMessage;
        Loaded += (_, _) => LicenseKeyBox.Focus();
    }

    private void LicenseKeyBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) Activate_Click(sender, e);
    }

    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        var key = LicenseKeyBox.Text.Trim();
        if (key.Length == 0)
        {
            StatusText.Text = "Merci de saisir une clé de licence.";
            return;
        }

        ActivateButton.IsEnabled = false;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("BrushTextSecondary");
        StatusText.Text = "Activation en cours...";

        var outcome = await LicenseManager.ActivateAsync(key);
        if (outcome.Mode == LicenseMode.Active)
        {
            ActivationSucceeded = true;
            DialogResult = true;
            Close();
            return;
        }

        ActivateButton.IsEnabled = true;
        StatusText.Foreground = (System.Windows.Media.Brush)FindResource("BrushDanger");
        StatusText.Text = outcome.BlockedMessage ?? "Activation refusée.";
    }

    private void Quit_Click(object sender, RoutedEventArgs e)
    {
        ActivationSucceeded = false;
        DialogResult = false;
        Close();
    }
}
