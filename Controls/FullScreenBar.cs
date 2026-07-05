using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace UbuntuRemote.Controls;

/// <summary>
/// 全画面表示中に画面上端へ表示する接続バー(mstsc の接続バー相当)。
/// RDP コントロールがフォーカスを持つと F11 がリモートへ送られて効かないため、
/// 全画面解除の手段としてマウスだけで操作できるバーを別ウィンドウで重ねる。
/// </summary>
public class FullScreenBar : Window
{
    public FullScreenBar(string title, Action exitFullScreen)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        ResizeMode = ResizeMode.NoResize;
        Width = 340;
        Height = 34;

        var text = new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 10, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var exitButton = new Button
        {
            Content = "全画面解除",
            Padding = new Thickness(12, 3, 12, 3),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        exitButton.Click += (_, _) => exitFullScreen();

        var panel = new DockPanel();
        DockPanel.SetDock(exitButton, Dock.Right);
        panel.Children.Add(exitButton);
        panel.Children.Add(text);

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 45, 45, 45)),
            CornerRadius = new CornerRadius(0, 0, 8, 8),
            Child = panel,
        };
    }
}
