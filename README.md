# Ubuntu Remote

Ubuntu マシン(xrdp)へ接続するための Windows 用リモートデスクトップクライアントです。

標準の mstsc では不便だった点 —— 毎回の接続情報入力、切断時に再接続されない、複数マシンを管理できない —— を解消するために作った WPF アプリです。

> macOS 版の姉妹プロジェクト: [ubuntu_remote_mac](https://github.com/smoltz29j/ubuntu_remote_mac)

## 機能

- **接続プロファイル管理** — 複数の接続先をサイドバーに登録し、ダブルクリックで接続。パスワードは DPAPI(CurrentUser)で暗号化して保存
- **タブ型セッション** — 1 タブ = 1 セッションで複数マシンへ同時接続
- **自動ログイン** — xrdp のログイン画面を接続情報で自動突破
- **自動再接続** — ネットワーク断などの予期しない切断時は最大 5 回、3 秒間隔で自動リトライ
- **解像度の自動追従** — ウィンドウリサイズ静止後に現在のサイズで繋ぎ直し(xrdp は同一セッションに復帰するため一瞬の暗転のみ)
- **全画面表示** — F11 またはサイドバーのボタンで切替。全画面中は画面上端にマウスを当てると接続バーが出て解除できる(mstsc 風)
- **音声リダイレクト** — サーバー側に `pipewire-module-xrdp` があればクライアントで再生
- **クリップボード / ドライブ共有** — プロファイルごとに有効・無効を設定可能
- **コマンドライン起動** — `UbuntuRemote.exe --connect <表示名 or ホスト>` で該当プロファイルへ即接続

## xrdp 向けの最適化

xrdp(特に 0.9 系、Ubuntu 24.04 標準)を前提にチューニングしています。

- GFX パイプライン非対応の xrdp 0.9 系で実質最速となる **RemoteFX** が使われるよう、接続種別 LAN + 帯域自動検出オフ + 32bpp を明示
- xrdp が非対応の **UDP トランスポートを無効化**(接続毎の無駄な待ち時間を排除)
- 自己署名証明書前提のため**サーバー証明書検証をスキップ**、NLA は既定オフ
- Display Control チャネル(動的解像度)非対応のため、リサイズ追従は切断→即再接続方式

## 動作環境

- Windows 10 / 11
- ビルドには .NET 10 SDK が必要(配布用 EXE は自己完結型のためランタイム不要)

## ビルド

```powershell
# ビルド
dotnet build UbuntuRemote.csproj

# 開発実行
dotnet run --project .

# 配布用(自己完結型 → publish/UbuntuRemote.exe)
# ※ -p:PublishSingleFile=true は Windows の Smart App Control 有効時に
#    未署名の単一ファイル EXE がブロックされるため使わない
dotnet publish UbuntuRemote.csproj -c Release -r win-x64 --self-contained -o publish
```

## 使い方

1. 「新規」で接続先を登録(表示名・ホスト・ポート・ユーザー名・パスワードなど)
2. 一覧のダブルクリックまたは「接続」ボタンで接続
3. F11 か「全画面表示」ボタンで全画面。解除は画面上端の接続バーから

### xrdp サーバー側のメモ

- GNOME リモートログイン(gnome-remote-desktop)が 3389 を使っている場合、xrdp を別ポート(例: 3390)で動かして接続先をそちらに向けること。GNOME 側は NLA 必須・認証情報も別物のため、誤って繋ぐと認証エラーになる
- 音声を鳴らすには `pipewire-module-xrdp` を導入しておく

## 設定ファイル・ログ

| パス | 内容 |
|---|---|
| `%APPDATA%\UbuntuRemote\profiles.json` | 接続プロファイル(パスワードは DPAPI 暗号化) |
| `%APPDATA%\UbuntuRemote\app.log` | 接続・切断(DisconnectCode 付き)・リサイズ再接続のログ |

NLA を有効にしたい場合は `profiles.json` の `UseNla` を直接編集します(編集ダイアログに UI はありません)。

## 技術スタック

- WPF(net10.0-windows)
- [RoyalApps.Community.Rdp.WinForms](https://github.com/royalapplications/royalapps-community-rdp) — Microsoft RDP ActiveX のラッパー(WindowsFormsHost 経由で WPF に埋め込み)
