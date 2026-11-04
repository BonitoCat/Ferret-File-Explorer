using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FileExplorer.Core.Services;
using FileExplorer.Core.Utilities;
using SecretsLib;

if (args.Contains("--system-provider"))
{
    string key = "ferret-smoke-" + Guid.NewGuid().ToString("N");
    SecretsManager.Initialize("org.fe.fileexplorer.tests");
    try
    {
        const string password = " test password ' $() ü\n";
        SecretsManager.StoreSecret(key, password);
        string? retrieved = SecretsManager.LookupSecret(key);
        Check(retrieved == password, "real Secret Service preserves whitespace and trailing newline");
        SecretsManager.ClearSecret(key);
        Check(SecretsManager.LookupSecret(key) == null, "real Secret Service deletion");
    }
    finally { SecretsManager.ClearSecret(key); }
    return;
}

string root = Path.Combine(Path.GetTempPath(), "ferret-credentials-" + Guid.NewGuid().ToString("N"));
string? oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
string? oldBusAddress = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
string? oldRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("XDG_DATA_HOME", root);
try
{
    string legacyPath = AppPaths.GetDataPath(".secrets");
    string legacy = EncryptLegacy("old password");
    File.WriteAllText(legacyPath, JsonSerializer.Serialize(new Dictionary<string, string> { ["legacy"] = legacy, ["broken"] = "invalid-ciphertext" }));
    string original = File.ReadAllText(legacyPath);

    // Point D-Bus at a socket that does not exist so libsecret cannot reach a keyring.
    string unavailableBus = "unix:path=" + Path.Combine(root, "no-bus");
    Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", unavailableBus);
    Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", root);

    CredentialService service = new();
    service.StoreCredential("session", "session password");
    Check(service.GetCredential("session") == "session password", "new credentials work in-session without provider");
    Check(File.ReadAllText(legacyPath) == original, "new credentials never write the legacy vault");
    Check(service.GetCredential("legacy") == "old password", "legacy password is readable");
    Check(File.ReadAllText(legacyPath) == original, "failed migration preserves legacy data");
    Check(service.GetCredential("broken") == null, "corrupt legacy entry is not returned");
    Check(File.ReadAllText(legacyPath) == original, "corrupt legacy entries are retained without rewriting the vault");
    service.DeleteCredential("session");
    Check(service.GetCredential("session") == null, "session credential deletion");

    Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", oldBusAddress);
    Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", oldRuntimeDir);

    if (OperatingSystem.IsLinux() && ProviderWorks())
    {
        const string specialKey = "host ' $(echo bad) ; --key";
        const string specialSecret = "secret \" $()\nwith newline\n";
        SecretsManager.StoreSecret(specialKey, specialSecret);
        Check(SecretsManager.LookupSecret(specialKey) == specialSecret, "argument isolation and secret whitespace round-trip");
        SecretsManager.ClearSecret(specialKey);
        Check(SecretsManager.LookupSecret(specialKey) == null, "provider delete and missing lookup");

        string migrateKey = "migrate-" + Guid.NewGuid().ToString("N");
        string retainKey = "retain-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(legacyPath, JsonSerializer.Serialize(new Dictionary<string, string> { [migrateKey] = legacy, [retainKey] = legacy }));
        try
        {
            Check(service.GetCredential(migrateKey) == "old password", "migration returns original password");
            Dictionary<string, string> remaining = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(legacyPath))!;
            Check(!remaining.ContainsKey(migrateKey) && remaining.ContainsKey(retainKey), "migration removes only the accepted entry");
            Check(SecretsManager.LookupSecret(migrateKey) == "old password", "migrated password persisted in provider");
            Check((File.GetUnixFileMode(legacyPath) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0, "legacy rewrite is private");
        }
        finally
        {
            try { SecretsManager.ClearSecret(migrateKey); } catch { }
        }
    }
    else
    {
        Console.WriteLine("SKIP: provider and migration-success checks require a usable Linux Secret Service.");
    }

    Console.WriteLine("All credential checks passed.");
}
finally
{
    Environment.SetEnvironmentVariable("XDG_DATA_HOME", oldData);
    Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", oldBusAddress);
    Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", oldRuntimeDir);
    Directory.Delete(root, recursive: true);
}

static bool ProviderWorks()
{
    string key = "ferret-probe-" + Guid.NewGuid().ToString("N");
    try
    {
        SecretsManager.Initialize("org.fe.fileexplorer");
        SecretsManager.StoreSecret(key, "probe");
        bool ok = SecretsManager.LookupSecret(key) == "probe";
        SecretsManager.ClearSecret(key);
        return ok;
    }
    catch
    {
        try { SecretsManager.ClearSecret(key); } catch { }
        return false;
    }
}

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}

static string EncryptLegacy(string plain)
{
    string machine = File.Exists("/etc/machine-id") ? File.ReadAllText("/etc/machine-id").Trim() : Environment.MachineName;
    if (machine.Length == 0) machine = Environment.MachineName;
    using Aes aes = Aes.Create();
    aes.Key = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes($"{machine}:{Environment.UserName}:fe_vault"),
        "fe_secure_credential_salt_v1_!@"u8.ToArray(), 100000, HashAlgorithmName.SHA256, 32);
    aes.GenerateIV();
    byte[] cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(plain), aes.IV);
    return Convert.ToBase64String(aes.IV.Concat(cipher).ToArray());
}
