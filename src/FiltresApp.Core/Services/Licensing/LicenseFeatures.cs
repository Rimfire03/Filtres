namespace FiltresApp.Core.Services.Licensing;

/// <summary>Identifiants de fonctionnalités de licence (valeurs de <c>license.features</c> renvoyées par le serveur
/// de licences) : une seule définition par module, à utiliser avec <see cref="LicenseManager.HasFeature"/>.</summary>
public static class LicenseFeatures
{
    /// <summary>Module « Suivi ZAC ».</summary>
    public const string SuiviZac = "suivi-zac";

    public const string SuiviZacNotIncludedMessage = "Module Suivi ZAC : non inclus dans votre licence";
}
