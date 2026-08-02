# C# Game Engine — アーキテクチャ・実装台帳 Skill

## 目的

このSkillは、C# Game Engineの実装が進むたびに、リポジトリとドキュメントを同期します。

次の4種類の「事実」を分けて管理します。

1. **計画されたアーキテクチャ** — L0〜L9に本来何が存在すべきか。
2. **実装されたアーキテクチャ** — 現在のソースコードと生成物に何が存在するか。
3. **リバースエンジニアリングされた挙動** — コード、テスト、生成ファイル、実行結果、診断から判明した実際の仕様。
4. **仕様適合状態** — 意図した仕様と、実装および観測された挙動が一致しているか。

計画やプロンプトに書かれているだけの機能を、実装済みとして扱ってはいけません。  
`計画済み`、`実装済み`、`推測済み`、`検証済み`、`部分実装`、`未実装`を明確に区別してください。

---

## 製品名

- ユーザー向け製品名: **C# Game Engine**
- 内部識別名: **CSharpGameEngine**
- ファイル名、プロジェクト名、アセンブリ名、名前空間、NuGet ID、VSIX ID、キャッシュキー、コマンド名に`#`を使用しないでください。

---

## アーキテクチャレイヤー

次のレイヤーを一貫して使用してください。

- **L0 — Platform / Native Backends**
- **L1 — Core Kernel**
- **L2 — Type Schema / Identity**
- **L3 — ECS / Job Runtime**
- **L4 — Domain Servers**
- **L5 — Runtime Feature Modules**
- **L6 — Runtime Artifacts**
- **L7 — Static Compiler / Bake**
- **L8 — Visual Studio Authoring Environment**
- **L9 — Game / Application**

レイヤー番号はアーキテクチャ上のメタデータです。公開名前空間には原則として`L0`、`L1`などを含めないでください。

依存規則:

> 上位レイヤーは下位レイヤーへ依存できます。下位レイヤーは上位レイヤーへ依存してはいけません。

追加の必須境界:

- RuntimeであるL0〜L6はRoslynやVisual Studio SDKへ依存しない。
- L7のCompiler/Bake処理はVisual Studioなしで実行できる。
- L8は、独立したEngine、Compiler、Runtimeに対するフロントエンドである。
- `.cs`ファイルと元アセットを正式なSource of Truthとする。
- ArtifactとCacheは再生成可能な生成物とする。
- ECSの実行時データはSoA／Archetype／Chunk形式で保持する。
- 表示上のHierarchyは投影であり、実行時オブジェクト所有ツリーではない。

---

## 維持するドキュメント

リポジトリが必要な段階に達したときに不足ファイルを作成してください。空の雛形を大量に作らないでください。

推奨構成:

```text
Docs/
├ Architecture.md
├ Roadmap.md
├ Implementation-Status.md
├ Reverse-Engineered-Spec.md
├ Conformance.md
├ Layers/
│  ├ L0-Platform.md
│  ├ L1-Core.md
│  ├ L2-Schema.md
│  ├ L3-ECS.md
│  ├ L4-Servers.md
│  ├ L5-Runtime-Features.md
│  ├ L6-Artifacts.md
│  ├ L7-Compiler-Bake.md
│  ├ L8-VisualStudio.md
│  └ L9-Game.md
├ Specifications/
│  ├ Numerics.md
│  ├ ECS.md
│  ├ Scene.md
│  ├ Rendering.md
│  ├ Omni.md
│  ├ Assets.md
│  ├ Behavior.md
│  └ Tooling.md
├ Decisions/
│  └ ADR-NNNN-*.md
└ Milestones/
   └ MNNN-*.md

CHANGELOG.md
README.md
```

実際の計画も実装証拠も存在しないSubsystemについて、仕様書だけを先に作らないでください。

---

## 状態語彙

各Featureには、次のうち1つだけを主状態として使用してください。

- `Planned` — 計画済み
- `Investigating` — 調査中
- `Specified` — 仕様確定
- `Implementing` — 実装中
- `Implemented` — コード実装済み
- `PartiallyImplemented` — 部分実装
- `Verified` — 仕様・実装・テスト・観測結果まで一致確認済み
- `Blocked` — 外部要因などで停止
- `Deferred` — 意図的に延期
- `Removed` — 削除済み

