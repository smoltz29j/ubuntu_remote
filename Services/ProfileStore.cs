using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UbuntuRemote.Models;

namespace UbuntuRemote.Services;

public static class ProfileStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UbuntuRemote");

    private static readonly string FilePath = Path.Combine(Dir, "profiles.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static List<ConnectionProfile> Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return [];
            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<List<ConnectionProfile>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IEnumerable<ConnectionProfile> profiles)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var json = JsonSerializer.Serialize(profiles.ToList(), JsonOptions);
            // 書き込み途中のクラッシュで profiles.json を壊さないよう tmp 経由で置き換える
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppLog.Write($"Profile save failed: {ex}");
        }
    }

    public static string ProtectPassword(string plain)
    {
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string UnprotectPassword(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64))
            return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedBase64), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return "";
        }
    }
}
