# アーキテクチャ

## Typed lighting boundary (ADR-0064, M0088)
The current analytic-light contract is [Typed Lighting](Specifications/Typed-Lighting.md). Contracts owns validated physical quantities; portable Runtime.Lighting shared source owns Directional/Point/Spot/RectArea and optional LightShadow; Runtime.Windows owns fixed-transform extraction; Rendering owns applicable-only submissions; RenderServer owns ordered typed frame replay. Scene JSON (ADR-0070) and the editor's native text Undo are the only authoring/history truth. Earlier light vocabulary below is historical where it conflicts with ADR-0064.

## プロダクト
- 表示名: `C# Game Engine`
- 内部名: `CSharpGameEngine`

## レイヤーモデル
- L0 Platform / Native Backends
- L1 Core Kernel
- L2 Type Schema / Identity
- L3 ECS / Job Runtime
- L4 Domain Servers
- L5 Runtime Feature Modules
- L6 Runtime Artifacts
- L7 Static Compiler / Bake
- L8 Visual Studio Authoring Environment
- L9 Game / Application

## 依存方向
- 上位レイヤーは下位レイヤーに依存してよい。
- 下位レイヤーは上位レイヤーに依存してはならない。
- ランタイムレイヤー(L0-L6)は Roslyn や Visual Studio SDK を参照してはならない。
- Visual Studio 拡張機能はあくまでオプションのフロントエンドであり、エンジンの必須依存ではない。
- CLI / Compiler / Bake のフローは Visual Studio なしで実行できなければならない。

## ソースとアーティファクトの原則
- `.cs` ファイルとオリジナルのバイナリアセットが正規のソース(Source)である。
- アーティファクトとキャッシュは生成物であり、再生成可能な出力である。

## プロジェクト構成(ADR-0034)

プロジェクトは分野で切ったのではなく、**ビルド形状の実際の違い**で切る(ADR-0034)。一覧の正本は
`CSharpGameEngine.slnx`で、2026-10-01時点の`src/`は14のcsprojと6の共有プロジェクト(shproj)からなる。
ADR-0034で決めた中核の5つは次のとおり。

| プロジェクト | TFM | 境界の理由 |
|---|---|---|
| `CSharpGameEngine.CodeAnalysis` | netstandard2.0 | Roslyn Analyzer。`analyzers/dotnet/cs`としてpackされる |
| `CSharpGameEngine.Entities` | net10.0 | **共有ソース**。`partial struct World`を消費側コンパイル内でSGが完成させる |
| `CSharpGameEngine.Core` | net10.0 | **外部パッケージ依存がゼロ** |
| `CSharpGameEngine.Import` | net10.0 | サードパーティのアセットSDK(SharpGLTF / AssimpNet)を抱える |
| `CSharpGameEngine.Windows` | net10.0-windows10.0.19041.0 / x64 | windows TFM・x64・unsafe・TerraFX/DXCのネイティブ依存 |

その後に加わったもの: `Audio`、`Content`、`Contracts`、`Numerics`、`Rendering`、`SceneModel`(シーン解析の共有ソース、
netstandard2.0)、`ShaderArtifacts`、`ShaderGraph`、`World`の各csprojと、ゲームのコンパイルへソースとして入る共有プロジェクト
`Runtime.Animation`/`Runtime.Character`/`Runtime.Lighting`/`Runtime.Physics`/`Runtime.Simulation`/`Runtime.Windows`。
層の境界(M0096 G、`EngineLayerSpec`で検証):
- `Runtime.Physics`/`Runtime.Character`はWindows専用の型を名指ししない。ホストの物理ドメインは`IPhysicsHost`
  (`PhysicsDomainRegistry`)として受け取り、`EngineHost`がそれを実装する。`StaticMesh`/`MeshMobility`と、World変換の解決
  (`RuntimeWorldTransform.TryResolveWorld`)はEntitiesの共有ソースにある(名前空間`CSharpGameEngine.Runtime.Windows`はシーン
  JSONの互換のため維持)。
- `tools/CSharpGameEngine.Physics.Cli`(L7)はゲームを参照しない。cook/inspectはECSを使わない物理ソースを取り込み、
  Bakeは渡されたゲームのビルドを`bake --assembly <game.dll>`で収集可能なALCへ読み込み、生成された
  `GeneratedSceneBakeHost.TryBake`を呼ぶ。

