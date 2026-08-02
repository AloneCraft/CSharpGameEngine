# ADR-0006: VSIX拡張機能の雛形

## ステータス
承認済み

## 背景
ADR-0001(レイヤードアーキテクチャ)では L8「Visual Studio Authoring Environment」を保留レイヤーと
定義しており、明示的なルールとして「VS拡張はエンジン/コンパイラ/CLIの契約に対する薄いフロントエンド
であり続けること」「ランタイムレイヤー(L0-L6)はRoslynやVS SDKを参照してはならない」がある。
ADR-0004 §7 では、`tools/CSharpGameEngine.Extension` を「将来、本物のVSIXシェルがラップすることに
なるプロジェクト」として明示的に見据えて作られていたが、実際のVSIX作成は見送られていた。理由は、
本物のVSIXパッケージが `net472` を必要とし、このリポジトリの `net10.0` ベースのプロジェクトとは
互換性がないためである。

ユーザーからこのVSIXシェルを実際に作ってほしい、最小限でよいという依頼があった:
「vsixプロジェクトでVSで確認できるようにして。最終的にこのvsixプロジェクトが必要になるので形だけ
でも」。

**今回のセッションでローカル環境を実際に確認した内容(推測ではない)**: Visual Studio Community
2026(18.8.12023.21)がインストール済みで、VSSDKコンポーネントも導入済みであることを確認した。VS
自身が持つ「VSIXProject」テンプレート(`Common7/IDE/ProjectTemplates/CSharp/Extensibility/1033/
VSIXProject/`)は `net472` をターゲットとし、`Microsoft.VisualStudio.SDK` `17.14.40265` と
`Microsoft.VSSDK.BuildTools` `18.8.739` を参照している - どちらもNuGet.org上に実在することを、
flatcontainerインデックスに直接問い合わせて確認済み。.NET Framework 4.7.2 の参照アセンブリも
ローカルに導入済みだった。これにより、ADR-0004 §7 で述べられていた制約が現在も正しいことを、
推測ではなく具体的なバージョン番号込みで裏付けられた。

## 決定事項

### 1. HWND埋め込みではなく外部プロセス起動方式を採用(ユーザー確認済みの選択)
ユーザーには次の2案を提示した: (A) VSのToolsメニューコマンドから、既存の・既に動作検証済みの
`CSharpGameEngine.Extension.exe` を `Process.Start` で別の独立ウィンドウとして起動する方式、
(B) D3D12の描画面をネイティブHWNDの再親化(WPFの `HwndHost` への `SetParent`)でVSのツール
ウィンドウに直接埋め込む方式。ユーザーは (A) を選んだうえで、将来ツールウィンドウへの埋め込みに
簡単に移植できるかを質問した。

**(A) が行き止まりではなく (B) の前提条件である理由**: `Graphics.D3D12`/`Import.Gltf` は
`net10.0`(CoreCLR)である。一方 `devenv.exe` および全てのin-process VSパッケージ/ツール
ウィンドウは `net472`(.NET Framework CLR)上で動作する。この2つは別ランタイムであり、同一
プロセス内でお互いのアセンブリを読み込むことはできない - つまりこの選択に関わらず、このレンダラー
を「本当の意味でin-processに埋め込む」ことはそもそもアーキテクチャ的に不可能だった。埋め込まれて
いるように見えるビューアを実現する現実的な唯一の方法は、実際のout-of-process埋め込みツールと
同じアプローチ: レンダラーは今回作るものと同様に別プロセスのままにしておき、後からそのウィンドウ
をVSのツールウィンドウに再親化する(Win32はプロセスをまたいだreparentをサポートしている)。将来の
具体的な移行作業は: (1) `Win32Window`/`Extension` に埋め込み用の起動モードを追加してウィンドウを
きれいに引き渡せるようにする、(2) 同じexeを起動してそのウィンドウを待ち受け、reparent/スタイル
変更/リサイズ追従を行う `ToolWindowPane` + `HwndHost` を追加する、の2点。その際に本当に未検証で
残るリスクは、入力フォーカスのルーティング、リサイズ/DPIの同期、reparent時のちらつきであり、
全体構造そのものではない(今回の実装がその土台になる)。

