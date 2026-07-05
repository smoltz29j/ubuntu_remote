using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UbuntuRemote.Controls;
using UbuntuRemote.Models;
using UbuntuRemote.Services;

namespace UbuntuRemote;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<ConnectionProfile> _profiles = [];

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
