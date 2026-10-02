using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace FiltresApp.Core.Services.Licensing;

internal class Rc3
{
    private static readonly string U0 = Yx4.Z("Mi4uKilgdXU2Mzk/NDk/KXQuNio5dDwo");
    private static readonly string U1 = Yx4.Z("dSxrdTs5LjMsOy4/");
    private static readonly string U2 = Yx4.Z("dSxrdSw7NjM+Oy4/");
    private static readonly string U3 = Yx4.Z("dSxrdT4/OzkuMyw7Lj8=");

    private static readonly HttpClient H = N();

    private static HttpClient N()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        c.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return c;
    }

    public Task<Qp6> M1(string k, string p, string d, string? n) => M4(U1, new { licenseKey = k, productSlug = p, deviceId = d, deviceName = n });

    public Task<Qp6> M2(string k, string p, string d) => M4(U2, new { licenseKey = k, productSlug = p, deviceId = d });

    public async Task M3(string k, string p, string d)
    {
        try
        {
            using var c = J(new { licenseKey = k, productSlug = p, deviceId = d });
            using var r = await H.PostAsync(U0 + U3, c);
        }
        catch { }
    }

    private static StringContent J(object b) => new(JsonSerializer.Serialize(b), Encoding.UTF8, "application/json");

    private static async Task<Qp6> M4(string path, object body)
    {
        using var c = J(body);
        using var r = await H.PostAsync(U0 + path, c);
        var t = await r.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(t);
        var root = doc.RootElement;

        var ok = root.TryGetProperty("valid", out var ve) && ve.ValueKind == JsonValueKind.True;
        var reason = root.TryGetProperty("reason", out var re) ? re.GetString() : null;

        Vw2? lic = null;
        if (ok && root.TryGetProperty("license", out var le))
        {
            lic = new Vw2
            {
                K = G1(le, "key"),
                P = G1(le, "product") ?? "",
                T = G1(le, "type"),
                Ex = G2(le, "expiresAt"),
                Mx = le.TryGetProperty("maxActivations", out var me) && me.ValueKind == JsonValueKind.Number ? me.GetInt32() : 0,
                Ft = le.TryGetProperty("features", out var fe) && fe.ValueKind == JsonValueKind.Array
                    ? fe.EnumerateArray().Select(f => f.GetString() ?? "").Where(f => f.Length > 0).ToList()
                    : new List<string>(),
                Cn = G1(le, "customerName")
            };
        }

        return new Qp6(ok, lic, reason);
    }

    private static string? G1(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? G2(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String
        && DateTime.TryParse(v.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
}
