using System.Text;
using System.Text.Json;

namespace FiltresApp.Core.Services.Licensing;

/// <summary>Lecture/écriture du fichier de licence local (chiffré, voir <see cref="LicenseCrypto"/>) et
/// détection du bypass "licence gratuite" par fichier - tous deux dans FiltreData, le même dossier de
/// données partagé que le reste de l'application (base de données, exports, réglages). Le fichier de
/// licence est donc lui aussi partagé entre tous les postes utilisant ce FiltreData : ils lisent le même
/// deviceId (généré une seule fois) et bénéficient ainsi de la même licence activée, sans consommer
/// plusieurs activations.</summary>
internal static class LicenseStorage
{
    private static string DataDirectory => Path.Combine(AppContext.BaseDirectory, "FiltreData");
    private static string LicenseFilePath => Path.Combine(DataDirectory, "license.dat");
    private static string FreeLicenseFlagPath => Path.Combine(DataDirectory, "licence.ini");

    /// <summary>Fichier vide "licence.ini" dans FiltreData : bypass "licence gratuite" pour un cas d'usage
    /// terrain (voir LicenseConfig pour l'interrupteur de build équivalent).</summary>
    public static bool HasFreeLicenseFlag() => File.Exists(FreeLicenseFlagPath);

    public static LicenseState? Load()
    {
        try
        {
            if (!File.Exists(LicenseFilePath)) return null;
            var cipher = File.ReadAllBytes(LicenseFilePath);
            var plain = LicenseCrypto.Decrypt(cipher);
            var json = Encoding.UTF8.GetString(plain);
            return JsonSerializer.Deserialize<LicenseState>(json);
        }
        catch
        {
            // Fichier corrompu, modifié à la main ou chiffré avec une autre clé : traité comme absent,
            // l'utilisateur devra réactiver sa licence.
            return null;
        }
    }

    public static void Save(LicenseState state)
    {
        Directory.CreateDirectory(DataDirectory);
        var json = JsonSerializer.Serialize(state);
        var cipher = LicenseCrypto.Encrypt(Encoding.UTF8.GetBytes(json));
        File.WriteAllBytes(LicenseFilePath, cipher);
    }

    public static void Delete()
    {
        try { if (File.Exists(LicenseFilePath)) File.Delete(LicenseFilePath); }
        catch { /* best-effort */ }
    }
}
