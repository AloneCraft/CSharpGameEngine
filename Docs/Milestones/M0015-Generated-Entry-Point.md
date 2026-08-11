# M0015: エントリポイントの生成 — `Program.cs`の廃止

## 目的

ユーザーの依頼「`Program.cs`を作らなくてもいいようにSystem/Componentに実装してほしい。プラットフォーム
のウィンドウシステムや依存も含めて。fbxはレベルとして扱う」を満たす。

[[ADR-0033-Static-Archetype-Ecs-Migration]]決定事項10が`Program.cs`を残した理由はContext引数だった
(ループ開始前に構築されて全期間で固定されるため、ウィンドウ/レンダラーの遅延生成が表現できない)。
エンジンオブジェクトをComponentのフィールドへ移せばこの制約は消え、`Run`のシグネチャが
`(ref State)`に確定してMainが機械的に生成できる。設計判断は
[[ADR-0035-Generated-Entry-Point-And-Engine-Objects-As-Components]]に記録した。

## 実装したこと

### ランタイム `src/CSharpGameEngine.Entities/`
- `Bootstrap.cs`(新規): 生成されたMainが作る唯一のシードEntityのComponent(`Done`フラグ付き)。
  `Parent.cs`/`SceneNodeTag.cs`と同じ「エンジンが持つComponent」の位置づけ。

### `GameSystemGenerator`(`src/CSharpGameEngine.CodeAnalysis/`)
- `GameSystemEntryPointEmitter.cs`(新規): `GameSystem.EntryPoint.g.cs`を出力する。
  出力条件の判定(`ShouldEmitPlan`)は、Bootstrapの形状を注入するかどうかを決めるためレジストリ構築の
  **前**に行う。条件はOutputKind・ユーザーMainの不在・ディスパッチSystemの存在・Context引数ゼロの4つ。
- `GameSystemGenerator.cs`: 上記の判定と`additionalComponentSets`へのBootstrap注入、Emitter呼び出しを配線。
  ローカル変数名を`sceneComponentSets`→`additionalComponentSets`へ(Scene専用ではなくなったため)。
- `GameSystemLoopEmitter.cs`: 「ディスパッチSystemが1件以上」の述語を`HasDispatchSystem`として公開し、
  EntryPointEmitterと共有する(2箇所で条件がズレると、生成されたMainが存在しない`GeneratedGameLoop`を
  呼ぶ壊れ方をするため)。
- `GameSystemDiagnostics.cs`/`AnalyzerReleases.Unshipped.md`: `SYSGEN021`(Context引数があるため
  エントリポイントを生成できない)を追加。

### `UserEcsProject/`
- **`Program.cs`を削除**。
- `Components.cs`: `EcsBootstrapRequest`を削除し、`AppHost`(タイトル/サイズ + `Win32Window?`)、
  `Level`(FBXパス + シェーダーパス + 初期カメラ + `StageRenderer?`)、`Renderable`(レンダラー参照 +
  オブジェクト番号)、`FramePresent`(フレーム末尾フェーズ)を追加。`Transform`は変更なし。
- `Systems/RuntimeSystem.cs`: **エンジンが提供するべきSystem**(いずれ共有ソースへ移すもの)を
  フレーム順に4つ。`WindowSystem`(生成/PumpEvents/終了判定) → `LevelLoadSystem`
  (FBX→StageRenderer→描画Entity生成) → `TransformSyncSystem`(旧`RenderingSystem`) →
  `PresentSystem`(RenderFrameと後始末)。**ゲーム固有のリテラルは1つも無い**。
- `Systems/GameSystems.cs`(新規): **このプロジェクト固有のSystem**(ランタイムへは移さない)。
  `GameStartupSystem`(旧`BootstrapSystem`)がウィンドウのタイトル/サイズ、レベルのFBXパス、
  シェーダーのパス、初期カメラを`World.Create`で宣言する。移設をファイル単位の移動にすることと、
  ランタイム側にゲームの決定が残っていないことを目視で保証することが目的(ADR-0035決定事項4-2)。
- `README.md`を更新。

### テスト
- `GameSystemEntryPointTests.cs`(新規、8件): 出力する/しない条件(Library・ユーザーMain有り・System無し・
  Context有り)、SYSGEN021、Bootstrap Archetypeの注入が生成時のみであること、そして
  **生成された`Main`をリフレクションで実際に実行**して「シード1個 → Systemが世界を組み立てる →
  `State.Enabled`でループが終わる」を検証。
