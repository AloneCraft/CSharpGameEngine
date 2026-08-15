# ADR-0043: Scene Editor のライト語彙は現行 L9 Component を Binding する

- 状態: 採用
- 日付: 2026-08-16
- 対象: M0020 Phase 6

## 文脈

Scene Editor のライト用 PropertyGizmo は、ライト Component の完全修飾名と編集対象 Member を識別する必要がある。現行ランタイムでは `DirectionalLight`、`PointLight`、`SpotLight` が `UserEcsProject` にあり、Renderer ハンドルの所有と ECS System による毎フレーム同期を担っている。一方、Camera と Transform は `CSharpGameEngine.Runtime.Windows` の標準 Component である。

Phase 6 の着手時点では、ライト型をエンジンへ標準化するか、現行 L9 型を Scene Editor の Binding 表へ登録するかが ADR-0041 決定事項 9 で未決定だった。

## 決定

Phase 6 では、次の現行型を完全修飾名の文字列 Binding として Scene Editor に登録する。

- `global::UserEcsProject.DirectionalLight`
- `global::UserEcsProject.PointLight`
- `global::UserEcsProject.SpotLight`

VSIX から `UserEcsProject` へのプロジェクト参照は追加しない。Binding は Scene 解析結果の完全修飾名と照合する宣言的なアダプターとし、型のロードや反射に依存しない。Camera は既存のエンジン標準型 `global::CSharpGameEngine.Runtime.Windows.Camera` を Binding する。

`RectLight` は現行ランタイムに型・描画契約が存在しないため、Phase 6 の対象外とする。

## 理由

ライト型を今ここで移すと、Component の名前だけでなく、Renderer ハンドルの所有、Level から実 Entity へのコピー、同期 System、Scene 宣言を同時に変更する必要がある。これは PropertyGizmo の提供範囲を超え、実行時挙動の回帰面を不必要に広げる。

完全修飾名 Binding は既存の Transform/mesh Binding と同じ拡張点であり、VSIX と L9 のコンパイル時依存を作らずに、現在の source of truth を編集できる。

## 帰結

- Phase 6 は現行シーンの Camera と3種のライトを編集できる。
- L9 型名または Member 名を変更するときは Binding と適合テストも同時に更新する。
- ライト標準化は、Renderer ハンドルを Component に残すか、ランタイムサービスへ分離するかを決める別ADRで扱う。
- RectLight を追加するときは、ランタイム型・描画契約・Scene Binding・PropertyGizmo を一括で設計する。
