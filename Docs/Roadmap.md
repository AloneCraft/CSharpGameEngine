# Roadmap

## Current asset editor windows update
M0127/ADR-0089 adds F-L8-036: asset editors open as floating, dockable Visual Studio documents (Static Mesh, Texture, Sound Wave, Font, Data Asset, plus the shader graph) and the asset preview gets Preview Level templates. Skinned glTF models get a skeleton tree and animation timeline, with the host loading them through the same sources as the game (ADR-0090). See [Asset Editors](Specifications/Asset-Editors.md) and [M0127](Milestones/M0127-Asset-Editor-Windows.md).

## Current logarithmic-native rendering, render-path and full-scale physics plan
ADR-0084〜ADR-0087(2026-10-25)が、対数深度を最初から対数で書く Stage シェーダー 1 系統にする(標準深度の削除)、URP の 4 描画パスを移植する、物理を Q64.64 のまま全スケールにする、描画と物理を適切な層へ置く、を決めた。計画は M0119〜M0123 で、新しい機能は F-L0-011(D3D12 の描画計画の実行器)、F-L4-022(URP の 4 描画パス)、F-L4-021(Entity を使わない物理の契約)、F-L5-047(全スケールの決定的な剛体)。F-L5-038 は「Logarithmic depth of every perspective view」へ改名する。順序は M0119(対数ネイティブ、`stage-v14`)→ M0120(描画の L4 化と Forward/Forward+、`stage-v15`)→ M0121(Deferred/Deferred+、`stage-v16`)、物理は進行中の M0112 の後に M0122(全スケール)→ M0123(契約とソルバーの分離)。M0113(破壊)は M0123 の後。詳細は [ADR-0084](Decisions/ADR-0084-Logarithmic-Native-Depth.md)、[ADR-0085](Decisions/ADR-0085-Urp-Render-Paths.md)、[ADR-0086](Decisions/ADR-0086-Full-Scale-Physics.md)、[ADR-0087](Decisions/ADR-0087-Rendering-And-Physics-Layers.md)。

## Current typed-light contract update
M0088/ADR-0064 refines existing F-L5-001, F-L8-024, F-L8-025, F-L4-002, F-L0-005 and F-L0-009 without adding a new feature identity. See [Typed Lighting](Specifications/Typed-Lighting.md) and [verification milestone](Milestones/M0088-Typed-Lighting.md).

