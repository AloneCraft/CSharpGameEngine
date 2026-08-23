# ADR-0046: Scene View描画モードとグローバル8分木デバッグ

- Status: Accepted
- Date: 2026-08-23

## Context

Scene Viewで実行中WorldのGlobal Spatial Indexを確認したい。一方、実行データをScene定義から再構築したり、System個別索引を暗黙収集したりすると、編集結果とランタイム状態が一致せず、ゲーム側のメモリ・処理コストも増える。

## Decision

1. Scene ViewはLit、Base Color、Normal、Wireframeをユーザー設定として永続化する。カメラプレビューは同一モード、ギズモは主ビューだけに描画する。
2. Global Spatial Octreeだけを、実行プロセスIDとCurrentUserOnlyで分離したNamed Pipeから要求駆動で取得する。未要求時はノード列挙・シリアライズを行わない。
3. ランタイムはRootと非空ノードを最大8,192件でスナップショット化し、VSIXは視錐台内を優先して最大512個のワイヤー立方体を表示する。個別System索引は明示チャネルが追加されるまで表示しない。
4. 通信断、不正入力、巨大座標、ゲーム終了はScene Viewを接続待ちへ戻すだけとし、ゲーム実行と通常描画へ伝播させない。ギズモと空間デバッグの頂点予算は分離する。

## Consequences

Scene Viewは実データを低コストで確認できる。デバッグ表示は最大件数とfloat変換範囲により完全な木を保証しないが、Rootと視錐台優先で操作性を維持する。
