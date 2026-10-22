# ADR-0078 — UE5 テンプレートの挙動をコードレベルで解析し、ECS で完全に移植する
- 状態: Accepted(2026-10-22。ユーザーの依頼:「ue5テンプレートの動作全てをコードレベルで解析して実装して」「ecsで実装するが必要となるライティングベイクシステムなども」「カオス破壊機能も実装」「ライティングの静的ベイクは今はなし。lumen相当を実装して」)
- 関連: ADR-0077(ゲームコードの書き方)、ADR-0069(物理ベースの空と動的 GI)、ADR-0067(キャラクターの移動)、ADR-0068(VS テンプレート)、ADR-0017(UE のレベル書き出し)、ADR-0079(プラグインとアセット)、M0102〜M0113

## 背景
- 4 つのテンプレート(ThirdPerson / FirstPerson / TopDown / Vehicle)は、核の遊び(歩く・見る・跳ぶ、クリック移動、運転)だけを移植していた(M0101 段階 8・9)。
- それまで UE の Blueprint は、既定値の書き出し(`dump_blueprint.py`)と、`.uasset` の中の読める文字列からの推測でしか読めず、処理の流れは推測だった。例: 推測では「速度 3 以上で歩く」としていたが、実際の `ABP_Unarmed` は `GroundSpeed > 0.01 && 加速度 ≠ 0` だった。

## 決定

### 1. 解析の正本は、UE のテキスト書き出し(T3D)とする
- `tools/UnrealExport/dump_blueprint_logic.py`(`run_dump_blueprint_logic.cmd`)で、資産ごとに次を書く。
  - `<名前>.t3d`: Blueprint 本体の全文。グラフのノード・ピン・接続、アニメーションの状態機械と遷移、ウィジェット、入力の割り当て、カーブを含む。
  - `.class.t3d` と `.defaults.t3d`: 生成クラスと既定オブジェクト(CDO)。Blueprint で足したコンポーネントのテンプレートが入る。T3D は既定値からの差分だけを書く。
  - `.components.txt`: CDO と、その各コンポーネントの全属性。構造体は UE の `export_text` で書く。Python の `dir()` に出ない編集専用の属性(例: `EngineSetup`、`Mass`、`WheelSetups`)は、エンジンのヘッダーの `UPROPERTY` から名前を拾って読む。
  - `.graphs.json`: `BlueprintGraphEditor` によるノードとピンの一覧。UE のロケールの文字列が入るので、参考扱い。
- 疑似コードは、T3D からエンジン側のツールで作る(`CSharpGameEngine.Unreal.SceneImporter logic <dir>` → `<名前>.logic.txt`)。
  - 入口(イベント・入力・関数)から実行ピンをたどって文にする。データのピンは、純粋なノードを式として展開する。分岐は出力ピンごとに字下げする。
  - アニメーションは、状態・遷移・遷移の条件式と、出力から入力ポーズへさかのぼる木にする。
  - UE5.8 の EditorToolset の逆コンパイラ(`read_graph_dsl`)は、ヘッドレスでは空を返したので使わない。
- 出力先は `.artifacts/UnrealExport/logic/<Template>/` で、リポジトリには入れない。UE の資産から何度でも作り直せるため。仕様は `Docs/Specifications/UE-Template-Behavior.md` にまとめる。

### 2. 作るもの・作らないもの
| 対象 | 判断 | 理由 |
|---|---|---|
| 4 テンプレートの Blueprint の処理(入力、移動、カメラ、アニメーション、リスポーン、車両の全操作、HUD) | 作る | 依頼の本体 |
| タッチ操作(`BPI_TouchInterface*`、`UI_Thumbstick`、`UI_Touch*`)とハンドルコントローラー(`IMC_SteeringWheel`) | 作らない | ユーザーの決定(画面 UI は作る、タッチは除く)。UE でもモバイルか強制設定のときだけ有効 |
| 配置されていない Blueprint(`BP_JumpPad`、`BP_DoorFrame`、`BP_WobbleTarget`、`BP_VehicleAdvOffroadCar`) | 作らない(解析だけ残す) | どのレベルにも置かれておらず、テンプレートの動作に現れない |
| XR(Vive・Oculus など)のキーの割り当て | 作らない | エンジンは XR の入力を持たない |
| 静的なライティングのベイク | 作らない | ユーザーの決定。UE のテンプレートも Lumen の動的 GI で、焼いていない |
| Lumen 相当の動的 GI と反射、ボリュメトリック雲、空の球 | 作る | ユーザーの決定。ADR-0069 の DDGI プローブは、低品質と WARP の代替の段として残す |
| Chaos Vehicle 相当(剛体、車輪ごとのサスペンション、トルク曲線、変速、アッカーマン操舵、タイヤの摩擦、空中の減衰、転倒とリセット) | 作る | ユーザーの決定。M0101 段階 9 の簡易モデルは置き換える |
| Chaos の破壊(Geometry Collection、Fracture、Field に相当) | 作る | ユーザーの追加依頼。テンプレートには無いので、エンジン機能と専用の検証シーンにする |
| Vehicle のレベルの路面(Landscape の舗装の材質)、Spline に沿ったトラックと縁石(`SM_Track_10M`)、傾斜、アーチ | 作る(M0112) | UE の参照画像との比較(2026-10-22)で、エンジンが平らな板 1 枚だと分かった。Landscape は glTF に書き出せないので、材質と Spline を別に取り込む |
| ナビメッシュ(TopDown の `SimpleMoveToLocation`) | 作る | 経路探索はテンプレートの挙動。ナビメッシュは生成したデータで、ライティングではない |
| 音 | テンプレートには無い(UE のテンプレートに音声資産が無い) | M0101 で取り込んだ Epic 教材の音はそのまま |