**アセンブリ名と名前空間は一致しない**。これは意図的である(ADR-0034決定事項2)。
`CSharpGameEngine.Core.dll`は`CSharpGameEngine.Core`/`.Spatial.Abstractions`/`.Spatial.Unity`/
`.Spatial.Unreal`/`.Spatial.Blender`/`.Spatial.Gltf`/`.Spatial.Fbx`/`.Geometry`を含み、
`CSharpGameEngine.Windows.dll`は`.Graphics.D3D12`/`.Platform.Windows`/`.Compiler.Hlsl`を、
`CSharpGameEngine.Import.dll`は`.Import.Gltf`/`.Import.Fbx`を含む。
**コード上の名前空間は統合前と完全に同一**なので、`using`は統合の前後で変わっていない。

`CSharpGameEngine.Entities`と`CSharpGameEngine.CodeAnalysis`が統合できないのは技術的制約である:
前者の`World`/`State`は`partial struct`で、C#のpartial型は同一コンパイル内でしか合成できないため
`<ProjectReference>`方式が原理的に成立しない(ADR-0033)。後者はnetstandard2.0でなければ
Roslynにロードされない。

## 現在のマイルストーンの範囲
- L1の`CSharpGameEngine.Core`に加えて、L0 の Windows/Direct3D 12 垂直スライスは
  `CSharpGameEngine.Windows`の中にある: `Platform/`(素の Win32 ウィンドウ)、
  `Hlsl/`(DXC ベースの HLSL コンパイル)、`Graphics/`(デバイス/スワップチェーン/パイプライン)。
  メッシュインポート(glTF/GLB・FBX、プラットフォーム非依存)は`CSharpGameEngine.Import`。
  **`Core` 自体には Windows/D3D/DXC への依存も、`PackageReference`も一切ない**。
- `tools/CSharpGameEngine.TestHost` と `tools/CSharpGameEngine.Extension` が合成ルート
  (composition root)である: `TestHost` はエンジンのセルフテストと `triangle` 垂直スライスを、
  `Extension` は glTF/FBX モデルをインポートして固定カメラで表示するヘッドレスビューアを実行する。
  いずれも `--verify` でピクセル検証まで行うため、CI/スモークのレンダリング経路検証に使う。
  Visual Studio 拡張の本体は別プロジェクト `tools/CSharpGameEngine.VSExtension`(net472、
  実際に`.vsix`を生成する)であり、重い処理は`Import.Gltf.Cli`/`Import.Fbx.Cli`/
  `Import.AssetAuthoring.Cli`/`Primitive.Cli`/`Physics.Cli`/`ShaderGraph.Cli`への別プロセスブリッジへ
  逃がしている(ADR-0010/0019/0023)。
- `UserEcsProject/` はエンドユーザーのゲームプロジェクトを模擬する**実際の`.csproj`**で、ECS と
  シーン JSON の実証場所である。新規プロジェクトの雛形は`Templates/CSharpGameEngine.Templates.ThirdPerson`。
- L3の唯一のECSランタイムは `src/CSharpGameEngine.Entities`(SoA専業、**閉集合(静的)Archetype**)
  である。ユーザーはComponent型(`struct`)と`ISystem`を実装する`struct`だけを書き、
  `src/CSharpGameEngine.CodeAnalysis` の `GameSystemGenerator` が`Update`/`Spawn`メソッドの
  **引数の渡し方**から役割を推論して、Archetype型・`World.Create`
  オーバーロード・`GeneratedGameLoop`を生成する(ADR-0033、[[Ecs-Soa-Systems]]参照)。
  ユーザーはQuery・foreach・Chunk走査・ゲームループ・起動時のエンティティ生成を書かない。
  `Update`は`void`が標準で、経過時間は`State.FrameDeltaSeconds`/`FixedDeltaSeconds`から読む(ADR-0072)。
  Phaseは`[SystemPhase]`、Phaseの中の順序は`[RunsAfter]`/`[RunsBefore]`で受け取り、辺の無いもの同士は
  完全修飾名順(ADR-0073)。
  歴史: `src/CSharpGameEngine.Ecs`(コンパイル時アーキタイプ型、ADR-0015)と
  `Generation.Abstractions`/`GenerateEntityGenerator`(AoS/SoA選択生成、ADR-0024)はADR-0029で、
  動的Archetype方式(ADR-0029)はADR-0033で、それぞれ削除済み(いずれもSuperseded)。
  `samples/` ディレクトリはADR-0034で廃止した。
