# M0102 — UE5 テンプレートの処理をコードレベルで解析する
- 日付: 2026-10-22
- 状態: 完了(2026-10-22)

## 目的
ユーザーの依頼(2026-10-22)「ue5テンプレートの動作全てをコードレベルで解析して実装して」の解析の段。4 つのテンプレート(ThirdPerson、FirstPerson、TopDown、Vehicle)の Blueprint を、推測ではなくノードの接続から読み、実装する挙動の表にする。決定は [ADR-0078](../Decisions/ADR-0078-UE-Template-Full-Parity.md)、結果は [UE-Template-Behavior](../Specifications/UE-Template-Behavior.md)。

## 進捗
| 段階 | 内容 | 状態 |
|---|---|---|
| 1 | UE からの書き出し(`dump_blueprint_logic.py`) | 完了 |
| 2 | T3D の読み取りと疑似コード(`logic` コマンド) | 完了 |
| 3 | 4 テンプレートの全 Blueprint の解析と挙動仕様 | 完了 |
| 4 | 文書 | 完了 |

## 段階 1. UE からの書き出し
- 実験: 計画では、UE5.8 の `EditorToolset` プラグインの逆コンパイラ(`read_graph_dsl`)で疑似コードを作る予定だった。ヘッドレスのエディター(`UnrealEditor-Cmd`)では、どのグラフでも空の文字列が返った。代わりに、エディターのテキスト書き出し(T3D)を正本にした。T3D には、ノード・ピン・接続、アニメーションの状態機械と遷移の条件、ウィジェット、入力の割り当て、カーブが全部入っていた(`ABP_Unarmed` と `UI_Vehicle` で確認)。
- 分かったこと: T3D は既定値からの差分しか書かない。車両の移動コンポーネントのエンジン・変速・車輪の設定は、親クラスから継いでいて、T3D に出なかった。Python の `dir()` には、編集専用の属性(`EngineSetup`、`Mass`、`WheelSetups`)が出ない。そこで、エンジンのヘッダーの `UPROPERTY` から名前を拾い、`get_editor_property` と `export_text` で全部を書く `.components.txt` を足した。Blueprint の親クラスは `parent_class` では読めず、`BlueprintEditorLibrary.get_blueprint_parent_class` で読む。
- 出力: 資産ごとに `.t3d`、`.class.t3d`、`.defaults.t3d`、`.components.txt`、`.graphs.json`(参考)、`_summary.json`。

## 段階 2. 疑似コード
- `T3dDocument`(T3D の読み取り。オブジェクト、属性、`CustomProperties Pin` の接続)と `BlueprintLogicWriter`(`CSharpGameEngine.Unreal.SceneImporter logic <dir>`)。
- 入口(イベント・入力・関数)から実行ピンをたどって文にする。純粋なノードは式として展開し、Reroute(knot)は透過にする。状態機械は、状態・別名状態(「from any of」)・遷移と条件式にする。アニメーションのポーズは、出力から入力へさかのぼる木にする。
- 直したこと:
  - knot を経由する実行の流れが切れていた。
  - 分岐の最初の文が抜けていた。
  - `Walk / Run` のような名前の `/` で、名前を切っていた。

## 段階 3. 解析
- 書き出しの結果(2026-10-22): ThirdPerson 18 資産 → 40 ファイル、FirstPerson 21 → 49、TopDown 10 → 18、Vehicle 27 → 55。どれもエラー 0。
- 推測から事実に置き換えたもの(例):
  - 歩きの判定: 推測は「速度 3 以上」。実際は `GroundSpeed > 0.01 && 加速度 ≠ 0`。
  - TopDown: 押していた時間が 0.5 秒未満なら `SimpleMoveToLocation` とカーソルの演出、押し続けたら追従する。
  - Vehicle: 3 秒ごとに転倒を調べる。`dot(上, Z) < −0.2` が 2 回続けばリセットし、50 cm 上へ戻す。空中の回転の減衰は 3。
  - Chaos の全数値: 質量 1500、エンジン 750 Nm・7000 rpm・アイドル 900、変速比 4.25/2.52/1.66/1.22/1.00・後退 4.04・最終減速 2.81、アッカーマン 0.7、後輪駆動、車輪の半径 39/40、ブレーキ 4500/1500、ハンドブレーキ 6000 など。
  - HUD の速度は ×0.036 で km/h。
- 結果は、テンプレートごとに「イベント → 処理 → 値 → 状態 → 担当のマイルストーン」の表にした(UE-Template-Behavior)。

## 自動化された証拠
- `tests/CSharpGameEngine.Import.Tests/Unreal/BlueprintLogicWriterTests.cs`(4 つのテスト、7 件):
  - T3D の読み取り(オブジェクト、属性、ピン、接続)
  - 実行の流れ(純粋な式の展開と knot の透過)
  - 名前の切り方(`/` を含む名前)
  - 最上位のカンマだけで分けること
- 状態機械と遷移の出力は、4 テンプレートの実データ(`.artifacts/UnrealExport/logic/`)を読んで確かめた。単体テストはまだ無い。
- UE を使う書き出し自体は手動の実行(UE のインストールが要る)。手順は `tools/UnrealExport/README.md`。

## 完了の判定
- 4 テンプレートの全 Blueprint の T3D と疑似コードが揃い、挙動仕様の表の各行に出典のノードがある。
- 意図と観測の一致: Matches(解析の範囲。実装は M0103 以降)。
