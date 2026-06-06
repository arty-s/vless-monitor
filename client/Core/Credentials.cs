using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VlessMonitor.Core;

public enum SshAuth { Password, Key }

/// <summary>VPS access for the install wizard. Persisted encrypted via DPAPI.</summary>
public class ServerCredentials
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string User { get; set; } = "root";
    public SshAuth Auth { get; set; } = SshAuth.Password;
    public string Password { get; set; } = "";
    public string KeyPath { get; set; } = "";
    public string KeyPassphrase { get; set; } = "";
    public string HostKeyFingerprint { get; set; } = "";

    private static string StorePath =>
        Path.Combine(AppContext.BaseDirectory, "credentials.dat");

    /// <summary>Encrypt with DPAPI (current Windows user) and write to disk.</summary>
    public void Save()
    {
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(this);
            var enc = ProtectedData.Protect(json, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(StorePath, enc);
        }
        catch (Exception ex) { Logger.Error("Не удалось сохранить учётные данные", ex); }
    }

    public static ServerCredentials? Load()
    {
        try
        {
            if (!File.Exists(StorePath)) return null;
            var enc = File.ReadAllBytes(StorePath);
            var json = ProtectedData.Unprotect(enc, null, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<ServerCredentials>(json);
        }
        catch (Exception ex) { Logger.Error("Не удалось прочитать учётные данные", ex); return null; }
    }

    public static bool Exists => File.Exists(StorePath);

    public static void Delete()
    {
        try { if (File.Exists(StorePath)) File.Delete(StorePath); } catch { }
    }
}
