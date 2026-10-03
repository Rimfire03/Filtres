using System.Windows;

namespace FiltresApp.Views.Dialogs;

/// <summary>Contrat de licence utilisateur final. Deux usages : acceptation au démarrage, tant que
/// FiltreData\CLUF.txt est absent (voir App.EnsureEulaAccepted) - "J'accepte" n'est accessible qu'une fois
/// la case cochée ; ou simple relecture depuis Paramètres (<paramref name="readOnly"/>), avec un seul
/// bouton "Fermer".</summary>
public partial class EulaWindow : Window
{
    public EulaWindow(string eulaText, bool readOnly = false)
    {
        InitializeComponent();
        EulaText.Text = eulaText;

        if (readOnly)
        {
            IntroText.Text = "Contrat accepté lors de l'installation du logiciel.";
            AcceptCheck.Visibility = Visibility.Collapsed;
            DeclineButton.Visibility = Visibility.Collapsed;
            AcceptButton.Content = "Fermer";
            AcceptButton.IsEnabled = true;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
    }

    private void AcceptCheck_Changed(object sender, RoutedEventArgs e) => AcceptButton.IsEnabled = AcceptCheck.IsChecked == true;

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Decline_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
