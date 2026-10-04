# ADR-0073 — System の実行順序を相対指定（`[RunsAfter]` / `[RunsBefore]`）にする
- 状態: Accepted（2026-10-01、ユーザー承認: 「[RunsAfter] による相対順序にして」、同着は「案1: 全部 RunsAfter + 名前順」、`[SystemOrder]` は廃止。`[RunsBefore]` は損得を確認して「残す」）
- 置き換え: ADR-0036（`[SystemOrder]`）。ADR-0072 決定 3 の「相対指定はユーザーの判断事項」はこの ADR で決着した。
- 関連: ADR-0033（静的 Archetype ECS）、ADR-0070（シーン JSON の起動シーン）、M0096

## 背景
2026-10-01 のリポジトリで観測した事実:
- ゲームとエンジンが 1 つの整数の空間を共有していた（エンジン 1〜30_000、ThirdPerson テンプレート 2 と 10、UserEcsProject 2）。ゲームを書く人は、エンジンの値の並びを知らないと順序を決められない。
- 同じ値の System の前後は、ファイルパスの文字列順 → ファイル内の宣言順で決まっていた。エンジンの `PhysicsBodySync` と `StaticMeshColliderSync` は同じ `10_001` で、前後はファイル名で決まっていた。ファイルの移動や名前の変更で順序が変わりうる。
- `[RunsAfter]` は書き込み競合の検査（`[ScheduledSystem]`）にしか使われず、実行順序を決めていなかった。付けると暗黙に `[ScheduledSystem]` になった。製品コードでの使用は 0 件。
- 起動シーンの合成 System は `startupOrder` を整数の順序として他の System と比べていたので、`Window` や `ShowcaseControls` が起動シーンの間に挟まっていた。

## 決定

### 1. 順序は Phase ごとの依存グラフで決める
- `[RunsAfter(typeof(X))]` は「この System は X の後」、`[RunsBefore(typeof(X))]` は「この System は X の前」。どちらも複数付けられる。
- Phase の順（FrameInput → FixedSimulation → PhysicsSimulation → PostPhysics → Presentation）は変わらない。前の Phase の System への `[RunsAfter]` と、後の Phase の System への `[RunsBefore]` は常に満たされるので何もしない。
- 同じ Phase の中は、辺を守るトポロジカル順に並べる。

### 2. 同着は完全修飾名の文字列順
- 辺でつながらない System 同士は、実行できるもののうち完全修飾名（`名前空間.型名`、Ordinal）が最も小さいものから実行する。
- **理由**: ファイルの場所や宣言の位置に依存せず、決定的で説明しやすい。
- **欠点**: 型や名前空間の名前を変えると、辺の無い System 同士の前後が変わりうる。順序に意味があるなら辺を書く。名前順は「辺の無いものの並べ方」であって、順序の保証ではない。

### 3. `[RunsBefore]` も用意する
- ユーザーの指示は `[RunsAfter]` だったが、`[RunsAfter]` だけでは書けない順序がリポジトリにあったので、その鏡である `[RunsBefore]` を足した。グラフの辺の向きが逆なだけで、規則は同じ。
- **理由**
  - エンジンはゲームの型を参照できない。ThirdPerson テンプレートの `ThirdPersonAnimation` は `LocomotionAnimator` の速度と落下を書き、エンジンの `LocomotionAnimation` がそれを読む。後に動くのはエンジン側なので、ゲーム側から「`LocomotionAnimation` の前」と書くしかない。名前順では `CSharpGameEngine.…` が `CgeThirdPerson.…` より前になり、1 フレーム遅れる。
  - 層の向き: Runtime.Physics / Character / Animation は Runtime.Windows の型を参照しない（M0096 G で移植可能にする）。Windows の System を下位の層の System より前に置くには、Windows 側から `[RunsBefore]` と書く。
