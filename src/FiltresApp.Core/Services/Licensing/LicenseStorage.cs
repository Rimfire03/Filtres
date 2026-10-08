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

    // Version installée (MSI) : même contenu chiffré, stocké dans le registre (HKLM) au lieu du fichier.
    // Lecture : registre d'abord, puis fichier (licence posée avant l'installation) ; écriture : registre,
    // fichier en repli si le registre refuse.
    private const string RV = "Profile";

    private static Tu5? D(byte[] c)
    {
        var p = Yx4.F(c);
        var j = Encoding.UTF8.GetString(p);
        return JsonSerializer.Deserialize<Tu5>(j);
    }

#pragma warning disable CA1416
    private static byte[]? RegRead()
    {
        try
        {
            using var k = InstallMode.OpenKey(false);
            return k?.GetValue(RV) as byte[];
        }
        catch { return null; }
    }

    private static bool RegWrite(byte[] c)
    {
        try
        {
            using var k = InstallMode.OpenKey(true);
            if (k == null) return false;
            k.SetValue(RV, c, Microsoft.Win32.RegistryValueKind.Binary);
            return true;
        }
        catch { return false; }
    }

    private static void RegDelete()
    {
        try
        {
            using var k = InstallMode.OpenKey(true);
            k?.DeleteValue(RV, false);
        }
        catch { }
    }
#pragma warning restore CA1416

    public static Tu5? L()
    {
        try
        {
            if (InstallMode.IsInstalled)
            {
                var r = RegRead();
                if (r != null) return D(r);
            }
            if (!File.Exists(P1)) return null;
            return D(File.ReadAllBytes(P1));
        }
        catch { return null; }
    }

    public static void S(Tu5 s)
    {
        var j = JsonSerializer.Serialize(s);
        var c = Yx4.E(Encoding.UTF8.GetBytes(j));
        if (InstallMode.IsInstalled && RegWrite(c))
        {
            try { if (File.Exists(P1)) File.Delete(P1); }
            catch { }
            return;
        }
        Directory.CreateDirectory(P0);
        File.WriteAllBytes(P1, c);
    }

    public static void X()
    {
        if (InstallMode.IsInstalled) RegDelete();
        try { if (File.Exists(P1)) File.Delete(P1); }
        catch { }
    }
}
