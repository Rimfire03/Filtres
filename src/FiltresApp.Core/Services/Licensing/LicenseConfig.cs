namespace FiltresApp.Core.Services.Licensing;

/// <summary>Interrupteur central du système de licence : à mettre à <c>true</c> avant de publier une
/// release distribuée sans contrôle de licence (aucun appel réseau, tous les modules accessibles, pied de
/// page "Licence gratuite"). Remettre à <c>false</c> pour réactiver le contrôle normal. Seul endroit à
/// modifier - ne pas dupliquer cette bascule ailleurs.</summary>
public static class LicenseConfig
{
    public const bool DisableLicensing = false;
}
