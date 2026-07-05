using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using UbuntuRemote.Controls;
using UbuntuRemote.Models;
using UbuntuRemote.Services;

namespace UbuntuRemote;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ConnectionProfile> _profiles = [];

    private bool _isFullScreen;
    private WindowState _stateBeforeFullScreen;
    private FullScreenBar? _fullScreenBar;
    private DispatcherTimer? _fullScreenBarTimer;
    private long _fullScreenBarGraceUntil;

    public MainWindow()
    {
        InitializeComponent();
        foreach (var p in ProfileStore.Load())
            _profiles.Add(p);
        ProfileList.ItemsSource = _profiles;
        Closing += (_, _) => CloseAllSessions();
        Loaded += (_, _) => ConnectFromCommandLine();
    }

    /// <summary>--connect <名前 or ホスト> で起動時に自動接続する。</summary>
    private void ConnectFromCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        var i = Array.IndexOf(args, "--connect");
        if (i < 0 || i + 1 >= args.Length)
            return;
        var key = args[i + 1];
        var profile = _profiles.FirstOrDefault(p =>
            string.Equals(p.DisplayText, key, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Host, key, StringComparison.OrdinalIgnoreCase));
        if (profile is not null)
            OpenSession(profile);
    }

    private void SaveProfiles() => ProfileStore.Save(_profiles);

    // --- プロファイル管理 ---

    private void AddProfile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProfileDialog { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _profiles.Add(dialog.Profile);
            SaveProfiles();
            ProfileList.SelectedItem = dialog.Profile;
        }
    }

    private void EditProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not ConnectionProfile profile)
            return;
        var dialog = new ProfileDialog(profile) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            SaveProfiles();
            ProfileList.Items.Refresh();
        }
    }

    private void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not ConnectionProfile profile)
            return;
        var result = MessageBox.Show(this,
            $"「{profile.DisplayText}」を削除しますか?", "確認",
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result == MessageBoxResult.Yes)
        {
            _profiles.Remove(profile);
            SaveProfiles();
        }
    }

    // --- セッション ---

    private void Connect_Click(object sender, RoutedEventArgs e) => ConnectSelected();

    private void ProfileList_MouseDoubleClick(object sender, MouseButtonEventArgs e) => ConnectSelected();

    private void ConnectSelected()
    {
        if (ProfileList.SelectedItem is not ConnectionProfile profile)
        {
            MessageBox.Show(this, "接続先を選択してください。", "Ubuntu Remote",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        OpenSession(profile);
    }

    private void OpenSession(ConnectionProfile profile)
    {
        var session = new RdpSessionView(profile);

        var headerText = new TextBlock
        {
            Text = profile.DisplayText,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
        };
        var closeButton = new Button
        {
            Content = "×",
            Width = 18,
            Height = 18,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            Background = System.Windows.Media.Brushes.Transparent,
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(headerText);
        header.Children.Add(closeButton);

        var tab = new TabItem { Header = header, Content = session };

        closeButton.Click += (_, _) => session.Close();
        session.SessionClosed += (_, _) =>
        {
            SessionTabs.Items.Remove(tab);
            UpdateEmptyState();
        };
        session.StatusChanged += (_, status) =>
        {
            headerText.Text = status == "接続済み"
                ? profile.DisplayText
                : $"{profile.DisplayText} ({status})";
        };

        if (_isFullScreen)
            tab.Visibility = Visibility.Collapsed;

        SessionTabs.Items.Add(tab);
        SessionTabs.SelectedItem = tab;
        UpdateEmptyState();

        session.Connect();
    }

    private void SessionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == SessionTabs &&
            SessionTabs.SelectedItem is TabItem { Content: RdpSessionView session })
        {
            Dispatcher.BeginInvoke(session.FocusSession);
        }
    }

    private void UpdateEmptyState()
    {
        var hasTabs = SessionTabs.Items.Count > 0;
        SessionTabs.Visibility = hasTabs ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = hasTabs ? Visibility.Collapsed : Visibility.Visible;
        // 最後のセッションが閉じたら全画面のままにしない
        if (!hasTabs && _isFullScreen)
            ExitFullScreen();
    }

    // --- 全画面表示 ---

    private void FullScreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // RDP コントロールにフォーカスがあると F11 はリモートへ送られるため、
        // ここに届くのはサイドバー等にフォーカスがあるときのみ。
        // 全画面中の解除は FullScreenBar(画面上端の接続バー)が担う
        if (e.Key == Key.F11)
        {
            ToggleFullScreen();
            e.Handled = true;
        }
    }

    public void ToggleFullScreen()
    {
        if (_isFullScreen)
            ExitFullScreen();
        else
            EnterFullScreen();
    }

    private void EnterFullScreen()
    {
        if (_isFullScreen)
            return;
        _isFullScreen = true;
        _stateBeforeFullScreen = WindowState;

        SidebarColumn.Width = new GridLength(0);
        Sidebar.Visibility = Visibility.Collapsed;
        SetTabHeadersVisible(false);
        SessionTabs.BorderThickness = new Thickness(0);
        SessionTabs.Padding = new Thickness(0);

        // タイトルバーなしで最大化するとタスクバーも覆う全画面になる。
        // 最大化状態のまま WindowStyle を変えるとサイズが更新されないため一度 Normal に戻す
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;

        var title = SessionTabs.SelectedItem is TabItem { Content: RdpSessionView s }
            ? s.Profile.DisplayText
            : "Ubuntu Remote";
        _fullScreenBar = new FullScreenBar(title, ExitFullScreen) { Owner = this };
        PositionFullScreenBar();
        _fullScreenBar.Show();

        // 入った直後はバーの存在が分かるよう数秒見せてから自動で隠す
        _fullScreenBarGraceUntil = Environment.TickCount64 + 2500;
        _fullScreenBarTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _fullScreenBarTimer.Tick += (_, _) => UpdateFullScreenBarVisibility();
        _fullScreenBarTimer.Start();

        FocusCurrentSession();
    }

    private void ExitFullScreen()
    {
        if (!_isFullScreen)
            return;
        _isFullScreen = false;

        _fullScreenBarTimer?.Stop();
        _fullScreenBarTimer = null;
        _fullScreenBar?.Close();
        _fullScreenBar = null;

        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
        if (_stateBeforeFullScreen == WindowState.Maximized)
            WindowState = WindowState.Maximized;

        SidebarColumn.Width = new GridLength(230);
        Sidebar.Visibility = Visibility.Visible;
        SetTabHeadersVisible(true);
        SessionTabs.ClearValue(BorderThicknessProperty);
        SessionTabs.ClearValue(PaddingProperty);

        FocusCurrentSession();
    }

    /// <summary>全画面中はタブ見出しを隠してリモート画面だけを表示する(内容は表示されたまま)。</summary>
    private void SetTabHeadersVisible(bool visible)
    {
        foreach (var item in SessionTabs.Items.OfType<TabItem>())
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PositionFullScreenBar()
    {
        if (_fullScreenBar is null)
            return;
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var dpi = VisualTreeHelper.GetDpi(this);
        var barWidthPx = _fullScreenBar.Width * dpi.DpiScaleX;
        _fullScreenBar.Left = (screen.Bounds.Left + (screen.Bounds.Width - barWidthPx) / 2) / dpi.DpiScaleX;
        _fullScreenBar.Top = screen.Bounds.Top / dpi.DpiScaleY;
    }

    /// <summary>
    /// WPF は WindowsFormsHost(RDP 画面)上のマウスイベントを受け取れないため、
    /// タイマーでカーソル位置を監視し、画面上端に触れたら接続バーを出す。
    /// </summary>
    private void UpdateFullScreenBarVisibility()
    {
        if (!_isFullScreen || _fullScreenBar is null)
            return;
        var cursor = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle);
        var dpi = VisualTreeHelper.GetDpi(this);
        var inGrace = Environment.TickCount64 < _fullScreenBarGraceUntil;

        if (_fullScreenBar.IsVisible)
        {
            // バーの高さ+少しの遊びより下へ離れたら隠す
            var barBottom = screen.Bounds.Top + (int)(_fullScreenBar.Height * dpi.DpiScaleY) + 8;
            if (!inGrace && (!screen.Bounds.Contains(cursor) || cursor.Y > barBottom))
                _fullScreenBar.Hide();
        }
        else if (screen.Bounds.Contains(cursor) && cursor.Y <= screen.Bounds.Top + 4)
        {
            _fullScreenBar.Show();
        }
    }

    private void FocusCurrentSession()
    {
        if (SessionTabs.SelectedItem is TabItem { Content: RdpSessionView session })
            Dispatcher.BeginInvoke(session.FocusSession);
    }

    private void CloseAllSessions()
    {
        foreach (var item in SessionTabs.Items.OfType<TabItem>().ToList())
        {
            if (item.Content is RdpSessionView session)
                session.Close();
        }
    }
}
