# Roadmap

この文書は計画順序を示す。現在の実装事実は`Implementation-Status.md`、機械実行可能なStage正本は`FeatureStageCatalog.cs`を参照する。

## Priority 0 — 自律検証基盤

- F-L7-021 Feature Runner: Implemented、Debug contract verified。
- F-L9-008 Stage Packs: Implemented、Debug/Release runtime verified。10個の独立process/projectとC# `IScene` hostを維持する。
- WARPをVisual Stageの必須adapter、Hardwareを任意の追加passにする。
- changed約15分、full約2時間、stress約8時間を目標とする。

## Priority 1 — v1 Physics完成

- F-L3-018: shipping loopへ0..N fixed tick schedulerを接続。
- F-L5-003: GJK/EPA、Static Mesh BVH、4点manifold、8/3 warm start、ContinuousStatic CCD。
- F-L6-002: actual convex/mesh/Voronoi cooking、cache playback/handoff、incremental cook。
- F-L7-020: real Scene Bake Host。
- F-L8-014: contact/tree/bond overlay、Bake Selected/All。

## Priority 2 — Rigid拡張

F-L5-004 Joints → 005 Ragdoll/006 Character/007 Vehicle → 008 Dynamic CCD → 009 Parallel → 010 GPU → 011 Rollback。

## Priority 3 — Deformable / Fluid / Destruction

F-L5-012 XPBDを粒子基盤にし、Cloth/Rope/Soft Bodyへ進む。FEM/MPMはsparse/GPU adapterとartifact完成後。FluidはSPH/PBF → FLIP/APIC → Eulerian、Destructionはprefracture完成後にruntime topology → voxel/SDFの順とする。

## Priority 4 — Celestial / ProbabilityQuantum / Coupling

KeplerとDirect N-bodyのreference Oracleを先に確立し、Barnes–Hut、FMM、hierarchical framesへ進む。Probabilityはゲーム用途と科学用途を明確に分離し、再現性・誤差・provenanceをartifactに持つ。Domain couplingは各単独Domainが検証済みになってから翌tick bridgeで実装する。

## Deferred foundation

- F-L3-020 Extended ECS mask: 現行64型上限を維持。必要になった時点で65–128二重maskを追加し、1–64 pathを不変にする。
- F-L0-008 D3D12 compute/readback。
- F-L3-019 deterministic job runtime。
- F-L4-003 engine-owned simulation contracts and optional L4 adapters。
- F-L6-003 field/graph artifacts。
- F-L5-032/033/034 pose bridge、volume renderer、thermal field。
