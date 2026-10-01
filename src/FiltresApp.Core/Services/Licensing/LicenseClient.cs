using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace FiltresApp.Core.Services.Licensing;

/// <summary>Appels au serveur de licences multi-produits (https://licences.tlpc.fr), sans authentification
/// (la clé de licence fait office de secret). Le produit de cette application est "filtre".</summary>
internal class LicenseClient
{
    private const string BaseUrl = "https://licences.tlpc.fr";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    public Task<LicenseApiResult> ActivateAsync(string licenseKey, string productSlug, string deviceId, string? deviceName) =>
        PostAsync("/v1/activate", new { licenseKey, productSlug, deviceId, deviceName });

    public Task<LicenseApiResult> ValidateAsync(string licenseKey, string productSlug, string deviceId) =>
        PostAsync("/v1/validate", new { licenseKey, productSlug, deviceId });

    /// <summary>Libère l'activation de ce poste. Best-effort : un échec réseau ne doit jamais empêcher la
    /// déconnexion locale (voir LicenseManager.DeactivateAsync, qui efface le fichier local dans tous les cas).</summary>
    public async Task DeactivateAsync(string licenseKey, string productSlug, string deviceId)
    {
        try
        {
            using var content = JsonContent(new { licenseKey, productSlug, deviceId });
            using var response = await Http.PostAsync(BaseUrl + "/v1/deactivate", content);
        }
        catch
        {
            // best-effort, voir ci-dessus.
        }
    }

    private static StringContent JsonContent(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static async Task<LicenseApiResult> PostAsync(string path, object body)
    {
        using var content = JsonContent(body);
        using var response = await Http.PostAsync(BaseUrl + path, content);
        var text = await response.Content.ReadAsStringAsync();

        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;

        var valid = root.TryGetProperty("valid", out var validEl) && validEl.ValueKind == JsonValueKind.True;
        var reason = root.TryGetProperty("reason", out var reasonEl) ? reasonEl.GetString() : null;

        LicenseInfo? license = null;
        if (valid && root.TryGetProperty("license", out var licEl))
        {
            license = new LicenseInfo
            {
                Key = GetString(licEl, "key"),
                Product = GetString(licEl, "product") ?? "",
                Type = GetString(licEl, "type"),
                ExpiresAt = GetDate(licEl, "expiresAt"),
                MaxActivations = licEl.TryGetProperty("maxActivations", out var maxEl) && maxEl.ValueKind == JsonValueKind.Number ? maxEl.GetInt32() : 0,
                Features = licEl.TryGetProperty("features", out var featEl) && featEl.ValueKind == JsonValueKind.Array
                    ? featEl.EnumerateArray().Select(f => f.GetString() ?? "").Where(f => f.Length > 0).ToList()
                    : new List<string>()
            };
        }

        return new LicenseApiResult(valid, license, reason);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTime? GetDate(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            && DateTime.TryParse(value.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
            ? parsed
            : null;
}