- **代わりの案**（採らなかった）: `ThirdPersonAnimation` を PostPhysics に移す。この例では同じ結果になるが、ゲームの System をエンジンの System の前に置く一般的な手段にならない。
- **ユーザーの判断**（2026-10-01）: 損得を確認したうえで残す。
  - 損: 同じ順序を 2 通りに書ける。ある System の制約が、その System と相手の System の両方に散らばるので、読むときに逆方向の検索が要る。
  - 得: ゲームの System をエンジンの System の前に置ける。
- 2026-10-01 に確認した事実: エンジン内の `EmissiveMeshLightSync` → `SkeletalMeshSetup` の `[RunsBefore]` は、移行前の実効順を保つためだけのもの。2 つはデータを共有しないので、この順序に意味は無い。

### 4. 起動シーンは FrameInput の先頭
- シーン JSON の起動シーン（ADR-0070）から作る合成 System は、FrameInput の他のすべての System より前に、`startupOrder` → 完全修飾名の順で動く。プレビューのブリッジ（`GeneratedEditorPreviewBridge`）の生成順も同じ規則。
- **理由**: 起動シーンは最初のフレームに Entity を作るだけで、同じフレームの他の System はすべての起動シーンの Entity を見られるべき。整数の比較で間に挟まる理由は無い。

### 5. 診断
| ID | 内容 |
|---|---|
| SYSGEN033 | `[RunsAfter]` / `[RunsBefore]` の対象が、このコンパイルの System ではない |
| SYSGEN034 | 同じ Phase の中で順序が循環している |
| SYSGEN035 | 後の Phase の System の後、または前の Phase の System の前に置こうとしている（Phase の順と矛盾） |

- `[SystemOrder]` は `[Obsolete(error: true)]` にした。System にもファイルマーカーにも付けられなくなり（CS0619）、メッセージが `[RunsAfter]` / `[RunsBefore]` を案内する。
- `[SystemOrder]` 専用の SYSGEN022〜025 と、スケジュール専用の SYSGEN030（存在）・SYSGEN031（循環）は削除し、上の 3 つにまとめた。

### 6. `[ScheduledSystem]` は書き込み競合の検査だけを意味する
- `[RunsAfter]` は `[ScheduledSystem]` を暗黙に付けない。
- 競合検査（SYSGEN032）の「順序が付いている」は、同じ Phase の順序グラフに経路があること。スケジュール外の System を経由する経路も数える。名前順の同着は「順序が付いている」に数えない。

### 7. 移行
- エンジンの System は、移行前の実効順序をそのまま鎖として書いた（例: Presentation の `LightShadowValidation` → … → `EmissiveMeshLightSync` は宣言順の鎖）。データの依存を分析して鎖を緩めるのは別の作業とする。
- ゲームの System は実際の依存を書いた。
  - `ShowcaseControls`、`ThirdPersonControls`: `[RunsAfter(typeof(Window))]`（ウィンドウのメッセージを処理した後に入力を読む）。
  - `ThirdPersonAnimation`: `[RunsBefore(typeof(LocomotionAnimation))]`。
  - `ShowcaseDashboard`: `[RunsAfter(typeof(LevelLoad))]`（`LevelRuntime` を読む）。描画先は D3D のフレームではなく Win32 の別パネルなので、`Present` との前後は関係しない。
- 移行で変わった実効順序（M0096 に記録）:
  - FrameInput: 起動シーンがすべて先頭に来た。
  - Presentation: `ShowcaseDashboard` は `Present` の後、`ThirdPersonAnimation` は `SkeletalMeshSetup` の後（`LocomotionAnimation` の前は保たれる）。
- UserEcsProject の Phase ごとの順序は `GeneratedSystemOrderTests` で固定した。

## 却下した案
- 整数の順序を残し、同着だけを規則で決める: ゲームとエンジンが整数の空間を共有する問題が残る。
- 順序の無い System の組をすべてエラーにする: 依存の無い System にも辺を書かせることになり、書く量が多い。名前順で決定的にすれば十分。
