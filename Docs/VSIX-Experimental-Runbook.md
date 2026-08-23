# VSIX Experimental インスタンス再現手順

Visual Studio の拡張を検証するときは、`CgeFixed` root suffix を使う。通常の Visual
Studio プロファイルや `.vs` を消すと、ウィンドウ状態の再現性を失うため、キャッシュの
手動削除や `/ResetSettings` は行わない。

## 更新手順

1. `tools/CSharpGameEngine.VSExtension/source.extension.vsixmanifest` と csproj の VSIX
   Version が一致していることを確認する。
2. `tools/Launch-VSExtension.ps1 -CloseCgeFixed` を実行する。実行中の VS は
   `CloseMainWindow()` による通常終了だけを要求し、プロセスを強制終了しない。
3. スクリプトがビルド、VSIX のアンインストール／インストール、DLL SHA-256 検証を完了
   してから起動するまで待つ。
4. VSIX の再起動後、ActivityLog に次の条件が揃っていることを確認する。
   - `Loaded VSIX <version> | <revision> | current`
   - `Loaded VSIX DLL matches the local build output.`
   - `loadedSha256` と `buildSha256` が一致する

ログは `%LOCALAPPDATA%\CSharpGameEngine\Logs` に出力される。Document Well の生成失敗は
`SceneDesignerDocumentWindow.log`、拡張のロードと DLL 同一性は `CgeFixed-ActivityLog-*.xml`
と `CgeFixed-VsixMaintenance-*.log` を見る。

## ウィンドウ状態の復元契約

`ExperimentalToolWindowState` は Hierarchy、Inspector、Component Palette の表示状態だけを
通常終了時に保存する。次回、ソリューションが開いた後に表示状態を復元する。

ドッキング位置、サイズ、タブグループ、Document Well の配置は Visual Studio に所有させる。
拡張側で `SetFramePos` や既定レイアウト適用を起動時に行わないため、ユーザーが変更した配置を
次回起動で上書きしない。保存状態がまだ存在しない初回起動も、現在の Visual Studio 配置を
そのまま維持する。

状態を確実に保存するには、テスト後に Visual Studio を通常終了する。クラッシュ、タスク
マネージャーからの強制終了、電源断では最後の変更が保存されない可能性がある。

## 再発防止チェック

- 同じ VSIX Version の DLL が複数インストールされていないこと。
- ActivityLog の `current` と DLL ハッシュ一致を画面またはログで確認すること。
- `vswhere -latest` の結果をそのまま使わず、`Common7\\IDE\\devenv.exe` を持つ実行可能な
  Visual Studio を選択すること。
- PowerShell の単一結果を `.Count` で判定するときは `@(...)` で配列化すること。
- `CgeFixed` 以外の root suffix の拡張やユーザー作業を自動削除しないこと。
- 失敗時はまず VSIX のロード同一性、次に Document Well の HRESULT、最後に Scene Renderer
  の診断を確認し、推測でキャッシュを削除しないこと。

## F5 preflight requirements

`Launch-VSExtension.ps1` uses a forced `Rebuild`. The build fails if any source XAML Page has no generated BAML, and deployment does not continue.

Before Visual Studio starts, verify the VSIX package DLL SHA-256 matches the local Debug DLL, exactly one `CgeFixed` installation exists for the extension ID, and the installed DLL has the expected version, hash, and every source XAML BAML resource.

If deployment reports missing BAML, rebuild with `dotnet build tools/CSharpGameEngine.VSExtension/CSharpGameEngine.VSExtension.csproj -c Debug -t:Rebuild`. If it reports a package hash mismatch, discard the stale package and rebuild. Duplicate, stale-version, stale-hash, or installed-BAML errors stop F5 before `devenv.exe`; inspect `%LOCALAPPDATA%\\CSharpGameEngine\\Logs\\CgeFixed-VsixMaintenance-*.log`.