- `GameSystemGeneratorTestHelper.cs`: `Bootstrap.cs`を全コンパイルに含め、
  `CreateApplicationCompilation`(OutputKind=ConsoleApplication)と`CompileAndLoad(CSharpCompilation)`を追加。
- テストcsprojに`Bootstrap.cs`のTestAssetsリンクを追加。

### ドキュメント
`ADR-0035`(新規)、`M0015`(本書)、`ADR-0033`決定事項10に更新済みである旨を追記、
`Docs/Implementation-Status.md`のF-L3-012 / F-L7-015 / F-L7-016を更新。

## 検証結果(実際に実行して確認)

```
dotnet build CSharpGameEngine.slnx -c Debug     → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Release   → 成功、警告0/エラー0
dotnet test  CSharpGameEngine.slnx              → 全9プロジェクト成功、合計373件・失敗0
```

テスト内訳: Core 111、Windows 67、CodeAnalysis 64(うち新規8)、VSExtension 40、Import 30、
UserGameProject 21、Entities 19、SceneAuthoring.Cli 13、Bridge.Hosting 8。

回帰の要点(いずれも実際に確認):
- `UserGameProject`(手書きProgram.cs + Context多用)は`GameSystem.EntryPoint.g.cs`が**出力されず**、
  CS0017も起きない。
- `CSharpGameEngine.Entities`単体ビルド(System 0件・Library)も従来どおり成功。
- `UserEcsProject`は`Program.cs`が無い状態でビルドが通る。

生成物の目視確認:
- `UserEcsProject/Generated/.../GameSystem.EntryPoint.g.cs`が`World`→`Bootstrap`シード→
  `GeneratedGameLoop.Run(ref State)`の3行になっている。
- `GameSystem.Loop.g.cs`のArchetypeループが`Archetype0{Bootstrap}` → `Archetype1{AppHost}` →
  `Archetype2{AppHost,Level}` → `Archetype3{Transform,Renderable}` → `Archetype4{FramePresent}`の順で、
  `Archetype2`のループ内が`windowSystem` → `levelLoadSystem`の記述順になっている。

### 実機確認

`UserEcsProject.exe`(apphost)を起動し、`PrintWindow`(`PW_RENDERFULLCONTENT`)でクライアント領域を
キャプチャした:

- ウィンドウタイトルが`GameStartupSystem`の指定どおり「C# Game Engine - ECS FBX Rendering PoC」。
- 1280x720のクライアント領域に`StageRenderer.ClearColor`(89,140,217)の青がクリアされ、
  中央に`Box.fbx`の立方体が描画されている。
- 起動から終了まで標準出力/標準エラーへの出力なし(未処理例外なし)。

すなわち「シードEntity → ウィンドウ生成 → FBXインポート → D3D12レンダラー生成 → 変換行列の同期 →
Present」が`Program.cs`なしで通っている。立方体が暗く見えるのは、この PoC が
`SetDirectionalLight`/`SetAmbientLight`を一度も呼ばないため(旧`Program.cs`も呼んでいなかったので
本変更による退行ではない)。

## 意図的に除外したこと

- **エンジン側システムの移設**。ユーザーの指示どおり、当面は`UserEcsProject`内で試行錯誤する。
  ただし移設単位はファイルとして既に切り分けてある(`Systems/RuntimeSystem.cs`)。
  移設先は`CSharpGameEngine.Entities`ではあり得ない(プラットフォーム非依存のため)ことをADR-0035に記録した。
- **実行順序のマーカーinterface**(`IFrameBeginSystem`/`IFrameEndSystem`)。全Systemが1ファイルにある
  現状では不要で、移設時に初めて必要になる。
- フレーム時間(`FrameContext`相当)の供給。この PoC はまだ時間を使わない。

## 残課題

- 移設した瞬間に、ユーザーのSystemとエンジンのSystemの相対順序が絶対パスのOrdinal順へ落ちる
  (ADR-0033からの積み残し)。マーカーinterfaceによる整列キーの追加が必要。
- `{AppHost}`単独のArchetypeが誰も入らない空ループとして生成される(害は無いが宣言形状と実体の乖離)。
- `Bootstrap`のArchetypeループが毎フレーム回り続ける(汎用`Destroy`が無いため)。
- 立方体のライティング未設定(この PoC の元からの状態)。
