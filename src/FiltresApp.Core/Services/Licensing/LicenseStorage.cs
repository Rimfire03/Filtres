using System.Text;
using System.Text.Json;

namespace FiltresApp.Core.Services.Licensing;

internal static class Nf8
{
    private static readonly string D0 = Yx4.Z("HDM2Lig/HjsuOw==");
    private static readonly string D1 = Yx4.Z("NjM5PzQpP3Q+Oy4=");
    private static readonly string D2 = Yx4.Z("NjM5PzQ5P3QzNDM=");

    private static string P0 => Path.Combine(AppContext.BaseDirectory, D0);
    private static string P1 => Path.Combine(P0, D1);
    private static string P2 => Path.Combine(P0, D2);

    public static bool F1() => File.Exists(P2);

    public static string? F2()
    {
        try
        {
            var t = File.ReadAllText(P2).Trim();
            return t.Length == 0 ? null : t.Split('\n')[0].Trim();
        }
        catch { return null; }
    }

    public static Tu5? L()
    {
        try
        {
            if (!File.Exists(P1)) return null;
            var c = File.ReadAllBytes(P1);
            var p = Yx4.F(c);
            var j = Encoding.UTF8.GetString(p);
            return JsonSerializer.Deserialize<Tu5>(j);
        }
        catch { return null; }
    }

    public static void S(Tu5 s)
    {
        Directory.CreateDirectory(P0);
        var j = JsonSerializer.Serialize(s);
        var c = Yx4.E(Encoding.UTF8.GetBytes(j));
        File.WriteAllBytes(P1, c);
    }

    public static void X()
    {
        try { if (File.Exists(P1)) File.Delete(P1); }
        catch { }
    }
}
