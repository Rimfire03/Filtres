namespace FiltresApp.Core.Services.Licensing;

/// <summary>Licence telle que renvoyée par le serveur (activate/validate), sérialisée dans le fichier
/// chiffré local - voir <see cref="LicenseState"/>.</summary>
public class LicenseInfo
{
    public string? Key { get; set; }
    public string Product { get; set; } = "";
    public string? Type { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int MaxActivations { get; set; }
    public List<string> Features { get; set; } = new();
}

/// <summary>Contenu du fichier de licence local (FiltreData\license.dat, chiffré - voir
/// <see cref="LicenseStorage"/>) : clé saisie par l'utilisateur, identifiant d'appareil partagé par tous
/// les postes utilisant ce même FiltreData (généré une seule fois, voir LicenseManager.ActivateAsync), et
/// dernière licence connue avec la date de sa dernière validation réussie (sert de point de départ à la
/// grâce hors-ligne).</summary>
public class LicenseState
{
    public string DeviceId { get; set; } = "";
    public string LicenseKey { get; set; } = "";
    public DateTime LastValidationUtc { get; set; }
    public LicenseInfo? License { get; set; }
}

/// <summary>Résultat d'un appel activate/validate au serveur de licences.</summary>
public record LicenseApiResult(bool Valid, LicenseInfo? License, string? Reason);

/// <summary>État d'utilisation courant, déterminé une fois au démarrage (voir
/// <see cref="LicenseManager.CheckAtStartupAsync"/>) et tenu à jour ensuite en mémoire.</summary>
public enum LicenseMode
{
    /// <summary>Flag de désactivation du système ou fichier licence.ini : aucun contrôle, tout accessible.</summary>
    Free,
    /// <summary>Licence validée avec succès (en ligne).</summary>
    Active,
    /// <summary>Serveur injoignable, mais dans la fenêtre de grâce hors-ligne (30 jours depuis la
    /// dernière validation réussie, sans dépasser la date d'expiration de la licence).</summary>
    Grace,
    /// <summary>Aucune licence utilisable : application bloquée, seul l'écran de saisie de clé est accessible.</summary>
    Blocked
}

/// <summary>Résultat du contrôle de licence au démarrage (ou après une tentative d'activation).</summary>
public record LicenseCheckOutcome(LicenseMode Mode, string StatusText, string? BlockedMessage = null, bool IsOfflineGrace = false);
