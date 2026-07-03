using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms.Integration;
using System.Windows.Media;
using System.Windows.Threading;
using RoyalApps.Community.Rdp.WinForms;
using RoyalApps.Community.Rdp.WinForms.Configuration.Connection;
using RoyalApps.Community.Rdp.WinForms.Configuration.Display;
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

    private readonly ConnectionProfile _profile;
    private readonly WindowsFormsHost _host;
    private readonly RdpControl _rdp;
    private readonly Border _overlay;
    private readonly TextBlock _statusText;
    private readonly Button _retryButton;

    private int _reconnectAttempts;
    private bool _closing;

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
        _retryButton.Click += (_, _) => { _reconnectAttempts = 0; Connect(); };

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

        // ウィンドウリサイズに解像度を追従させる
        cfg.Display.ResizeBehavior = ResizeBehavior.SmartReconnect;
        cfg.Display.ColorDepth = ColorDepth.ColorDepth32Bpp;

        cfg.Redirection.RedirectClipboard = _profile.RedirectClipboard;
        cfg.Redirection.RedirectDrives = _profile.RedirectDrives;

        cfg.Performance.EnableEnhancedGraphics = true;
        cfg.Performance.EnableFontSmoothing = true;
    }

    private void Rdp_OnConnected(object? sender, ConnectedEventArgs e)
    {
        AppLog.Write($"Connected: {_profile.Host} mode={e.SessionMode}");
        Dispatcher.Invoke(() =>
        {
            _reconnectAttempts = 0;
            HideOverlay();
            StatusChanged?.Invoke(this, "接続済み");
            FocusSession();
        });
    }

    private void Rdp_OnDisconnected(object? sender, DisconnectedEventArgs e)
    {
        AppLog.Write($"Disconnected: {_profile.Host} code={e.DisconnectCode} desc='{e.Description}' userInitiated={e.UserInitiated} closing={_closing}");
        Dispatcher.Invoke(() =>
        {
            if (_closing)
                return;

            if (e.UserInitiated)
            {
                // リモート側からのサインアウトや自分での切断はセッション終了扱い
                Cleanup();
                SessionClosed?.Invoke(this, EventArgs.Empty);
                return;
            }

            StatusChanged?.Invoke(this, "切断");
            if (_reconnectAttempts < MaxAutoReconnectAttempts)
            {
                _reconnectAttempts++;
                ShowOverlay(
                    $"切断されました: {e.Description}\n自動再接続中... ({_reconnectAttempts}/{MaxAutoReconnectAttempts})",
                    showRetry: false);
                var timer = new DispatcherTimer { Interval = ReconnectDelay };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (!_closing)
                        Connect();
                };
                timer.Start();
            }
            else
            {
                ShowOverlay(
                    $"切断されました: {e.Description}\n自動再接続に失敗しました。",
                    showRetry: true);
            }
        });
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
