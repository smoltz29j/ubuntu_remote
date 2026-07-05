using System.Windows;
using UbuntuRemote.Models;
using UbuntuRemote.Services;

namespace UbuntuRemote;

public partial class ProfileDialog : Window
{
    private readonly ConnectionProfile _profile;
    private readonly bool _isNew;

    public ConnectionProfile Profile => _profile;

    public ProfileDialog(ConnectionProfile? existing = null)
    {
        InitializeComponent();
        _isNew = existing is null;
        _profile = existing ?? new ConnectionProfile();

        if (!_isNew)
        {
            NameBox.Text = _profile.Name;
            HostBox.Text = _profile.Host;
            PortBox.Text = _profile.Port.ToString();
            UsernameBox.Text = _profile.Username;
            ClipboardCheck.IsChecked = _profile.RedirectClipboard;
            DrivesCheck.IsChecked = _profile.RedirectDrives;
            if (!string.IsNullOrEmpty(_profile.ProtectedPassword))
                PasswordHint.Visibility = Visibility.Visible;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(HostBox.Text))
        {
            MessageBox.Show(this, "ホストを入力してください。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!int.TryParse(PortBox.Text, out var port) || port < 1 || port > 65535)
        {
            MessageBox.Show(this, "ポートは 1〜65535 の数値で入力してください。", "入力エラー",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _profile.Name = NameBox.Text.Trim();
        _profile.Host = HostBox.Text.Trim();
        _profile.Port = port;
        _profile.Username = UsernameBox.Text.Trim();
        _profile.RedirectClipboard = ClipboardCheck.IsChecked == true;
        _profile.RedirectDrives = DrivesCheck.IsChecked == true;

        // 空欄なら既存パスワードを維持(新規なら空パスワード)
        if (PasswordBox.Password.Length > 0)
            _profile.ProtectedPassword = ProfileStore.ProtectPassword(PasswordBox.Password);

        DialogResult = true;
    }
}