### 3. Chaos 相当(車両と破壊)のレイヤー
`Docs/Architecture.md` の L0〜L9 と、既存の決定的な物理(M0055 / M0096)に合わせて置く。

| レイヤー | 置くもの |
|---|---|
| L5 `Runtime.Physics`(既存の拡張) | ソルバー本体: 剛体、結合のひずみ、クラスターの分離、破片の剛体化、アンカー、破片の上限と除去。UE の Chaos の実行時に当たる。 |
| L5 プラグイン `plugins/CSharpGameEngine.Vehicles`(ADR-0079) | 車両: 車輪ごとのサスペンション、トルク曲線と変速、アッカーマン操舵、タイヤの摩擦、空中の減衰、転倒とリセット。UE の ChaosVehicles プラグインに当たり、物理の上に載る。M0101 段階 9 の `Runtime.Character` の簡易版は、ここへ移して置き換える。 |
| L5 プラグイン `plugins/CSharpGameEngine.Destruction`(ADR-0079) | 破壊の Component と System、Field。UE の GeometryCollection プラグインに当たる。結合とクラスターを解くソルバーは上の `Runtime.Physics` に置く(UE でも Chaos 本体はエンジンにある)。 |
| L4 物理ドメインの契約(`IPhysicsHost`、`PhysicsDomainRegistry`、`PhysicsContracts`) | 点に加える力、トルク、接触の衝撃(ひずみの元)のコマンドとイベント。L5 の車両は、この契約だけを使う(L5 の solver は engine-owned な L4 の契約だけを参照する、という Architecture の決定)。 |
| L3 ECS | Component と System。固定 tick の PhysicsSimulation・PostPhysics のフェーズで回る。ランタイムとベイクで同じ System を使う。 |
| L6 成果物 | 調理済みの破砕資産(破片・結合・クラスター)、破壊を記録した物理キャッシュ(`.cgephyscache` の拡張)。 |
| L7 ベイクとコンパイラ | ボロノイ分割の調理(`Physics.Cli`)、破壊を記録する Scene Bake Host。 |
| L8 VS | Fracture の操作(分割数・シード・プレビュー)、物理パネルのベイク。 |
| L9 ゲーム | Vehicle テンプレート、破壊の検証用シーン。 |
| L0 | 置かない。解くのは決定的な CPU のコードで、描画は既存のレンダラーが行う。 |

### 4. マイルストーン
計画の段階 0〜2 を、M0102(解析)、M0103(プラグインとアセットの仕組み。ADR-0079。車両・ナビメッシュ・破壊をプラグインとして作るので先に置く)、M0104(画面 UI)、M0105(実行時の生成と使い捨てエフェクト)、M0106(ナビメッシュ)、M0107(キャラクターとアニメーション)、M0108(入力の拡張)、M0109(Chaos 相当の車両)、M0110〜M0111(Lumen 相当)、M0112(テンプレートの仕上げ)、M0113(破壊)とする。各マイルストーンの決定は、そのマイルストーンの文書に書き、この ADR の表を更新する。

完了したマイルストーン: M0102(解析)、M0103(プラグインとアセット)、M0104(画面 UI。[M0104](../Milestones/M0104-Screen-UI.md): 描画契約 `stage-v11`、SDF のフォント、`ScreenText`・`ScreenImage`、Vehicle の HUD)、M0105(実行時の生成と使い捨てエフェクト。[M0105](../Milestones/M0105-Runtime-Spawn-And-Effects.md): 描画物が Entity の増減に追従する(描画契約は変えない)、`FixedTransform` の Generation 固定の修正、`Spawner.Spawn<TScene>(位置)` と `ISpawnableScene`、`Lifetime`、`ParticleBurst`、TopDown のクリック地点の目印)。

## 結果
- UE の処理を推測ではなく、ノードの接続から読める。実装と UE の差は `UE-Template-Behavior.md` の表の行ごとに、出典のノードと実装・テストを並べて追える。
- 解析は UE の書き出しに依る。UE の版が変わったら、`run_dump_blueprint_logic.cmd` と `logic` を流し直して差分を見る。
