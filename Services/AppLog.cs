using System.IO;

namespace UbuntuRemote.Services;

/// <summary>接続トラブル調査用の簡易ログ (%APPDATA%\UbuntuRemote\app.log)。</summary>
public static class AppLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UbuntuRemote", "app.log");

    private static readonly object Lock = new();

    private const long MaxLogBytes = 1_000_000;

    static AppLog()
    {
        // 無限に肥大化しないよう、起動時に大きくなっていたら 1 世代だけ退避する
        try
        {
            var info = new FileInfo(LogPath);
            if (info.Exists && info.Length > MaxLogBytes)
            {
                var old = LogPath + ".old";
                File.Delete(old);
                File.Move(LogPath, old);
            }
        }
        catch
        {
        }
    }

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
