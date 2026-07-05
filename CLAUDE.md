# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Ubuntu Remote** — Ubuntu マシン(xrdp)へ接続する Windows 用リモートデスクトップクライアント。標準の mstsc の使い勝手(毎回の入力、再接続なし、複数マシン管理不可)を改善するために作られた WPF アプリ。UI は日本語。

姉妹プロジェクト: `github.com/smoltz29j/ubuntu_remote_mac`(macOS 版、tkinter + sdl-freerdp ランチャー構成)。実機側の調査結果はあちらの CLAUDE.md にも蓄積されている。

## 実機 elwhite (192.168.101.201) の構成

RDP サーバーが 2 つ動いている。**繋ぐべきは 3390 の xrdp**(Ubuntu のログイン情報で入れる):

- **3389 = GNOME リモートログイン**(gnome-remote-desktop)。NLA 必須で認証情報も別物。誤って繋ぐと NLA 認証エラーになる。
- **3390 = xrdp 0.10.6(ソースビルド、GFX/H.264 対応)**。2026-07-05 に apt の 0.9.24 から移行済み。`--prefix=/usr/local` でビルドされ `--enable-x264 --enable-opus --enable-pixman --enable-fuse` 付き。稼働中の設定は **`/usr/local/etc/xrdp/xrdp.ini`**(`port=3390`、`security_layer=negotiate`)と `/usr/local/etc/xrdp/gfx.toml`。`/etc/xrdp/xrdp.ini` は旧 apt 版の残骸なので見ない(apt パッケージ xrdp 0.9.24 は hold 状態で残っているが未使用)。systemd ユニットは `/usr/local/lib/systemd/system/xrdp.service`。
- Ubuntu Remote からの接続で **GFX + H.264 がネゴシエートされることを実機ログで確認済み**(`/var/log/xrdp.log` に `client supports gfx protocol` → `Codec search order is H264, RFX` → `Matched H264 mode`)。クライアント側の変更なしで到達している。
- SSH (22) も開いている(`ssh smoltz@192.168.101.201`、Mac とこの Windows マシンの両方から鍵認証済み・パスワード不要)。サーバー側調査はこれで。
- 音声はサーバー側に pipewire-module-xrdp 導入済み(クライアントが音声リダイレクトを有効にすれば鳴る)。

## xrdp 向けのクライアント設定(RdpSessionView.ApplyConfiguration)

- **「NetworkConnectionType=LAN + BandwidthDetection=false + 32bpp」を明示**している。もともとは GFX 非対応の xrdp 0.9 系で実質最速の RemoteFX を引き出すための設定だが、0.10 系(GFX/H.264)に対してもこの設定のまま H.264 がネゴシエートされる(上記の実機ログで確認)ので変更不要。
- **UDP トランスポートは無効化**(`DisableUdpTransport = true`)。xrdp は UDP 非対応で、試行の待ち時間が無駄になるだけ。
- **一括圧縮(`Connection.Compression`)とビットマップキャッシュ(`Performance.BitmapCaching`)はこのライブラリでは既定 off なので明示的に on**(mstsc の既定に合わせる)。GFX/RemoteFX 部分への効果は限定的だが、旧来型更新やクリップボード転送で効く。
- リサイズ追従は `SmartSizing`(リサイズ中はスケーリング)+ リサイズ静止後 800ms で自前の切断→即再接続(`ReconnectForResize`)。xrdp への再接続は同一セッションに復帰するので体感は一瞬の暗転で済む。この方式にした経緯: xrdp 0.9 系は Display Control チャネル(動的解像度)非対応、かつ **`ResizeBehavior.SmartReconnect`(ActiveX の Reconnect ベース)は xrdp 相手だと切断イベントも出さず白画面のまま固まる**ため使用禁止。xrdp 0.10 は Display Control チャネル対応なので動的解像度(再接続なしのリサイズ追従)に切り替えられる可能性があるが、未検証。現方式は 0.10 でも問題なく動作している。

## Build & Run

.NET 10 SDK はユーザースコープでインストール済み(`%LOCALAPPDATA%\Microsoft\dotnet`、ユーザー PATH に登録済み)。新しいシェルなら `dotnet` で直接呼べるが、PATH 未反映のシェルではフルパス `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"` を使う。