任意の検証状態:

- `Unverified`
- `CodeVerified`
- `TestVerified`
- `RuntimeVerified`
- `ReverseEngineered`
- `ConformanceVerified`

「完了」「ほぼ完成」「動くはず」などの曖昧な表現だけを使用せず、必ず上記状態へ対応させてください。

---

## Feature台帳形式

`Docs/Implementation-Status.md`を単一のFeature台帳として維持してください。

各Featureは次の形式を基本とします。

```markdown
## F-L3-004 — Archetype Storage

- Layer: L3 — ECS / Job Runtime
- Parent capability: ECS Runtime
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Specifications/ECS.md#archetype-storage
- Implementation evidence:
  - src/CSharpGameEngine.Ecs/Archetype.cs
  - src/CSharpGameEngine.Ecs/Chunk.cs
- Tests:
  - tests/CSharpGameEngine.Ecs.Tests/ArchetypeTests.cs
- Reverse-engineered behavior:
  - Entity LocationはWorld所有のLocation Tableから解決される。
  - Componentは型別のChunk列へ保存される。
- Conformance: Matches
- Known deviations: None
- Next action: Structural Command Bufferを実装する。
```

Feature IDは安定させ、一度使用した番号を再利用しないでください。

形式:

```text
F-L<レイヤー番号>-<連番>
```

例:

- `F-L0-001` Win32 Window Host
- `F-L1-003` StableId
- `F-L3-012` Structural Command Buffer
- `F-L5-008` Omni Camera-Relative Mapping
- `F-L8-004` Scene Hierarchy Tool Window

---

## リバースエンジニアリング規則

既存コードがある場合、仕様書を更新する前に、証拠から現在の挙動を導出してください。

必要に応じて次の順に調査します。

1. 公開APIと型宣言
2. 内部実装
3. Unit Test／Integration Test
4. 生成コード
5. Build ScriptとProject参照
6. Runtime LogとDiagnostic
7. Serialize／Artifact形式
8. 実際の実行結果

各主張には、証拠パスと確信度を記録してください。

- `High` — コードとテストによって直接保証される。
- `Medium` — コードから強く読み取れるが、テストが不十分。
- `Low` — 名前、コメント、未完成経路からの推測。

偶発的なバグに合わせて意図した仕様を書き換えないでください。仕様不一致として記録してください。

例:

```markdown
### RE-L5-002 — Omni Mapping Continuity

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Omni/OmniMapping.cs
  - tests/CSharpGameEngine.Omni.Tests/ContinuityTests.cs
- Observed behavior:
  - Linear領域とLogarithmic領域は指定Thresholdで連続する。
- Intended behavior:
  - 観測結果と同じ。
- Result: Matches
```

---

## 仕様適合モデル

`Docs/Conformance.md`では、次の4者を比較してください。

```text
意図した仕様
↕
実装
↕
リバースエンジニアリングで観測した挙動
↕
テスト・実行検証
```

適合状態:

- `Matches`
- `MatchesWithDocumentedExtension`
- `Partial`
- `Mismatch`
- `Unknown`
- `ObsoleteSpecification`

Featureを`Verified`にできるのは、次をすべて満たした場合だけです。

1. 意図した挙動が文書化されている。
2. 実装証拠が特定されている。
3. テストまたは実行検証が存在する。
4. 観測された挙動と意図した挙動を比較した。
5. 未解決のMismatchが存在しない。

仕様と実装が異なる場合、両方を黙って変更して一致したように見せてはいけません。Mismatchを記録し、コードと仕様のどちらを変更するかを決定し、必要ならADRまたは次のFeature作業を作成してください。

---

## 各実装タスクで必ず行うこと

### 実装前

1. このSkillを読む。
2. `Docs/Architecture.md`、`Docs/Implementation-Status.md`、関連するLayer／Specification文書を読む。
3. リポジトリを調査して現在の実装状態を確定する。
4. タスクが変更するFeature IDを特定する。
5. 意図した結果と既存の実装証拠を整理する。

### 実装中

1. レイヤー境界を維持する。
2. 関係のない将来機能を実装しない。
3. 観測可能な契約を証明するテストを追加・更新する。
4. コードだけでは理由が不明瞭な設計判断を記録する。
5. コードが存在する前にImplementedへ変更しない。

