using System.IO;
using System.Text;
using System.Windows;
using FiltresApp.Views.Dialogs;

namespace FiltresApp;

/// <summary>Contrat de licence utilisateur final (CLUF) : le texte est intégré à l'exécutable
/// (Legal\CLUF.txt, ressource incorporée) ; sa copie dans FiltreData\CLUF.txt vaut acceptation. Tant que
/// ce fichier est absent (première installation, fichier supprimé), le contrat est présenté au démarrage
/// et doit être accepté pour continuer.</summary>
public partial class App
{
    private const string EulaResourceName = "FiltresApp.CLUF.txt";

    private static string EulaFilePath => Path.Combine(AppContext.BaseDirectory, "FiltreData", "CLUF.txt");

    /// <summary>False si l'utilisateur refuse le contrat (l'application doit alors s'arrêter).</summary>
    private static bool EnsureEulaAccepted()
    {
        if (File.Exists(EulaFilePath)) return true;

        string text;
        using (var stream = typeof(App).Assembly.GetManifestResourceStream(EulaResourceName)
                            ?? throw new InvalidOperationException($"Ressource {EulaResourceName} introuvable."))
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            text = reader.ReadToEnd();

        if (new EulaWindow(text).ShowDialog() != true) return false;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(EulaFilePath)!);
            File.WriteAllText(EulaFilePath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }
        catch (Exception ex)
        {
            // Acceptation valable pour cette session ; elle sera redemandée au prochain lancement.
            MessageBox.Show($"Le contrat a été accepté, mais il n'a pas pu être enregistré dans :\n{EulaFilePath}\n\n{ex.Message}\n\n" +
                            "Il vous sera de nouveau présenté au prochain lancement.",
                "Contrat de licence", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return true;
    }
}
