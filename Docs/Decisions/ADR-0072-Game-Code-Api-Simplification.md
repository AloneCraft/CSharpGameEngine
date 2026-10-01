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

### 3. 実行順序の重複を検出する
- 同じ Phase で同じ `[SystemOrder]` の値を持つ System が複数あると、順序がファイルパスの文字列順で決まる（`GameSystemExecutionOrder`）。これを生成時の診断にする。
- **理由**
  - リポジトリに重複が既にある。エンジンの `PhysicsBodySync` と `StaticMeshColliderSync` がどちらも `10_001` で、ファイル名の順でしか前後が決まらない。
  - `[RunsAfter]` は書き込み競合の検査（`[ScheduledSystem]`）にしか使われておらず、実行順序を決めない。使用箇所も 0 件。
  - ファイル名を変えるだけで実行順が変わるのは、決定的実行（ADR-0055）の前提に反する。
- 移行: エンジン内の重複は値を分けて解消する。相対指定（`[RunsAfter]` による並べ替え）は、整数の空間をゲームとエンジンが共有する問題を解く次の段階として扱う。

### 4. 画面基準の方向をエンジンが提供する
- カメラの向きから「画面の右」「画面の上」を返すヘルパーをエンジンに置き、テンプレートの手書きの補正（`PlayerCamera.AddLook` のヨーの減算、「画面右 = (-cos, 0, sin)」）を置き換える。
- **理由**: このエンジンでは +R が、+F を向いたときの画面の左に写る（ADR-0042、`CreateLookAt` は右手系）。この知識をゲームコードに要求すると、左右反転のバグを繰り返す（2026-10-01 の ThirdPerson の入力の左右反転がその実例）。

### 5. シーン JSON の角度の単位をそろえる
- 同じ JSON の中で、回転は `EulerRuf` のラジアン、`ControlYawDegrees` は度になっている。スキーマで単位を明示し、エディタの表示と書き込みを 1 つの単位にそろえる。
- **理由**: 単位の取り違えは静かな不具合になる（1.57 を度として読むとほぼ回転しない）。

## 実施状況
| 項目 | 状態 |
|---|---|
| 1. 経過時間 | M0095 で実施 |
| 2. `void Update` | M0095 で実施 |
| 3. 順序の重複検出 | M0095 で実施（相対指定は次段階） |
| 4. 画面基準の方向 | 未着手 |
| 5. 角度の単位 | 未着手 |

## 却下した案
- `Update` の `bool` を即座に禁止する: 既存のゲームが一斉にビルドエラーになる。意味がないことを示したうえで `void` を標準にし、`bool` は残す。
- 経過時間を新しい Context 引数として注入する: `State` は全 System が既に受け取っており、`InterpolationAlpha` や `FixedTickRate` と同じ場所に置くのが最も見つけやすい。
