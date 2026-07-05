# Ubuntu Remote

日本語 | [English](#ubuntu-remote-english)

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

xrdp を前提にチューニングしています。0.9 系(Ubuntu 24.04 標準)・0.10 系(GFX/H.264 対応)のどちらでも動作確認済みです。

- 接続種別 LAN + 帯域自動検出オフ + 32bpp を明示。これにより **0.9 系では実質最速の RemoteFX**、**GFX 対応の 0.10 系ではそのまま H.264(GFX)** がネゴシエートされる
- xrdp が非対応の **UDP トランスポートを無効化**(接続毎の無駄な待ち時間を排除)
- 自己署名証明書前提のため**サーバー証明書検証をスキップ**、NLA は既定オフ
- リサイズ追従は切断→即再接続方式(0.9 系が Display Control チャネル非対応のため。同一セッションに復帰するので 0.10 系でも実用上問題なし)

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

---

# Ubuntu Remote (English)

[日本語](#ubuntu-remote) | English

A Windows remote desktop client for connecting to Ubuntu machines running xrdp.

This WPF app was built to fix the pain points of the stock mstsc client — re-entering credentials every time, no automatic reconnection, and no way to manage multiple machines.

> macOS sibling project: [ubuntu_remote_mac](https://github.com/smoltz29j/ubuntu_remote_mac)

## Features

- **Connection profile management** — Register multiple hosts in the sidebar and connect with a double-click. Passwords are stored encrypted with DPAPI (CurrentUser)
- **Tabbed sessions** — One tab per session; connect to multiple machines at once
- **Auto login** — Automatically passes credentials through the xrdp login screen
- **Auto reconnect** — On unexpected disconnects (e.g. network drops), retries up to 5 times at 3-second intervals
- **Resolution follows window size** — After a resize settles, reconnects at the current size (xrdp restores the same session, so it's just a brief flicker)
- **Full screen** — Toggle with F11 or the sidebar button. While in full screen, move the mouse to the top edge to reveal an mstsc-style connection bar for exiting
- **Audio redirection** — Plays sound on the client if the server has `pipewire-module-xrdp` installed
- **Clipboard / drive sharing** — Can be enabled or disabled per profile
- **Command-line launch** — `UbuntuRemote.exe --connect <display name or host>` connects to the matching profile immediately

## Optimizations for xrdp

Tuned specifically for xrdp. Verified against both the 0.9 series (the default on Ubuntu 24.04) and the 0.10 series (GFX/H.264 capable).

- Explicitly sets connection type LAN + bandwidth detection off + 32bpp. With this, **xrdp 0.9 negotiates RemoteFX** (effectively its fastest codec) and **GFX-capable xrdp 0.10 negotiates H.264 (GFX)** with no extra configuration
- **Disables UDP transport**, which xrdp doesn't support (eliminates wasted wait time on every connection)
- **Skips server certificate validation** (xrdp uses self-signed certificates); NLA is off by default
- Resize tracking uses a disconnect-and-immediately-reconnect approach (because xrdp 0.9 lacks the Display Control channel; xrdp restores the same session, so this works fine on 0.10 as well)

## Requirements

- Windows 10 / 11
- .NET 10 SDK is required to build (the distributed EXE is self-contained, so no runtime is needed to run it)

## Build

```powershell
# Build
dotnet build UbuntuRemote.csproj

# Run for development
dotnet run --project .

# For distribution (self-contained → publish/UbuntuRemote.exe)
# Note: do not use -p:PublishSingleFile=true — with Windows Smart App Control
# enabled, unsigned single-file EXEs are blocked from launching
dotnet publish UbuntuRemote.csproj -c Release -r win-x64 --self-contained -o publish
```

## Usage

1. Click "新規" (New) to register a host (display name, host, port, username, password, etc.)
2. Double-click an entry in the list or click "接続" (Connect) to connect
3. Press F11 or click "全画面表示" (Full Screen) for full screen; exit via the connection bar at the top edge of the screen

### Notes on the xrdp server side

- If GNOME Remote Login (gnome-remote-desktop) is using port 3389, run xrdp on a different port (e.g. 3390) and point the profile there. The GNOME side requires NLA with separate credentials, so connecting to it by mistake results in an authentication error
- To get audio, install `pipewire-module-xrdp` on the server

## Config files and logs

| Path | Contents |
|---|---|
| `%APPDATA%\UbuntuRemote\profiles.json` | Connection profiles (passwords DPAPI-encrypted) |
| `%APPDATA%\UbuntuRemote\app.log` | Logs of connects, disconnects (with DisconnectCode), and resize reconnects |

To enable NLA, edit `UseNla` in `profiles.json` directly (there is no UI for it in the edit dialog).

## Tech stack

- WPF (net10.0-windows)
- [RoyalApps.Community.Rdp.WinForms](https://github.com/royalapplications/royalapps-community-rdp) — a wrapper around the Microsoft RDP ActiveX control (embedded in WPF via WindowsFormsHost)
