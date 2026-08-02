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
- `SourceLocation` の line/column は、判明している場合は **1始まり**。`0` は不明を意味する。
- レイヤー番号はドキュメント/ソリューション整理のためだけのものであり、名前空間には含めない。

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
