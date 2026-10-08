namespace FiltresApp.Core.Services.Licensing;

public static class LicenseManager
{
    private const string Pz = "filtre";

    private static readonly Rc3 Rq = new();

    private static LicenseMode _mode = LicenseMode.Blocked;

    // Une licence mise en grâce hors ligne (serveur injoignable) ne dépasse jamais sa date d'expiration
    // (démo / expiring) : le mode effectif bascule alors tout seul sur Expired, sans attendre une
    // revalidation (qui, hors ligne, ne pourrait de toute façon pas conclure autrement).
    public static LicenseMode Mode
    {
        get => _mode == LicenseMode.Grace && Cs?.L is { } l && Ex1(l) ? LicenseMode.Expired : _mode;
        private set => _mode = value;
    }

    private static Tu5? Cs { get; set; }

    /// <summary>Déclenché après chaque changement d'état (activation, validation, expiration, démo...). Peut
    /// l'être depuis n'importe quel thread : les abonnés ramènent eux-mêmes sur le thread d'interface.</summary>
    public static event Action? Changed;

    private static void Ch() { try { Changed?.Invoke(); } catch { } }

    /// <summary>Interrupteur global de build : aucun contrôle de licence ni aucun appel réseau.</summary>
    public static bool IsGlobalFree => Zq1.K2;

    /// <summary>Nom du client lu dans licence.ini (ou reçu du serveur), pour l'affichage "Licence gratuite — nom".</summary>
    private static volatile string? BypassName;

    /// <summary>Déclenché (thread quelconque) après suppression de licence.ini sur ordre du serveur.</summary>
    public static event Action? BypassRemoved;

    private static DateTime _lastPing = DateTime.MinValue;
    private static int _pingBusy;
    private static bool _installTried;

    /// <summary>Mode bypass (licence.ini présent) : ping best-effort au démarrage puis toutes les 24 h, en arrière-plan,
    /// timeout 5 s, toute erreur ignorée. Le nom du client est relu dans licence.ini avant chaque ping. Jamais de
    /// clé de licence dans l'appel. Ordre "remove_bypass" : suppression du fichier, accusé, puis événement
    /// <see cref="BypassRemoved"/> (l'application relance le flux normal de licence).</summary>
    public static async Task BypassPingAsync()
    {
        if (Zq1.K2 || !Nf8.F1()) return;
        if (System.Threading.Interlocked.Exchange(ref _pingBusy, 1) == 1) return;
        try
        {
            _lastPing = DateTime.UtcNow;
            var name = await Task.Run(Nf8.F2);
            if (name != BypassName) { BypassName = name; Ch(); }

            var d = Dv();
            var cmd = await Rq.M6(Pz, d, Environment.MachineName, name);
            if (cmd == "remove_bypass" && Nf8.Rm1())
            {
                try { await Rq.M7(Pz, d); } catch { }
                BypassName = null;
                try { BypassRemoved?.Invoke(); } catch { }
            }
        }
        catch { }
        finally { System.Threading.Interlocked.Exchange(ref _pingBusy, 0); }
    }

    // Ordre "install_bypass" reçu d'un /v1/validate réussi : une seule tentative d'écriture par lancement ;
    // échec (droits) = licence normale conservée, aucun message. Réussite : ping, puis licence gratuite
    // immédiatement (la licence stockée n'est pas effacée : ignorée tant que licence.ini existe).
    private static bool Ib1(Qp6 r)
    {
        if (!r.Ib || _installTried || Zq1.K2) return false;
        _installTried = true;
        if (!Nf8.W(r.Bn)) return false;
        BypassName = Nf8.F2();
        Mode = LicenseMode.Free;
        Ch();
        _ = BypassPingAsync();
        return true;
    }

    public static bool IsFreeLicense => Mode == LicenseMode.Free;
    public static bool IsBlocked => Mode == LicenseMode.Blocked;
    public static bool IsExpired => Mode == LicenseMode.Expired;
    public static bool IsOfflineGrace => Mode == LicenseMode.Grace;

