# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**Ubuntu Remote** — Ubuntu マシン(xrdp)へ接続する Windows 用リモートデスクトップクライアント。標準の mstsc の使い勝手(毎回の入力、再接続なし、複数マシン管理不可)を改善するために作られた WPF アプリ。UI は日本語。

## Build & Run

.NET 10 SDK はユーザースコープでインストール済み(`%LOCALAPPDATA%\Microsoft\dotnet`、ユーザー PATH に登録済み)。新しいシェルなら `dotnet` で直接呼べるが、PATH 未反映のシェルではフルパス `& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe"` を使う。

```powershell
# ビルド
dotnet build UbuntuRemote/UbuntuRemote.csproj

# 開発実行(apphost はユーザースコープの .NET を見つけられないため DOTNET_ROOT が必要)
$env:DOTNET_ROOT = "$env:LOCALAPPDATA\Microsoft\dotnet"
dotnet run --project UbuntuRemote

# 配布用(自己完結型シングル EXE → publish/UbuntuRemote.exe)
dotnet publish UbuntuRemote/UbuntuRemote.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish
```

テストプロジェクトは現状なし。動作確認は起動 + 実接続で行う。デスクトップに `Ubuntu Remote.lnk`(publish 版へのショートカット)あり。

## Architecture

WPF (net10.0-windows) + `RoyalApps.Community.Rdp.WinForms`(Microsoft RDP ActiveX のラッパー)。RDP コントロールは WinForms 製なので `WindowsFormsHost` 経由で WPF に埋め込む。**csproj で `System.Windows.Forms` / `System.Drawing` の暗黙 global using を Remove している**(WPF 型との CS0104 衝突防止)。WinForms 型が必要な箇所は完全修飾で書く。

- `MainWindow` — 左サイドバー(プロファイル一覧、ObservableCollection)+ 右 TabControl(1 タブ = 1 セッション)。タブ生成・クローズはコードビハインドで行う。
- `Controls/RdpSessionView.cs` — 1 セッション分のビュー(XAML なし、コードのみ)。`RdpControl` をホストし、接続設定の適用・状態オーバーレイ・自動再接続(RDP 組み込みの `EnableAutoReconnect` で回復できなかった非ユーザー起因の切断を最大 5 回、3 秒間隔でリトライ)を担当。`ResizeBehavior.SmartReconnect` によりウィンドウリサイズで解像度がリモートに追従する。
- `Models/ConnectionProfile.cs` + `Services/ProfileStore.cs` — プロファイルは `%APPDATA%\UbuntuRemote\profiles.json` に保存。パスワードは DPAPI (CurrentUser) で暗号化した Base64 のみ永続化し、平文は保存しない。編集ダイアログでパスワード空欄 = 既存値維持、という規約。
- xrdp 前提の既定値: サーバー証明書検証なし(`AuthenticationLevel.NoAuthenticationOfServer`、xrdp は自己署名のため)、NLA はプロファイル毎に選択可(既定オフ。Ubuntu の xrdp は通常 NLA 非対応)。ユーザー名/パスワードは RDP 接続時に渡され xrdp のログイン画面が自動突破される。

## Notes

- RoyalApps パッケージの API を調べる必要が出たら、リフレクションでダンプするのが確実(過去のダンプ手法: scratchpad に console プロジェクトを作り `GetExportedTypes()` を列挙)。
- `RdpControl` の主要 API: `RdpConfiguration`(設定ツリー)、`Connect()`/`Disconnect()`、`OnConnected`/`OnDisconnected(DisconnectedEventArgs: DisconnectCode/Description/UserInitiated)`、`FocusRdpClient()`。
