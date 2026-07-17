using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Threading;
using RoyalApps.Community.Rdp.WinForms;
using RoyalApps.Community.Rdp.WinForms.Configuration.Connection;
using RoyalApps.Community.Rdp.WinForms.Configuration.Display;
using RoyalApps.Community.Rdp.WinForms.Configuration.Performance;
using RoyalApps.Community.Rdp.WinForms.Controls.Clients;
using RoyalApps.Community.Rdp.WinForms.Controls.Events;
using UbuntuRemote.Models;
using UbuntuRemote.Services;
using Button = System.Windows.Controls.Button;

namespace UbuntuRemote.Controls;

/// <summary>
/// 1 つの RDP セッションを表示する WPF ビュー。
/// 予期しない切断時は自動で再接続を試みる。
/// </summary>
public class RdpSessionView : Grid
{
    private const int MaxAutoReconnectAttempts = 5;
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ResizeDebounce = TimeSpan.FromMilliseconds(800);

    private readonly ConnectionProfile _profile;
    private readonly WindowsFormsHost _host;
    private readonly RdpControl _rdp;
    private readonly Border _overlay;
    private readonly TextBlock _statusText;
    private readonly Button _retryButton;

    private readonly DispatcherTimer _resizeTimer;
    private DispatcherTimer? _reconnectTimer;
    private int _reconnectAttempts;
    private bool _closing;
    private bool _connected;
    private bool _resizeReconnectPending;

    /// <summary>セッションが完全に終了した(タブを閉じてよい)ときに発生。</summary>
    public event EventHandler? SessionClosed;

    /// <summary>接続状態の変化(タブ見出しの更新用)。</summary>
    public event EventHandler<string>? StatusChanged;

    public ConnectionProfile Profile => _profile;

