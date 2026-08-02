# ADR-0012: UserGameProjectの実プロジェクト化、VS起動時の自動オープン、アセット`.meta`の自動生成

## ステータス
承認済み

## 背景

`UserGameProject/`はADR-0004時点で「VSIXを通じてCSharpGameEngineを消費するエンドユーザーの
ゲームプロジェクトを模擬する」ためのプレースホルダーとして作られたが、意図的に`.csproj`を
持たない状態だった。ユーザーから、このシミュレーションを実物に近づけるため以下の要望があった:

1. `UserGameProject/`に実際の`.csproj`を作り、`CSharpGameEngine.VSExtension`実行時に自動で
   開くようにする。
2. Unity/UEのように、3Dアセット(.glb/.gltf/.fbx)に対して`.meta`ファイル(安定GUID)を自動生成し、
   ソリューションエクスプローラーでは代表ファイルのみ表示する。

ユーザーへの確認により、自動起動は「何もソリューション/プロジェクトが開いていない時のみ」発動する
こと、`.meta`ファイルの中身は「安定GUIDのみ」とすることが決定した。

## 決定事項

### 1. `UserGameProject/UserGameProject.csproj`の新規作成
net10.0の最小限のクラスライブラリ(`IsPackable=false`、プロジェクト参照なし - エンジンとの統合は
将来の別マイルストーン)。`Assets\**\*`を`<None>`として含める。**`CSharpGameEngine.slnx`には
意図的に追加しない**(重要、下記「経験的に判明した問題」参照)。

### 2. VS起動時の自動オープン(ソリューション/プロジェクト未オープン時のみ)
`CSharpGameEngineViewerPackage`に`[ProvideAutoLoad("adfc4e64-0397-11d1-9f4e-00a0c911004f",
PackageAutoLoadFlags.BackgroundLoad)]`を追加した。このGUID文字列は`VSConstants.
UICONTEXT_NoSolution`(「ソリューションが開いていない」UIコンテキスト)の値だが、この定数は
`System.Guid`型でありコンパイル時定数として属性引数に直接渡せないため、実際に参照している
`Microsoft.VisualStudio.Shell.Framework.dll`(17.14.40264)をリフレクションで検査して実際の値
を確認した上でハードコードした。

このUIコンテキストトリガーは、既存の「コマンド初回実行時に遅延ロードされる」経路に**追加**
される形で機能し、既存の動作には影響しない。`InitializeAsync`内の
`TryAutoOpenUserGameProjectAsync`が、`IVsSolution.GetProperty(VSPROPID_IsSolutionOpen)`で
「本当に何も開いていないか」を防御的に再確認した上で、`RepoLocator.ResolveRepoRoot()`で
解決したパスから`UserGameProject.csproj`を`EnvDTE.DTE.ExecuteCommand("File.OpenProject",
path)`で開く。

### 3. アセット`.meta`ファイルの自動生成(安定GUIDのみ)
新規クラス`AssetMetaWatcher.cs`が、`.glb`/`.gltf`/`.fbx`を対象に`UserGameProject/Assets`配下を
再帰的にスキャン・監視する。パッケージロード時に既存ファイルの初回スキャン(`.meta`が無いものだけ
生成)を行い、その後`FileSystemWatcher`で新規作成・リネームを継続監視する(`FabDownloadWatcher`
(ADR-0011)と同じ構造だが、コピーではなく`.meta`生成を行う点が異なる)。`.meta`ファイルの形式は
安定GUID一行のみ(`guid: <32桁16進数>`)。既存の`.meta`は上書きしない。ユーザー操作を必要とせず、
パッケージロード時に自動的に開始する。

`UserGameProject.csproj`では、`.meta`ファイルを`<None Include="Assets\**\*.meta"
Visible="false" />`として明示的に項目登録した上で非表示にしている(下記「経験的に判明した問題」
参照 - 単純な`Exclude`だけでは不十分だった)。

## 経験的に判明した問題と修正(実装時に発見、いずれも実機で確認・解決済み)

このADRの機能は、実装直後は「バックエンド(DTEモデル)は正しいのに画面に何も反映されない」状態
から始まり、3つの異なる根本原因を1つずつ切り分けて初めて完全に動作するようになった。いずれも
推測ではなく、`dte.Solution.FullName`/`IsOpen`/`Projects.Count`/`MainWindow.Visible`等を
一時的にファイルへ書き出すデバッグログと、実際のスクリーンショットで確認した。

