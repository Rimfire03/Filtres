namespace FiltresApp.Core.Services.Licensing;

/// <summary>Orchestre le contrôle de licence : bascule "licence gratuite" (flag de build ou fichier
/// licence.ini), activation/validation auprès du serveur https://licences.tlpc.fr, grâce hors-ligne, et
/// état courant interrogé par l'interface (pied de page, activation/désactivation des modules). Produit
/// "filtre" (3 fonctionnalités activables côté serveur : "filtres", "roulements", "courroies", une par
/// module de l'application).</summary>
public static class LicenseManager
{
    private const string ProductSlug = "filtre";

    /// <summary>Durée de grâce hors-ligne : l'application reste utilisable jusqu'à 30 jours après la
    /// dernière validation réussie si le serveur est injoignable, sans jamais dépasser la date d'expiration
    /// propre de la licence si elle est plus proche.</summary>
    private const int OfflineGraceDays = 30;

    private static readonly LicenseClient Client = new();

    public static LicenseMode Mode { get; private set; } = LicenseMode.Blocked;
    private static LicenseState? CurrentState { get; set; }

    public static bool IsFreeLicense => Mode == LicenseMode.Free;
    public static bool IsBlocked => Mode == LicenseMode.Blocked;
    public static bool IsOfflineGrace => Mode == LicenseMode.Grace;

    /// <summary>Texte affiché en permanence dans le pied de page de l'application.</summary>
    public static string FooterText => Mode switch
    {
        LicenseMode.Free => "Licence gratuite",
        LicenseMode.Active => BuildActiveStatusText(CurrentState?.License),
        LicenseMode.Grace => BuildActiveStatusText(CurrentState?.License) + " (mode hors ligne)",
        LicenseMode.Blocked => "Aucune licence",
        _ => ""
    };

    /// <summary>Un module (voir AppSettings.ShowXModule) n'est utilisable que si sa fonctionnalité est
    /// présente dans la licence active - toujours vrai en licence gratuite. Clés attendues : "filtres",
    /// "courroies", "roulements".</summary>
    public static bool HasFeature(string feature) =>
        Mode == LicenseMode.Free || (CurrentState?.License?.Features.Contains(feature) ?? false);

    /// <summary>À appeler une seule fois au démarrage, avant d'afficher la fenêtre principale : détermine
    /// si l'application peut s'ouvrir normalement (Free/Active/Grace) ou doit rester bloquée sur l'écran
    /// de saisie de clé (Blocked).</summary>
    public static async Task<LicenseCheckOutcome> CheckAtStartupAsync()
    {
        if (LicenseConfig.DisableLicensing)
        {
            Mode = LicenseMode.Free;
            return new LicenseCheckOutcome(LicenseMode.Free, FooterText);
        }

        if (LicenseStorage.HasFreeLicenseFlag())
        {
            Mode = LicenseMode.Free;
            return new LicenseCheckOutcome(LicenseMode.Free, FooterText);
        }

        var state = LicenseStorage.Load();
        if (state is null || string.IsNullOrWhiteSpace(state.LicenseKey))
        {
            CurrentState = null;
            Mode = LicenseMode.Blocked;
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText,
                "Aucune licence active. Saisissez votre clé de licence pour continuer.");
        }

        return await ValidateStoredAsync(state);
    }

    /// <summary>Activation d'une nouvelle clé (écran de saisie) : génère le deviceId une seule fois (pas un
    /// identifiant matériel, voir LicenseState) et le stocke dans FiltreData, partagé par tous les postes.</summary>
    public static async Task<LicenseCheckOutcome> ActivateAsync(string licenseKey)
    {
        licenseKey = licenseKey.Trim();
        if (licenseKey.Length == 0)
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Merci de saisir une clé de licence.");

        var existing = LicenseStorage.Load();
        var deviceId = !string.IsNullOrWhiteSpace(existing?.DeviceId) ? existing!.DeviceId : Guid.NewGuid().ToString("N");

        try
        {
            var result = await Client.ActivateAsync(licenseKey, ProductSlug, deviceId, Environment.MachineName);
            if (result.Valid && result.License is not null)
            {
                var state = new LicenseState
                {
                    DeviceId = deviceId,
                    LicenseKey = licenseKey,
                    LastValidationUtc = DateTime.UtcNow,
                    License = result.License
                };
                LicenseStorage.Save(state);
                CurrentState = state;
                Mode = LicenseMode.Active;
                return new LicenseCheckOutcome(LicenseMode.Active, FooterText);
            }

            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, ReasonMessage(result.Reason));
        }
        catch
        {
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText,
                "Impossible de contacter le serveur de licences. Vérifiez votre connexion internet et réessayez.");
        }
    }

    /// <summary>"Se déconnecter / changer de licence" (Paramètres) : libère l'activation côté serveur
    /// (best-effort) puis efface le fichier local - ne touche jamais à licence.ini.</summary>
    public static async Task DeactivateAsync()
    {
        var state = CurrentState ?? LicenseStorage.Load();
        if (state is not null)
            await Client.DeactivateAsync(state.LicenseKey, ProductSlug, state.DeviceId);

        LicenseStorage.Delete();
        CurrentState = null;
        Mode = LicenseMode.Blocked;
    }

    private static async Task<LicenseCheckOutcome> ValidateStoredAsync(LicenseState state)
    {
        try
        {
            var result = await Client.ValidateAsync(state.LicenseKey, ProductSlug, state.DeviceId);
            if (result.Valid && result.License is not null)
            {
                state.License = result.License;
                state.LastValidationUtc = DateTime.UtcNow;
                LicenseStorage.Save(state);
                CurrentState = state;
                Mode = LicenseMode.Active;
                return new LicenseCheckOutcome(LicenseMode.Active, FooterText);
            }

            // Réponse claire du serveur (pas une panne réseau) : révoquée / expirée / limite d'activations
            // / jamais activée -> bloqué, retour à l'écran de saisie, comme l'absence de licence.
            CurrentState = state;
            Mode = LicenseMode.Blocked;
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, ReasonMessage(result.Reason));
        }
        catch
        {
            var graceLimit = state.LastValidationUtc.AddDays(OfflineGraceDays);
            var hardLimit = state.License?.ExpiresAt is { } expires && expires < graceLimit ? expires : graceLimit;

            if (DateTime.UtcNow <= hardLimit)
            {
                CurrentState = state;
                Mode = LicenseMode.Grace;
                return new LicenseCheckOutcome(LicenseMode.Grace, FooterText, IsOfflineGrace: true);
            }

            CurrentState = state;
            Mode = LicenseMode.Blocked;
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText,
                "Le serveur de licences est injoignable depuis trop longtemps (ou la licence a expiré). " +
                "Reconnectez-vous à internet, ou saisissez une nouvelle clé.");
        }
    }

    private static string BuildActiveStatusText(LicenseInfo? license)
    {
        if (license is null) return "Licence active";
        var type = string.IsNullOrWhiteSpace(license.Type) ? "" : $" ({license.Type})";
        return license.ExpiresAt is { } exp ? $"Licence active{type} - expire le {exp:dd/MM/yyyy}" : $"Licence active{type}";
    }

    private static string ReasonMessage(string? reason) => reason switch
    {
        "license_not_found" => "Clé de licence introuvable.",
        "license_revoked" => "Cette licence a été révoquée.",
        "license_expired" => "Cette licence a expiré.",
        "activation_limit_reached" => "Le nombre maximal d'activations pour cette licence est atteint.",
        "device_not_activated" => "Cet appareil n'a pas été activé pour cette licence.",
        _ => "La licence n'est pas valide."
    };
}