- Scene Authoring はプロジェクトの`Scenes`フォルダーにあるシーン JSON(`Scenes\**\*.json`)を正本とする
  (ADR-0070。C#の`IScene`クラスによる方式はM0093 C3bで削除し、残っていればSCENE206)。同じ文書を
  `GameSystemGenerator`が実行時生成(`Instantiate`、位置つきは`Spawner.Spawn<TScene>`、M0105)に、VSIXのシーン エディター(Design / JSON の2つのビュー、
  ADR-0071)が編集に使う。エディターの書き込みは変わった値だけを元のテキストへ戻し、作者の書式を保つ(M0095)。
  ゲームがビルド済みなら、Scene ビューはゲーム自身のランタイムで描く(ADR-0070 B1)。
- シーンエディタのインタラクション(ナビゲーション・ピッキング・ギズモ・編集トランザクション)は
  M0020のPhase 0〜7を実装済みである。カメラ外部注入・階層Transform合成に加え、ナビゲーション、
  CPUピッキングと双方向選択、Move/Rotate/Scaleギズモと増分スナップ、Duplicate・Follow View・
  頂点/面スナップ、Camera/Lightのプロパティギズモ、Renderer定数だけのドラッグpreview、
  最新世代へのリベース、1操作=1 Undoの書き戻し、差分の増分更新(`SceneIncrementalUpdatePolicy`)を持つ。
  ギズモ軸はゲーム本体の実行時Camera行列規約を維持し、既定視点のワールド`+X`は画面左へ写る
  ([[ADR-0042-Scene-Editor-Gizmo-Axis-Handedness]])。要求仕様は`Docs/シーンエディタの仕様.md`、意思決定は
  [[ADR-0041-Scene-Editor-Interaction-Architecture]]、詳細設計は
  [[Scene-Editor-Design]](`Docs/Specifications/Scene-Editor-Design.md`)、フェーズ計画は
  [[M0020-Scene-Editor-Interaction]]を参照。
   Scene View/Hierarchy/Inspector/Component PaletteのEscはL8共通routerでVisual Studio command routingの前に消費し、一時操作の取消→共通Scene選択の解除の順に同期する。

- `SourceLocation` の line/column は、判明している場合は **1始まり**。`0` は不明を意味する。
- レイヤー番号はドキュメント/ソリューション整理のためだけのものであり、名前空間には含めない。
  ADR-0034 以降、**レイヤー境界はアセンブリでは強制されない**(`Core`がL1とL2を跨ぐ)。
  レイヤーは Feature ID 単位で [[Implementation-Status]] が追跡する。
- 名前空間 `CSharpGameEngine.Spatial.Abstractions`(L1、RUF/XYZ値型・`ICoordinateConvention`/
  `ITransformConvention`契約・`Spatial<TConvention>`型ベースFacade)と、その具体的Convention実装
  `CSharpGameEngine.Spatial.Unity`/`.Unreal`/`.Blender`/`.Gltf`/`.Fbx`(いずれもL1、すべて
  `CSharpGameEngine.Core`アセンブリ内)が、ゲームコードからUnity/Unreal/Blenderの物理XYZ配置差を
  隠すRUF意味座標系を提供する(ADR-0025、[[Spatial-RUF]]参照)。
  `CameraBasis`/`DirectionalLightBasis`/`SpotLightBasis`はL2(Camera/Light基底)。
  コンパイル時Preset選択機構(`SpatialPresetSelection.props`)はADR-0034で廃止した。
  Convention型は残っているので、実行時に選択して使うことは引き続きできる。
- エンジンのRUF規約は`UnityCoordinateConvention`(左手系)に固定されている(ADR-0026)。
  `CSharpGameEngine.Graphics.D3D12`(Camera)・`CSharpGameEngine.Geometry`
  (ProceduralMeshFactory)の内部実装は
  RUF型経由で計算する(ADR-0026、ADR-0028)。`CSharpGameEngine.Import.Gltf`/`.Fbx`は
  `Spatial.Gltf`/`Spatial.Fbx`の各Conventionを使い、Import境界でソース形式の座標系からエンジン
  空間へ変換する(ADR-0027) — この変換により、ADR-0017が当初宣言していた「エンジン空間=glTF空間
  (右手系)」は上書きされた。Rendering境界そのもの(View/Projection規約・NDC等)は依然RUF未契約
  ([[Spatial-RUF]]「未確定事項」参照)。

## ソリューションファイルの保守
- `CSharpGameEngine.slnx` がこのリポジトリの正規のソリューションファイルで、全プロジェクトを登録する。
  Visual Studio によってレガシーな `CSharpGameEngine.sln` 形式から移行済みで、
  そのファイルはもう存在しない。`UserEcsProject.slnx` は `UserEcsProject` だけを含む補助のソリューションで、
  VSIX の実験用インスタンスでゲームを開くため（README の手順）にある。
- リポジトリ直下と全サブフォルダにある `bin`、`obj`、`.git`、`.vs`、`.gitignore` で除外された
  ローカル `VSIXProject1` 以外の全 `.csproj` と
  `.shproj` は、配置場所に関係なく `CSharpGameEngine.slnx` に登録すること。登録漏れがあっても
  `dotnet build`/`dotnet test`/`dotnet run --project` はプロジェクトに対して直接動作するが、
  Visual Studio 上には表示されない。
- 全 `.projitems` も `CSharpGameEngine.slnx` のソリューション項目として登録すること。
- `.shproj` は対応する `.projitems` を `Label="Shared"` で import し、共有プロジェクトの管理項目は
  ワイルドカードではなく `.projitems` に明示すること。
- プロジェクトまたは共有プロジェクトファイルの追加・削除・移動・改名を行う際は、同じ変更の中で
  `CSharpGameEngine.slnx` を更新し、登録整合性テストを実行すること。
- ソリューションが宣言しているプラットフォームは **x64 と AnyCPU** である。x64 専用の Windows 依存プロジェクト（`CSharpGameEngine.Windows`、`Windows.Tests`、`TestHost`、`EditorPreview.Host`、`FeatureRunner` など）は x64 に、VSIX や `World` など一部は AnyCPU に、`CSharpGameEngine.slnx` の `<Platform Project=...>` でプロジェクトごとに対応付けている。
- `UserGameProject.slnx`(2つ目のソリューションファイル)は ADR-0034 で削除した。
  M0009 で消えた `CSharpGameEngine.Ecs` 等を参照しており、読み込み不能な状態で放置されていた。


## Radial (logarithmic) depth (ADR-0076, M0099)

Scene cameras use logarithmic depth without a depth-mode selector. Stage owns one native vertex/pixel interface and writes colour, physical ray distance and conservative SV_DepthGreaterEqual; its fourteen-entry ABI has no radial wrapper entries and the renderer has no depth-mode-specific PSO family. L4 RenderViewDepth validates the projection and constructs GPU depth packets. Orthographic previews use linear depth, while the optional observer inset (RadialObserver) compresses positions around the camera. Positions stay Q64.64 until the single camera-relative float conversion. Preview protocol 9 has no selector and requires physical perspective metadata; matrix submissions and thumbnails use validated depth packets. M0119 still needs to remove the legacy Model/Triangle renderers before its full closure. The contract and non-goals are in [Omni](Specifications/Omni.md).

## Spatial partitioning

L3 World owns a read-only fixed-point loose-octree snapshot. L3 Systems may own separate mutable indexes when their narrowed membership or update policy offsets duplicated storage and maintenance.

### Scene View spatial debug channel

Scene Viewの描画モードはVSIXのPSO切替で実装し、ランタイムのGlobal Spatial Octreeは `SpatialDebugHub` の要求駆動Named Pipeで読み取り専用公開する。個別System索引の暗黙収集は行わず、描画用頂点予算は既存ギズモ4,096と空間デバッグ12,288を分離する。

Editor PreviewはVSIX内の複製実装ではなく、生成済みRuntimeアセンブリを隔離Hostで実行する共通World経路である。


## Shared Windows ECS Runtime

`CSharpGameEngine.Runtime.Windows` is a source-shared L5 runtime module. Its `.projitems` is compiled into the user ECS compilation together with Entities and the analyzer, on the single Q64.64 `LogicalScalar` of ADR-0063 (the former selectable precision profiles are gone). This keeps generated World/State APIs, hierarchy resolution, stage rendering, XAudio2 selection, and runtime companion components on one concrete Logical type.

UserEcsProject remains an L9 composition root for scenes, gameplay components, assets, and precision selection. Runtime systems do not depend on a scene class name or on `PlayerStart`; one active Camera is required and its Transform is authoritative.

**Other source-shared L5 modules.** `CSharpGameEngine.Runtime.Character` (kinematic character, controller and spring arm; ADR-0067) and `CSharpGameEngine.Runtime.Animation` (skeletons, clips and the locomotion graph; ADR-0066) are compiled into each game in the same way. Vehicles are not one of them: they are the engine plugin `plugins/CSharpGameEngine.Vehicles`, compiled into the games that enable it (ADR-0079).

**How a game project gets the engine.** Every game project takes the engine through `build/CSharpGameEngine.Game.props`. In the repository (SourceMode), `UserEcsProject` and `Templates/CSharpGameEngine.Templates.ThirdPerson` import it by relative path. Elsewhere (PackageMode), it arrives through the `CSharpGameEngine.Sdk` NuGet package, whose layout mirrors the repository (ADR-0065). The Visual Studio extension finds the game project through `CgeProjectContext` rather than a fixed repository path.

## プラグイン(ADR-0079)

プラグインは、エンジンの外から機能を足す単位で、ゲームのプロジェクトの `Plugins/<名前>`、エンジンの `plugins/`(SDK パッケージに同梱)、NuGet パッケージの 3 か所に置ける。仕様は `Docs/Specifications/Plugins-And-Assets.md`。

| モジュール | 形 | レイヤー |
|---|---|---|
| Runtime | 共有ソース(`.projitems`)。ゲームと同じコンパイルに入る。Component、System、`[EngineService]`、イベント | L5(ゲームの中ではエンジンの Runtime.* と同じ扱い) |
| Library | DLL(`[assembly: PluginLibrary]`)。`World`・`State`・`Entity`・`ISystem` を名指ししない | L4〜L5 |
| Cook | DLL(`IAssetCooker`)。`CSharpGameEngine.Cook.Cli` が別のプロセスで動かす | L7 |
| エディタの宣言 | 属性(アイコン、表示順) | L8 が読む |

- ECS のコードがソースでしか足せないのは、`World` と `State` が生成器の完成させる partial 型で、Archetype の空間がゲームごとに 1 つだからである。生成器は 1 つで、ユーザーは生成器を足さない。
- エンジンのプラグイン: Vehicles(`plugins/CSharpGameEngine.Vehicles`、UE の ChaosVehicles に当たる)、Navigation(`plugins/CSharpGameEngine.Navigation`、UE の NavigationSystem と Recast に当たる。Runtime は L5 のソース、Cook モジュールは L7 で、ビルドが別のプロセスで動かして `Assets/Navigation/<Id>.cgenav` を焼く。M0106)。
- 取り込みは `build/CSharpGameEngine.Game.targets`(プロジェクトファイルの後に読む)、エンジンの版は `build/CSharpGameEngine.Version.props`。

## Deterministic Simulation / Physics domains

`CSharpGameEngine.Runtime.Simulation`(L3/L5共通実行契約)は整数tick、phase、Domain command/event、cache identityを所有する。`CSharpGameEngine.Runtime.Physics`(L5)はユーザーCompilation内でQ64.64 `LogicalScalar`局所剛体を実行し、Physics専用Dynamic AABB Treeを使う。Global Loose Octreeは一般空間Query/表示のままである。Cooking/CacheはL6 artifact、CLIと生成Scene Bake HostはL7、Scene ViewはL8 frontendとする。RuntimeとBakeは同じ生成System setとsolverを使い、BakeだけがFrameInput/Presentationを除外してcanonical frameを逐次出力する。異種Simulationは翌tick Bridgeだけを共有しsolverを共有しない。

### VSIX localization boundary

VSIXが所有する表示文言は英語neutralの`VsixStrings`と日本語`ja-JP` satelliteへ集約する。Package初期化時に取得したVisual StudioのUI cultureを`VsixLocalization`へ固定し、devenv全体のThread cultureは変更しない。WPFは`LocExtension`、C#は`VsixText.Get/Format`、VSCT/VSPackage/VSIX installerはVSSDKのculture別resourceを使用する。

Spatial/Physics debug protocolは不変の理由enum・wire token・数値DTOだけを運び、Scene Viewがlocale別の全文HUDへ変換する。ファイルパス、C#シンボル、HRESULT、Stable ID、Build UTC identityは翻訳しない。詳細はADR-0050を参照する。

現実装の`Transform`共有ソースは物理的にはEntitiesにあるが公開namespaceが`Runtime.Windows`であり、Physics systemがその名前空間を参照する。共有Transform契約をWindows非依存namespaceへ移すまでは、意図したL5モジュール分離に対する既知の構造上の逸脱である。

## Non-physics target design and verification boundary

The Rendering, Asset/Content, Editor/VSIX, ECS/Runtime, Audio/Input/Platform, and Testing/CI/Release target designs are specified in `Docs/Specifications/` and adopted by ADR-0052 through ADR-0057. They are intended architecture, not evidence that their runtime bodies already exist.

The target keeps Win32/D3D12/XAudio2 binding at L0, portable values at L1-L5, engine-owned runtime servers at L4, artifacts at L6, static compilers/bake at L7, and Visual Studio only at L8. Runtime L0-L6 must not reference Roslyn or Visual Studio SDK. Scene JSON under each project's `Scenes` folder is the scene source of truth (ADR-0070); artifacts/caches are reproducible and old readers are removed with an immediate feature cutover.

ECS preserves the generated one-to-sixty-four-component single-`ulong` generator path with no runtime mask cost. F-L3-020 extends only the Source Generator subset test to a second `ulong` for 65–128 Component types, and ADR-0079 adds one word per further 64 types up to 512; generated Runtime dispatch remains direct Archetype loops. The Feature catalog v2 records this as an executable foundation Stage alongside tracks, dependency Levels, dependency IDs, oracle-bearing target Stages, and Visual alias registration.

## Autonomous Feature verification

`CSharpGameEngine.FeatureRunner`はL7の独立CLIであり、L0〜L9のFeature IDを自動Stageへ対応付ける。L9 Stage PackはFoundation、Rendering、Rigid、Deformable、Fluid、Destruction、Celestial、ProbabilityQuantum、Coupling、VSIXに分離し、process、timeout、artifact、再開単位を隔離する。Visual Stageはシーン JSON(ADR-0070)を正本にし、WARPを必須adapter、Hardwareを任意追加passとする。結果は`.artifacts/FeatureStages`のversioned JSONであり、source/artifactの正本関係を変えない。詳細はADR-0051と`Docs/Feature-Stages.md`を参照する。

### External provider boundary

- OS/native/driver bindingはL0に置き、Win32/D3D12と同じ抽象度にする。
- FFT、sparse solver、geometry、compute等の任意providerはL4 adapterの背後に置く。
- L5 solverはengine-owned L4 contractだけを参照し、外部型をComponent、公開API、Command/Event、artifactへ漏らさない。
- engine-owned CPU referenceをcorrectnessの必須経路とし、GPU・外部providerは追加backendとして同じOracleで比較する。

### Static ECS mask boundary

現行のComponent型上限は512である(ADR-0079。プラグインがゲームと同じ番号の空間を使うため、128から広げた)。Source GeneratorのArchetype部分集合判定は64型ごとに1語の`ulong`を使い(`ComponentMask`、語の名前はLow・High・Word2…)、1–64型はlow一語、65–128型はlow/high二語で、この範囲の生成コードは広げる前と同じ文字列である。生成Runtimeにはmask判定が残らず、513型以上はSYSGEN026で拒否する。
