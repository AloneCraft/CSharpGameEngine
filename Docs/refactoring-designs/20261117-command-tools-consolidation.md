# コマンドの道具を 1 つの実行ファイルにまとめる

## 用語

| 言葉 | 意味 |
|---|---|
| 道具 | `tools/` の下にある、コマンドで動かす実行ファイル |
| 下位コマンド | 1 つの実行ファイルの最初の引数で選ぶ働き。例: `Cge gltf <入力> <出力>` |
| ブリッジ | Visual Studio の拡張が、Visual Studio の外の別プロセスとして起動する道具。取り込みやプレビュー用の形づくりを、VS を巻き込まずに行う |
| 常駐 | ブリッジを起動したままにして、名前付きパイプ(プロセス間の通り道)で次の依頼を受けること(ADR-0023) |
| 内部の型 | `internal` の型。`InternalsVisibleTo` で名前を挙げた試験からだけ見える |

## 背景

`tools/` には実行ファイルのプロジェクトが 21 個ある(2026-11-16 のプロジェクト整理の未決事項 1)。所有者は、コマンドの道具を 1 つにまとめるよう指示した。

## 現状の観察(2026-11-17、`4c607e7e`)

1. コマンドの道具は 12 個ある。`SceneJson.Cli`、`SceneSchema.Cli`、`Primitive.Cli`、`ShaderGraph.Cli`、`Physics.Cli`、`FontBaker`、`Unreal.SceneImporter`、`Import.Gltf.Cli`、`Import.Fbx.Cli`、`Import.AssetAuthoring.Cli`、`Cook.Cli`、`ContentCompiler`。
2. `Physics.Cli` は、ゲームの数値の型ごとに作り直すプロジェクトである(`CgeNumericProfileProject`)。
3. `Cook.Cli` は、プラグインの DLL を、契約の `Cooking` だけを共有する別の読み込み場所で動かす(ADR-0079)。プラグインの組み立てと SDK が、この道具をプロジェクト名で直接作って呼ぶ。
4. `SceneJson.Cli` は、SceneModel のソース(内部の型)をまるごと取り込み、古い版のシーンを読む処理(`CGE_SCENE_UPGRADE`)を持つ唯一の道具である(ADR-0098 §5 規則 2)。実行中に .NET SDK の MSBuild を読み込むので、MSBuild の部品を出力に置けない。
5. `CodeAnalysis` も SceneModel の内部の型を持ち、`エンジンのテスト` に見せている。
6. ブリッジ(`Import.Gltf.Cli`、`Import.Fbx.Cli`、`Import.AssetAuthoring.Cli`、`Primitive.Cli`)と `ShaderGraph.Cli` は、VS 拡張の中の `Bridges/<名前>/` に同梱される。VS 拡張は、フォルダー名から実行ファイル、常駐のパイプの名前、依頼の種類(取り込み・プレビュー・焼き込み)を決める。
7. ブリッジの入口は、どれも `--serve <パイプ>` で常駐し、それ以外は 1 回だけ動く同じ形である(`Bridge.Hosting`)。
8. `ShaderGraph.Cli` と `FontBaker` は `net10.0-windows` だが、使う部品(TerraFX、System.Drawing.Common)は `net10.0` でも組み立てられる。`エンジンのテスト` と `FeatureRunner` は `net10.0` で、`ContentCompiler` などを部品として参照している。
9. ゲームの組み立ては、`ShaderGraph.Cli`(Platform=x64)、`SceneSchema.Cli`(設定なし)、`ContentCompiler`(数値の型 Fixed 128/64)を、それぞれ別の設定で作る。

## 目的

- コマンドの道具を 1 つの実行ファイル `Cge` にまとめ、プロジェクトの数を減らす。
- 道具の働きと出力は変えない。

## 非目的

- 道具の中のコードの書き直し。
- ブリッジを別プロセスで動かす作りをやめること(FBX の取り込みが壊れても VS を巻き込まないため、毎回別プロセスで起動する作りは保つ)。

## 提案設計

### まとめるもの: 9 個 → `tools/Cge/Cge.csproj`

| 元の道具 | 下位コマンド | フォルダー |
|---|---|---|
| `SceneSchema.Cli` | `scene-schema` | `SceneSchema/` |
| `Primitive.Cli` | `primitive` | `Primitive/` |
| `ShaderGraph.Cli` | `shader-graph` | `ShaderGraph/` |
| `FontBaker` | `font` | `FontBaker/` |
| `Unreal.SceneImporter` | `unreal` | `Unreal/` |
| `Import.Gltf.Cli` | `gltf` | `Gltf/` |
| `Import.Fbx.Cli` | `fbx` | `Fbx/` |
| `Import.AssetAuthoring.Cli` | `asset-authoring` | `AssetAuthoring/` |
| `ContentCompiler` | `content` | `Content/` |

