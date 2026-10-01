namespace FiltresApp.Core.Services.Licensing;

public static class LicenseManager
{
    private const string Pz = "filtre";

    private static readonly Rc3 Rq = new();

    public static LicenseMode Mode { get; private set; } = LicenseMode.Blocked;
    private static Tu5? Cs { get; set; }

    public static bool IsFreeLicense => Mode == LicenseMode.Free;
    public static bool IsBlocked => Mode == LicenseMode.Blocked;
    public static bool IsOfflineGrace => Mode == LicenseMode.Grace;

    public static string FooterText => Mode switch
    {
        LicenseMode.Free => "Licence gratuite",
        LicenseMode.Active => Bt(Cs?.L),
        LicenseMode.Grace => Bt(Cs?.L) + " (mode hors ligne)",
        LicenseMode.Blocked => "Aucune licence",
        _ => ""
    };

    public static bool HasFeature(string f) => Mode == LicenseMode.Free || (Cs?.L?.Ft.Contains(f) ?? false);

    public static async Task<LicenseCheckOutcome> CheckAtStartupAsync()
    {
        if (Zq1.K2) { Mode = LicenseMode.Free; return new LicenseCheckOutcome(LicenseMode.Free, FooterText); }
        if (Nf8.F1()) { Mode = LicenseMode.Free; return new LicenseCheckOutcome(LicenseMode.Free, FooterText); }

        var s = Nf8.L();
        if (s is null || string.IsNullOrWhiteSpace(s.K))
        {
            Cs = null;
            Mode = LicenseMode.Blocked;
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Aucune licence active. Saisissez votre clé de licence pour continuer.");
        }

        return await Vs(s);
    }

    public static async Task<LicenseCheckOutcome> ActivateAsync(string key)
    {
        key = key.Trim();
        if (key.Length == 0) return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Merci de saisir une clé de licence.");

        var ex = Nf8.L();
        var d = !string.IsNullOrWhiteSpace(ex?.D) ? ex!.D : Guid.NewGuid().ToString("N");

        try
        {
            var r = await Rq.M1(key, Pz, d, Environment.MachineName);
            if (r.Ok && r.L is not null)
            {
                var s = new Tu5 { D = d, K = key, V = DateTime.UtcNow, L = r.L };
                Nf8.S(s);
                Cs = s;
                Mode = LicenseMode.Active;
                return new LicenseCheckOutcome(LicenseMode.Active, FooterText);
            }
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, Rm(r.R));
        }
        catch
        {
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Impossible de contacter le serveur de licences. Vérifiez votre connexion internet et réessayez.");
        }
    }

    public static async Task DeactivateAsync()
    {
        var s = Cs ?? Nf8.L();
        if (s is not null) await Rq.M3(s.K, Pz, s.D);
        Nf8.X();
        Cs = null;
        Mode = LicenseMode.Blocked;
    }

    // Une fois activée avec succès au moins une fois, la licence n'est plus jamais redemandée à
    // l'utilisateur : la validation ci-dessous reste tentée à chaque lancement (pour rafraîchir les
    // fonctionnalités/la date d'expiration affichées), mais un échec - serveur injoignable, ou refus
    // explicite (révoquée/expirée/limite atteinte) - ne bloque plus jamais l'application ni ne ramène
    // à l'écran de saisie : elle continue avec les dernières informations connues localement.
    private static async Task<LicenseCheckOutcome> Vs(Tu5 s)
    {
        try
        {
            var r = await Rq.M2(s.K, Pz, s.D);
            if (r.Ok && r.L is not null)
            {
                s.L = r.L;
                s.V = DateTime.UtcNow;
                Nf8.S(s);
                Cs = s;
                Mode = LicenseMode.Active;
                return new LicenseCheckOutcome(LicenseMode.Active, FooterText);
            }
        }
        catch { }

        Cs = s;
        Mode = LicenseMode.Grace;
        return new LicenseCheckOutcome(LicenseMode.Grace, FooterText, IsOfflineGrace: true);
    }

    private static string Bt(Vw2? l)
    {
        if (l is null) return "Licence active";
        var t = string.IsNullOrWhiteSpace(l.T) ? "" : $" ({l.T})";
        return l.Ex is { } e ? $"Licence active{t} - expire le {e:dd/MM/yyyy}" : $"Licence active{t}";
    }

    private static string Rm(string? r) => r switch
    {
        "license_not_found" => "Clé de licence introuvable.",
        "license_revoked" => "Cette licence a été révoquée.",
        "license_expired" => "Cette licence a expiré.",
        "activation_limit_reached" => "Le nombre maximal d'activations pour cette licence est atteint.",
        "device_not_activated" => "Cet appareil n'a pas été activé pour cette licence.",
        _ => "La licence n'est pas valide."
    };
}