### 実装後

1. 該当するRestore／Build／Test／Runtime検証を実行する。
2. プロンプトではなく最終コードから実際の仕様を再調査する。
3. Feature状態と証拠パスを更新する。
4. 契約を意図的に変えた場合だけ、意図した仕様を更新する。
5. リバースエンジニアリングされた挙動を更新する。
6. 意図した仕様と観測結果を比較する。
7. Mismatch、未解決事項、次の作業を記録する。
8. Milestone文書を更新する。
9. ユーザー向け挙動または公開APIが変わった場合だけCHANGELOGを更新する。
10. Setupまたは利用方法が変わった場合だけREADMEを更新する。

ドキュメント更新はDefinition of Doneの一部です。

---

## 自動ドキュメント更新手順

各タスクで次の手順を実行してください。

### Phase A — Discover

- 変更対象と関連Projectを列挙する。
- 各ProjectをL0〜L9へ割り当てる。
- 現在の文書とFeature Entryを探す。
- 実装API、Test、生成ファイル、Runtime Entry Pointを検索する。
- 実装済みだが未文書化の挙動を発見する。
- 文書化済みだが未実装の挙動を発見する。

### Phase B — Plan

内部作業表に次を整理する。

```text
Feature ID
Layer
現在のStatus
目標Status
調査するファイル
変更するファイル
必要な検証
更新するドキュメント
```

### Phase C — Implement

依頼された範囲だけを実装する。

### Phase D — Verify

適用可能な最も強い検証を行う。

- Unit Test
- Integration Test
- Golden／Snapshot Test
- GPU Validation
- Runtime Scenario
- CLI Smoke Test
- Architecture Dependency Test

### Phase E — Reverse Engineer

最終実装を再読し、元のプロンプトとは独立して、実際の挙動を要約する。

### Phase F — Reconcile

意図した挙動と観測結果を比較する。

```text
Intended = Observed                 → Matches
Observedが互換性のある上位集合      → MatchesWithDocumentedExtension
一部だけ存在                        → Partial
挙動が異なる                        → Mismatch
証拠不足                            → Unknown
```

### Phase G — Update Docs

関係する文書だけを更新する。無関係な大規模書き換えを避ける。

### Phase H — Report

完了報告には必ず次を含める。

1. 変更したFeature ID
2. 変更前と変更後のStatus
3. 実装証拠
4. 検証コマンドと結果
5. リバースエンジニアリングした実際の挙動
6. Conformance結果
7. 更新したドキュメント
8. 既知の差異と次の作業

---

## 計画Featureツリー

詳細版は`Docs/Roadmap.md`で維持してください。最低限、次を将来のInventoryとして扱います。

### L0 — Platform / Native Backends

- Win32 Window Host
- Time／Event Loop
- Input Backend
- Native Library Loading
- D3D12 Binding Boundary
- DXGI Adapter／Swap Chain
- D3D12 Device／Queue／Command List／Fence
- GPU Resource Allocation
- Descriptor Management
- Upload／Readback
- Native Diagnostic／Debug Layer

### L1 — Core Kernel

- Stable ID／Hash
- Diagnostic
- Result／Error Model
- Memory Ownership／Allocator
- Lower Layer向けCollection
- High-Precision Numerics
- Math Primitives
- Deterministic Low-Level Utilities

### L2 — Type Schema / Identity

- Type ID／Schema
- Component Schema
- Asset Identity
- Scene Identity
- Logical Coordinate Types
- Serialization Metadata
- Version／Migration Descriptor

### L3 — ECS / Job Runtime

- Entity ID／Generation
- World所有Entity Location Table
- Archetype
- Chunk／SoA Column
- Query
- System
- Structural Command Buffer
- Enable Mask／Change Version
- Job Scheduling
- Debug Inspection Projection

### L4 — Domain Servers

- Render Server
- Render Graph Execution
- Physics Server
- Audio Server
- Resource／Device Services
- Async Handle-Based API

### L5 — Runtime Feature Modules

- Simulation-to-Render Extraction
- Camera／Visibility
- Omni／Logarithmic Mapping
- High-Precision Transform Hierarchy
- Culling／LOD
- Behavior Runtime
- Animation Runtime
- Navigation／Character
- Gameplay／UI Runtime Modules