    public RdpSessionView(ConnectionProfile profile)
    {
        _profile = profile;

        _rdp = new RdpControl { Dock = System.Windows.Forms.DockStyle.Fill };
        _rdp.OnConnected += Rdp_OnConnected;
        _rdp.OnDisconnected += Rdp_OnDisconnected;

        _host = new WindowsFormsHost { Child = _rdp };
        Children.Add(_host);

        _statusText = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 16,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        _retryButton = new Button
        {
            Content = "再接続",
            Margin = new Thickness(0, 16, 0, 0),
            Padding = new Thickness(24, 6, 24, 6),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Visibility = Visibility.Collapsed,
        };
        _retryButton.Click += (_, _) =>
        {
            StopReconnectTimer();
            _reconnectAttempts = 0;
            Connect();
        };

        var panel = new StackPanel
        {
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
        };
        panel.Children.Add(_statusText);
        panel.Children.Add(_retryButton);

        _overlay = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(230, 30, 30, 30)),
            Child = panel,
            Visibility = Visibility.Collapsed,
        };
        Children.Add(_overlay);

        // リサイズが落ち着いてから解像度を合わせて再接続する(下記 ReconnectForResize 参照)
        _resizeTimer = new DispatcherTimer { Interval = ResizeDebounce };
        _resizeTimer.Tick += (_, _) =>
        {
            _resizeTimer.Stop();
            ReconnectForResize();
        };
        SizeChanged += (_, _) =>
        {
            if (_connected && !_closing)
            {
                _resizeTimer.Stop();
                _resizeTimer.Start();
            }
        };
    }

    public void Connect()
    {
        // WindowsFormsHost(と RDP ActiveX)のハンドルはビジュアルツリーに
        // 載ってから生成されるため、Loaded 前の Connect は失敗する
        if (!IsLoaded)
        {
            AppLog.Write($"Connect deferred until Loaded: {_profile.Host}");
            RoutedEventHandler? once = null;
            once = (_, _) =>
            {
                Loaded -= once;
                DoConnect();
            };
            Loaded += once;
            return;
        }
        DoConnect();
    }

    private void DoConnect()
    {
        ApplyConfiguration();
        ShowOverlay($"{_profile.Host} に接続しています...", showRetry: false);
        StatusChanged?.Invoke(this, "接続中");
        AppLog.Write($"Connect: {_profile.Username}@{_profile.Host}:{_profile.Port} nla={_profile.UseNla}");
        try
        {
            _rdp.Connect();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Connect threw: {ex}");
            ShowOverlay($"接続に失敗しました:\n{ex.Message}", showRetry: true);
            StatusChanged?.Invoke(this, "切断");
        }
    }

    /// <summary>ユーザー操作によるセッション終了(タブを閉じる等)。</summary>
    public void Close()
    {
        _closing = true;
        _resizeTimer.Stop();
        StopReconnectTimer();
        try
        {
            _rdp.Disconnect();
        }
        catch
        {
            // 未接続時の Disconnect は失敗してよい
        }
        Cleanup();
        SessionClosed?.Invoke(this, EventArgs.Empty);
    }

    public void FocusSession()
    {
        try
        {
            _rdp.FocusRdpClient();
        }
        catch
        {
        }
    }

    private void ApplyConfiguration()
    {
        var cfg = _rdp.RdpConfiguration ??= new RoyalApps.Community.Rdp.WinForms.Configuration.RdpClientConfiguration();
        cfg.Server = _profile.Host;
        cfg.Port = _profile.Port;
        cfg.Credentials.Username = _profile.Username;
        cfg.Credentials.Domain = _profile.Domain ?? "";
        cfg.Credentials.Password = new RoyalApps.Community.Rdp.WinForms.Configuration.SensitiveString(
            ProfileStore.UnprotectPassword(_profile.ProtectedPassword));
        cfg.Credentials.NetworkLevelAuthentication = _profile.UseNla;

        // xrdp は自己署名証明書のためサーバー認証は行わない
        cfg.Security.AuthenticationLevel = AuthenticationLevel.NoAuthenticationOfServer;

        // ネットワーク断からの回復は RDP 組み込みの自動再接続に任せ、
        // それでも切れた場合はこのクラスの再試行ロジックが引き継ぐ
        cfg.Connection.EnableAutoReconnect = true;
        cfg.Connection.MaxReconnectAttempts = 20;

        // xrdp は UDP トランスポート非対応。有効のままだと接続毎に UDP を試して待たされる
        cfg.Connection.DisableUdpTransport = true;

        // RDP の一括圧縮(mstsc は既定で有効だがこのライブラリの既定は無効)。
        // RemoteFX でエンコードされない旧来型の画面更新やクリップボード転送の転送量を減らす
        cfg.Connection.Compression = true;

        // 再送済みビットマップをクライアント側にキャッシュして再描画を省く
        cfg.Performance.BitmapCaching = true;

        // リサイズ中はスケーリング表示で凌ぎ、静止後に ReconnectForResize が解像度を合わせる。
        // SmartReconnect(ActiveX の Reconnect ベース)は xrdp 0.9 相手だと
        // 切断イベントも出さずに白画面のまま固まるため使わない
        cfg.Display.ResizeBehavior = ResizeBehavior.SmartSizing;
        cfg.Display.ColorDepth = ColorDepth.ColorDepth32Bpp;

        cfg.Redirection.RedirectClipboard = _profile.RedirectClipboard;
        cfg.Redirection.RedirectDrives = _profile.RedirectDrives;

        // 音声はクライアント側で再生する(サーバーは pipewire-module-xrdp 導入済み)
        cfg.Redirection.AudioRedirectionMode = AudioRedirectionMode.RedirectToClient;
        cfg.Redirection.AudioQualityMode = AudioQualityMode.High;

        // xrdp 0.9 系は GFX パイプライン非対応で、コーデックは RemoteFX が実質最速
        // (Mac 版で実測済み)。mstsc 系クライアントが RemoteFX を提示するのは
        // 「接続種別 LAN + 帯域自動検出オフ」の組み合わせのときなので明示する
        cfg.Performance.NetworkConnectionType = NetworkConnectionType.LAN;
        cfg.Performance.BandwidthDetection = false;
        cfg.Performance.EnableEnhancedGraphics = true;
        cfg.Performance.EnableFontSmoothing = true;
        cfg.Performance.EnableHardwareMode = true;
    }

    /// <summary>
    /// ウィンドウリサイズ静止後に、セッション解像度を現在のコントロールサイズへ合わせる。
    /// xrdp 0.9 系は Display Control チャネル(動的解像度)非対応なので、
    /// いったん切断して現サイズで繋ぎ直す(xrdp への再接続は同一セッションに復帰する)。
    /// </summary>
    private void ReconnectForResize()
    {
        if (_closing || !_connected)
            return;
        var client = _rdp.RdpClient;
        if (client is null || client.ConnectionState != ConnectionState.Connected)
            return;
        var target = _rdp.ClientSize;
        if (target.Width < 100 || target.Height < 100)
            return;
        // 数 px の違いで再接続を繰り返さないよう遊びを持たせる
        if (Math.Abs(client.DesktopWidth - target.Width) < 8 &&
            Math.Abs(client.DesktopHeight - target.Height) < 8)
            return;

        AppLog.Write($"Resize reconnect: {client.DesktopWidth}x{client.DesktopHeight} -> {target.Width}x{target.Height} {_profile.Host}");
        _resizeReconnectPending = true;
        ShowOverlay("解像度を変更しています...", showRetry: false);
        try
        {
            _rdp.Disconnect();
        }
        catch (Exception ex)
        {
            AppLog.Write($"Resize disconnect threw: {ex.Message}");
            _resizeReconnectPending = false;
            HideOverlay();
        }
    }

    private void Rdp_OnConnected(object? sender, ConnectedEventArgs e)
    {
        AppLog.Write($"Connected: {_profile.Host} mode={e.SessionMode}");
        Dispatcher.Invoke(() =>
        {
            _connected = true;
            _reconnectAttempts = 0;
            HideOverlay();
            StatusChanged?.Invoke(this, "接続済み");
            FocusSession();
            // 接続処理中にウィンドウサイズが変わっていた場合に備えて一度だけ確認
            _resizeTimer.Stop();
            _resizeTimer.Start();
        });
    }

    private void Rdp_OnDisconnected(object? sender, DisconnectedEventArgs e)
    {
        AppLog.Write($"Disconnected: {_profile.Host} code={e.DisconnectCode} desc='{e.Description}' userInitiated={e.UserInitiated} closing={_closing}");
        Dispatcher.Invoke(() =>
        {
            _connected = false;
            if (_closing)
                return;

            if (_resizeReconnectPending)
            {
                // ReconnectForResize による意図的な切断。すぐ現サイズで繋ぎ直す
                _resizeReconnectPending = false;
                Connect();
                return;
            }

            if (e.UserInitiated)
            {
                // リモート側からの切断(ログオフや、別クライアントによるセッション引き継ぎ等)。
                // 黙ってタブを閉じると理由が分からないため、理由を表示してタブは残す
                StatusChanged?.Invoke(this, "切断");
                ShowOverlay($"リモート側でセッションが終了しました:\n{e.Description}", showRetry: true);
                return;
            }

            StatusChanged?.Invoke(this, "切断");
            if (_reconnectAttempts < MaxAutoReconnectAttempts)
            {
                _reconnectAttempts++;
                ShowOverlay(
                    $"切断されました: {e.Description}\n自動再接続中... ({_reconnectAttempts}/{MaxAutoReconnectAttempts})",
                    showRetry: false);
                StopReconnectTimer();
                _reconnectTimer = new DispatcherTimer { Interval = ReconnectDelay };
                _reconnectTimer.Tick += (_, _) =>
                {
                    StopReconnectTimer();
                    if (_closing)
                        return;
                    // SmartReconnect(リサイズ時の内部再接続)や RDP 組み込みの自動再接続が
                    // 先に回復させていたら、こちらから重ねて Connect しない
                    if (_rdp.RdpClient?.ConnectionState is ConnectionState.Connected or ConnectionState.Connecting)
                    {
                        AppLog.Write($"Manual reconnect skipped (already {_rdp.RdpClient.ConnectionState}): {_profile.Host}");
                        return;
                    }
                    Connect();
                };
                _reconnectTimer.Start();
            }
            else
            {
                ShowOverlay(
                    $"切断されました: {e.Description}\n自動再接続に失敗しました。",
                    showRetry: true);
            }
        });
    }

    private void StopReconnectTimer()
    {
        _reconnectTimer?.Stop();
        _reconnectTimer = null;
    }

    private void ShowOverlay(string message, bool showRetry)
    {
        _statusText.Text = message;
        _retryButton.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
        _overlay.Visibility = Visibility.Visible;
    }

    private void HideOverlay() => _overlay.Visibility = Visibility.Collapsed;

    private void Cleanup()
    {
        try
        {
            _rdp.OnConnected -= Rdp_OnConnected;
            _rdp.OnDisconnected -= Rdp_OnDisconnected;
            _host.Child = null;
            _rdp.Dispose();
            _host.Dispose();
        }
        catch
        {
        }
    }
}
