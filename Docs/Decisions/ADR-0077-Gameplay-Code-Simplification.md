# ADR-0077 — ゲームコードの書き方を、ECS と Roslyn の前提で簡潔にする
- 状態: Accepted（2026-10-20、ユーザーの依頼:「ユーザーが『こう書けたらいいな』と思えるコードを書いて。UE や unity のレガシーを清算したようなものを」「コードとしては妥当なので実装を始めて」。理想コードは `Templates/` に置き、ユーザーが確認した）
- 関連: ADR-0033（属性ゼロの System）、ADR-0072（ゲームコード API の簡素化）、ADR-0073（実行順序）、ADR-0070（シーン JSON）、ADR-0056（オーディオ）、M0101、[理想コード](../../Templates/README.md)

## 背景
- 現在のテンプレートのユーザーコードには、機械が決められることを書かせている箇所が多い。入力の GUID と文字列のキー名、初回に入力を据え付けるフラグ、`[SystemPhase]`/`[RunsAfter]`、末尾の `ref State`、親の値を読むための `State.World.TryGetComponent`、LogicalScalar を戻すための `TryToSingle`。
- UE の名前と概念（`PlayerController`、`SpringArm`、`CharacterMovement` の CMC の項目名、`IA_*`/`IMC_*`、`BP_*`）がそのまま残っている。
- 理想のコードを先に書き（`Templates/ThirdPerson`、`FirstPerson`、`TopDown`、`Vehicle`）、実コードに照らして実現性をレビューした。実装は可能だが、全項目が System の引数の役割表（ADR-0033 §2）の見直しを前提とする。

## 決定

### 1. 引数の役割表を次のように改める（実装済み: M0101 段階 1）
| 引数 | 役割 |
|---|---|
| 先頭・値渡しの `Entity` | Entity |
| 末尾の `ref State` | State。**任意**（SYSGEN013 を廃止） |
| `ref T`（非 nullable struct） | 書き込む Component |
| `in T`（非 nullable struct。プリミティブを除く） | **読み取る Component**。`[ReadOnlyComponent] in T` と同じで、変更バージョンを刻まない |
| 参照型の値渡し | Context |

- `[ReadOnlyComponent]` は受け付けるが不要になる（廃止は後の段階で行う）。
- 構造体の Context（`in` で受ける struct）は書けなくなる。Context は参照型の値渡しだけ。エンジンの System とテンプレートには、この形の Context は無かった。
- ADR-0033 §3「Write→Read の推論は原理的に成立しない」は、`ref`=書く、`in`=読む と明示したことで、後の段階（フェーズと順序の推論）で扱える前提ができた。

### 2. 以降の段階（方針。実装するときにこの ADR へ追記する）
実現性レビューの結果と、削る・縮める判断を含む。

| 段階 | 内容 | 判断 |
|---|---|---|
| M2（型とフィールドの改名は完了。`GameWindow` の統合と `ViewAngles` の turns 化は M4 と合わせる） | UE 由来の名前の清算（`CharacterMovement`→`CharacterMotion` など）。シーン JSON は `SceneJson.Cli` で一括変換（schemaVersion 4）。並行リーダーは作らない（ADR-0070 §7） | 型名の変更でスキーマ指紋が変わり、既存のスナップショットと物理キャッシュは読み込みを拒否される |
| M3（完了） | `in Parent<T>`（親の Component を型で読む）。登録のためだけの空の `Spawn` を廃止 | `ref T` と `in Parent<T>` の併用は診断で禁止（SYSGEN037）。`Parent<T>` は値のコピーと親 Entity を持つ。`Parent` 自体の登録のための `HierarchyQueryRegistration` は残す |
| M4（完了） | `[InputActions]`（入力を構造体で宣言）。ID はメンバー名から決める。入力を読む System は FrameInput に入り、`Window` の後に動く。視点は `Look` 型 | 同じ Generator の中で合成 System を作る。再割り当てとコンテキスト切り替えは当面しない（ユーザー判断: 短さを優先） |
| M5（完了。WAV は当面そのまま読む） | 音。`Audio` サービスと XAudio2 のワンショット再生。WAV はビルド時に `.cgeaudio` へ変換する案（ADR-0056）は未実施で、`.wav` の容器だけを読む | `Audio` は Presentation 専用。`XAudio2Backend.SubmitBuffer` のバッファ管理を直す |
| M6（完了） | イベント（`in Jumped Event`） | 位置は `Event.Source`（Entity）で渡す。`Footstep` は歩幅から作る。寿命は固定フェーズと Presentation で分ける |
| M7（完了） | 型付きアセット `Assets.*` | ADR-0070 §2 との衝突があるため後回し。文字列パスのままでもサンプルは動く |
| M8 | フェーズと順序の推論 | 全面的なデータフロー推論はしない。規則表と、限定した辺だけ |
| 見送り | `Start` メソッド | 「到着バージョン」の保持場所とスナップショットの扱いが未決。入力の据え付けは M4 が吸収する |

### 3. 置き場所
- 新しいテンプレートと既定のサンプルは `Templates/ThirdPerson`、`FirstPerson`、`TopDown`、`Vehicle`（UE のテンプレート名と同じ）に置く。現行の `Templates/CSharpGameEngine.Templates.ThirdPerson` は、移行が終わるまで出荷物として残し、その後に置き換える。
- 理想コードに `.csproj` は置かない。ビルドできるようにするのは、対応する段階の実装が入ってから。

## 採らなかった案
- 役割表を変えずに属性で補う（`[Read]`、`[Service]`）。ADR-0033 の「属性ゼロ」を崩し、書く量が増える。
- 構造体の Context を残し、`[ReadOnlyComponent]` を必須にする（現状維持）。`in` の意味がコードの上で 2 通りに分かれ、読み取りの宣言を書き忘れると Context になって黙って走らなくなる。
- Generator を機能ごとに分ける。Generator 同士は互いの出力を見られず、順序の推論と合成 System に支障が出る。

## 影響
- `in` で受ける struct が Component になるので、構造体の Context を使っていた System は壊れる。リポジトリ内には該当が無かった（エンジンの System は `ref`、`StageAtmosphere` は `ISystem` ではない）。
- Update が Component を 1 つも取らなくなる場合の扱い（`IWorldSystem`）は変わらない。
- 今は `in` の struct を読み取りにする効果だけが出る。フェーズと順序の推論はまだ行わないので、明示的な `[SystemPhase]` と `[RunsAfter]` は引き続き要る。

## 検証
- `GameSystemGeneratorDiagnosticsTests`（State 省略、`in` の読み取り、`in`+`ref` の重複 SYSGEN011）、既存の CodeAnalysis.Tests 220 件。
- テンプレートと UserEcsProject がビルドできること。

## 関連する機能
F-L7-050（ゲーム API の簡素化）。
