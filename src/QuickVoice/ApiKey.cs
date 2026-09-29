using System.Security.Cryptography;
using System.Text;

namespace QuickVoice;

/// <summary>
/// The Jev key: TYPESAFE_API_KEY from the environment, else a file in %APPDATA%\QuickVoice encrypted with
/// Windows DPAPI, so only your Windows account on this PC can read it.
/// </summary>
internal static class ApiKey
{
    public static readonly string File = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickVoice", "api-key.bin");

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("QuickVoice/Jev API key");

    public static string? Load()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("TYPESAFE_API_KEY")?.Trim();
        if (!string.IsNullOrEmpty(fromEnvironment)) return fromEnvironment;
        if (!System.IO.File.Exists(File)) return null;
        try
        {
            var key = Encoding.UTF8.GetString(ProtectedData.Unprotect(System.IO.File.ReadAllBytes(File), Entropy, DataProtectionScope.CurrentUser)).Trim();
            return key.Length > 0 ? key : null;
        }
        catch (CryptographicException)
        {
            return null;  // written by another Windows account: ask again
        }
    }

    public static void Save(string key)
    {
        key = key.Trim();
        if (key.Length == 0) throw new ArgumentException("a chave está vazia");
        Directory.CreateDirectory(Path.GetDirectoryName(File)!);
        System.IO.File.WriteAllBytes(File, ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser));
    }
}
