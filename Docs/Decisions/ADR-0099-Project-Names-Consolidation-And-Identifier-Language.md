# ADR-0099 — プロジェクトの名前、まとめ方、識別子の言語

- 状態: Accepted(2026-11-16。ユーザーの依頼:「全般的に `CSharpGameEngine.` を削除して」「プロジェクトが多すぎる。まとめれるのはまとめて」「日本語識別子を使っていいのは…英語識別子必須なのは…」)
- 関連: ADR-0001(層)、ADR-0034(src を 16 から 5 へまとめた)、ADR-0040(SceneModel の共有ソース)、ADR-0051(確かめの Stage と Pack)、ADR-0063(数値の精度)、ADR-0079(プラグインと Cook)、ADR-0087(描画と物理の層)
- 設計の経緯: [20261116-project-consolidation](../refactoring-designs/20261116-project-consolidation.md)

## 背景
- フォルダ名・プロジェクト名・DLL 名のすべてに `CSharpGameEngine.` が付いていて長かった。
- プロジェクトは csproj 79 個と共有プロジェクト 11 個の、合わせて 90 個あった。分ける理由の無いものも多かった。
- 日本語の識別子をどこで使ってよいかの決まりが無かった。

## 決定

### 1. 名前
- フォルダ名・プロジェクト名・DLL 名には `CSharpGameEngine.` を付けない(`src/Engine/Engine.csproj` が `Engine.dll` を作る)。
- 名前空間、シーン JSON の型名、NuGet の PackageId、VSIX の ID、テンプレートの ID は `CSharpGameEngine` のまま変えない。既存のゲーム、保存されたシーン、導入済みの拡張を壊さないため。
- 既定の名前空間(RootNamespace)は `CSharpGameEngine.<プロジェクト名>` のまま保つ(埋め込みリソースの名前を変えないため)。
- エンジンのプロジェクトかどうか(`CgeEngineAssembly`)とテストかどうか(`CgeTestProject`)は、名前ではなく置き場所(`src`・`tools`・`plugins`・`stages` と `tests`)で決める。名前の頭で判定していた数値の検査(NUM001)とシーンのスキーマ作りは、これに合わせて直した。名前を変えても、エラーを出さずに検査から外れることが無いようにするため。

### 2. まとめたもの(90 → 52。csproj 47、共有プロジェクト 5)
| まとめた後 | 中身 | 理由 |
|---|---|---|
| `src/Engine`(Engine.dll) | Core、Content、Rendering | どれも net10.0 で、パッケージ参照も unsafe も無い |
| `src/Contracts` | Contracts、ShaderArtifacts、ShaderGraph | どれも netstandard2.0 |
| `src/Numerics` | Numerics、World | どちらも数値の精度ごとに作り直す |
| `src/Runtime`(共有プロジェクト 1 つ) | Runtime.* の 6 つ | どれもゲームの組み立てにまとめて取り込まれる |
| `stages/FeatureStages` | 10 個の Pack と共有の host | どの Pack も solver 本体を持たず、同じ host と参照だけを持つ。場面の ID は重ならない |
| `tests` の 7 個 | 23 個 | 下の表 |

テスト(名前は日本語。決定 4):

| プロジェクト | 中身 | 分けておく理由 |
|---|---|---|
| エンジンのテスト | net10.0 の 13 個 | — |
| Windowsのテスト | Windows、FontBaker、ShaderArtifacts、ShaderGraph、ShaderGraph.Cli | — |
| ECSのテスト | Entities | Entities のソースを取り込み、生成器を自分に対して動かす。Entities の DLL を参照する試験と同じ型が 2 つになる |
| シーンモデルのテスト | SceneModel | SceneModel のソースを取り込む。CodeAnalysis の DLL と同じ型が 2 つになる |
| コンテンツのテスト | Content | JSON のリフレクションをプロセス全体で止めて確かめる |
| 焼き込みのテスト | Cook | 試験の DLL 自身を部品として道具に読ませる。ほかの分野の偽の部品まで見つかる |
| VS拡張のテスト | VSExtension | net472 |