1. **`UserGameProject.csproj`を`CSharpGameEngine.slnx`のプロジェクト一覧に含めていたことが
   根本原因だった**: 当初、新規作成した規約(「`src/`・`tests/`・`tools/`配下にプロジェクトを
   追加したら`slnx`も更新する」)を`UserGameProject/`にも適用してしまい、`slnx`に
   `<Project Path="UserGameProject/UserGameProject.csproj" />`を追加していた。この状態で
   `ExecuteCommand("File.OpenProject", <UserGameProject.csprojのパス>)`を呼ぶと、VSは
   「このプロジェクトは既存のソリューション(`CSharpGameEngine.slnx`)に属している」と正しく
   認識し、**単独のプロジェクトではなく、それを含む`CSharpGameEngine.slnx`全体を開いてしまう**
   (`dte.Solution.FullName`が`CSharpGameEngine.slnx`になることをログで確認)。そもそも
   「`src/`・`tests/`・`tools/`配下」という既存規約の対象外だったフォルダに規約を過剰適用した
   ことが原因であり、`UserGameProject.csproj`は`CSharpGameEngine.slnx`から意図的に除外した
   (「エンジン開発用ソリューション」と「エンドユーザーのゲームプロジェクト」を明確に分離する、
   という本来の設計意図にも合致する)。
2. **`MainWindow.Visible`がこの自動ロードのタイミングでは`false`のままだった**: 上記1を修正後、
   `dte.Solution.IsOpen=true`・`Projects.Count=1`・Solution Explorer/Git 変更ツールウィンドウも
   正しく存在するにもかかわらず、画面には依然としてStart Windowが表示され続けた。
   `dte.MainWindow.Visible`をログに出したところ`False`だった - `[ProvideAutoLoad]`による
   「ソリューション未オープン」コンテキストの自動ロードは、シェル自身のメインウィンドウが
   完全に表示される前の非常に早いタイミングで発火するため、バックエンドの状態は正しくても
   画面に反映されていなかった。`dte.MainWindow.Visible = true; dte.MainWindow.Activate();`を
   明示的に呼ぶことで解決した。
3. **`Exclude`だけでは`.meta`ファイルをソリューションエクスプローラーから隠せなかった**:
   `<None Include="Assets\**\*" Exclude="Assets\**\*.meta" />`だけでは、実際に`.meta`ファイルが
   生成された状態でSolution Explorerを確認すると、`.meta`ファイルが依然として表示された
   (最近のVSのソリューションエクスプローラーは、プロジェクトアイテムとして含まれていない
   ファイルでも、含まれるフォルダの物理ファイルとして表示することがあるため)。`<None
   Include="Assets\**\*.meta" Visible="false" />`として明示的にアイテム登録した上で
   `Visible="false"`を指定することで、実際に非表示にできることを確認した。

また、自動ロードコード自体は`await Task.Delay(TimeSpan.FromSeconds(2))`をシェル自身の起動
シーケンスとの競合を避けるために入れている(無くても`dte.Solution`の状態自体は正しくなることを
確認済みだが、念のため残した)。

## 検証結果(実際に実行して確認)
- `dotnet build`/`dotnet test`でリポジトリ全体(17プロジェクト、88件のテスト)に回帰がないことを
  確認した。
- VS実験用インスタンスを**ソリューション引数なしで**起動し、上記3つの問題を全て修正した状態で、
  ウィンドウタイトルが「UserGameProject - Microsoft Visual Studio - 実験的なインスタンス」に
  なり、ソリューションエクスプローラーに`UserGameProject`プロジェクト(依存関係・Assets・
  README.md)が正しく表示されることを実際のスクリーンショットで確認した。
- `UserGameProject/Assets`配下にテスト用`.glb`/`.fbx`ファイルを置いたところ、`.meta`ファイル
  (`guid: <32桁16進数>`形式)が自動生成されることを実際のファイル内容で確認した。
- ソリューションエクスプローラーで`Assets > Models`フォルダを展開したスクリーンショットで、
  `Box.fbx`/`Box.glb`は表示され、対応する`Box.fbx.meta`/`Box.glb.meta`は`Visible="false"`
  修正後は一切表示されないことを確認した。
- テスト用に配置した`.glb`/`.fbx`サンプルは検証後に削除し、`UserGameProject/Assets`は元の
  (`Fab/`のみの)状態に戻した。

## 未解決・保留事項
- `dte.Solution.Open(csprojPath)`(生のAPI呼び出し)は試したが、MRUには登録されるものの
  Start Windowが表示されたままになる不完全な動作だったため、`ExecuteCommand("File.OpenProject",
  ...)`に切り替えた経緯を記録する(上記「経験的に判明した問題」1・2の切り分け作業で判明)。