### L6 — Runtime Artifacts

- Shader Artifact
- Mesh／Texture／Material Artifact
- Scene Artifact／Spawn Plan
- Behavior Artifact
- Versioned Binary Format
- Dependency／Content Hash Metadata

### L7 — Static Compiler / Bake

- HLSL Compilation
- Asset Import
- Fab Import Bridge
- Dependency Graph
- Incremental Artifact Compiler
- Scene DSL Bake
- Behavior DSL Bake
- Source Generator／Analyzer
- World Partition Bake
- Omni Bounds／LOD Bake

### L8 — Visual Studio Authoring Environment

- VSIX Shell
- Command
- Output／Error List Integration
- Asset Browser
- Inspector
- Scene Hierarchy
- Scene View
- Render Graph Viewer／Editor
- Behavior Graph Viewer／Editor
- Debugger Visualizer
- Roslyn Code Rewriting

### L9 — Game / Application

- Game Project
- Static C# Scene Definitions
- Components／Systems
- Material／Behavior／Build Definitions
- Game-Specific Features
- Shipping Configuration

---

## ドキュメント上の事実の優先順位

情報が矛盾する場合、現在の実際の挙動は次の順に判定します。

1. 再現可能な実行結果と成功するTest
2. 最終Source CodeとGenerated Code
3. 実際に出力・読み込みされるArtifact Schema
4. 現在のSpecification
5. 意図した判断を記録したADR
6. Roadmap／Milestone Plan
7. Prompt／Comment／Informal Note

これは「現在観測できる事実」の優先順位であり、望ましい挙動の優先順位ではありません。意図した契約と異なる場合はMismatchとして記録してください。

---

## ADR作成規則

次のような判断にはADRを作成してください。

- 依存方向を変更する。
- Ownership／Lifetimeを変更する。
- 正式なSource Formatを変更する。
- Public APIまたはPersisted Formatへ影響する。
- 新しいNative Bindingまたは外部依存を導入する。
- ECS LayoutまたはIdentity Semanticsを変更する。
- Scene、Asset、Behavior、Render Graph、Omniの意味を変更する。
- 後から戻すコストが高い。

通常の局所的RefactorにはADRを作成しないでください。

ADR基本形式:

```markdown
# ADR-NNNN — タイトル

- Status:
- Date:
- Related features:

## Context
なぜ判断が必要か。

## Decision
何を決定したか。

## Alternatives considered
他の候補と採用しなかった理由。

## Consequences
利点、欠点、制約、将来への影響。

## Verification
判断が守られていることをどう検証するか。
```

---

## Milestone文書規則

各Milestone文書には最低限、次を記録してください。

- 目的
- 対象Feature ID
- 対象範囲
- 対象外
- 実装証拠
- 実行した検証
- 完了条件
- 現在の結果
- 未解決事項
- 次のMilestoneへ進む条件

Milestoneを完了扱いにするのは、完了条件を実際に検証した場合だけです。

---

## 変更範囲の規律

- ドキュメント同期を理由に無関係なCode Refactorを行わない。
- 1つのFeature変更で全Layer文書を一括整形しない。
- Source Pathが変わった場合はEvidence Pathを更新する。
- 削除されたFeatureは台帳から消さず、`Removed`として履歴を残す。
- Feature IDをRenumberしない。
- 仕様変更とバグ修正を区別する。
- 未確認事項を事実として記述しない。
- 「おそらく」「想定」しか根拠がない場合はConfidenceを下げる。

---

## 実装プロンプトへ自動的に追加する完了条件

すべての実装タスクについて、次を暗黙の完了条件とします。

1. 関連Feature IDを特定または新規登録した。
2. 実装前のStatusを確認した。
3. 依頼範囲だけを実装した。
4. Testまたは実行検証を追加・更新した。
5. 最終コードから実際の仕様を再調査した。
6. Intended／Observedを比較した。
7. 台帳、逆解析仕様、適合表、関連Layer文書を更新した。
8. 必要な場合だけADR、Milestone、CHANGELOG、READMEを更新した。
9. 検証不能な点を隠さず報告した。
10. 次に実装すべきFeature IDを示した。

ドキュメントが同期されていない実装は、完了ではありません。
