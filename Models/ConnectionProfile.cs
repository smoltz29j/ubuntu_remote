using System.Text.Json.Serialization;

namespace UbuntuRemote.Models;

public class ConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 3389;
    public string Username { get; set; } = "";
    public string? Domain { get; set; }

    /// <summary>DPAPI (CurrentUser) で暗号化したパスワードの Base64。</summary>
    public string? ProtectedPassword { get; set; }

    public bool RedirectClipboard { get; set; } = true;
    public bool RedirectDrives { get; set; }
    public bool UseNla { get; set; }

    [JsonIgnore]
    public string DisplayText => string.IsNullOrWhiteSpace(Name) ? $"{Username}@{Host}" : Name;

    public override string ToString() => DisplayText;
}
