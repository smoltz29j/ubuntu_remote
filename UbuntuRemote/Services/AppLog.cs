using System.IO;

namespace UbuntuRemote.Services;

/// <summary>接続トラブル調査用の簡易ログ (%APPDATA%\UbuntuRemote\app.log)。</summary>
public static class AppLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UbuntuRemote", "app.log");

    private static readonly object Lock = new();

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }
}