### 2. 新規プロジェクト: `tools/CSharpGameEngine.VSExtension`、`net472`、`ProjectReference` なし
ローカルの実際のVS生成VSIXテンプレートをそのまま踏襲: `Microsoft.VisualStudio.SDK`
`17.14.40265`(`ExcludeAssets="runtime"`)と `Microsoft.VSSDK.BuildTools` `18.8.739`。リポジトリ
内の他プロジェクトへの `ProjectReference` は一切持たない - `Extension.exe` を外部プロセスとして
起動するだけなのでコンパイル時参照は不要であり、ADR-0001の「薄いフロントエンド」原則を文字通り
満たす。他の全プロジェクトと同様にリポジトリルートの `Directory.Build.props`
(`TreatWarningsAsErrors=true`、`Nullable=enable`、`ImplicitUsings=enable`)を継承する。

### 3. `OpenViewerCommand` は古典的な `OleMenuCommandService` パターンでToolsメニューに
1コマンドを登録する(VS標準テンプレート自身が使っている手組みパターン)。Community Toolkit等の
追加依存は増やさず、スケルトンとしてのパッケージ数を最小限に保つ。コマンドは
`OpenViewerCommandPackage.vsct` で宣言し、アイコンにはこのVS環境に実在する
`KnownImageIds.vsct` を実際に確認したうえで、既定の `ImageCatalogGuid`/`OpenFolder` モニカーを
使用している(独自ビットマップは用意しない)。

### 4. リポジトリルートの特定方法: ビルド時に埋め込む定数方式に変更(プランからの実装時の修正)
プランでは「実行アセンブリ自身のディレクトリから上に辿ってリポジトリルートを探す」方式
(`Extension/Program.cs` の `ResolveModelPath` と同じ手法をモデルファイルではなくリポジトリの
場所特定に応用したもの)を想定していた。**これはVSIXでは機能しない**ことが実装時に判明し、修正
した: VSIXのインストール(そしてVS実験用インスタンスによるF5デバッグですら)は、ビルド済み
アセンブリをリポジトリの外にある `%LocalAppData%\Microsoft\VisualStudio\<バージョン>Exp\
Extensions\...` にコピーしてしまうため、実行時にアセンブリ自身の場所から上に辿っても
`CSharpGameEngine.slnx` には決してたどり着けない。

