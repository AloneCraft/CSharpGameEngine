# Visual Studio / VSIX 起動障害の記録

## 発生日

2026-08-21

## 症状

- VSIX の F5 起動後、`UserEcsProject.slnx` を開いてもソリューションエクスプローラーが空になる。
- `Components.cs` を開くと `0x80131500` が発生する。
- Visual Studio が `EditorPackage`、`ProjectPackage`、`ManagedProjectSystemPackage`、`RoslynPackage`、`CSharpPackage` などを読み込めない。

## 原因

1. VSIX プロジェクトの `StartArguments` だけを `CgeFixed` に変更していたが、VSSDK のデプロイ先プロファイル (`VSSDKTargetPlatformRegRootSuffix`) は未指定のため既定値 `Exp` のままだった。
2. そのため F5 は壊れた `Exp` プロファイルを起動していた。`Exp` では MEF の C# プロジェクトシステム依存 (`IProjectServiceAccessor`、`IDesignTimeBuildService` など) が解決できず、`.slnx` 自体ではなくプロジェクトシステムが読み込みに失敗した。
3. `CgeFixed` 側にも古い VSIX DLL の CodeBase / ComponentModelCache が残っていた。また、パッケージ初期化中に初回レイアウト適用を開始し、ツールウィンドウ生成待ちがパッケージ読み込みと循環する経路があった。

## 対策

- `CSharpGameEngine.VSExtension.csproj` に次を明示した。

  ```xml
  <VSSDKTargetPlatformRegRootSuffix>CgeFixed</VSSDKTargetPlatformRegRootSuffix>
  <StartArguments>/rootsuffix CgeFixed "$(UserEcsProjectSolutionPath)"</StartArguments>
  ```

- `CgeFixed` の現行 VSIX DLL を配置し、古い CodeBase 互換ディレクトリにも DLL を同期した。
- `CgeFixed` の ComponentModelCache は削除せず退避して再生成させた。
- パッケージ初期化中の自動レイアウト適用を外し、レイアウト適用は明示コマンドから行うようにした。
- `CgeFixed` で `UserEcsProject.slnx` を直接起動し、ウィンドウタイトルが `UserEcsProject - Components.cs` になり、CGE パッケージの Begin/End とプロジェクトシステムの正常読み込みを確認した。

## 再発防止策

- VSIX の F5 設定を変更するときは、`VSSDKTargetPlatformRegRootSuffix` と `StartArguments` の両方を同じ RootSuffix にする。
- 変更後は次を実行し、値が一致することを確認する。

  ```powershell
  dotnet msbuild tools/CSharpGameEngine.VSExtension/CSharpGameEngine.VSExtension.csproj `
    -getProperty:VSSDKTargetPlatformRegRootSuffix,StartArguments
  ```

- VSIX プロジェクトを既に開いている Visual Studio は、起動設定変更後に再起動またはプロジェクト再読み込みを行う。既存プロセスは古い `Exp` 設定をメモリに保持することがある。
- `Launch-VSExtension.ps1` は `CgeFixed` を使用し、VSIX 配置後に DLL の SHA-256 と配置先を検証する。
- `/setup` は壊れた外部拡張の manifest によりハングすることがあるため、必要時以外は実行しない。
- 起動障害時は、まず `ActivityLog.xml` で `Begin package load` と `End package load`、`ProjectPackage` / `RoslynPackage` の失敗有無を確認する。`.slnx` の内容を疑う前に RootSuffix とプロジェクトシステムを確認する。


## 古い VSIX フォルダーが複数残った理由

- VSIXInstaller は同じ VSIX を開発中に繰り返しインストールすると、`Extensions\<ランダム名>` の配置先を更新・再利用せず、バージョンやインストール状態ごとに新しいフォルダーを作ることがある。
- 旧 `Launch-VSExtension.ps1` は毎回 `/rootSuffix:Exp` へ VSIXInstaller を実行していたが、古い配置の整理、配置 DLL のハッシュ確認、失敗時の後処理を行っていなかった。
- アンインストールが exit code 1 になった回や、壊れた private registry / ComponentModelCache が残った回では、フォルダーだけでなく古い CodeBase 参照も残った。
- 今回確認した `xwqdduxg.0sc` / `afguo3at.zlu` のように manifest 付きのものは通常の VSIX 配置、`xlu2dp3h.4oo` のように manifest がないものは古い CodeBase 参照を救済するための互換フォルダーであり、同じ種類の「インストール済み VSIX」ではない。

現在のランチャーでは `CgeFixed` に限定し、現行 DLL の SHA-256 と manifest 付き配置を検証してから起動する。manifest のない既存互換フォルダーは新規作成せず、必要な場合だけ現行 DLL を同期する。
## ツールバーコマンド追加時の再発防止

- VSCT に `<Button>` を追加しただけではツールバーには表示されない。ツールバーの `<Group>` を親にする `<CommandPlacement>` も必ず追加する。
- コマンド追加時は、Tools メニュー、ツールバー、必要なコンテキストメニューの配置先を個別に確認する。
- VSIX ビルド後、既存の Visual Studio セッションには古い VSCT が残るため、VSIX を再インストールして Visual Studio を再起動する。
- 最低限、`dotnet build tools/CSharpGameEngine.VSExtension/CSharpGameEngine.VSExtension.csproj -c Debug --no-restore` と `git diff --check` を実行する。
## 項目テンプレートが「新しい項目」に表示されない場合（2026-08-21）

### 原因

- 旧 VSIX は `Microsoft.VisualStudio.ItemTemplate` の Asset に個別の `SceneFile.zip` を指定していた。Visual Studio 17/18 の VSIX では、VSSDK が生成する `ItemTemplates\templateManifest0.1033.vstman` と展開済みテンプレート一式を Asset の `Path="ItemTemplates"` として登録する必要がある。個別 ZIP 指定ではテンプレートが登録されず、項目ダイアログに表示されない。
- VSSDK の `VsixUtil createTemplateManifests` は、テンプレート定義が短くなった増分ビルド時に既存の `.vstman` を十分に切り詰めず、末尾に古い XML を残すことがある。壊れた `.vstman` は Visual Studio により無視される。
- CgeFixed に古い `0.1.0` の配置が残っているだけでは、修正済み VSIX が自動的に反映されない。

### 対策と確認手順

- `CSharpGameEngine.VSExtension.csproj` で `VSTemplate` を宣言し、生成 ZIP を `VSIXSourceItem` に渡す。最終 VSIX は `ItemTemplates` 配下に `.vstman`、`.vstemplate`、テンプレート C#、アイコンを含む形にする。
- `GenerateTemplatesManifest` の直前に、生成済み `.vstman`、`createTemplateManifests.cache`、`templateFiles.json` を削除するターゲットを置く。これにより増分ビルドでも XML 末尾の残骸を防ぐ。
- `source.extension.vsixmanifest` の VSIX バージョンを上げ、`<Asset Type="Microsoft.VisualStudio.ItemTemplate" Path="ItemTemplates" />` を登録する。`Launch-VSExtension.ps1` は VSIXInstaller の終了を待ち、展開済みの版・テンプレートマニフェスト・テンプレート SHA-256 を検証してから `/InstallVSTemplates` を実行する。
- 検証は、生成 `.vsix` を ZIP として開き、`ItemTemplates/templateManifest0.1033.vstman` が単一の有効 XML であること、`CSharp/GameEngine/1033/SceneFile` 一式があることを自動テストで確認する。