## Current environment-lighting update
M0089/ADR-0069 adds F-L4-014 (physical sky, sky light, exposure, cascaded sun shadows, dynamic GI and ambient occlusion) and refines F-L5-001 and F-L0-005. See [Rendering](Specifications/Rendering.md#environment-lighting) and the [verification milestone](Milestones/M0089-Physically-Based-Environment-Lighting.md).

## Current character-movement update
M0090/ADR-0067 implements F-L5-006. It adds kinematic character movement on the deterministic physics world, StaticMesh triangle collision, a spring-arm follow camera, and Enhanced Input value composition with XInput. It refines F-L4-010 and F-L0-010. The ThirdPerson template is playable. See the [verification milestone](Milestones/M0090-ThirdPerson-Character-Movement.md).

## Current scene JSON and editor world update
M0093/ADR-0070 refines F-L7-012, F-L7-013, F-L8-011 and F-L8-025. Scenes are JSON documents under each project's `Scenes` folder. They are checked against a per-project schema, turned into C# at build time, and edited in memory by the Scene Designer until Save. Every repository scene is converted, and a C# scene class is a build error (SCENE206). For a built game the Scene view is drawn by the game runtime, and value edits show without a build; a structural edit asks for one. M0094/ADR-0071 opens a scene JSON by double-click in the Scene Editor (Design / JSON tabs over one text buffer) and removes the scene context menus. Manual Visual Studio checks remain. See the [verification milestone](Milestones/M0093-Scene-Json-And-Editor-World.md).

## Current SDK package and project template update
M0092/ADR-0065/ADR-0068 add F-L6-009 and F-L8-032. Games build from the `CSharpGameEngine.Sdk` NuGet package anywhere; the VSIX finds the game project from the open solution and ships its bridges. Visual Studio's New Project dialog offers "Third Person / 三人称", which creates the ported Unreal template. See the [verification milestone](Milestones/M0092-Sdk-Package-And-Project-Template.md).

## Current skeletal-animation update
M0091/ADR-0066 adds F-L4-015. It adds glTF skins and clips, GPU compute skinning, and Unreal's ABP_Unarmed locomotion graph. The ThirdPerson template now shows Quinn idling, walking, jogging, jumping and landing. See [Rendering](Specifications/Rendering.md#skeletal-meshes-and-animation) and the [verification milestone](Milestones/M0091-Skeletal-Animation.md).

## Non-physics Feature index (authoritative)

This index is the authoritative machine-tracked mapping for the six non-physics tracks. The older narrative tables below remain planning context. Every row has one Feature and one target Stage; `Specified`/`Deferred` target Stages are deliberately `NotImplemented` until their implementation lands.

M0063 adds isolated `CgeAutomation` Experimental evidence, stable Shader Graph AutomationIds, and false-pass-safe ledger/Feature Runner contracts. F-L8-020 and F-L8-021 are Implemented/TestVerified, and Debug/Release 1041 evidence closes F-L8-031 as Matches.

M0064 makes `UserEcsProject` a visible engine Showcase. Portable input events, the Win32 event adapter, deterministic Input Actions, the live feature manifest, and the WARP-verified dashboard are Implemented/TestVerified. F-L9-009 records the cross-track visible-progress vertical slice without claiming unfinished Physics features.

Current baseline corrections: point/spot/area/emissive shadows and HDR/ACES/bloom are observed renderer baseline; Scene Designer transform tools now have executable WARP/rebase/source/Undo visual conformance under F-L8-023 while human Experimental evidence remains separately Pending; existing ECS phase/structural behavior is a base for the unified target; C# `IScene` remains the only authoring truth. F-L3-020 is `Implemented`: 1–64 uses one generation-time `ulong`, 65–128 uses two, and neither path leaves a runtime mask penalty.

### Rendering

| Level | Feature ID | Status | Summary | Depends on | Specification | Stage |
|---:|---|---|---|---|---|---|
| 1 | F-L7-022 | Implemented | Rendering baseline conformance | None | [Rendering](Specifications/Rendering.md) | rendering.baseline |
| 1 | F-L2-005 | Implemented | Shader Graph canonical schema | F-L7-022 | [Shader Graph](Specifications/Shader-Graph.md) | shadergraph.schema |
| 2 | F-L7-049 | Implemented | Shader Graph compiler | F-L2-005 | [Shader Graph](Specifications/Shader-Graph.md) | shadergraph.compiler |
| 2 | F-L4-004 | Implemented | Engine-owned Render Graph | F-L7-022 | [Rendering](Specifications/Rendering.md) | `rendering.render-graph` |
| 2 | F-L0-008 | Implemented | D3D12 compute/readback | F-L7-022 | [Rendering](Specifications/Rendering.md) | `foundation.d3d12-compute` |
| 3 | F-L6-004 | Implemented | Shader artifacts and PSO provenance | F-L7-022, F-L7-049 | [Shader Graph](Specifications/Shader-Graph.md) | rendering.shader-artifacts |
| 3 | F-L4-005 | Implemented | Materials and textures | F-L6-004 | [Rendering](Specifications/Rendering.md) | `rendering.materials` |
| 4 | F-L4-006 | Implemented | Visibility, LOD, instancing | F-L4-004, F-L4-005 | [Rendering](Specifications/Rendering.md) | `rendering.visibility` |
| 4 | F-L4-007 | Implemented | Quality profiles | F-L4-006 | [Rendering](Specifications/Rendering.md) | `rendering.quality-profiles` |
| 5 | F-L4-014 | Implemented | Physically based environment lighting | F-L4-007, F-L0-008 | [Rendering](Specifications/Rendering.md#environment-lighting) | `rendering.environment-lighting` |
| 5 | F-L4-015 | Implemented | Skeletal meshes and animation | F-L4-007, F-L0-008 | [Rendering](Specifications/Rendering.md#skeletal-meshes-and-animation) | `rendering.skeletal-animation` |
| 5 | F-L0-009 | Implemented | Device recovery | F-L4-004, F-L4-007 | [Rendering](Specifications/Rendering.md) | `rendering.device-recovery` |
| 5 | F-L8-018 | Implemented | Shared isolated preview | F-L0-009, F-L8-022 | [Rendering](Specifications/Rendering.md) | `vsix.rendering-preview` |
| 6 | F-L7-023 | Implemented | Performance evidence | F-L4-007, F-L0-009 | [Rendering](Specifications/Rendering.md) | `rendering.performance` |
| 6 | F-L5-038 | Implemented | Radial (logarithmic) depth camera | F-L4-014, F-L0-009 | [Omni](Specifications/Omni.md) | `rendering.radial-depth` |
| 7 | F-L7-024 | Implemented | Release evidence | F-L7-023, F-L8-018 | [Rendering](Specifications/Rendering.md) | `rendering.release` |
| 7 | F-L5-039 | Implemented | Radial observer view | F-L5-038 | [Omni](Specifications/Omni.md#observer-view) | `rendering.radial-depth` |

### Asset / Content

| Level | Feature ID | Status | Summary | Depends on | Specification | Stage |
|---:|---|---|---|---|---|---|
| 1 | F-L2-004 | Implemented | Asset identity and canonical metadata | None | [Asset / Content](Specifications/Asset-Content-Pipeline.md) | `content.identity` |
| 2 | F-L7-025 | Implemented | Importer conformance | F-L2-004 | [Asset / Content](Specifications/Asset-Content-Pipeline.md) | `content.importer-conformance` |
| 3 | F-L6-005 | Implemented | Content artifact envelope | F-L7-025 | [Asset / Content](Specifications/Asset-Content-Pipeline.md) | `content.artifact-schema` |
| 4 | F-L7-026 | Implemented | Incremental compiler | F-L6-005 | [Asset / Content](Specifications/Asset-Content-Pipeline.md) | `content.incremental-cook` |
| 4 | F-L7-027 | Implemented | Cache recovery and GC | F-L7-026 | [Asset / Content](Specifications/Asset-Content-Pipeline.md) | `content.cache-recovery` |
| 5 | F-L4-008 | Implemented | Runtime asset handles | F-L7-027 | [Asset / Content](Specifications/Asset-Content-Pipeline.md) | `content.runtime-load` |
| 5 | F-L6-006 | Implemented | Shipping content manifest | F-L4-008 | [Asset / Content](Specifications/Asset-Content-Pipeline.md) | `content.package` |
| 6 | F-L6-009 | Implemented | Engine SDK package and game project context | F-L6-006 | [ADR-0065](Decisions/ADR-0065-Engine-Sdk-Package-And-Project-Context.md) | `vsix.project-template` |
| 6 | F-L8-019 | Implemented | VSIX asset authoring | F-L6-006, F-L8-022 | [Asset / Content](Specifications/Asset-Content-Pipeline.md) | `vsix.asset-authoring` |
| 6 | F-L7-028 | Implemented | Performance evidence | F-L6-006 | [Asset / Content](Specifications/Asset-Content-Pipeline.md) | `content.performance` |
| 7 | F-L7-029 | Implemented | Release evidence | F-L7-028, F-L8-019 | [Asset / Content](Specifications/Asset-Content-Pipeline.md) | `content.release` |

### Editor / VSIX

| Level | Feature ID | Status | Summary | Depends on | Specification | Stage |
|---:|---|---|---|---|---|---|
| 3 | F-L8-031 | Implemented | Visual Shader Graph editor | F-L7-049, F-L8-021 | [Shader Graph](Specifications/Shader-Graph.md) | vsix.shader-graph |
| 1 | F-L8-020 | Implemented | Manual/Experimental baseline | None | [Editor / VSIX](Specifications/Editor-VSIX.md) | `vsix.experimental-smoke` |
| 2 | F-L8-021 | Implemented | Experimental automation | F-L8-020 | [Editor / VSIX](Specifications/Editor-VSIX.md) | `vsix.experimental-automation` |
| 3 | F-L8-022 | Implemented | Versioned authoring host | F-L8-021 | [Editor / VSIX](Specifications/Editor-VSIX.md) | `vsix.host-lifecycle` |
| 4 | F-L8-023 | Implemented | Transform visual conformance | F-L8-022 | [Editor / VSIX](Specifications/Editor-VSIX.md) | `vsix.transform-tools` |
| 4 | F-L8-024 | Implemented | Typed inspector and palette | F-L8-022 | [Editor / VSIX](Specifications/Editor-VSIX.md) | `vsix.inspector-contracts` |
| 5 | F-L8-025 | Implemented | Scene document synchronization | F-L8-023, F-L8-024 | [Editor / VSIX](Specifications/Editor-VSIX.md) | `vsix.source-sync` |
| 5 | F-L8-026 | Implemented | Bake, asset, diagnostic UI | F-L8-025 | [Editor / VSIX](Specifications/Editor-VSIX.md) | `vsix.bake-integration` |
| 6 | F-L8-028 | Implemented | Compatibility matrix | F-L8-026 | [Editor / VSIX](Specifications/Editor-VSIX.md) | `vsix.compatibility` |
| 7 | F-L8-029 | Implementing | Release evidence | F-L8-028 | [Editor / VSIX](Specifications/Editor-VSIX.md) | `vsix.release` |
| 6 | F-L8-035 | PartiallyImplemented | Solution Explorer linked asset workspace | F-L8-019, F-L8-024, F-L8-025 | [Asset Workspace](Specifications/Asset-Workspace.md) | `vsix.asset-workspace` |
| 7 | F-L8-032 | Implemented | ThirdPerson Visual Studio project template | F-L6-009, F-L8-031 | [ADR-0068](Decisions/ADR-0068-ThirdPerson-Project-Template.md) | `vsix.project-template` |
| 7 | F-L8-036 | PartiallyImplemented | Asset editor windows and preview levels | F-L8-035 | [Asset Editors](Specifications/Asset-Editors.md) | `vsix.asset-editors` |

### ECS / Runtime

| Level | Feature ID | Status | Summary | Depends on | Specification | Stage |
|---:|---|---|---|---|---|---|
| 1 | F-L7-030 | Implemented | ECS baseline conformance | None | [ECS / Runtime](Specifications/Ecs-Runtime.md) | `ecs.baseline` |
| 2 | F-L3-018 | Implemented | Fixed tick scheduler | F-L7-030 | [ECS / Runtime](Specifications/Ecs-Runtime.md) | `ecs.fixed-tick` |
| 2 | F-L5-035 | Implemented | Portable shared Transform | F-L7-030 | [ECS / Runtime](Specifications/Ecs-Runtime.md) | `ecs.runtime-boundary` |
| 3 | F-L3-021 | Implemented | Unified structural command buffer | F-L3-018, F-L5-035 | [ECS / Runtime](Specifications/Ecs-Runtime.md) | `ecs.structural-changes` |
| 4 | F-L3-022 | Implemented | Enable masks and change versions | F-L3-021 | [ECS / Runtime](Specifications/Ecs-Runtime.md) | `ecs.change-tracking` |
| 4 | F-L3-019 | Implemented | Deterministic job runtime | F-L3-022 | [ECS / Runtime](Specifications/Ecs-Runtime.md) | `foundation.deterministic-jobs` |
| 5 | F-L7-031 | Implemented | Generated schedule diagnostics | F-L3-019 | [ECS / Runtime](Specifications/Ecs-Runtime.md) | `ecs.schedule-diagnostics` |
| 5 | F-L6-007 | Implemented | Schema-fingerprinted snapshots | F-L3-022, F-L3-019 | [ECS / Runtime](Specifications/Ecs-Runtime.md) | `ecs.snapshot` |
| 6 | F-L7-032 | Implemented | Performance evidence | F-L7-031, F-L6-007 | [ECS / Runtime](Specifications/Ecs-Runtime.md) | `ecs.performance` |
| 7 | F-L7-033 | Implemented | Release evidence | F-L7-032 | [ECS / Runtime](Specifications/Ecs-Runtime.md) | `ecs.release` |

### Audio / Input / Platform

| Level | Feature ID | Status | Summary | Depends on | Specification | Stage |
|---:|---|---|---|---|---|---|
| 1 | F-L1-008 | Implemented | Platform event/timestamp contracts | None | [Audio / Input / Platform](Specifications/Audio-Input-Platform.md) | `platform.boundaries` |
| 2 | F-L0-010 | Implemented | Win32 window and input binding | F-L1-008 | [Audio / Input / Platform](Specifications/Audio-Input-Platform.md) | `platform.window-input` |
| 2 | F-L4-009 | Implemented | Audio device lifecycle | F-L1-008 | [Audio / Input / Platform](Specifications/Audio-Input-Platform.md) | `audio.lifecycle` |
| 3 | F-L4-010 | Implemented | Deterministic input actions | F-L0-010 | [Audio / Input / Platform](Specifications/Audio-Input-Platform.md) | `input.actions` |
| 3 | F-L4-011 | Implemented | Mixer and offline oracle | F-L4-009 | [Audio / Input / Platform](Specifications/Audio-Input-Platform.md) | `audio.mixer` |
| 4 | F-L4-012 | Implemented | Spatial audio and recovery | F-L4-010, F-L4-011 | [Audio / Input / Platform](Specifications/Audio-Input-Platform.md) | `audio.spatial` |
| 6 | F-L7-034 | Implemented | Compatibility evidence | F-L4-012 | [Audio / Input / Platform](Specifications/Audio-Input-Platform.md) | `platform.compatibility` |
| 7 | F-L7-035 | Implemented | Release evidence | F-L7-034 | [Audio / Input / Platform](Specifications/Audio-Input-Platform.md) | `platform.release` |

### Testing / CI / Release

| Level | Feature ID | Status | Summary | Depends on | Specification | Stage |
|---:|---|---|---|---|---|---|
| 4 | F-L9-009 | Implemented | Engine Showcase and live feature progress | F-L7-030, F-L3-018, F-L7-022, F-L6-004, F-L4-010, F-L5-003 | [M0064](Milestones/M0064-Visible-Engine-Showcase.md) | `showcase.visible-progress` |
| 1 | F-L7-036 | Implemented | Coverage and catalog validation | F-L7-021 | [Testing / CI / Release](Specifications/Testing-CI-Release.md) | `verification.coverage` |
| 1 | F-L7-037 | Implemented | Manual evidence ledger | F-L7-036 | [Testing / CI / Release](Specifications/Testing-CI-Release.md) | `verification.manual-ledger` |
| 2 | F-L7-038 | Implemented | Changed-path stage selection | F-L7-036 | [Testing / CI / Release](Specifications/Testing-CI-Release.md) | `verification.changed-selection` |
| 2 | F-L7-039 | Implemented | Hermetic clean build | F-L7-038 | [Testing / CI / Release](Specifications/Testing-CI-Release.md) | `verification.clean-build` |
| 3 | F-L7-040 | Implemented | Golden artifact policy | F-L7-039 | [Testing / CI / Release](Specifications/Testing-CI-Release.md) | `verification.golden-policy` |
| 4 | F-L7-041 | Implemented | Performance framework | F-L7-040 | [Testing / CI / Release](Specifications/Testing-CI-Release.md) | `verification.performance` |
| 4 | F-L7-042 | Implemented | Explicit CI matrix | F-L7-041 | [Testing / CI / Release](Specifications/Testing-CI-Release.md) | `verification.ci-matrix` |
| 5 | F-L7-043 | Implemented | Retry/quarantine policy | F-L7-042 | [Testing / CI / Release](Specifications/Testing-CI-Release.md) | `verification.failure-policy` |
| 5 | F-L7-044 | Implemented | Stress contract | F-L7-043 | [Testing / CI / Release](Specifications/Testing-CI-Release.md) | `verification.stress-contract` |
| 6 | F-L7-045 | Implemented | Package and SBOM validation | F-L7-044 | [Testing / CI / Release](Specifications/Testing-CI-Release.md) | `release.packages` |
| 6 | F-L7-046 | Implemented | Compatibility matrix | F-L7-045 | [Testing / CI / Release](Specifications/Testing-CI-Release.md) | `release.compatibility` |
| 7 | F-L7-047 | Implemented | Release candidate gate | F-L7-046 | [Testing / CI / Release](Specifications/Testing-CI-Release.md) | `release.candidate` |
| 7 | F-L7-048 | Implemented | Immutable release manifest | F-L7-047 | [Testing / CI / Release](Specifications/Testing-CI-Release.md) | `release.manifest` |

## Non-physics integration order

Implement cross-track work by dependency Level 1 through 7. At each Level, first complete the listed contract Stage and then replace it with an executable Stage in the same Feature change. Do not retain old APIs, readers, generated artifacts, or renderer/host paths as compatibility bridges.

この文書は物理・非物理を含む全体の計画順序を示す。現在の実装事実は`Implementation-Status.md`、機械実行可能なStage正本は`FeatureStageCatalog.cs`を参照する。ここに記載した未着手Stage IDは候補名であり、実装着手時にFeature IDを採番してカタログへ登録するまでは機械実行可能なStageではない。

## Priority 0 — 自律検証基盤

- F-L7-021 Feature Runner: Implemented、Debug/Release contract verified。
- F-L9-008 Stage Packs: Implemented、Debug/Release runtime verified。10個の独立process/projectとC# `IScene` hostを維持する。
- WARPをVisual Stageの必須adapter、Hardwareを任意の追加passにする。
- changed約15分、full約2時間、stress約8時間を目標とする。

## Priority 1 — v1 Physics完成

- F-L3-018: 60 Hz・最大8 tick・backlog保持のshipping loopを実装済み。
- F-L5-003: Implemented。GJK/EPA、Static Mesh BVH、4点manifold、8/3 warm start、angular integration、ContinuousStatic CCD、Release定常0 allocationまで検証済み。
- F-L6-002: Implemented。convex/static-mesh cooking、immutable incremental reuse、cache playback/handoff、offline Voronoi prefracture mesh/collision/bond playback artifactまでDebug/Releaseと実GLBで検証済み。
- F-L7-020: Implemented。compile-time Scene dispatch、headless fixed tick、bounded streaming cache、atomic CLI publicationをDebug/Releaseで検証済み。
- F-L8-014: contact/tree/bond overlay、Bake Selected/All。

## Priority 2 — Rigid拡張

F-L5-004 Joints → 005 Ragdoll/006 Character/007 Vehicle → 008 Dynamic CCD → 009 Parallel → 010 GPU → 011 Rollback。

- F-L5-006: Implemented（M0090/ADR-0067）。walking/falling、壁滑り、坂・段差、SpringArm、ThirdPersonテンプレートのWARP実走まで検証済み。rigid bodyとの押し合いは未対応。

## Priority 3 — Deformable / Fluid / Destruction

F-L5-012 XPBDを粒子基盤にし、Cloth/Rope/Soft Bodyへ進む。FEM/MPMはsparse/GPU adapterとartifact完成後。FluidはSPH/PBF → FLIP/APIC → Eulerian、Destructionはprefracture完成後にruntime topology → voxel/SDFの順とする。

## Priority 4 — Celestial / ProbabilityQuantum / Coupling

KeplerとDirect N-bodyのreference Oracleを先に確立し、Barnes–Hut、FMM、hierarchical framesへ進む。Probabilityはゲーム用途と科学用途を明確に分離し、再現性・誤差・provenanceをartifactに持つ。Domain couplingは各単独Domainが検証済みになってから翌tick bridgeで実装する。

## Deferred and specified foundation

- F-L3-020 Extended ECS mask: `Implemented`。1–64 single-mask pathを不変に保ち、65–128だけをdual-maskへ分岐する。上限はADR-0079で512に広げ、513以上をSYSGEN026で拒否する。
- F-L0-008 D3D12 compute/readbackは`Implemented`。offline fixed-ABI Compute artifactをWARPでdispatchし、CPU vectorとの一致とdebug-layer error 0を検証する。
- F-L3-019 deterministic job runtime (`Implemented`; executable Stage is `foundation.deterministic-jobs`)。
- F-L4-003 engine-owned simulation contracts and optional L4 adapters。
- F-L6-003 field/graph artifacts。
- F-L5-032/033/034 pose bridge、volume renderer、thermal field。

## 非物理ロードマップの進め方

物理ロードマップと次の6トラックを並行して進める。Priority 1〜4はSimulationトラック内の優先順位であり、非物理トラック全体を完了後まで停止する意味ではない。実際の着手順は以下の依存レベルで決める。

> **番号付与ルール**: 以下の番号は依存関係レベルを表す。
> - 小さい番号 = 上流であり、先に着手する。
> - 同じ番号 = 互いの変更範囲を分離できる場合は並列実行できる。
> - 大きい番号 = 上流の契約・実装・Stage合格後に着手する。

| 依存レベル | 横断ゲート |
|---:|---|
| 1 | 現行挙動・性能・手動確認残を固定し、Feature ID、Intended Specification、Stage Oracleを確定する |
| 2 | L0〜L4の所有権・ライフサイクル・抽象境界と、L2/L6のidentity・artifact schemaを実装する |
| 3 | 各トラックの中核ランタイムを実装し、headlessまたはWARPのreference経路を通す |
| 4 | 高度機能、並列化、device recovery、差分更新を中核ランタイムの上に実装する |
| 5 | L8 Editor/VSIXを、実装済みのCLI・Runtime・artifactに接続する |
| 6 | 性能、互換性、packaging、clean checkout再現性をRelease候補として検証する |
| 7 | full・stress・vsixと任意Hardware passを完走し、Release判定とrollback情報を保存する |

### 全トラック共通の実装規則

- 未採番項目は、実装を始める変更でL0〜L9の所有Layerに安定Feature IDを採番し、`Implementation-Status.md`へ`Planned`または`Specified`として追加する。Roadmap項目名をFeature IDの代用にしない。
- Featureを実装する前に、候補Stage ID、対象Feature ID、入力path、configuration、timeout、Oracleを`FeatureStageCatalog.cs`へ`NotImplemented`として登録する。実装と検証が揃った変更でのみ`Executable`へ変更する。
- Runtime L0〜L6はRoslyn/Visual Studio SDKへ依存しない。L8はCLI・Compiler・Runtimeのfrontendに限定し、VSIX内へengine機能を複製しない。
- `.cs`とオリジナルassetをSource、L6 cache/artifactを再生成可能な出力とする。外部libraryの型をComponent、公開API、Command/Event、artifact schemaへ漏らさない。
- OS/native/driver bindingはWin32/D3D12と同じL0に置く。Runtimeで使う任意のgeometry、codec、shader、compute等のproviderはL4 adapterの背後に置き、source import SDKはL7 import境界内に閉じ込める。上位からproviderを直接参照せず、providerを置換してもengine-owned CPU referenceと同じOracleを使う。
- Visual StageはWARPを必須とし、Hardwareは追加passとする。Hardware成功でWARP失敗を相殺しない。
- ECS Component型は512を上限とする(ADR-0079で128から拡張)。1〜64のsingle-mask生成pathを維持し、65〜128はdual-mask、それ以上は64型ごとに1語を足す。
- C#変更は型推論可能なローカル宣言で`var`を使う。project/shared projectを変更した場合は同じ変更で`CSharpGameEngine.slnx`を同期する。
- 各項目の完了には、実装、単体/結合Stage、Debug/Release、最終コードからのreverse engineering、Conformance判定、Feature ledger・関連仕様・Milestone更新を含む。

### 現在利用できる非物理Stage基盤

| トラック | 実装済みFeatureの基準Stage | 現在の役割 |
|---|---|---|
| Rendering | `rendering.warp-triangle` | D3D12/WARP readback、debug layer、Camera/Input/Render Server契約 |
| Asset/Content | `geometry.import` | procedural mesh、glTF/FBX座標変換、Unreal import fixture |
| Editor/VSIX | `vsix.toolchain`、`vsix.scene-editor` | command/bridge/package、Scene編集・同期・localization契約 |
| ECS/Runtime | `foundation.codegen`、`ecs.scene`、`spatial.runtime` | Generator、静的Archetype、Scene lifecycle、Octree/debug projection |
| Audio/Input/Platform | `audio.windows`、`rendering.warp-triangle` | Audio/XAudio2 headless契約、Win32 inputを含むWindows垂直スライス |
| Testing/CI/Release | `runner.contract`、`stage-packs.contract` | catalog/result schemaと10個の独立Pack実行 |

## Epic: Rendering Roadmap（依存レベル1）

### 目的

D3D12の現在の垂直スライスを、resource lifetime、Render Graph、artifact、visibility、品質profile、device recoveryまで一貫したRender Serverへ拡張する。描画結果だけでなく、barrier、lifetime、diagnostic、性能予算を検証可能にする。

### FeatureとTask

| Level | Feature / Task | 依存 | 成果物 | Done条件 | 候補Stage / Oracle |
|---:|---|---|---|---|---|
| 1 | Rendering仕様とbaselineを固定する | なし | Rendering specification、WARP golden、GPU/CPU時間・descriptor/resource数baseline | F-L0-001/002/007とF-L4-002の現在値、許容差、比較環境が記録される | `rendering.baseline`: 現行golden、debug error 0、resource leak 0 |
| 2 | Render Graphとresource lifetimeを実装する | Rendering baseline | engine-owned pass/resource/queue contract、barrier計画、transient allocation、fence ownership | pass依存をtopological sortでき、不正cycle/use-after-free/barrier不足を診断する | `rendering.render-graph`: reference graphの順序・barrier・lifetime hash一致 |
| 2 | F-L0-008 D3D12 compute/readbackを実装する | Rendering baseline | compute pipeline、upload/readback、fence、CPU reference | WARP computeが既知vectorと一致し、debug layer error 0 | 既存`foundation.d3d12-compute` |
| 3 | Shader artifactとPSO cacheを実装する | Render Graph、L6 artifact基盤 | DXC出力、reflection、root-signature/PSO key、dependency hash、diagnostic mapping | 同一入力は同一key、変更対象だけ再compile、破損cacheは拒否して再生成する | `rendering.shader-artifacts`: cold/warm build一致、1依存変更だけinvalidated |
| 3 | Texture/Material runtimeをartifact駆動にする | Render Graph、Asset artifact schema | texture upload、sampler、material schema、fallback、lifetime管理 | importer型を漏らさず、欠損/不正assetが決定的fallbackまたは明示errorになる | `rendering.materials`: reference material gridのWARP image tolerance内一致 |
| 3 | visibility、culling、LOD、instancingを実装する | Render Graph、既存ECS spatial snapshot（F-L3-016） | extraction contract、frustum/occlusion方針、LOD、batch/instance buffer | CPU reference集合と描画集合が一致し、camera移動時にstale objectが残らない | `rendering.visibility`: visible ID/LOD hashとWARP image一致 |
| 4 | Lightingとpost-process品質profileを拡張する | Material、visibility | point/spot/cascade shadow、IBL、exposure、AAをprofile単位で追加 | 各機能を個別無効化でき、既存Directional/HDR/ACES/Bloom互換を維持する | `rendering.quality-profiles`: scene別goldenと輝度/edge誤差予算内 |
| 4 | resize、device lost、adapter変更から復旧する | Render Graph、resource lifetime | swap-chain再生成、resource再作成、失敗状態とdiagnostic | resize/minimize/restoreと意図的device removal後に二重解放なく復旧または明示終了する | `rendering.device-recovery`: lifecycle traceと最終frame一致 |
| 5 | Scene Viewとshipping runtimeを同じRender Server経路へ統合する | Runtime Rendering、Editor host | preview adapter、render-mode/overlay接続 | VSIXにrenderer本体を複製せず、同一Scene/Cameraでruntimeとpreviewが許容差内一致する | `vsix.rendering-preview`: isolated hostとshipping hostのimage/state比較 |
| 6 | Rendering性能gateを固定する | Rendering機能一式 | scene別CPU/GPU/frame memory budget、leak/soak計測 | baseline更新は理由付き承認を必須とし、統計的閾値を越える退行を失敗にする | `rendering.performance`: warm-up後中央値/p95/memoryが予算内 |
| 7 | Rendering Release gateを完走する | 性能gate | Debug/Release WARP artifact、任意Hardware比較、driver情報 | full/stressでWARP必須passが成功し、Hardware差分と既知deviationが記録される | 既存suite `full`+`stress`、Hardware追加pass |

### 主なリスク

- Render Graph導入時に既存`StageRenderer`/`ModelRenderer`を同時に全面置換すると、正しいreferenceを失う。既存経路をgoldenとして段階移行する。
- WARPとHardwareでfloating-point、format、driver挙動が異なる。完全byte一致ではなく、用途別の数値・画像許容差を仕様化する。
- descriptor、upload、readbackの所有権が曖昧だと長時間Stageだけでleakが現れる。作成/使用/破棄をartifactへ記録する。

## Epic: Asset / Content Pipeline Roadmap（依存レベル1）

### 目的

glTF/FBX/Unrealの現在のimport垂直スライスを、stable identity、versioned artifact、依存graph、incremental cook、runtime load、shipping packageまでつなぐ。外部SDKはimport境界内に閉じ込める。

### FeatureとTask

| Level | Feature / Task | 依存 | 成果物 | Done条件 | 候補Stage / Oracle |
|---:|---|---|---|---|---|
| 1 | Asset identityとsource/artifact契約を確定する | なし | Stable ID、source URI、subasset ID、import settings hash、content/dependency hash、schema version | rename/move/reimport/同名subassetのidentity規則とmigration方針が仕様化される | `content.identity`: rename/move/collision/version vector一致 |
| 2 | importer conformance matrixを固定する | Asset identity | glTF/GLB、FBX、Unreal/Fab fixture corpus、座標・単位・material対応表 | external SDK型を境界外へ出さず、unsupported要素をwarning/errorとして分類する | 既存`geometry.import`を拡張しfixture artifact hash一致 |
| 2 | L6 artifact schemaを定義する | Asset identity | mesh、texture、material、shader、scene/spawn planのversioned schema | endianness、alignment、version、dependency、provenance、検証checksumが定義される | `content.artifact-schema`: round-trip、旧version拒否/migration、corruption検出 |
| 3 | dependency graphとincremental compilerを実装する | importer matrix、artifact schema | L7 graph、dirty propagation、parallel-safe task、atomic publish | 1 source変更で必要nodeだけ再buildし、失敗時に前回成功artifactを壊さない | `content.incremental-cook`: cold/warm/one-changeのbuild setとhash一致 |
| 3 | cache lookup、migration、garbage collectionを実装する | artifact schema | local cache index、schema migration、reachability GC、lock/transaction | concurrent build、process中断、破損entryから決定的に回復する | `content.cache-recovery`: kill/restart/corruption後の最終artifact一致 |
| 4 | runtime load/stream/unloadを実装する | incremental artifact | handle API、async load、dependency retain/release、fallback、hot reload境界 | Runtimeはsource SDKを参照せず、unload後にhandle/resourceが残らない | `content.runtime-load`: load/use/unload/reload traceとresource count一致 |
| 4 | shipping content manifestを実装する | incremental artifact | content manifest、chunk/group、dedup、version compatibility | clean outputへ必要artifactだけ入り、欠損/余分/重複を検出する | `content.package`: manifest closure、hash、再build byte identity |
| 5 | Asset Browser、preview、reimport設定をVSIXへ接続する | compiler CLI、runtime load | L8 browser/search/preview/reimport UI、Output/Error List連携 | VSIXを閉じてもCLIで同じ結果を生成でき、UIはprocess cancel/restartを扱える | `vsix.asset-authoring`: model contract、bridge crash/restart、preview WARP |
| 6 | content build性能と大規模projectを検証する | pipeline一式 | file数/容量別budget、parallelism、peak memory、cache hit率 | warm buildが不要なimportを実行せず、budget退行を検出する | `content.performance`: corpus別elapsed/p95/memory/cache-hit予算内 |
| 7 | Content Release gateを完走する | packaging、performance | clean checkoutからのshipping content、provenance、復元手順 | sourceだけからartifactを再生成でき、Release packageとmanifestを照合できる | `full`+`stress`でclean/cold/warm/corrupt-cacheを完走 |

### 主なリスク

- importer version差で同じsourceから別artifactが出る。tool/provider versionをprovenanceとcache keyに含める。
- pathをidentityにするとrenameで参照が切れ、content hashだけにすると同内容の別assetを区別できない。Stable IDとcontent hashの役割を分離する。
- hot reloadとshipping packageを同じ可変storeで扱うと再現性を失う。build中のworking setとpublished artifactを分ける。

## Epic: Editor / VSIX Roadmap（依存レベル1）

### 目的

実装済みScene Designer、Hierarchy、Inspector、Component Palette、Move gizmoを、安定したauthoring frontendへ完成させる。C# `IScene`を唯一の正本とし、すべての重い処理をRuntime/CLIの共通経路へ委譲する。

### FeatureとTask

| Level | Feature / Task | 依存 | 成果物 | Done条件 | 候補Stage / Oracle |
|---:|---|---|---|---|---|
| 1 | 現行VSIXの手動確認残を解消する | なし | F-L8-015/016/017、Scene Designer、UserEcsProjectの確認記録 | ja-JP/en-US、DPI、Esc、Physics sidebar、起動・描画・音響をExperimental Instanceで確認する | `vsix.experimental-smoke`: checklist artifact、screenshot、activity log、crash 0 |
| 2 | Experimental Instance smokeを可能な範囲で自動化する | 現行手動baseline | VS起動、extension load、window open、command routing、activity-log収集host | timeout/起動失敗/extension load失敗をInfrastructureErrorとして保存する | `vsix.experimental-automation`: expected package/window/command state一致 |
| 2 | bridge/preview hostのlifecycleを強化する | 現行toolchain | cancellation、timeout、process restart、protocol version、error mapping | VS終了時にchild processが残らず、crash後の再実行で同じSceneを開ける | `vsix.host-lifecycle`: cancel/crash/version-mismatchの状態遷移一致 |
| 3 | Rotate/Scale、snapping、修飾操作を実装する | Move transaction、camera/picking | gizmo、local/world mode、numeric snap、multi-select方針 | preview中はsourceを書かず、commit 1回がVS native Undo 1回になる | `vsix.transform-tools`: drag math、rebase、undo/redo、WARP overlay一致 |
| 3 | Inspector/Paletteの型付き編集を拡張する（F-L8-024完了） | Scene model、asset identity | enum/vector/reference editor、validation、add/remove/reorder | 宣言member型を正本とし、不正・未宣言・同一入力ではsource操作を作らず、constructor既定値とruntime companion規則を維持する | `vsix.inspector-contracts`: typed editと再解析model一致 |
| 4 | source sync、競合、Undo/Redoを完成させる（F-L8-025完了） | transform/inspector編集 | latest-source token、minimal text change、typed rebase/reject、canonical reanalysis | stale generationへ書かず、連続操作・外部編集・undo/redoで選択と表示が収束し、editor-owned historyを持たない | `vsix.source-sync`: operation sequence後のtext/model/selection identity一致 |
| 4 | Bake、Asset、diagnostic UIをCLIへ接続する（F-L8-026完了） | Asset pipeline、各Bake Host | enabled command、progress/cancel、typed inline diagnostics、partial artifact link | UI独自のbuild処理を持たず、共通host DTOと同じrequest/progress/result/diagnostic identityを表示する | `vsix.bake-integration`: success/failure/cancel/false-success、host診断、部分artifact結果の一致 |
| 5 | Render Graph viewer、profiler、debug visualizerを追加する（F-L8-027 はM0097 で廃止。Scene Designer の診断の代わりに、VS のデバッガーとプロファイラーを拡張機能メニューから呼び出す） | 既存Runtime debug projection（F-L3-017）、Render Graph | 読み取り専用viewer、frame capture summary、entity/component visualizer | Runtime ownershipを変更せず、要求駆動snapshotだけを表示する | `vsix.debug-tools`: protocol DTO、budget、disconnect/reconnect契約 |
| 6 | VS互換性、localization、DPI、accessibilityをRelease品質にする（F-L8-028 matrix/validator完了） | Editor機能一式 | 対応VS version matrix、resource parity、keyboard/focus、high-DPI layout | versioned matrixがnet472、VS 17.14+ amd64、neutral/en-US/ja-JP、CgeFixed/CgeAutomation、CI、詳細/manual evidenceを双方向照合し、Pendingを成功へ変換しない | `vsix.compatibility`: package tests、matrix validator、Experimental ledger sweep |
| 7 | VSIX Release gateを完走する | compatibility | signed/installable VSIX候補、payload manifest、rollback手順 | `vsix` suite、Experimental Instance、install/update/uninstallが成功する | 既存`vsix` suite+`vsix.experimental-smoke` |

### 主なリスク

- WPF previewとWin32 child surfaceのfocus/command routingは自動テストだけでは保証できない。手動確認をゼロに見せず、証跡付きRelease gateとして残す。
- C# sourceと予測modelの二重正本化を避ける。予測は一時projectionであり、Roslyn再解析結果へ必ず収束させる。
- VS SDK versionごとの差をengine Runtimeへ持ち込まない。compatibility adapterはL8内に閉じ込める。

## Epic: ECS / Runtime Roadmap（依存レベル1）

### 目的

現在の閉集合・静的Archetype・SoA生成Runtimeを維持しながら、fixed tick、構造変更、change tracking、deterministic jobs、snapshot、性能診断を完成させる。64型以下の生成pathには追加mask costを入れず、65〜128だけを二語へ分岐する。

### FeatureとTask

| Level | Feature / Task | 依存 | 成果物 | Done条件 | 候補Stage / Oracle |
|---:|---|---|---|---|---|
| 1 | 静的ECSのAPI・生成量・性能baselineを固定する | なし | Archetype/Chunk/Query/System/Scene lifecycle仕様、1/16/32/64型compile/runtime benchmark | 既存generated source、allocation、iteration、compile時間のbaselineが再現できる | `ecs.baseline`: generated hash、state hash、allocation、throughput予算 |
| 2 | F-L3-018へ0..N fixed tick schedulerを接続する | ECS baseline | accumulator、catch-up上限、pause/step、render interpolation境界 | 同じtimestamp/command列でtick数・state/event hashが一致し、spiralを制限する | `ecs.fixed-tick`: 0/1/N tick、pause/step、frame-rate独立hash一致 |
| 2 | platform非依存の共有Transform/runtime契約へ移す | ECS baseline、layer boundary | Windows namespaceから独立したauthoritative Transform/companion contract | Physics/Rendering/Audioが同じ型を使い、L5がWindows固有namespaceへ依存しない | `ecs.runtime-boundary`: reference graphと生成API互換テスト |
| 3 | structural command bufferとentity lifecycleを実装する | fixed tick、runtime contract | create/destroy/add/remove command、stable ordering、世代検証 | iteration中の構造変更をbuffer化し、stale entityと二重destroyを診断する | `ecs.structural-changes`: command permutation別の最終state/event hash一致 |
| 3 | enable mask、change version、query filterを実装する | ECS baseline | chunk/entity enable状態、component/system version、changed filter | disabled entityを構造移動なしで除外し、wraparoundを含むversion比較が正しい | `ecs.change-tracking`: enable/change queryのreference集合一致 |
| 4 | F-L3-019 deterministic job runtimeを実装する | structural/change contract | access declaration、dependency graph、worker scheduler、deterministic merge | single-threadとscheduled実行のstate/event hashが一致し、競合を事前診断する | 既存`foundation.deterministic-jobs` |
| 4 | source generatorの競合・循環・順序診断を拡張する | job runtime | compile-time access graph、diagnostic、generated schedule | diagnostic orderingが決定的で、実行時reflection/dictionary dispatchを増やさない | `ecs.schedule-diagnostics`: invalid corpusのID/location/message一致 |
| 5 | snapshot、serialization、migration、debug projectionを実装する | stable lifecycle、artifact schema | World snapshot、schema/version、restore、inspection DTO | restore後のstate hashが一致し、unknown/incompatible schemaを明示拒否する | `ecs.snapshot`: save/restore/migrate/corrupt artifact Oracle |
| 6 | ECS scale/performance gateを固定する | ECS機能一式 | entity/chunk/archetype/system数別budget、SG compile budget | 1〜64型のsingle-mask生成pathが維持され、65〜128 dual-mask pathを含め既存baselineを理由なく退行させない | `ecs.performance`: generated source構造とmedian/p95/allocation比較 |
| 7 | ECS Release gateを完走する | performance gate | Debug/Release/stress結果、UserEcsProject runtime artifact | clean build、long-run、create/destroy、snapshot、job repeatabilityが成功する | `full`+`stress`、UserEcsProject smoke |

### Boundary

- F-L3-020は128型まで実装済みである。129型以上への拡張は現在の要求外とし、dynamic Archetype導入の根拠にはしない。
- dynamic Archetype、runtime reflection query、外部job runtimeへの全面移行は現行architectureの変更であり、Roadmap項目として暗黙に実装しない。必要時は別ADRを先行する。

### 主なリスク

- job並列化でイベント順やfloating-point集約順が変わる。partition、merge、event orderingをcontractとして固定する。
- structural changeとsnapshotを同時に導入するとlifecycle bugの原因を分離しにくい。command buffer合格後にsnapshotへ進む。
- Source Generatorの高機能化でcompile時間が悪化しやすい。runtime性能だけでなくincremental generator stepの再実行数を測る。

## Epic: Audio / Input / Platform Roadmap（依存レベル1）

### 目的

Win32 inputとXAudio2垂直スライスを、window/time/device lifecycle、action mapping、mixer/streaming/spatial audio、device recoveryまで拡張する。OS固有bindingとengine-owned contractを明確に分離する。

### FeatureとTask

| Level | Feature / Task | 依存 | 成果物 | Done条件 | 候補Stage / Oracle |
|---:|---|---|---|---|---|
| 1 | Platform/Input/Audioのlayer契約を確定する | なし | L0 native binding、L1 value contract、L4 server、L5 gameplay adapterの所有表 | OS handle/provider型がL1以上の公開契約へ漏れず、thread/lifetimeが定義される | `platform.boundaries`: project/reference/public API scanが許可graphと一致 |
| 2 | window、clock、event loop、input lifecycleを実装する | layer契約 | focus、capture、raw mouse/keyboard、DPI、resize、monotonic clock、hotplug event | activate/deactivate/minimize/resizeでstuck inputや負のdeltaが発生しない | `platform.window-input`: synthetic message traceとstate transition一致 |
| 2 | Audio server lifecycleとsilent reference backendを実装する | layer契約 | device/voice ownership、clock、command queue、null/silent backend | audible deviceなしでもcontract testが走り、start/stop/disposeが冪等である | `audio.lifecycle`: command/voice/resource trace一致 |
| 3 | action mapping、rebind、device hotplugを実装する | input lifecycle | action/axis/chord schema、context、dead zone、rebind、gamepad adapter | 同じraw event列から同じaction列を生成し、disconnect時に入力がreleaseされる | `input.actions`: keyboard/mouse/gamepad traceとaction hash一致 |
| 3 | mixer、bus、voiceを実装する | Audio lifecycle | bus graph、gain/pan、voice limit、offline buffer、underrun診断 | offline/silent referenceでsample数、gain、routing、loop境界が一致する | `audio.mixer`: offline PCM hash/peak/RMSとvoice lifecycle一致 |
| 4 | streaming、spatial audio、device recoveryを実装する | mixer、Transform contract、artifact loader | decode/stream buffer、listener/source transform、attenuation、doppler policy、device change/recreate | coordinate conventionがRendering/Physicsと一致し、device再作成でvoice状態を規定通り復元する | `audio.spatial`: offline channel energy/attenuation、stream/recovery trace一致 |
| 5 | Input/Audio diagnosticsとVSIX frontendを追加する（F-L8-030 はM0097 で廃止。Scene Designer の診断の代わりに、VS のデバッガーとプロファイラーを拡張機能メニューから呼び出す） | runtime contracts | device/voice/action inspector、meter、capture、設定UI | UIなしでもCLI/runtime APIが完結し、表示snapshotがruntimeを所有しない | `vsix.platform-debug`: disconnect/reconnect、snapshot budget、localization契約 |
| 6 | Windows構成と実機matrixを検証する | Platform機能一式 | Debug/Release、device有無、DPI、keyboard layout、audio format別結果 | headless必須passが常に成功し、実機依存passは環境情報付きで分離保存される | `platform.compatibility`: synthetic必須pass+Hardware追加pass |
| 7 | Platform Release gateを完走する | compatibility | native dependency/payload manifest、crash log、rollback情報 | clean machine相当で起動・入力・silent audio・実audio追加確認が成功する | `full`+`stress`+実機追加pass |

### 主なリスク

- 実audio出力だけをOracleにするとCIで再現できない。offline/silent referenceを必須にし、聴感確認は追加passにする。
- Win32 message、render frame、fixed tickを同一clockとして扱うと入力の取りこぼしや二重適用が起きる。timestampとconsumption境界を明示する。
- 将来別OSを追加する場合も、OS backendをL4/L5へ置かない。Windowsと同じL0 backend位置に実装し、共通server contractを再利用する。

## Epic: Testing / CI / Release Roadmap（依存レベル1）

### 目的

Feature RunnerとStage Packsを、変更選択、clean build、visual/performance regression、長時間安定性、package、Release判定まで一貫した品質ゲートにする。retryや手動確認で失敗を隠さない。

### FeatureとTask

| Level | Feature / Task | 依存 | 成果物 | Done条件 | 候補Stage / Oracle |
|---:|---|---|---|---|---|
| 1 | Feature/Stage coverageとsuite予算を継続監査する | なし | 全Featureのstatus/stage対応、changed約15分・full約2時間・stress約8時間の計測 | Implemented/PartiallyImplementedに実行可能Stage、DeferredにOracle付き`NotImplemented`がある | 既存`runner.contract`、catalog completeness test |
| 1 | 既存の手動・環境依存検証を台帳化する | なし | VSIX、Hardware、audio、locale/DPIのmanual gate一覧と証跡schema | 自動passとmanual pendingを別statusで追跡し、未確認を成功表示しない | `verification.manual-ledger`: required evidence field completeness |
| 2 | changed selectionとdependency impact mapを強化する | coverage | source/generated/artifact/project依存graph、explain出力 | 変更した公開契約のconsumer Stageを漏らさず、無関係Stageを理由付きで除外する | `verification.changed-selection`: mutation corpusのexpected Stage集合一致 |
| 2 | clean checkout/hermetic buildを実装する | coverage | pinned tool manifest、offline/online restore方針、temp artifact root | repository管理外cacheなしのclean buildで同じ成果物を生成する | `verification.clean-build`: source commitからartifact/package hash一致 |
| 3 | golden/snapshot更新policyを実装する | result schema | image/data golden manifest、tolerance、review metadata、更新command | テスト本体とgoldenの同時無条件更新を拒否し、差分artifactを保存する | `verification.golden-policy`: intentional/unreviewed更新のaccept/reject一致 |
| 3 | performance regression基盤を実装する | baseline Stage | warm-up、sample、median/p95、noise判定、machine fingerprint | absolute budgetとbaseline deltaを分離し、測定不能をpassにしない | `verification.performance`: synthetic regressionを確実に検出 |
| 3 | Debug/Release/WARP CI matrixを固定する | clean build | changed PR、full定期、stress夜間、vsix候補workflow | 同じStage result schemaをlocal/CIで使い、WARP必須visual passを常時実行する | 既存suiteをconfiguration別に実行しresult JSONを収集 |
| 4 | timeout、flaky、retry、quarantine policyを実装する | CI matrix | failure taxonomy、限定retry、owner/expiry付きquarantine | deterministic failureを自動retryで成功化せず、期限切れquarantineを失敗にする | `verification.failure-policy`: injected fail/timeout/crashのstatus一致 |
| 4 | leak、soak、fault-injection Stageを拡張する | stress基盤 | process/resource leak、device loss、cache corruption、cancel/restart scenario | 反復後のhandle/memory/process数が予算内で、回復不能時は明示失敗する | `verification.stress-contract`: injected faultごとのrecovery/status一致 |
| 5 | NuGet、CLI、VSIX、template、native payloadをpackage検証する | 各トラック成果物 | version統一、manifest、dependency closure、SBOM、署名入力 | packageを隔離環境へinstallし、欠損/余分/開発path参照を検出する | `release.packages`: pack/install/run/uninstallとpayload hash一致 |
| 5 | compatibility matrixを実装する | CI、各トラックcompatibility Stage | .NET/Windows/VS/locale/DPI/adapterの対応表と実行結果 | 必須matrixの未実行をRelease blockerとして機械判定できる | `release.compatibility`: required cellがすべてPassedまたは承認済みdeviation |
| 6 | Release Candidate gateを自動集約する | package、compatibility、各性能gate | commit、dirty、tool version、全Stage result、manual evidence、known deviationのmanifest | dirty build、NotImplemented必須Feature、Failed/TimedOut/InfrastructureError、期限切れwaiverを拒否する | `release.candidate`: gate decisionと理由の再計算一致 |
| 7 | Release、rollback、artifact retentionを実装する | RC合格 | immutable release manifest、tag/version入力、retention、rollback手順 | 同じcommitから再検証でき、直前版へ戻すasset/schema互換情報がある | `full`+`stress`+`vsix`結果をrelease manifestへ固定 |

### Suite運用

| Suite | 必須範囲 | 失敗時の扱い |
|---|---|---|
| `changed` | impact mapで選ばれたunit/contract/WARP Stage | 漏れが疑われる場合は`smoke`へ拡大する。未選択を成功とは記録しない |
| `smoke` | Foundation、主要Runtime、WARP、Audio headless、VSIX contract | PRの日常gate。環境不足は`InfrastructureError`として失敗と分離する |
| `full` | 全Executable StageのDebug/Release | Release候補の必須条件。Deferred Stageは除外するが存在をcatalogから消さない |
| `stress` | 反復決定性、性能、leak、fault injection、長時間scenario | 夜間およびRelease候補で実行し、retry前後を両方保存する |
| `vsix` | VSIX model/packageとExperimental Instance候補 | 自動化できない項目はmanual evidenceを要求し、未確認を`RuntimeVerified`にしない |

### 主なリスク

- test数だけを品質指標にすると、Stage Oracleと実環境の欠落を見逃す。Feature coverage、失敗分類、manual pending、性能予算を別々に追跡する。
- golden更新を実装変更と同時に自動承認すると退行を固定する。差分と更新理由をartifact化する。
- Release処理がlocal専用scriptになると再現できない。Feature Runner result schemaとpackage manifestを唯一の入力にする。

## 非物理トラックの統合順序

以下を1つの巨大変更ではなく、依存レベルごとの完結した変更として実装する。同一レベルの作業は、project/文書/Feature IDの所有範囲が重ならない場合だけ並列化する。

- [ ] 1. 6トラックの現行baseline、手動確認残、Feature ID採番候補、Intended Specification、Stage Oracleを確定する。
- [ ] 2. Render Graph/resource、Asset identity/artifact、ECS fixed tick/Transform、Platform lifecycle、clean build/impact mapの共通基盤を実装する。
- [ ] 3. Shader/Material/Visibility、incremental cook/cache、ECS structural/change tracking、Input actions/Audio mixer、VSIX transform/inspector、CI/golden/performanceを実装する。
- [ ] 4. runtime streaming、source sync/Bake、stress/failure policyを実装する（quality profile、deterministic jobs、spatial audio/device recoveryは完了）。
- [ ] 5. 各Runtime/CLI機能をVSIX frontendへ接続し、packageとdebug toolingを統合する。
- [ ] 6. トラック別性能・互換性・clean package gateを通し、Release Candidate manifestを生成する。
- [ ] 7. `full`、`stress`、`vsix`、WARP必須pass、任意Hardware/実audio passを完走し、既知deviationとrollback情報を固定する。

## 非物理トラック共通の完了判定

各Epicは、次をすべて満たすまで完了扱いにしない。

- 対象Featureが`Implementation-Status.md`に安定ID、status、evidence、test、next action付きで存在する。
- Intended Specificationと、必要な所有権・依存方向・永続形式のADRが存在する。
- すべてのImplemented/PartiallyImplemented FeatureがExecutable Stageへ割り当てられ、Specified/Deferred FeatureはOracle付き`NotImplemented` Stageを持つ。
- Debug/Releaseのunit/integration testが成功し、Visual FeatureはWARP、performance Featureは予算、Editor Featureは必要なExperimental Instance証跡を満たす。
- 最終コードから観測挙動を再調査し、`Matches`、`MatchesWithDocumentedExtension`、`Partial`、`Mismatch`のいずれかを記録する。
- clean checkoutからbuild/test/packageを再現でき、生成artifactにcommit、tool/provider version、input/dependency hash、configurationが記録される。
- 未実装、manual pending、timeout、infrastructure failure、waiverを成功へ読み替えていない。