    /// <summary>Vrai pour une démo (type "demo") en cours, pour l'affichage uniquement : l'accès aux modules
    /// ne dépend jamais du type, seulement de license.features (voir <see cref="HasFeature"/>).</summary>
    public static bool IsDemo => string.Equals(Cs?.L?.T, "demo", StringComparison.OrdinalIgnoreCase);

    public static string FooterText => Mode switch
    {
        LicenseMode.Free => BypassName is { } n ? $"Licence gratuite — {n}" : "Licence gratuite",
        LicenseMode.Active => Bt(Cs?.L),
        LicenseMode.Grace => Bt(Cs?.L) + " (mode hors ligne)",
        LicenseMode.Expired => "Licence expirée",
        LicenseMode.Blocked => "Aucune licence",
        _ => ""
    };

    /// <summary>Statut à afficher en couleur d'alerte : licence expirée, ou démo à 2 jours de la fin ou moins.</summary>
    public static bool FooterIsAlert
    {
        get
        {
            var m = Mode;
            if (m == LicenseMode.Expired) return true;
            return (m == LicenseMode.Active || m == LicenseMode.Grace) && IsDemo && Cs?.L?.Ex is { } e && Dj(e) <= 2;
        }
    }

    /// <summary>Un module n'est disponible que s'il figure dans license.features (licence valide) : aucune
    /// règle spéciale selon le type (une démo reçoit simplement la liste complète du serveur).</summary>
    public static bool HasFeature(string f)
    {
        var m = Mode;
        if (m == LicenseMode.Free) return true;
        if (m != LicenseMode.Active && m != LicenseMode.Grace) return false;
        return Cs?.L?.Ft.Contains(f) ?? false;
    }

    public static async Task<LicenseCheckOutcome> CheckAtStartupAsync()
    {
        if (Zq1.K2) { Mode = LicenseMode.Free; return new LicenseCheckOutcome(LicenseMode.Free, FooterText); }
        if (Nf8.F1())
        {
            Mode = LicenseMode.Free;
            _ = BypassPingAsync(); // arrière-plan, ne retarde jamais le démarrage
            return new LicenseCheckOutcome(LicenseMode.Free, FooterText);
        }

        var s = Nf8.L();
        if (s is null || string.IsNullOrWhiteSpace(s.K))
        {
            Cs = null;
            Mode = LicenseMode.Blocked;
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Aucune licence active. Saisissez votre clé de licence pour continuer.");
        }

        return await Vs(s);
    }

    /// <summary>Revalidation de la licence stockée (périodique, ou bouton "Réessayer" d'une licence expirée).
    /// Sans effet en licence gratuite (aucun appel réseau).</summary>
    public static async Task<LicenseCheckOutcome> RevalidateAsync()
    {
        if (Mode == LicenseMode.Free)
        {
            if (DateTime.UtcNow - _lastPing >= TimeSpan.FromHours(24)) _ = BypassPingAsync();
            return new LicenseCheckOutcome(LicenseMode.Free, FooterText);
        }

        var s = Nf8.L();
        if (s is null || string.IsNullOrWhiteSpace(s.K))
        {
            Cs = null;
            Mode = LicenseMode.Blocked;
            Ch();
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Aucune licence active. Saisissez votre clé de licence pour continuer.");
        }

        return await Vs(s);
    }

