# M0123 — 物理の L4 契約と L5 ソルバーの分離
- 日付: 2026-10-25
- 状態: 計画(2026-10-25 に ADR-0087 を Accepted。M0122 の完了後に始める)
- 決定: [ADR-0087](../Decisions/ADR-0087-Rendering-And-Physics-Layers.md) §2

## 目的
`Runtime.Physics` に同居している L4 の契約と L5 のソルバーを、Entity を使わない契約と、ECS を知らないソルバー・成果物のプロジェクトに分ける。

## 段階
1. **契約と分離**: `src/CSharpGameEngine.Physics`(L4)、`src/CSharpGameEngine.Physics.Deterministic`(L5)、`src/CSharpGameEngine.Physics.Artifacts`(L6)を作る(slnx に登録し、登録の試験を流す)。`PhysicsBodyId`、`IReadOnlyPhysicsWorld`/`IPhysicsWorld`、`SceneComponentOrder`/`Icon` の属性を Contracts へ。ECS への接続は共有プロジェクト `Runtime.Physics`(L5 アダプター)に残す。
2. **利用者の移行と試験**: `Runtime.Character`・`plugins/CSharpGameEngine.Vehicles`・`Navigation`・FootIk を移し、`Physics.Cli` を ProjectReference にし、物理キャッシュの `SchemaVersion` を 3 に。`EngineLayerSpec` の物理の規則。アダプターの `Runtime.Windows` への依存を許すかを明記する。文書。

## 完了条件
- `EngineLayerSpec` の物理の規則に合格。`Physics.Cli` が ECS なしでソルバー全部をコンパイルする(4 ファイル → 全部)。
- `PhysicsGoldenStateTests` の SHA とテンプレートの実走の値が M0122 の終わりと同じ。F-L4-021 が Implemented。

## 範囲外
`Runtime.Simulation` の層の移動、破壊(M0113)。

## 機能 ID
F-L4-021、F-L5-047、F-L5-003
