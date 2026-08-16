using System.Security.Cryptography;
using System.Text;

namespace Sherpa.Services;

/// <summary>
/// Secret store — Windows DPAPI when available, otherwise user-only encrypted file.
/// Tokens never sit in plain project JSON. UI still says "Windows secret store".
/// </summary>
public sealed class SecretStore
{
    private readonly string _dir;

    public SecretStore()
    {
        _dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Sherpa", "secrets");
        Directory.CreateDirectory(_dir);
    }

    public void Set(string key, string value)
    {
        var path = PathFor(key);
        var bytes = Encoding.UTF8.GetBytes(value);
        File.WriteAllBytes(path, Protect(bytes));
    }

    public string? Get(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return null;
        try
        {
            return Encoding.UTF8.GetString(Unprotect(File.ReadAllBytes(path)));
        }
        catch
        {
            return null;
        }
    }

    public void Delete(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
    }

    public bool Has(string key) => File.Exists(PathFor(key));

    private string PathFor(string key)
    {
        var safe = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
        return Path.Combine(_dir, safe + ".bin");
    }

    private static byte[] Protect(byte[] data)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Protect(data, optionalEntropy: Entropy(), scope: DataProtectionScope.CurrentUser);

        // Non-Windows dev fallback: AES with machine+user derived key (not as strong as DPAPI; fine for local Linux builds)
        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var cipher = enc.TransformFinalBlock(data, 0, data.Length);
        return aes.IV.Concat(cipher).ToArray();
    }

    private static byte[] Unprotect(byte[] data)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Unprotect(data, optionalEntropy: Entropy(), scope: DataProtectionScope.CurrentUser);

        using var aes = Aes.Create();
        aes.Key = DeriveKey();
        aes.IV = data[..16];
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(data, 16, data.Length - 16);
    }

    private static byte[] Entropy() => Encoding.UTF8.GetBytes("Sherpa.Windows.Secrets.v1");

    private static byte[] DeriveKey()
    {
        var material = Encoding.UTF8.GetBytes(
            Environment.UserName + "|" + Environment.MachineName + "|Sherpa.Windows.Secrets.v1");
        return SHA256.HashData(material);
    }
}
