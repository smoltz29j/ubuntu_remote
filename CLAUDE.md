# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Ubuntu Remote** — Ubuntu マシン(xrdp)へ接続する Windows 用リモートデスクトップクライアント。標準の mstsc の使い勝手(毎回の入力、再接続なし、複数マシン管理不可)を改善するために作られた WPF アプリ。UI は日本語。

姉妹プロジェクト: `github.com/smoltz29j/ubuntu_remote_mac`(macOS 版、tkinter + sdl-freerdp ランチャー構成)。実機側の調査結果はあちらの CLAUDE.md にも蓄積されている。

## 実機 elwhite (192.168.101.201) の構成

RDP サーバーが 2 つ動いている。**繋ぐべきは 3390 の xrdp**(Ubuntu のログイン情報で入れる):

- **3389 = GNOME リモートログイン**(gnome-remote-desktop)。NLA 必須で認証情報も別物。誤って繋ぐと NLA 認証エラーになる。
- **3390 = xrdp**(`/etc/xrdp/xrdp.ini` で `port=3390`)。TLS のみ・NLA なし。xrdp 0.9.24(Ubuntu 24.04)。
- SSH (22) も開いている(`ssh smoltz@192.168.101.201`、Mac からは鍵認証済み)。サーバー側調査はこれで。
- 音声はサーバー側に pipewire-module-xrdp 導入済み(クライアントが音声リダイレクトを有効にすれば鳴る)。

## xrdp 0.9 系向けのクライアント設定(RdpSessionView.ApplyConfiguration)

- **xrdp 0.9 系は GFX パイプライン非対応で、RemoteFX が実質最速**(Mac 版で実測)。mstsc 系クライアントが RemoteFX を提示する条件は「NetworkConnectionType=LAN + BandwidthDetection=false + 32bpp」なので明示している。
- **UDP トランスポートは無効化**(`DisableUdpTransport = true`)。xrdp は UDP 非対応で、試行の待ち時間が無駄になるだけ。
- xrdp 0.9 系は Display Control チャネル非対応 → 動的解像度は効かない。さらに **`ResizeBehavior.SmartReconnect`(ActiveX の Reconnect ベース)は xrdp 相手だと切断イベントも出さず白画面のまま固まる**ため使用禁止。代わりに `SmartSizing`(リサイズ中はスケーリング)+ リサイズ静止後 800ms で自前の切断→即再接続(`ReconnectForResize`)で解像度を追従させる。xrdp への再接続は同一セッションに復帰するので体感は一瞬の暗転で済む。

## Build & Run

.NET 10 SDK はユーザースコープでインストール済み(`%LOCALAPPDATA%\Microsoft\dotnet`、ユーザー PATH に登録済み)。新しいシェルなら `dotnet` で直接呼べるが、PATH 未反映のシェルではフルパス `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"` を使う。

```powershell
# ビルド
dotnet build UbuntuRemote.csproj

# 開発実行(apphost はユーザースコープの .NET を見つけられないため DOTNET_ROOT が必要)
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
dotnet run --project .

# 配布用(自己完結型シングル EXE → publish/UbuntuRemote.exe)
dotnet publish UbuntuRemote.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

テストプロジェクトは現状なし。動作確認は起動 + 実接続で行う。デスクトップに `Ubuntu Remote.lnk`(publish 版へのショートカット)あり。

## Architecture

WPF (net10.0-windows) + `RoyalApps.Community.Rdp.WinForms`(Microsoft RDP ActiveX のラッパー)。RDP コントロールは WinForms 製なので `WindowsFormsHost` 経由で WPF に埋め込む。**csproj で `System.Windows.Forms` / `System.Drawing` の暗黙 global using を Remove している**(WPF 型との CS0104 衝突防止)。WinForms 型が必要な箇所は完全修飾で書く。

- `MainWindow` — 左サイドバー(プロファイル一覧、ObservableCollection)+ 右 TabControl(1 タブ = 1 セッション)。タブ生成・クローズはコードビハインドで行う。
- `Controls/RdpSessionView.cs` — 1 セッション分のビュー(XAML なし、コードのみ)。`RdpControl` をホストし、接続設定の適用・状態オーバーレイ・自動再接続(RDP 組み込みの `EnableAutoReconnect` で回復できなかった非ユーザー起因の切断を最大 5 回、3 秒間隔でリトライ)を担当。ウィンドウリサイズ時の解像度追従は `ReconnectForResize`(上記 xrdp セクション参照)。`Connect()` は `IsLoaded` 前だと ActiveX のハンドル未生成で失敗するため、Loaded まで遅延する。
- `Models/ConnectionProfile.cs` + `Services/ProfileStore.cs` — プロファイルは `%APPDATA%\UbuntuRemote\profiles.json` に保存。パスワードは DPAPI (CurrentUser) で暗号化した Base64 のみ永続化し、平文は保存しない。編集ダイアログでパスワード空欄 = 既存値維持、という規約。
- xrdp 前提の既定値: サーバー証明書検証なし(`AuthenticationLevel.NoAuthenticationOfServer`、xrdp は自己署名のため)、NLA は `ConnectionProfile.UseNla` で保持(既定オフ。Ubuntu の xrdp は通常 NLA 非対応)。ただし編集ダイアログに NLA の UI はなく、変更するには profiles.json を直接編集する。ユーザー名/パスワードは RDP 接続時に渡され xrdp のログイン画面が自動突破される。

## Notes

- 接続トラブルの調査は `%APPDATA%\UbuntuRemote\app.log`(`Services/AppLog.cs`)を見る。接続・切断(DisconnectCode 付き)・リサイズ再接続がすべて記録される。
- 起動引数 `--connect <表示名 or ホスト>` で該当プロファイルへ自動接続する(大文字小文字無視)。
- RoyalApps パッケージの API を調べる必要が出たら、リフレクションでダンプするのが確実(過去のダンプ手法: scratchpad に console プロジェクトを作り `GetExportedTypes()` を列挙)。
- `RdpControl` の主要 API: `RdpConfiguration`(設定ツリー)、`Connect()`/`Disconnect()`、`OnConnected`/`OnDisconnected(DisconnectedEventArgs: DisconnectCode/Description/UserInitiated)`、`FocusRdpClient()`。
