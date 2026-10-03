using System.Windows;

namespace FiltresApp.Views.Dialogs;

/// <summary>Acceptation du contrat de licence utilisateur final au démarrage, tant que FiltreData\CLUF.txt
/// est absent (voir App.EnsureEulaAccepted). "J'accepte" n'est accessible qu'une fois la case cochée.</summary>
public partial class EulaWindow : Window
{
    public EulaWindow(string eulaText)
    {
        InitializeComponent();
        EulaText.Text = eulaText;
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
