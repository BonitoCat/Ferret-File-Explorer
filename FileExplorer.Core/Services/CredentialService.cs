using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SecretsLib;
using FileExplorer.Core.Utilities;

namespace FileExplorer.Core.Services;

public class CredentialService
{
    private static readonly Dictionary<string, string> _sessionCache = new(StringComparer.Ordinal);
    private static bool _secretsManagerInitialized;
    private static readonly object _lock = new();

    private readonly string _fallbackFilePath;
    private readonly Lazy<byte[]> _derivedKey;

    public CredentialService()
    {
        _fallbackFilePath = AppPaths.GetDataPath(".secrets");
        _derivedKey = new(DeriveKey);
    }

    public void StoreCredential(string key, string secret)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_lock)
        {
            _sessionCache[key] = secret;
        }

        EnsureInitialized();

        try
        {
            SecretsManager.StoreSecret(key, secret);
            RemoveFallbackSecret(key);
            return;
        }
        catch
        {
        }

        StoreFallbackSecret(key, secret);
    }

    public string? GetCredential(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        lock (_lock)
        {
            if (_sessionCache.TryGetValue(key, out string? cached))
            {
                return cached;
            }
        }

        EnsureInitialized();

        try
        {
            string? secret = SecretsManager.LookupSecret(key);
            if (!string.IsNullOrEmpty(secret))
            {
                lock (_lock)
                {
                    _sessionCache[key] = secret;
                }

                return secret;
            }
        }
        catch
        {
        }

        string? fallback = GetFallbackSecret(key);
        if (!string.IsNullOrEmpty(fallback))
        {
            lock (_lock)
            {
                _sessionCache[key] = fallback;
            }

            return fallback;
        }

        return null;
    }

    public void DeleteCredential(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        lock (_lock)
        {
            _sessionCache.Remove(key);
        }

        EnsureInitialized();

        try
        {
            SecretsManager.ClearSecret(key);
        }
        catch
        {
        }

        RemoveFallbackSecret(key);
    }

    private static void EnsureInitialized()
    {
        if (_secretsManagerInitialized)
        {
            return;
        }

        lock (_lock)
        {
            if (_secretsManagerInitialized)
            {
                return;
            }

            try
            {
                SecretsManager.Initialize("org.fe.fileexplorer");
                _secretsManagerInitialized = true;
            }
            catch
            {
            }
        }
    }

    private static byte[] DeriveKey()
    {
        string machineId = "";
        try
        {
            if (File.Exists("/etc/machine-id"))
            {
                machineId = File.ReadAllText("/etc/machine-id").Trim();
            }
        }
        catch
        {
        }

        if (string.IsNullOrEmpty(machineId))
        {
            machineId = Environment.MachineName;
        }

        string user = Environment.UserName;
        byte[] salt = "fe_secure_credential_salt_v1_!@"u8.ToArray();
        string material = $"{machineId}:{user}:fe_vault";

        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(material),
            salt,
            100000,
            HashAlgorithmName.SHA256,
            32);
    }

    private void StoreFallbackSecret(string key, string secret)
    {
        lock (_lock)
        {
            Dictionary<string, string> dict = LoadFallbackFile();
            dict[key] = Encrypt(secret);
            SaveFallbackFile(dict);
        }
    }

    private string? GetFallbackSecret(string key)
    {
        lock (_lock)
        {
            Dictionary<string, string> dict = LoadFallbackFile();
            if (dict.TryGetValue(key, out string? encrypted))
            {
                try
                {
                    return Decrypt(encrypted);
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }
    }

    private void RemoveFallbackSecret(string key)
    {
        lock (_lock)
        {
            Dictionary<string, string> dict = LoadFallbackFile();
            if (dict.Remove(key))
            {
                SaveFallbackFile(dict);
            }
        }
    }

    private Dictionary<string, string> LoadFallbackFile()
    {
        if (!File.Exists(_fallbackFilePath))
        {
            return new();
        }

        try
        {
            string json = File.ReadAllText(_fallbackFilePath);
            Dictionary<string, string>? dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict != null)
            {
                return dict;
            }
        }
        catch
        {
        }

        return new();
    }

    private void SaveFallbackFile(Dictionary<string, string> dict)
    {
        try
        {
            string json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_fallbackFilePath, json);

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                File.SetUnixFileMode(_fallbackFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
        }
        catch
        {
        }
    }

    private string Encrypt(string plainText)
    {
        using Aes aes = Aes.Create();
        aes.Key = _derivedKey.Value;
        aes.GenerateIV();

        using MemoryStream ms = new();
        ms.Write(aes.IV, 0, aes.IV.Length);

        using (CryptoStream cs = new(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
        {
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
            cs.Write(plainBytes, 0, plainBytes.Length);
            cs.FlushFinalBlock();
        }

        return Convert.ToBase64String(ms.ToArray());
    }

    private string Decrypt(string cipherText)
    {
        byte[] fullCipher = Convert.FromBase64String(cipherText);

        using Aes aes = Aes.Create();
        aes.Key = _derivedKey.Value;

        byte[] iv = new byte[aes.BlockSize / 8];
        Array.Copy(fullCipher, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using MemoryStream ms = new(fullCipher, iv.Length, fullCipher.Length - iv.Length);
        using CryptoStream cs = new(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
        using StreamReader sr = new(cs, Encoding.UTF8);

        return sr.ReadToEnd();
    }
}
