# アーキテクチャ

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

`src/` は5プロジェクトである。この5つは分野で切ったのではなく、**ビルド形状の実際の違い**で
切ってある(ADR-0034)。

| プロジェクト | TFM | 境界の理由 |
|---|---|---|
| `CSharpGameEngine.CodeAnalysis` | netstandard2.0 | Roslyn Analyzer。`analyzers/dotnet/cs`としてpackされる |
| `CSharpGameEngine.Entities` | net10.0 | **共有ソース**。`partial struct World`を消費側コンパイル内でSGが完成させる |
| `CSharpGameEngine.Core` | net10.0 | **外部パッケージ依存がゼロ** |
| `CSharpGameEngine.Import` | net10.0 | サードパーティのアセットSDK(SharpGLTF / AssimpNet)を抱える |
| `CSharpGameEngine.Windows` | net10.0-windows10.0.19041.0 / x64 | windows TFM・x64・unsafe・TerraFX/DXCのネイティブ依存 |

依存辺は3本のみ: `Entities → CodeAnalysis`(Analyzer参照)、`Import → Core`、`Windows → Core`。

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
  `SceneAuthoring.Cli`への別プロセスブリッジへ逃がしている(ADR-0010/0019/0023)。
- `UserGameProject/` はエンドユーザーのゲームプロジェクトを模擬する**実際の`.csproj`**であり、
  購入済みで手動配置される Fab アセットの置き場(`UserGameProject/Assets/Fab/`)を持つ。
  ECS の唯一の実証場所でもある。
- L3の唯一のECSランタイムは `src/CSharpGameEngine.Entities`(SoA専業、**閉集合(静的)Archetype**)
  である。ユーザーはComponent型(`struct`)と`ISystem`を実装する`struct`だけを書き、
  `src/CSharpGameEngine.CodeAnalysis` の `GameSystemGenerator` が`Update`/`Spawn`メソッドの
  **引数の渡し方だけ**(属性ゼロ)から役割を推論して、Archetype型・`World.Create`
  オーバーロード・`GeneratedGameLoop`を生成する(ADR-0033、[[Ecs-Soa-Systems]]参照)。
  ユーザーはQuery・foreach・Chunk走査・ゲームループ・起動時のエンティティ生成を書かない。
  実行順は**メソッドの記述順**で決まる。
  歴史: `src/CSharpGameEngine.Ecs`(コンパイル時アーキタイプ型、ADR-0015)と
  `Generation.Abstractions`/`GenerateEntityGenerator`(AoS/SoA選択生成、ADR-0024)はADR-0029で、
  動的Archetype方式(ADR-0029)はADR-0033で、それぞれ削除済み(いずれもSuperseded)。
  `samples/` ディレクトリはADR-0034で廃止した。
- Scene Authoring は `[SceneDefinition]` クラスを `GameSystemGenerator` が静的解析し、
  `SceneNodeSymbol`実装と`CreateRuntime(ref World)`を生成する方式である(ADR-0030/0031/0033)。
  JSON方式の旧`src/CSharpGameEngine.Scene`はADR-0034で削除した。VSIX が読む `*.scene` の
  パーサ(`tools/CSharpGameEngine.VSExtension/SceneFile.cs`)は VSIX 内に残っており、
  出荷される`UserGameProject/DefaultScene.scene`を実際に読む回帰テストは
  `tests/CSharpGameEngine.VSExtension.Tests/DefaultSceneFileTests.cs` にある。
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
  (ProceduralMeshFactory)・`UserGameProject/Systems`(PlayerControlSystem/CameraSystem)の内部実装は
  RUF型経由で計算する(ADR-0026、ADR-0028)。`CSharpGameEngine.Import.Gltf`/`.Fbx`は
  `Spatial.Gltf`/`Spatial.Fbx`の各Conventionを使い、Import境界でソース形式の座標系からエンジン
  空間へ変換する(ADR-0027) — この変換により、ADR-0017が当初宣言していた「エンジン空間=glTF空間
  (右手系)」は上書きされた。Rendering境界そのもの(View/Projection規約・NDC等)は依然RUF未契約
  ([[Spatial-RUF]]「未確定事項」参照)。

## ソリューションファイルの保守
- `CSharpGameEngine.slnx` がこのリポジトリの唯一のソリューションファイルである。
  Visual Studio によってレガシーな `CSharpGameEngine.sln` 形式から移行済みで、
  そのファイルはもう存在しない。
- `src/`、`tests/`、`tools/` 配下の各プロジェクトは、対応する `/src/`、`/tests/`、`/tools/`
  フォルダ内で必ず `CSharpGameEngine.slnx` に登録すること。登録漏れがあっても
  `dotnet build`/`dotnet test`/`dotnet run --project` は `.csproj` に対して直接動作するが、
  Visual Studio 上には表示されない。
- プロジェクトの追加・削除・移動を行う際は、同じ変更の中で `CSharpGameEngine.slnx` も
  更新すること。
- ソリューションが宣言しているプラットフォームは **x64 のみ**である。
- `UserGameProject.slnx`(2つ目のソリューションファイル)は ADR-0034 で削除した。
  M0009 で消えた `CSharpGameEngine.Ecs` 等を参照しており、読み込み不能な状態で放置されていた。
