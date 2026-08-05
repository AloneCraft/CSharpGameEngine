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

## 現在のマイルストーンの範囲
- L1(`CSharpGameEngine.Core`)に加えて、L0 の Windows/Direct3D 12 垂直スライス:
  `CSharpGameEngine.Platform.Windows`(素の Win32 ウィンドウ)、`CSharpGameEngine.Compiler.Hlsl`
  (DXC ベースの HLSL コンパイル)、`CSharpGameEngine.Graphics.D3D12`(デバイス/スワップチェーン/
  パイプライン)、および `CSharpGameEngine.Import.Gltf`(glTF/GLB メッシュインポート、
  プラットフォーム非依存)。`Core` 自体には Windows/D3D/DXC への依存は一切ない。
- `tools/CSharpGameEngine.TestHost` と `tools/CSharpGameEngine.Extension` の2つが合成ルート
  (composition root)である: `TestHost` はエンジンのセルフテストと `triangle` 垂直スライスを
  実行する。`Extension` はプレースホルダーのホスト(まだ本物の VSIX ではない - ADR-0004 参照)で
  あり、glTF モデルをインポートして固定カメラで表示する。
- `UserGameProject/` はエンドユーザーのゲームプロジェクトを模擬しており、
  購入済みで手動配置される Fab アセットの置き場(`UserGameProject/Assets/Fab/`)を持つ。
  現段階ではフォルダのみであり、`.csproj` ではない。
- `src/CSharpGameEngine.Generation.Abstractions`(L7、Roslyn 非依存)と
  `src/CSharpGameEngine.CodeAnalysis` の `GenerateEntityGenerator` が、
  `[GenerateEntity(EntityGenerationSettings.Kind)]` を付けた partial struct から AoS または
  ECS SoA(`src/CSharpGameEngine.Ecs`)いずれかの物理ストレージ実装をコンパイル時に選択して
  生成する(ADR-0024、[[Generated-Entity-Storage]]参照)。`samples/` はこのリポジトリで
  実行可能な設計サンプル(`AosSoaGeneratedExpansion` 等)を独立プロジェクトとして置く場所で、
  本番の `src/`/`UserGameProject/` からは参照されない。
- `SourceLocation` の line/column は、判明している場合は **1始まり**。`0` は不明を意味する。
- レイヤー番号はドキュメント/ソリューション整理のためだけのものであり、名前空間には含めない。
- `src/CSharpGameEngine.Spatial.Abstractions`(L1、RUF/XYZ値型・`ICoordinateConvention`/
  `ITransformConvention`契約・`Spatial<TConvention>`型ベースFacade)と、その具体的Convention実装
  `src/CSharpGameEngine.Spatial.Unity`/`.Unreal`/`.Blender`/`.Gltf`/`.Fbx`(いずれもL1)が、
  ゲームコードからUnity/Unreal/Blenderの物理XYZ配置差を隠すRUF意味座標系を提供する(ADR-0025、
  [[Spatial-RUF]]参照)。`CameraBasis`/`DirectionalLightBasis`/`SpotLightBasis`はL2(Camera/Light
  基底)。コンパイル時Preset選択は`SpatialPresetSelection.props`が担い、`samples/SpatialPresetDemo`
  がその使用例を示す。
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