- 下位コマンドの後ろの引数は、元の道具の引数と同じ。例: `Cge shader-graph build <graph> <artifact> ...`、`Cge gltf --serve <pipe>`。
- 名前空間は変えない(`CSharpGameEngine.ShaderGraph.Cli` など)。各道具の入口(トップレベルの文)は、`<道具>Command.RunAsync(string[] Arguments)` に直す。
- 対象の環境は `net10.0`(観察 8)。RuntimeIdentifier は付けない。FBX の取り込みが使う `assimp.dll` は、今の `Import.AssetAuthoring.Cli` と同じように横に写す。DXC の DLL(`dxcompiler.dll`、`dxil.dll`)も横に写す。
- `Cook.Cli` を `Cook/` に写す処理(AssetAuthoring の模型の下ごしらえ用)は `Cge` に移す。

### まとめないもの

| 道具 | 理由 |
|---|---|
| `SceneJson.Cli` | 観察 4・5。まとめると、`Cge` と `CodeAnalysis` の両方が `エンジンのテスト` に同じ名前の内部の型を見せ、試験の組み立てが曖昧さのエラーになる。古い版を読む処理の境界(ADR-0098)と、MSBuild を読み込む作りも、この道具だけに閉じておく |
| `Physics.Cli` | 観察 2。まとめると、大きな実行ファイル全体を数値の型ごとに作り直すことになる |
| `Cook.Cli` | 観察 3。まとめると、ほかの道具の部品(Roslyn、SharpGLTF、TerraFX など)が同じプロセスに入り、プラグインの部品と版がぶつかりうる |

### 呼ぶ側

- **VS 拡張:** ブリッジを「プロジェクトのフォルダー + 下位コマンド」(`BridgeTool`)で表す。実行ファイルは `Bridges/Cge/Cge.exe`(同梱)か `tools/Cge/bin/...`(開発時)。1 回だけ動かすときは下位コマンドを引数の前に付け、常駐させるときは `Cge.exe <下位コマンド> --serve ...` で起動する。常駐の依頼の中身(引数)は今と同じ。パイプの名前と依頼の種類は、下位コマンドも含めて決める。`Physics.Cli` と `Cook.Cli` は下位コマンドなしのまま。
- **VS 拡張への同梱:** 5 個のブリッジの代わりに `Cge` を 1 個、`Bridges/Cge/` に入れる(`Cook.Cli` はそのまま)。
- **ゲームの組み立て:** シェーダー、シーンの書式、素材の 3 か所は、`Cge` を同じ設定(Configuration と数値の型 Fixed 128/64)で作る。こうすると MSBuild は 1 回の組み立ての中で結果を使い回す。呼び出しは `"Cge.exe" shader-graph ...`、`dotnet Cge.dll scene-schema ...`、`dotnet Cge.dll content ...`。
- **SDK:** `tools/ShaderGraph`、`tools/SceneSchema`、`tools/ContentCompiler` の 3 つを `tools/Cge` の 1 つにする。
- **試験と FeatureRunner:** 参照先を `tools/Cge/Cge.csproj` にする。`InternalsVisibleTo` は `エンジンのテスト` と `Windowsのテスト` に出す。
- **Unreal の書き出しの手順(`tools/UnrealExport/*.cmd`)と文書:** 新しい呼び方に直す。

## 影響範囲

`tools/` の 9 個の道具、`tools/VSExtension`、`build/*.targets`、`packaging/Sdk`、`tests/`、`tools/FeatureRunner`、`tools/UnrealExport`、`tools/ShaderGraphMaintenance`、`.vscode`、`CSharpGameEngine.slnx`、Docs。

## 移行手順

1. 9 個のソースを `git mv` で `tools/Cge/<フォルダー>/` に移し、入口を関数に直して、振り分けの `Program.cs` を書く。
2. VS 拡張、組み立て、SDK、試験、手順書、文書の呼び方を直す。
3. 組み立てと試験で確かめ、保存する。

## テスト計画

- ソリューション全体の組み立て(警告をエラーとして扱う設定のまま)。
- 全試験。特に VS 拡張のブリッジの試験、SDK の包みの試験、シェーダーの試験、素材の組み立ての試験。
- 7 枚の基準画像の比較(シェーダーの道具が変わるため)。
- `Cge` の各下位コマンドを実際に動かす(使い方の文、glTF の取り込み、基本形の書き出し、シーンの書式)。

## リスクと対策

| リスク | 対策 |
|---|---|
| 入口を関数に直すときに、引数の扱いが変わる | 下位コマンドを外した残りの引数を、そのまま元の処理に渡す。各下位コマンドを実際に動かす |
| 常駐ブリッジのパイプが道具の種類をまたいで共有される | パイプの名前に下位コマンドを含める。試験で glTF と FBX の名前が違うことを確かめる |
| 実機の Visual Studio での確認ができない | 単体試験と、同梱されたブリッジを直接起動する試験で確かめる。実機での確認は所有者に頼む |

## 合意状態

- 2026-11-17: 所有者が「2 のコマンドの道具を 1 つにまとめて」と指示し、続けて「このチャットで言ったことを一通り実装して」と指示した。まとめないもの 3 個は、観察した事実(上の表)にもとづいて決めた。最初の提案は `SceneJson.Cli` も含む 10 個だったが、調べた結果まとめられないと分かった。