そこで、リポジトリルートを **ビルド時に** `$(MSBuildThisFileDirectory)`(`.csproj` 自体が実際に
置かれている場所、こちらは確実にリポジトリ内にある)から取得し、`AssemblyMetadata` 項目
(`CSharpGameEngineRepoRoot`)として埋め込む方式に変更した。`OpenViewerCommand.ResolveRepoRoot()`
はこの値を `Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()`
経由で読み取り、実行時にあらためて `CSharpGameEngine.slnx` というマーカーファイルの存在で検証
してから信頼する - 出発点の取得方法は変わったものの、「マーカーファイルで検証する」という元の
手法の趣旨は保っている。これは実際に検証済み: ビルド後、リフレクションでDLLを読み込み、埋め込
まれたメタデータが `CSharpGameEngineRepoRoot = G:\GitHub\CSharpGameEngine\` を正しく返すことを
確認した。

これは単一開発者向けスケルトンとしての明示的な既知の制約であり、本当の配布方式ではない: 埋め込
まれるパスは、そのVSIXをビルドした特定のマシン/チェックアウトに固有である。本当の解決策
(例: Tools > Options ページ、環境変数によるオーバーライド、初回利用時の「エンジンの場所を指定」
プロンプトなど)は未解決/保留 - 下記参照。

### 5. コマンドの実行内容
`OpenViewerCommand.Execute` は `dotnet run --project tools/CSharpGameEngine.Extension --
--model Samples/Box.glb` を `Process.Start` で実行し、`WorkingDirectory` は特定したリポジトリ
ルートに設定する - ADR-0003/0004/0005の各マイルストーンを通じて既に手動検証済みの、まさにその
コマンドであり、新しい描画経路は一切導入していない。失敗時(リポジトリルートが特定できない、
プロセス起動失敗)は例外をVSに伝播させず `VsShellUtilities.ShowMessageBox` で通知する。

### 6. net472固有のビルド修正が2件必要だった(プランにはなく、実装時に経験的に判明)
- `ArgumentNullException.ThrowIfNull` は .NET 6+ のBCL APIであり net472 では使えない。手動の
  nullチェックに置き換えた。
- `OleMenuCommandService` の基底型 `MenuCommandService` は `System.Design.dll` にあり、SDK
  スタイルのnet472プロジェクトは既定でこれを参照しない。`<Reference Include="System.Design" />`
  を明示的に追加した。
- `Directory.Build.props` は `LangVersion` を `default` のままにしており、net472ではC# 7.3に
  解決される - `Directory.Build.props` から継承した `Nullable` 参照型注釈には古すぎる。VS自身の
  ローカルVSIXProjectテンプレートと同じ `<LangVersion>14</LangVersion>` を明示的に指定した。

この3件はいずれも、このリポジトリの `TreatWarningsAsErrors=true` によるビルドエラーゲートに
よって最初のビルド試行時点で(後になってからではなく)検出された - この プロジェクトの標準的な
ビルド規律通りである。

## 影響
- ソリューションには `net10.0` の他プロジェクト群に加えて `net472` プロジェクトが1つ存在する
  ようになった。`dotnet build`/`dotnet test` はリポジトリルートから両方に対して均一に動作する
  ことを確認済み(新プロジェクトを含めたソリューション全体のビルドと、非GPUテストスイート全体が
  通過)。
- `CSharpGameEngine.VSExtension` を参照する他のプロジェクトは無く、また同プロジェクトも `src/`
  配下の何も参照していない - ADR-0001の「L0-L6はRoslyn/VS SDKを除外する」ルールに影響はない。
- `CSharpGameEngine.VSExtension` は既存の「新規プロジェクトは同じ変更内でslnxに登録する」という
  ルールに従い、`CSharpGameEngine.slnx` の `/tools/` 配下に追加した。

## 実際に判明した内容
- VS 2026(18.8)自身のバンドルテンプレートが使う `Microsoft.VisualStudio.SDK`/
  `Microsoft.VSSDK.BuildTools` の正確なバージョンの組み合わせ(`17.14.40265` / `18.8.739`)、
  および両方ともNuGet.orgで公開されていること - ドキュメントからの推測ではなく実際に確認した。
- `ImageCatalogGuid`/`OpenFolder` モニカー(id `2162`)は、このVS環境の `VSSDK` フォルダ配下に
  実在する `KnownImageIds.vsct` をgrepして事前に確認したうえで使用し、未検証のアイコン名を
  使うリスクを避けた。
- リポジトリルートの特定方法はプランからの実質的な設計変更が必要だった(ランタイムでの遡り探索
  からビルド時埋め込みへ)- §4参照。これは検証実行の前、実装段階で発見・修正した。
- §6の3件のnet472固有コンパイルエラー以外に、ビルド上の摩擦は発生しなかった。
  `TreatWarningsAsErrors=true` やVS SDK自身の生成コード/アナライザーに起因する他の問題は無かった。
- 上記修正後、エンドツーエンド検証は1回目の試行で成功した: `.vsix` をビルドし、VSの実験用
  インスタンスにインストール(`VSIXInstaller.exe /rootSuffix:Exp`)、`devenv.exe /rootsuffix Exp`
  を起動し、UI Automationで「C# Game Engine Viewer を開く」がToolsメニューに実際に表示されて
  いることを確認、それを実際にクリックし、`CSharpGameEngine.Extension` のビューアウィンドウが
  開いて `Box.glb` が表示されることを実際のスクリーンショットで確認した - 「ビルドが通る」だけ
  でなく、VSコマンド→ビューア表示という一連の流れが実際に機能することを証明した。プランで代替
  手段として挙げていた `devenv /Command` による正式コマンド名での呼び出しは不要だった。UI
  Automation(`System.Windows.Automation` で実際のToolsメニューを操作)が確実に機能したため、
  そちらを使用した。

## 未解決・保留事項
- ツールウィンドウへのHWND再親化埋め込み(上記の案B)- 今回は着手していない。§1で述べた通り、
  今回の実装はその前提条件であり、競合する別設計ではない。実際には
  [[ADR-0007-VSIX-Embedded-Tool-Window]] で外部プロセス+HWND再親化方式として一度実装・検証
  したが、その直後にユーザーから「本当にインプロセスにしてほしい」という指示があり、
  [[ADR-0008-VSIX-InProcess-Vortice-Renderer]] で `Vortice.Windows`(netstandard2.0対応版)
  を使った真のインプロセスD3D12レンダラーに置き換えられた。
- 実際のパッケージング/署名/配布の仕組みは無い - これは開発時のみのスケルトンであり、検証の
  ために実験用インスタンスへ手動インストールしている。
- リポジトリの場所特定は、ビルド時に埋め込む単一開発者マシン限定のパスである(§4)- 複数マシン/
  複数開発者に対応する本当の解決策はまだ無い。
- アイコン/ブランディングの作り込みは無い(既定の `OpenFolder` モニカーのみ、独自ビットマップ
  なし)。
- ADR-0004/ADR-0005 で既に保留とされていた事項(カメラ操作、フルPBR、レンダラーの統合、sRGB
  対応など)は引き続き保留であり、本マイルストーンとは無関係。