    /// <summary>Active une clé saisie. Sert aussi à "changer de licence" : la nouvelle clé est activée
    /// d'abord, l'ancienne n'est désactivée (côté serveur) qu'ensuite ; en cas d'échec, l'ancienne reste
    /// stockée et en vigueur.</summary>
    public static async Task<LicenseCheckOutcome> ActivateAsync(string key)
    {
        key = key.Trim();
        if (key.Length == 0) return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Merci de saisir une clé de licence.");

        var ex = Nf8.L();
        var d = Dv();

        try
        {
            var r = await Rq.M1(key, Pz, d, Environment.MachineName);
            if (r.Ok && r.L is not null)
            {
                var s = new Tu5 { D = d, K = key, V = DateTime.UtcNow, L = r.L };
                Nf8.S(s);
                Cs = s;
                Mode = LicenseMode.Active;

                if (ex is not null && !string.IsNullOrWhiteSpace(ex.K) && ex.K != key)
                    await Rq.M3(ex.K, Pz, ex.D);

                Ch();
                return new LicenseCheckOutcome(LicenseMode.Active, FooterText);
            }
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, Rm(r.R));
        }
        catch
        {
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Impossible de contacter le serveur de licences. Vérifiez votre connexion internet et réessayez.");
        }
    }

    /// <summary>Démo de 7 jours demandée au serveur, sans clé à saisir : le serveur crée et active la démo
    /// pour ce poste, la clé reçue est stockée directement (jamais affichée, copiée ni journalisée) et
    /// n'a pas à être activée ensuite. Exige une connexion (pas de grâce sans validation préalable).</summary>
    public static async Task<LicenseCheckOutcome> RequestDemoAsync()
    {
        if (Mode == LicenseMode.Free) return new LicenseCheckOutcome(LicenseMode.Free, FooterText);

        var ex = Nf8.L();
        var d = Dv();

        try
        {
            var r = await Rq.M5(Pz, d, Environment.MachineName);
            if (r.Ok && r.L is not null && !string.IsNullOrWhiteSpace(r.L.K))
            {
                var s = new Tu5 { D = d, K = r.L.K!, V = DateTime.UtcNow, L = r.L };
                Nf8.S(s);
                Cs = s;
                Mode = LicenseMode.Active;
                Ch();
                var info = r.L.Ex is { } e
                    ? $"Démo activée : tous les modules sont disponibles jusqu'au {Lc(e):dd/MM/yyyy}."
                    : "Démo activée : tous les modules sont disponibles.";
                return new LicenseCheckOutcome(LicenseMode.Active, FooterText, InfoMessage: info);
            }
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, Dm(r.Ok ? null : r.R));
        }
        catch
        {
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, "Impossible de joindre le serveur de licences, vérifiez votre connexion.");
        }
    }

    public static async Task DeactivateAsync()
    {
        var s = Cs ?? Nf8.L();
        if (s is not null) await Rq.M3(s.K, Pz, Dv());
        Nf8.X();
        Cs = null;
        Mode = LicenseMode.Blocked;
        Ch();
    }

    // Une fois activée avec succès au moins une fois, la licence stockée n'est effacée que sur refus
    // explicite "révoquée" / "introuvable" ; une licence expirée est conservée (une prolongation côté serveur
    // la rend de nouveau valide : voir RevalidateAsync). Serveur injoignable : grâce hors ligne avec les
    // dernières informations connues, plafonnée à la date d'expiration (démo / expiring). Un refus explicite
    // du serveur n'ouvre jamais de grâce.
    private static async Task<LicenseCheckOutcome> Vs(Tu5 s)
    {
        Qp6? r;
        try
        {
            r = await Rq.M2(s.K, Pz, Dv());
            // Licence prolongée après expiration : le serveur peut avoir oublié l'activation de ce poste.
            if (!r.Ok && r.R == "device_not_activated")
                r = await Rq.M1(s.K, Pz, Dv(), Environment.MachineName);
        }
        catch
        {
            r = null;
        }

        if (r is { Ok: true, L: not null })
        {
            // Remplace type, expiresAt, features et customerName par ceux du serveur (prolongation,
            // changement de modules) ; l'état des modules est recalculé par les abonnés de Changed.
            s.D = Dv();
            s.L = r.L;
            s.V = DateTime.UtcNow;
            Nf8.S(s);
            Cs = s;
            Mode = LicenseMode.Active;
            Ch();
            if (Ib1(r)) return new LicenseCheckOutcome(LicenseMode.Free, FooterText);
            return new LicenseCheckOutcome(LicenseMode.Active, FooterText);
        }

        if (r is not null)
        {
            if (r.R == "license_expired")
            {
                Cs = s;
                Mode = LicenseMode.Expired;
                Ch();
                return new LicenseCheckOutcome(LicenseMode.Expired, FooterText, Rm(r.R));
            }

            if (r.R is "license_revoked" or "license_not_found")
            {
                Nf8.X();
                Cs = null;
                Mode = LicenseMode.Blocked;
                Ch();
                return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, Rm(r.R));
            }

            Cs = null;
            Mode = LicenseMode.Blocked;
            Ch();
            return new LicenseCheckOutcome(LicenseMode.Blocked, FooterText, Rm(r.R));
        }

        Cs = s;
        Mode = LicenseMode.Grace;
        Ch();
        if (Mode == LicenseMode.Expired)
            return new LicenseCheckOutcome(LicenseMode.Expired, FooterText, "Licence expirée. Impossible de joindre le serveur de licences, vérifiez votre connexion.");
        return new LicenseCheckOutcome(LicenseMode.Grace, FooterText, IsOfflineGrace: true);
    }

    private static string? _dv;

    /// <summary>Identifiant du poste, identique à chaque appel au serveur (démo, activation, validation,
    /// désactivation) : SHA-256 hexadécimal de HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid (vue 64 bits).
    /// Jamais stocké, jamais aléatoire : survit à la suppression de la licence, à la réinstallation et au
    /// changement de session Windows.</summary>
