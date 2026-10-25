# M0055: 決定的Physics Runtime基盤

- Status: PartiallyImplemented
- Date: 2026-08-24

## Goal

共有Simulation tick、System-major phase、Q32.32剛体、Physics専用broadphase、Cooking/Cache/Prefracture契約、Runtime/Editor Preview共通debugを導入する。

## Implemented

- `Runtime.Simulation` / `Runtime.Physics`共有プロジェクトとLogical128/192/256同一Compilation。
- 永続GeneratedSystemSet、SystemPhase、固定tick順序。
- Collider/RigidBody/Velocity/Commands/Query、Q32.32、決定的AABB tree、基本落下・AABB接触・Sleep。
- Cooking key、versioned physics cache、stable bond ordering、CLI entry point。
- demand-driven Physics Named Pipe、Scene ViewのAABB/Velocity/Sleep/COM表示、Simulate/Pause/Step/Reset。

## Deliberately incomplete

0..N fixed catch-upのshipping loop統合、GJK/EPA、Static Mesh BVH、完全なSequential Impulse、CCD、実Voronoi/破片Playback、Build差分cook、Scene Bake Host、cache playback/handoff、contact/tree/bond overlay。Bake UIは誤った空cacheを完成品として生成しないよう無効で表示する。

後続: 実Voronoiの調理、破片の剛体化とキャッシュの再生を含む実行時の破壊は[M0113](M0113-Chaos-Destruction.md)で実装した(ここの`Destructible`と`PrefractureRuntime`は使われないまま置き換えられた)。

## Feature IDs

F-L3-018、F-L5-002、F-L5-003、F-L6-002、F-L7-012、F-L8-014。

## Verification

Debug全Solutionは実行中Editor Preview DLL lockを避ける独立artifact出力で0警告0エラー、Release全Solutionも0警告0エラー。Debug/Release各1,527件の全テストが成功。UserEcsProjectを10秒間起動し、正常なframe loopを確認後に終了した。

## Conformance

共有tick・数値・API・debug基盤は意図と一致。v1 solver/bake/destruction全体はPartial。
