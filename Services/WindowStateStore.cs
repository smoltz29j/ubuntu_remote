using System.IO;
using System.Text.Json;

namespace UbuntuRemote.Services;

/// <summary>前回終了時のメインウィンドウ位置・サイズ。</summary>
public class WindowPlacement
{
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsMaximized { get; set; }
}

/// <summary>ウィンドウ位置を %APPDATA%\UbuntuRemote\window.json に保存/復元する。</summary>
public static class WindowStateStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UbuntuRemote", "window.json");

    public static WindowPlacement? Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;
            return JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(FilePath));
        }
        catch
        {
            return null;
        }
    }

    public static void Save(WindowPlacement placement)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(placement));
        }
        catch
        {
        }
    }
}