#pragma warning disable CA1416 // application Windows uniquement
    private static string Dv()
    {
        if (_dv is not null) return _dv;
        using var b = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryView.Registry64);
        using var k = b.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        var g = k?.GetValue("MachineGuid") as string;
        if (string.IsNullOrWhiteSpace(g)) throw new InvalidOperationException("Identifiant du poste indisponible.");
        _dv = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(g))).ToLowerInvariant();
        System.Diagnostics.Debug.WriteLine("[licence] deviceId " + _dv[..8]);
        return _dv;
    }
#pragma warning restore CA1416

    // Date d'expiration dépassée (démo / expiring ; une licence perpétuelle n'en a pas).
    private static bool Ex1(Vw2 l) => l.Ex is { } e && e.ToUniversalTime() <= DateTime.UtcNow;

    private static DateTime Lc(DateTime e) => e.Kind == DateTimeKind.Utc ? e.ToLocalTime() : e;

    // Nombre de jours calendaires restants avant la date d'expiration (0 le jour même ou après).
    private static int Dj(DateTime e) => Math.Max(0, (Lc(e).Date - DateTime.Today).Days);

    private static string Bt(Vw2? l)
    {
        if (l is null) return "Licence active";

        if (string.Equals(l.T, "demo", StringComparison.OrdinalIgnoreCase))
        {
            if (l.Ex is not { } de) return "Démo";
            var n = Dj(de);
            return $"Démo — expire le {Lc(de):dd/MM/yyyy} (dans {n} jour{(n > 1 ? "s" : "")})";
        }

        if (l.Ex is { } e) return $"Licence — expire le {Lc(e):dd/MM/yyyy}";

        return string.IsNullOrWhiteSpace(l.Cn) ? "Licence active" : $"Licence accordée à {l.Cn}";
    }

    private static string Rm(string? r) => r switch
    {
        "license_not_found" => "Clé de licence introuvable.",
        "license_revoked" => "Cette licence a été révoquée.",
        "license_expired" => "Licence expirée.",
        "activation_limit_reached" => "Le nombre maximal d'activations pour cette licence est atteint.",
        "device_not_activated" => "Cet appareil n'a pas été activé pour cette licence.",
        _ => "La licence n'est pas valide."
    };

    private static string Dm(string? r) => r switch
    {
        "license_expired" => "La démo de ce poste est terminée. Saisissez une clé de licence.",
        "license_revoked" => "Cette démo a été désactivée. Saisissez une clé de licence.",
        "product_not_found" => "Produit introuvable sur le serveur de licences.",
        _ => "La démo n'a pas pu être activée."
    };
}
