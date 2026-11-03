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
string? oldPath = Environment.GetEnvironmentVariable("PATH");
Directory.CreateDirectory(root);
Environment.SetEnvironmentVariable("XDG_DATA_HOME", root);
try
{
    string legacyPath = AppPaths.GetDataPath(".secrets");
    string legacy = EncryptLegacy("old password");
    File.WriteAllText(legacyPath, JsonSerializer.Serialize(new Dictionary<string, string> { ["legacy"] = legacy, ["broken"] = "invalid-ciphertext" }));
    string original = File.ReadAllText(legacyPath);
    Environment.SetEnvironmentVariable("PATH", root); // Never touch a real keyring.
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

    if (OperatingSystem.IsLinux())
    {
        Environment.SetEnvironmentVariable("FERRET_TEST_VAULT", Path.Combine(root, "provider.json"));
        string tool = Path.Combine(root, "secret-tool");
        File.WriteAllText(tool, """
            #!/usr/bin/python3
            import json, os, sys, time
            mode = os.environ.get('FERRET_TEST_PROVIDER_MODE', '')
            if mode == 'timeout': time.sleep(30)
            if mode == 'locked':
                sys.stderr.write('locked')
                sys.exit(1)
            args = sys.argv[1:]
            action = args.pop(0)
            if action == 'store': args.pop(0)
            assert args[0] == 'application' and args[1] == 'org.fe.fileexplorer' and args[2] == 'key'
            key = args[3]
            path = os.environ['FERRET_TEST_VAULT']
            data = json.load(open(path)) if os.path.exists(path) else {}
            if action == 'store': data[key] = sys.stdin.read()
            elif action == 'lookup':
                if key not in data: sys.exit(1)
                sys.stdout.write(data[key])
            elif action == 'clear': data.pop(key, None)
            with open(path, 'w') as f: json.dump(data, f)
            """.Replace("\r\n", "\n") + "\n");
        File.SetUnixFileMode(tool, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        SecretsManager.Initialize("org.fe.fileexplorer");
        const string specialKey = "host ' $(echo bad) ; --key";
        const string specialSecret = "secret \" $()\nwith newline\n";
        SecretsManager.StoreSecret(specialKey, specialSecret);
        Check(SecretsManager.LookupSecret(specialKey) == specialSecret, "argument isolation and secret whitespace round-trip");
        SecretsManager.ClearSecret(specialKey);
        Check(SecretsManager.LookupSecret(specialKey) == null, "provider delete and missing lookup");

        File.WriteAllText(legacyPath, JsonSerializer.Serialize(new Dictionary<string, string> { ["migrate"] = legacy, ["retain"] = legacy }));
        Check(service.GetCredential("migrate") == "old password", "migration returns original password");
        Dictionary<string, string> remaining = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(legacyPath))!;
        Check(!remaining.ContainsKey("migrate") && remaining.ContainsKey("retain"), "migration removes only the accepted entry");
        Check(SecretsManager.LookupSecret("migrate") == "old password", "migrated password persisted in provider");
        Check((File.GetUnixFileMode(legacyPath) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0, "legacy rewrite is private");

        Environment.SetEnvironmentVariable("FERRET_TEST_PROVIDER_MODE", "locked");
        original = File.ReadAllText(legacyPath);
        Check(service.GetCredential("retain") == "old password", "locked keyring permits legacy session use");
        Check(File.ReadAllText(legacyPath) == original, "locked keyring retains migration source");
        service.StoreCredential("locked-session", "temporary");
        Check(service.GetCredential("locked-session") == "temporary", "locked keyring uses session cache");
        Check(File.ReadAllText(legacyPath) == original, "locked keyring does not create fallback writes");

        Environment.SetEnvironmentVariable("FERRET_TEST_PROVIDER_MODE", "timeout");
        DateTime started = DateTime.UtcNow;
        try { SecretsManager.LookupSecret("slow"); throw new Exception("Expected timeout"); }
        catch (TimeoutException) { }
        Check(DateTime.UtcNow - started < TimeSpan.FromSeconds(10), "provider timeout is bounded");
    }
    else
        Console.WriteLine("SKIP: Linux provider and migration-success checks require Linux.");
    Console.WriteLine("All credential checks passed.");
}
finally
{
    Environment.SetEnvironmentVariable("XDG_DATA_HOME", oldData);
    Environment.SetEnvironmentVariable("PATH", oldPath);
    Environment.SetEnvironmentVariable("FERRET_TEST_VAULT", null);
    Environment.SetEnvironmentVariable("FERRET_TEST_PROVIDER_MODE", null);
    Directory.Delete(root, recursive: true);
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