```powershell
# ビルド
dotnet build UbuntuRemote.csproj

# 開発実行(apphost はユーザースコープの .NET を見つけられないため DOTNET_ROOT が必要)
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
dotnet run --project .

# 配布用(自己完結型フォルダー publish → publish/UbuntuRemote.exe)
# 注意: -p:PublishSingleFile=true は使わない。Smart App Control が未署名の
# 単一ファイルバンドルをブロックして起動できない(フォルダー形式の apphost EXE は通る)
dotnet publish UbuntuRemote.csproj -c Release -r win-x64 --self-contained -o publish
```

テストプロジェクトは現状なし。動作確認は起動 + 実接続で行う。デスクトップに `Ubuntu Remote.lnk`(publish 版へのショートカット)あり。

## Architecture

WPF (net10.0-windows) + `RoyalApps.Community.Rdp.WinForms`(Microsoft RDP ActiveX のラッパー)。RDP コントロールは WinForms 製なので `WindowsFormsHost` 経由で WPF に埋め込む。**csproj で `System.Windows.Forms` / `System.Drawing` の暗黙 global using を Remove している**(WPF 型との CS0104 衝突防止)。WinForms 型が必要な箇所は完全修飾で書く。

- `MainWindow` — 左サイドバー(プロファイル一覧、ObservableCollection)+ 右 TabControl(1 タブ = 1 セッション)。タブ生成・クローズはコードビハインドで行う。全画面表示(F11 / サイドバーのボタン)もここで管理: WindowStyle=None + Maximized でタスクバーごと覆い、サイドバーとタブ見出しを隠す。RDP コントロールにフォーカスがあると F11 はリモートへ送られるため、解除手段として `Controls/FullScreenBar.cs`(mstsc 風の画面上端バー、別 Window・Topmost)を用意。WindowsFormsHost 上では WPF がマウスイベントを受け取れないので、バーの出し入れはタイマーでカーソル位置を監視して行う。
- `Controls/RdpSessionView.cs` — 1 セッション分のビュー(XAML なし、コードのみ)。`RdpControl` をホストし、接続設定の適用・状態オーバーレイ・自動再接続(RDP 組み込みの `EnableAutoReconnect` で回復できなかった非ユーザー起因の切断を最大 5 回、3 秒間隔でリトライ)を担当。ウィンドウリサイズ時の解像度追従は `ReconnectForResize`(上記 xrdp セクション参照)。`Connect()` は `IsLoaded` 前だと ActiveX のハンドル未生成で失敗するため、Loaded まで遅延する。
- `Models/ConnectionProfile.cs` + `Services/ProfileStore.cs` — プロファイルは `%APPDATA%\UbuntuRemote\profiles.json` に保存。パスワードは DPAPI (CurrentUser) で暗号化した Base64 のみ永続化し、平文は保存しない。編集ダイアログでパスワード空欄 = 既存値維持、という規約。
- xrdp 前提の既定値: サーバー証明書検証なし(`AuthenticationLevel.NoAuthenticationOfServer`、xrdp は自己署名のため)、NLA は `ConnectionProfile.UseNla` で保持(既定オフ。Ubuntu の xrdp は通常 NLA 非対応)。ただし編集ダイアログに NLA の UI はなく、変更するには profiles.json を直接編集する。ユーザー名/パスワードは RDP 接続時に渡され xrdp のログイン画面が自動突破される。

## Notes

- 接続トラブルの調査は `%APPDATA%\UbuntuRemote\app.log`(`Services/AppLog.cs`)を見る。接続・切断(DisconnectCode 付き)・リサイズ再接続がすべて記録される。
- 起動引数 `--connect <表示名 or ホスト>` で該当プロファイルへ自動接続する(大文字小文字無視)。
- RoyalApps パッケージの API を調べる必要が出たら、リフレクションでダンプするのが確実(過去のダンプ手法: scratchpad に console プロジェクトを作り `GetExportedTypes()` を列挙)。
- `RdpControl` の主要 API: `RdpConfiguration`(設定ツリー)、`Connect()`/`Disconnect()`、`OnConnected`/`OnDisconnected(DisconnectedEventArgs: DisconnectCode/Description/UserInitiated)`、`FocusRdpClient()`。
