namespace FiltresApp.Core.Services.Licensing;

internal class Vw2
{
    public string? K { get; set; }
    public string P { get; set; } = "";
    public string? T { get; set; }
    public DateTime? Ex { get; set; }
    public int Mx { get; set; }
    public List<string> Ft { get; set; } = new();
    public string? Cn { get; set; }
}

internal class Tu5
{
    public string D { get; set; } = "";
    public string K { get; set; } = "";
    public DateTime V { get; set; }
    public Vw2? L { get; set; }
}

internal record Qp6(bool Ok, Vw2? L, string? R);

public enum LicenseMode { Free, Active, Grace, Blocked }

public record LicenseCheckOutcome(LicenseMode Mode, string StatusText, string? BlockedMessage = null, bool IsOfflineGrace = false);
