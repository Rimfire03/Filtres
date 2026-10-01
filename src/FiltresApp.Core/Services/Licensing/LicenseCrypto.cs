using System.Security.Cryptography;

namespace FiltresApp.Core.Services.Licensing;

/// <summary>Chiffrement AES du fichier de licence local, avec une clé embarquée dans l'exécutable : évite
/// qu'un fichier JSON en clair soit lu ou modifié à la main dans un éditeur de texte. Ce n'est pas un
/// secret cryptographique fort (la clé voyage nécessairement avec le binaire distribué) - l'objectif est
/// d'empêcher une lecture/modification triviale, pas de résister à une rétro-ingénierie poussée.</summary>
internal static class LicenseCrypto
{
    private static readonly byte[] Key =
    {
        0x4A, 0x1F, 0x6D, 0x92, 0x3B, 0xC7, 0x58, 0xE1,
        0x0D, 0x77, 0xA4, 0x2E, 0x9C, 0x63, 0xF0, 0x15,
        0x8B, 0x2A, 0x5D, 0xE9, 0x41, 0xC6, 0x0A, 0x73,
        0xB8, 0x3F, 0x16, 0xD2, 0x6E, 0x94, 0x5C, 0xA1
    };

    private static readonly byte[] Iv =
    {
        0x7C, 0x1E, 0x4B, 0x9A, 0x33, 0xD6, 0x58, 0x02,
        0xE7, 0x4F, 0x8A, 0x21, 0x6D, 0xB3, 0x90, 0x5E
    };

    public static byte[] Encrypt(byte[] plain)
    {
        using var aes = Aes.Create();
        aes.Key = Key;
        aes.IV = Iv;
        using var encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plain, 0, plain.Length);
    }

    public static byte[] Decrypt(byte[] cipher)
    {
        using var aes = Aes.Create();
        aes.Key = Key;
        aes.IV = Iv;
        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
    }
}
