# ADR-0072 — ゲームコード API の簡素化
- 状態: Accepted（2026-10-01、ユーザー承認: 「ADR を書いた上で破壊的変更 OK、理由も書く」）
- 関連: ADR-0033（静的 Archetype ECS）、ADR-0035（生成エントリポイント）、ADR-0036（SystemOrder）、ADR-0042（ギズモの利き手）、ADR-0055（決定的実行）、ADR-0070（シーン JSON）、M0095

## 背景
M0095 の調査で、ゲーム開発者が書く System に、エンジンの都合による定型・落とし穴が多いことが分かった。数字はすべて 2026-10-01 のリポジトリ（`src`、`templates`、`UserEcsProject`、`stages`）で数えた。

## 決定

### 1. 経過時間を `State` で公開する
- `State.FrameDeltaSeconds`（直前のフレームからの実時間）と `State.FixedDeltaSeconds`（固定 tick 1 回分、`1 / FixedTickRate`）を追加する。生成されたループが `RunFrame` の先頭で設定する。
- **理由**
  - 生成ループは毎フレーム経過時間を測っている（`GameSystemLoopEmitter` の `ElapsedCounter`）のに、System からは読めない。そのため ThirdPerson テンプレートの `ThirdPersonControls` と、エンジンの `LocomotionAnimation` がそれぞれ `Stopwatch` で同じ計算をしていた。
  - 自前計算のために System struct のフィールドに前回時刻を持ち、初回フレームを「時刻の初期化だけして入力を捨てる」特別扱いにしていた。
  - 自前の `Stopwatch` は一時停止・ステップ実行・エディタプレビューを知らない。ループの値に寄せれば、時間の出所が 1 つになる。
- 互換性: 追加のみ。既存コードは壊れない。

### 2. `Update` の戻り値を `void` にする
- 生成器は `void Update` を受け付け、標準とする。リポジトリの System はすべて `void` に移す。`bool` も当面受け付ける（下の World System の打ち切りのため）が、新しいコードでは使わない。
- **理由**
  - リポジトリの `Update` 43 個のうち 42 個は常に `true` を返し、`false` を返すのは `Window` の 1 個だけだった。
  - 生成コードでは、エンティティごとの System の `false` は `continue;`（ループ末尾）で、**効果がない**。`false` が意味を持つのは Component を持たない World System（そのフレームの残りを打ち切る）だけで、リポジトリの World System はどれも `false` を返していない。
  - `Window` の `false` も実際には何もしておらず、ループを止めているのは直前の `State.Enabled=false`。つまり全 System が意味のない `return true` を書かされていた。
- 互換性: `bool` を受け付け続けるので、既存のゲームは壊れない。

### 3. 実行順序の重複を検出する（取り下げ、2026-10-01）
- 当初の案: 同じ Phase で同じ `[SystemOrder]` の値を持つ System を生成時の診断にする。
- **取り下げた理由**: `GameSystemOrderAnalyzer` には、ユーザーが確認済みの判断として「ファイル既定値の衝突だけをエラーにし、System に直接付けた明示値どうしの重複は意図的な同着として許す」と記録されている。この案はその判断を覆すので採らない。同着の順序は「ファイルパスの文字列順 → ファイル内の宣言順」で決まり、決定的ではある。
- 観測した事実（判断の材料として残す）
  - エンジンの `PhysicsBodySync` と `StaticMeshColliderSync` はどちらも `10_001` で、前後はファイル名の順で決まる。
  - `[RunsAfter]` は書き込み競合の検査（`[ScheduledSystem]`）にしか使われておらず、実行順序を決めない。使用箇所も 0 件。
  - ゲームとエンジンが同じ整数の空間を共有している（エンジン 1〜30_000、ThirdPerson テンプレート 2 と 10）。
- 相対指定（`[RunsAfter]` による並べ替え）を導入するかは、ユーザーの判断事項として残す。
- → 決着（2026-10-01）: ユーザーの判断で `[SystemOrder]` を廃止し、`[RunsAfter]` / `[RunsBefore]` の相対指定と完全修飾名順にした（ADR-0073）。

### 4. 画面基準の方向をエンジンが提供する
- カメラの向きから「画面の右」「画面の上」を返すヘルパーをエンジンに置き、テンプレートの手書きの補正（`PlayerCamera.AddLook` のヨーの減算、「画面右 = (-cos, 0, sin)」）を置き換える。
- **理由**: このエンジンでは +R が、+F を向いたときの画面の左に写る（ADR-0042、`CreateLookAt` は右手系）。この知識をゲームコードに要求すると、左右反転のバグを繰り返す（2026-10-01 の ThirdPerson の入力の左右反転がその実例）。

### 5. シーン JSON の角度の単位をそろえる
- 同じ JSON の中で、回転は `EulerRuf` のラジアン、`ControlYawDegrees` は度になっている。スキーマで単位を明示し、エディタの表示と書き込みを 1 つの単位にそろえる。
- **理由**: 単位の取り違えは静かな不具合になる（1.57 を度として読むとほぼ回転しない）。

## 実施状況
| 項目 | 状態 |
|---|---|
| 1. 経過時間 | 実施（784b783） |
| 2. `void Update` | 実施（9ff33c5） |
| 3. 順序の重複検出 | 取り下げ（既存のユーザー判断と衝突）。順序の指定自体を ADR-0073 で相対指定に置き換えた |
| 4. 画面基準の方向 | 既に満たされている: 補正は `PlayerControl.AddLook` / `MoveDirection`（Runtime.Character）にあり、ThirdPerson テンプレートは画面基準の入力を渡すだけ。使い手のない汎用ヘルパーは追加しない |
| 5. 角度の単位 | ユーザーの判断で形式を変えた: シーン JSON の角度は turns（1 = 1 回転）、Inspector は度（ADR-0074） |

## 却下した案
- `Update` の `bool` を即座に禁止する: 既存のゲームが一斉にビルドエラーになる。意味がないことを示したうえで `void` を標準にし、`bool` は残す。
- 経過時間を新しい Context 引数として注入する: `State` は全 System が既に受け取っており、`InterpolationAlpha` や `FixedTickRate` と同じ場所に置くのが最も見つけやすい。
