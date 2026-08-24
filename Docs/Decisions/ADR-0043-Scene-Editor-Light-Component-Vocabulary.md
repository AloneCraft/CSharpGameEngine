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

## 2026-08-21 更新: 物理ライトと Emissive

この節は上記の Phase 6 決定を置換する。Scene Editor の正規のライト語彙は、完全修飾名による宣言的な Binding を維持したまま、物理単位を持つ 4 種の Component とする。

### Component 契約

- `global::UserEcsProject.DirectionalLight`
- `global::UserEcsProject.PointLight`
- `global::UserEcsProject.SpotLight`
- `global::UserEcsProject.AreaLight`

すべてのライトは同じ Entity の `global::CSharpGameEngine.Runtime.Windows.Transform` を必須とする。位置は Transform の Translation、向きはローカル `+Z`（光の進行方向）から取得する。Directional は回転のみ、Point は位置のみ、Spot と Area は位置と回転を使用する。Area はローカル `+Z` 側だけが発光する片面矩形であり、Transform Scale は物理サイズに暗黙適用しない。

| Component | 強度 | 固有の物理値 |
| --- | --- | --- |
| DirectionalLight | 照度 `lux` | Linear RGB、影設定 |
| PointLight | 全光束 `lumens` | Range `m`、影設定 |
| SpotLight | 全光束 `lumens` | Range `m`、内外の全角 `degree`、影設定 |
| AreaLight | 輝度 `nits` (`cd/m²`) | Width/Height/Range `m`、片面矩形、影設定 |

各ライトの共通影設定は `CastsShadows`、`ShadowPriority`、`ShadowBiasMeters` とする。色は Linear RGB とし、Camera の `ExposureEv100` を使って HDR 露出へ変換する。

### Editor と移行

PropertyGizmo と Inspector は上記の物理フィールドを編集する。旧 `Intensity`、ライト内 `Position*`/`Direction*`、radian の Cone 角は廃止し、移行時には新しいフィールド名と単位・Transform への移動先を示す専用診断を出す。既存 Scene はこの契約へ一括移行し、ライトの追加時に Transform がなければ同じ編集トランザクションで追加する。

### Emissive

Material の Emissive は自己発光表示だけでなく、直接光と影を生成する発光面として扱う。glTF の `emissiveFactor`、`emissiveTexture`、`KHR_materials_emissive_strength` を読み込み、値 1.0 は既定で 100 nits に換算する。発光三角形は決定的に最大 16 個の矩形パッチへ近似し、Object の Transform に追従して AreaLight と同じ直接光・シャドウ経路へ送る。二次反射および多重 GI はこの決定の対象外である。


## Namespace update (ADR-0048)

The authored physical light vocabulary is `CSharpGameEngine.Runtime.Windows.{DirectionalLight,PointLight,SpotLight,AreaLight}`. VSIX bindings and migration diagnostics use these names; old UserEcsProject names remain diagnostic-only.