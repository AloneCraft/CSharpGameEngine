# ADR-0049 — 決定的Physics DomainとBake境界

- Status: Accepted
- Date: 2026-08-24

## Context

局所剛体、破壊、流体、天体、量子は数値表現・正本・適切なsolverが異なる。一方、Runtime/Editor Preview/Bakeでtick順序、命令、イベント、Artifact識別を共有しなければ再現性を維持できない。

## Decision

`Runtime.Simulation`が整数tick、Domain、Command/Event envelope、Cache identity、翌tick Bridgeを所有する。`Runtime.Physics`は選択中Logical profileと同じユーザーCompilationへ共有ソースとして入り、局所Q32.32剛体Domainを所有する。Global Loose OctreeとPhysics Dynamic AABB Treeは統合しない。異種solverは共通Domain契約へ接続するが、単一solverへ統合しない。

Runtime、Editor Preview、Bake Hostは同じ生成System setと同じsolverを使う。永続Artifactにはprofile、tick rate、schema、依存hash、stable IDを必須とし、不一致時はhard errorとする。

## Alternatives considered

- 全SimulationをLogical座標の単一solverへ統合: 局所接触性能と各モデルの正本が両立しないため不採用。
- Global Loose Octreeをbroadphaseへ兼用: 更新規則と決定順が異なるため不採用。
- Cache不一致時の自動Realtime fallback: 再現性を隠すため不採用。

## Consequences

共有tickとArtifactは再利用できるが、Domainごとのsolver/cooker/debug adapterが必要になる。v1はx64、同一schema/profile/asset hash/command stream内のCPU逐次決定性に限定する。

## Verification

Q32.32、Logical変換、固定tick、stable順command/event、broadphase Oracle、cache identityを自動試験する。完成していないsolver/cooker/editor機能はPartiallyImplementedとして追跡する。

## Related features

- F-L3-018
- F-L5-002
- F-L5-003
- F-L6-002
- F-L7-012
- F-L8-014