### 3. まとめなかったもの
- **SceneModel のソース**: Contracts へ入れると、Contracts も参照する VS 拡張の中で同じ型が 2 つになる。使われていなかった `SceneModel.csproj` だけを消し、ソースは CodeAnalysis・VS 拡張・SceneJson.Cli・試験へ取り込む(ADR-0040 のとおり)。
- **Cooking と Import**: Cook.Cli は Import を「部品」として専用の読み込み空間(`PluginModuleLoadContext`)に分けて読み込み、道具と共有するのは契約の Cooking だけ。まとめると、共有する契約と分けて読み込む部品が同じものになる。
- **取り込みのブリッジ(glTF・FBX・図形)**: VS 拡張がそれぞれ別のプロセスで常駐させる。FBX はネイティブのライブラリがクラッシュする危険を持つので、分けて閉じ込める。
- **Extension**: 配布物の `cli/Extension.exe` で、VS 拡張の「モデルを開く」も起動する。開発用の TestHost にまとめると、配布物と VS 拡張の動きが変わる。
- **Unreal.SceneImporter**: AssetAuthoring.Cli は VS 拡張に同梱して常駐させるブリッジなので、開発用の変換道具を入れない。
- **Showcase.Automation**: 単独でまとめても 1 個減るだけで、Windows の試験の参照も変わる。
- **テンプレート、例、プラグイン、SDK の梱包**: それぞれが 1 つのゲームか、配布の単位そのもの。

判断の基準は「安全で、利用者にもプログラムにも単純な方」とした。別のプロセスで動くこと、ソースのまま取り込むこと、配布物の単位であることは、まとめない理由になる。

### 4. 識別子の言語
- 日本語を使ってよいもの: コメント、テストのプロジェクト(`tests/` の下)の中のすべて、ランタイムの内部(利用者が使わない、public でない要素)。
- 英語(ASCII の英数字と `_`)が必須のもの: 利用者が使うライブラリの public・protected の要素すべて(型、メソッド、引数、プロパティ、フィールド、イベント、列挙値、型引数)。
- ソースのまま利用者のゲームへ入るファイル(`src/Runtime`、`src/Entities`、`plugins/*/Runtime`)では、利用者のコードから見える `internal` も英語にする。日本語を使えるのは private とローカル変数だけ。
- 診断 NAME001(`EnglishApiIdentifierAnalyzer`)がビルドエラーにする。`CgeEnforceEnglishApi` が true のコンパイル(エンジン自身のプロジェクトと、このリポジトリの中のゲーム)だけで働き、利用者のゲームの名前の付け方は縛らない。

## 結果
- 層の決まりのうち、Core・Content・Rendering の間の決まりは、プロジェクトの参照ではなく、フォルダーが名指しする名前空間を調べる試験(`EngineFoldersKeepTheirLayers`)で守る。
- 短い DLL 名(`Engine.dll`、`Windows.dll` など)は、利用者のゲームの出力フォルダで、利用者が付けた同じ名前のプロジェクトとぶつかる可能性がある。利用者が決めた範囲(名前の頭を外す)なので受け入れた。
- 全 Pack を 1 つの実行ファイルにしたので、生成される ECS の `World` は全 Pack のコンポーネントをまとめた 1 つになる。各場面は自分の `IScene` だけを実体化するので、state hash は変わらない(Foundation・Fluid で確かめた)。将来 solver を持つ Pack が別のプロセスを必要とした時は、その Pack だけを分け直す。

## 確認
- 各段で全体のビルドと全試験を流し、まとめる前の結果と試験の名前を突き合わせ、消えた試験が無いことを確かめた。
- NAME001 は、試験 13 件と、実際のビルドで public の日本語の型が止まり internal が通ることで確かめた。
