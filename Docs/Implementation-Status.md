# Implementation Status (Feature Ledger)

このファイルは`.github/skills/CSharpGameEngine-Documentation/SKILL.md`が定義するFeature ledgerです。
`F-L<layer>-<number>`形式の安定IDで機能を追跡します。既存(ADR-0001〜ADR-0013)の機能への遡及的な
Backfillは行っておらず、ADR-0014(CSharpGameEngine.CodeAnalysis基盤)で導入した2エントリを起点として
開始し、ADR-0015(アーキタイプ型ECS静的ランタイム)で5エントリ、ADR-0016(ECS駆動ステージと
ThirdPersonキャラクターコントローラー)で7エントリ、ADR-0017/ADR-0018(UE5.8 FirstPersonステージ
移植、M0004)で既存2エントリを更新し5エントリを追加、ADR-0019(VSIXのglTFアウトオブプロセス
ブリッジ統一)で1エントリ、ADR-0020(シーンダブルクリックプレビューとヒエラルキー選択連動)で
3エントリを追加、ADR-0021(GameEngineレイアウトとシェイプパレット、`.scene`拡張子修正)で
F-L8-003を更新し3エントリを追加、ADR-0022(ヒエラルキーへのドラッグ&ドロップ配置)で1エントリを
追加、ADR-0010追記(ブリッジ起動を事前ビルド済みexeの直接起動に切り替え、シーンタブの体感速度
問題を解消)でF-L8-002/F-L8-003を更新、ADR-0023(ブリッジプロセスの常駐化・Named Pipeサーバー)で
F-L8-002を更新し1エントリを追加、ADR-0024(Generated Entity Source Generator、AoS/ECS SoA)で
F-L3系2件・F-L7系3件を追加、ADR-0024追記(`[Flags]`による1宣言からの両バックエンド生成、M0006)で
F-L7-006/F-L7-007を更新しF-L9系1件を追加、ADR-0025(RUFセマンティック座標系、M0007)でF-L1系
(このLayer初のエントリ)4件・F-L2系1件を追加、ADR-0026/ADR-0027/ADR-0028(Renderer/Import/ECS
内部へのRUF全面統合、M0008)でF-L0-001・F-L2-002・F-L0-005・F-L9-003・F-L9-004を更新し
F-L0系1件・F-L1系2件を追加、ADR-0029(動的Archetype SoA ECSへの全面移行、M0009)で
`CSharpGameEngine.Ecs`/`CSharpGameEngine.Generation.Abstractions`関連の12エントリ
(F-L3-001〜006、F-L7-003・004・006〜008、旧F-L9-001重複ID)をRemovedへ更新し、
F-L9-003・F-L9-004を更新の上でF-L3系4件・F-L7系3件・F-L9系1件を新規追加、
ADR-0030(C#コードをSceneの唯一の正本とするScene Authoring Phase A、M0010)でF-L3系1件・
F-L7系2件・F-L9系1件を新規追加、ADR-0031(`[SceneDefinition]`クラスのコンテキストメニュー
3Dプレビュー、M0011)でF-L8-003を更新の上でF-L7系1件・F-L8系1件・F-L9系1件を新規追加しています。
2026-08のシーンエディタ設計文書整備(ADR-0040全面改稿・ADR-0041/M0019/M0020/
Scene-Editor-Design.md新規)でF-L8-011をPlannedで追加し、F-L4-002/F-L8-010の「ADR-0040自体の
文書化はPhase 11で行う」注記を解消、旧`Docs/EditorSpec.md`の削除に伴う参照注記を行いました。
続くM0020 Phase 0(前提修正とデッドコード削除)でF-L8-006/F-L8-007をRemovedへ更新し、
F-L8-011をPartiallyImplementedへ、F-L8-002/F-L8-003/F-L8-010の`.scene`残存記述と
旧プリミティブ資産パスの記述を現状へ訂正しています。
M0049(Sceneの保存形式をJSON化、第1段: Runtimeパイプライン)でF-L7-019を新規追加し、
F-L3-011・F-L3-013・F-L7-012・F-L7-013・F-L7-017のKnown deviationsを更新して、実運用のScene検知が
C#構文(`IScene`)からJSON(`*.scene.json`)へ切り替わったことを記録しています。

---

## F-L7-001 — CSharpGameEngine.CodeAnalysisプロジェクト基盤

- Layer: L7 — Static Compiler / Bake
- Parent capability: Source Generator / Analyzer基盤
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0014-CodeAnalysis-Generator-Foundation.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/CSharpGameEngine.CodeAnalysis.csproj
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/CSharpGameEngine.CodeAnalysis.Tests.csproj
- Reverse-engineered behavior:
  - netstandard2.0・`LangVersion=14`・`IsRoslynComponent=true`・`EnforceExtendedAnalyzerRules=true`で
    ビルドされ、`Microsoft.CodeAnalysis.CSharp`/`Microsoft.CodeAnalysis.Analyzers` 4.14.0を
    `PrivateAssets="all"`で参照する。
  - `dotnet pack -c Release`で`analyzers/dotnet/cs/CSharpGameEngine.CodeAnalysis.dll`のみを含む
    NuGetパッケージを生成する(`lib/`配下は空、`DevelopmentDependency=true`)。
  - どの既存プロジェクトからも`ProjectReference`されていない(通常参照・`OutputItemType="Analyzer"`
    参照のいずれも無し)。
- Conformance: Matches
- Known deviations: None
- Next action: 将来2つ目のGeneratorまたは最初のDiagnosticAnalyzerが必要になった時点で、
  `CSharpGameEngine.CodeAnalysis.Abstractions`の新規作成を再検討する(ADR-0014参照)。

## F-L7-002 — CSharpGameEngineBootstrapGenerator

- Layer: L7 — Static Compiler / Bake
- Parent capability: Source Generator / Analyzer基盤
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0014-CodeAnalysis-Generator-Foundation.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/CSharpGameEngineBootstrapGenerator.cs
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/BootstrapGeneratorTests.cs
- Reverse-engineered behavior:
  - `IIncrementalGenerator`実装で、`Initialize`は`RegisterPostInitializationOutput`の1呼び出しのみ
    (`CompilationProvider`/`AnalyzerConfigOptionsProvider`は消費しない)。
  - 常に`CSharpGameEngineBootstrap.g.cs`という1ファイルのみを、コンパイル内容によらず出力する。
  - 出力文字列は`string[]`を`"\n"`結合して構築しており、生成元ソースファイル自身の改行コードに
    依存しない。
  - 独立した2回の`CSharpGeneratorDriver`実行で出力が完全に一致すること(決定性)、および
    `CSharpSyntaxTree.ParseText`での再パースが診断0件で成功すること(構文妥当性)を単体テストで
    確認済み。
- Conformance: Matches
- Known deviations: None
- Next action: 実際にこのGeneratorをどこかのプロジェクトから`OutputItemType="Analyzer"`で消費する
  マイルストーンが発生した時点で、VS上での実機ロード確認を行う。

## F-L3-001 — Entity / World基盤(旧`CSharpGameEngine.Ecs`、削除済み)

- Layer: L3 — ECS / Job Runtime
- Parent capability: アーキタイプ型ECS静的ランタイム
- Status: Removed
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0015-Archetype-Ecs-Static-Runtime.md
- Implementation evidence:
  - src/CSharpGameEngine.Ecs/Entity.cs
  - src/CSharpGameEngine.Ecs/World.cs
- Tests:
  - tests/CSharpGameEngine.Ecs.Tests/EntitySpec.cs
  - tests/CSharpGameEngine.Ecs.Tests/WorldSpec.cs
- Reverse-engineered behavior:
  - `Entity`はId+Generationの値型で、`World`はfree-list方式のIdアロケータを持つ。`Destroy`後に
    `Create`すると同じIdをGenerationを1つ増やして再利用し、古いハンドルへのアクセスは
    `InvalidOperationException`になる。
  - `World.GetComponentRef<T>`は呼び出しごとにエンティティの現在位置(`Storage`+`Row`)を
    位置テーブルから解決してから`ref`を返す。ビュー構造体は行番号をキャッシュしない。
  - `Destroy`はswap-back方式(末尾行を削除対象の行に詰める)で実装されており、移動が発生した
    場合は移動先エンティティの位置テーブルを更新する。
- Conformance: Matches
- Known deviations: None
- Next action: 削除済み(ADR-0029、M0009)。後継はF-L3-007(`CSharpGameEngine.Entities`の
  `Entity`/`World`)。

## F-L3-002 — Archetypeストレージ共有(旧`CSharpGameEngine.Ecs`、削除済み)

- Layer: L3 — ECS / Job Runtime
- Parent capability: アーキタイプ型ECS静的ランタイム
- Status: Removed
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0015-Archetype-Ecs-Static-Runtime.md
- Implementation evidence:
  - src/CSharpGameEngine.Ecs/ArchetypeStorage.cs
  - src/CSharpGameEngine.Ecs/ArchetypeSignature.cs
  - src/CSharpGameEngine.Ecs/ComponentColumn.cs
- Tests:
  - tests/CSharpGameEngine.Ecs.Tests/ArchetypeStorageSharingSpec.cs
  - tests/CSharpGameEngine.Ecs.Tests/ComponentColumnSpec.cs
- Reverse-engineered behavior:
  - `ArchetypeSignature`は`Type[]`の要素シーケンス一致で等価性を判定するため、正規化後の
    コンポーネント構成が一致する複数の名前付きアーキタイプ(宣言順違い含む)は、実行時に
    同一の`ArchetypeStorage`インスタンスを共有する。実際に`TwoArchetypesWithSameNormalizedComponentSet_ShareSameStorage`
    で`World.DebugArchetypeStorageCount`が1件になることを確認済み。
  - `ComponentColumn<T>`はコンポーネント型ごとにただ1種類の汎用SoA列実装であり、アーキタイプ
    専用のストレージクラスは一切生成されない。
- Conformance: Matches
- Known deviations: None
- Next action: 削除済み(ADR-0029、M0009)。後継のArchetypeは実行時のComponent集合で動的生成され
  (F-L3-007)、コンパイル時に宣言された「同一正規化形状の別名アーキタイプ」という概念自体が
  存在しなくなった。

## F-L3-003 — `IEcsArchetype<TSelf>`ランタイム契約(旧`CSharpGameEngine.Ecs`、削除済み)

- Layer: L3 — ECS / Job Runtime
- Parent capability: アーキタイプ型ECS静的ランタイム
- Status: Removed
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0015-Archetype-Ecs-Static-Runtime.md
- Implementation evidence:
  - src/CSharpGameEngine.Ecs/IEcsArchetype.cs
  - src/CSharpGameEngine.Ecs/WorldEcsExtensions.cs
  - src/CSharpGameEngine.Ecs/EcsArchetypeAttribute.cs
  - src/CSharpGameEngine.Ecs/EcsReadOnlyAttribute.cs
- Tests:
  - tests/CSharpGameEngine.Ecs.Tests/FakeArchetypes.cs(Fake実装によるランタイム単体検証)
  - tests/CSharpGameEngine.CodeAnalysis.Tests/EcsArchetypeGeneratorEndToEndTests.cs
    (Generatorが実際に生成する実装での検証)
- Reverse-engineered behavior:
  - `static abstract`インターフェースメンバー(`ComponentTypes`/`CreateColumns`/`CreateView`)による
    CRTP契約で、`World`は個々のアーキタイプの形状を一切ハードコードしない。
  - `WorldEcsExtensions.Create<T>()`が引数なし版の唯一の汎用実装。`World`自身は`Create`という
    名前のインスタンスメンバーを持たない(拡張メソッド解決の隠蔽を避けるため)。
- Conformance: Matches
- Known deviations: None
- Next action: 削除済み(ADR-0029、M0009)。後継はF-L7-009(`[GameSystem]`/`[Update]`属性による
  引数からのQuery/Read-Write推論、`static abstract`CRTP契約は不要になった)。

## F-L7-003 — `EcsArchetypeGenerator`(削除済み)

- Layer: L7 — Static Compiler / Bake
- Parent capability: アーキタイプ型ECS静的ランタイム
- Status: Removed
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0015-Archetype-Ecs-Static-Runtime.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/EcsArchetypeGenerator.cs
  - src/CSharpGameEngine.CodeAnalysis/EcsArchetypeAnalyzer.cs
  - src/CSharpGameEngine.CodeAnalysis/EcsArchetypeEmitter.cs
  - src/CSharpGameEngine.CodeAnalysis/EcsArchetypeModel.cs
  - 削除済みの旧L9アプリケーション`Components.cs`/`Archetypes.cs`(履歴は`40f08e4`以前)
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/EcsArchetypeGeneratorEmissionTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/EcsArchetypeGeneratorEndToEndTests.cs
- Reverse-engineered behavior:
  - `ForAttributeWithMetadataName("CSharpGameEngine.Ecs.EcsArchetypeAttribute", ...)`でアーキタイプ
    構造体を検出し、コンポーネントプロパティを完全修飾名のOrdinal文字列でソートして正規化する。
    旧L9アプリケーションを実際にビルドし、`Player`の生成コード(`ComponentTypes`配列・列インデックス)が
    宣言順ではなく正規化順(`Health,PlayerTag,Position,Rotation,Velocity`)になっていることを
    目視確認済み。
  - 名前付き引数版ファクトリ(`{StructName}EcsFactory.Create<T>`)は、アーキタイプごとに生成される
    ネストされた空のマーカーインターフェース(`{StructName}.IMarker`)への型制約により、正規化後の
    引数形状が完全に一致する複数アーキタイプが存在しても曖昧にならずに解決される。
    `EcsArchetypeGeneratorEndToEndTests`で実際にコンパイル・`Emit`・`Assembly.Load`・リフレクション
    実行して確認済み。
  - 診断が1件でもあるアーキタイプは、`EcsArchetypeEmitter`がそのアーキタイプのソース生成を
    完全にスキップする。
  - `EcsArchetypeAnalysisResult`/`EcsComponentPropertyModel`は`record`ではなく通常の`struct`
    (netstandard2.0では`record`が要求する`IsExternalInit`をBCLが提供しないため)。
- Conformance: Matches
- Known deviations: `ImmutableArray<T>`フィールドを持つため、`IncrementalValuesProvider`の
  キャッシュとしては参照等価性の制約を受ける(正しさには影響しないパフォーマンス上の制約)。
- Next action: 削除済み(ADR-0029、M0009)。後継はF-L7-009(`GameSystemAnalyzer`/
  `GameSystemExecutorEmitter`)。

## F-L7-004 — `EcsArchetypeGenerator`診断(ECSGEN001〜009、削除済み)

- Layer: L7 — Static Compiler / Bake
- Parent capability: アーキタイプ型ECS静的ランタイム
- Status: Removed
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0015-Archetype-Ecs-Static-Runtime.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/EcsArchetypeDiagnostics.cs
  - src/CSharpGameEngine.CodeAnalysis/AnalyzerReleases.Shipped.md
  - src/CSharpGameEngine.CodeAnalysis/AnalyzerReleases.Unshipped.md
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/EcsArchetypeGeneratorDiagnosticsTests.cs
- Reverse-engineered behavior:
  - `ECSGEN001`(非partial)、`ECSGEN002`(ジェネリック禁止)、`ECSGEN003`(ネスト禁止)、
    `ECSGEN004`(コンポーネント0件禁止)、`ECSGEN005`(ref/ref readonly必須)、
    `ECSGEN006`(非structコンポーネント型禁止)、`ECSGEN007`(コンポーネント型重複禁止、
    ユーザー提示の`InvalidObject`例で確認)、`ECSGEN008`/`ECSGEN009`(`[EcsReadOnly]`と
    `ref readonly`の双方向整合性)の9件、カテゴリ`CSharpGameEngine.Ecs.CodeGen`、すべて`Error`。
  - 当初予定していた「get-onlyでなければならない」という診断は、`ref`/`ref readonly`を返す
    プロパティへの`set`付与自体がC#自身のコンパイルエラー(CS8147)であるため実際には到達不能と
    判明し、実装時に削除した。
  - `EnforceExtendedAnalyzerRules=true`下で`RS2008`(Analyzer release tracking)が実際に
    発生したため、`AnalyzerReleases.Shipped.md`/`AnalyzerReleases.Unshipped.md`を新規に
    `AdditionalFiles`として追加した(このリポジトリで初のAnalyzer release tracking導入)。
- Conformance: Matches
- Known deviations: None
- Next action: 削除済み(ADR-0029、M0009)。後継はF-L7-011(SYSGEN001〜020)。

## F-L0-001 — `Camera`(オービットカメラ+一人称カメラ)

- Layer: L0 — Platform / Native Backends
- Parent capability: ECS駆動ステージ
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0016-Ecs-Driven-Stage-And-ThirdPerson-Controller.md、
  Docs/Decisions/ADR-0018-Textured-Stage-And-Ecs-Systems.md(一人称モード追加)、
  Docs/Decisions/ADR-0026-Camera-Geometry-Ruf-Integration.md(RUF内部実装統合)
- Implementation evidence:
  - src/CSharpGameEngine.Windows/Graphics/Camera.cs
- Tests:
  - tests/CSharpGameEngine.Windows.Tests/Graphics/CameraTests.cs
- Reverse-engineered behavior:
  - Yaw/Pitch/Distance/Targetからview/projection行列を計算するUE5風スプリングアームカメラ。
    `System.Numerics`のみに依存しTerraFXを含まないため、GPU無しで単体テストできる。
  - `ApplyMouseDelta`はPitchをインスタンスフィールド`PitchClampMinRadians`/
    `PitchClampMaxRadians`にクランプする(`CreateDefault`は静的定数`MinPitchRadians`/
    `MaxPitchRadians`(-80°/+40°)を、`CreateFirstPerson`は`MinPitchRadiansFirstPerson`/
    `MaxPitchRadiansFirstPerson`(±85°)をそれぞれ設定する)。
  - `CreateFirstPerson`は`Distance=0`で`Target`フィールドがそのまま目の位置を兼ねる。
    `GetView()`は`Distance > 1e-4f`で分岐し、一人称時は`eye + GetForward()`を注視点にすることで
    `eye==Target`による退化を回避する。
  - (ADR-0026追記)`GetForward`/`GetFlatForward`は`RufQuaternion`合成(Yaw軸`RufVector.Up`、
    Pitch軸`RufVector.Left`)で計算するよう書き換えた。`GetFlatRight`は
    `Spatial<UnityCoordinateConvention>.RightFromUpForward`へ置換し、旧`Cross(UnitY,flatForward)`
    式とビット単位で一致することを確認済み。`GetView`/`GetProjection`自体(`Matrix4x4.CreateLookAt`/
    `CreatePerspectiveFieldOfView`)はRUF化していない(Rendering境界は依然未確定、
    Spatial-RUF.md参照)。`BuildViewMatrix`静的ヘルパーを新設し`ModelRenderer`と共有する。
- Conformance: Matches
- Known deviations: `GetFlatRight()`の符号がRUFの`RightFromUpForward`(Handedness反映済み)と
  数式として一致することは確認済みだが、実機での前後左右の直感一致は依然未確認(ADR-0016/ADR-0026参照)。
- Next action: 実機確認で符号が逆であれば反転する。

## F-L0-002 — `StageRenderer`(複数オブジェクト・テクスチャ付きPBR・データ駆動ライティング)

- M0098: a material may carry a `StageWorldGrid` (main/sub grid lines drawn per pixel from the world position, projected along the dominant world axes; ADR-0075). `ObjectCB` grew 112 → 160 bytes (`WorldGridParams`, `WorldGridColor`, `WorldSubGridColor`; the slot stride is 512) and materials without a grid keep their output. WorldGridGpuIntegrationTests (WARP: floor main/sub line hierarchy, independence from mesh UVs, no grid means one flat colour, wall lines).
- M0099 Phase 4: radial depth mode (F-L5-038, ADR-0076). Seven radial pipeline states share the standard root signature; `SetCamera` validates the radial parameters, `WriteDepthMapping` appends the 32-byte packet every frame, and the sky, cascade and AO passes take `InvertibleViewProjection` (the camera matrix for a standard camera). Standard pipeline states and their frames are unchanged (`StandardModeGoldenFrameTests`).
- M0099 Phase 0: the grid and the simplified height fog add the render origin back, because the runtime renders camera-relative. `ObjectCB.WorldGridAnchor` (176 bytes; the render origin modulo the grid size, set per material by `SetWorldGridAnchor`) and `FogParams0.z` (render origin altitude, `SetRenderOriginAltitude`) are zero for world-coordinate callers. `EngineHost` computes the anchor exactly in Q64.64 every frame; `RenderFrameState` carries both forward for device recovery. RenderOriginTests (EngineHost end to end), WorldGridGpuIntegrationTests camera-relative cases, RenderFrameStatePersistenceTests.

- Layer: L0 — Platform / Native Backends
- Parent capability: ECS駆動ステージ
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0016-Ecs-Driven-Stage-And-ThirdPerson-Controller.md、
  Docs/Decisions/ADR-0018-Textured-Stage-And-Ecs-Systems.md(テクスチャ・PBR・ライティング拡張)
- Implementation evidence:
  - src/CSharpGameEngine.Windows/Graphics/StageRenderer.cs
  - src/CSharpGameEngine.Windows/Graphics/StageRenderInputs.cs
  - assets/Shaders/Stage.hlsl
- Tests:
  - tests/CSharpGameEngine.Windows.Tests/Graphics/StageGpuIntegrationTests.cs
- Reverse-engineered behavior:
  - `ModelRenderer`とは別クラス(ADR-0004の複製方針)のままだが、ADR-0018でメッシュ/マテリアル/
    オブジェクトを分離するAPI(`StageMeshCreateInfo`/`StageMaterialCreateInfo`/
    `StageObjectCreateInfo`)に拡張した。旧`Create(IReadOnlyList<MeshCreateInfo>)`は
    1メッシュ=1マテリアル=1オブジェクトのアダプタとして残存し、`StageGpuIntegrationTests`の
    既存テストは無改修のまま成功する。
  - 頂点layoutはPosition/Normal/Tangent(float4)/UV(48バイトstride)。マテリアル毎に3テクスチャ
    (BaseColor/Normal/MetallicRoughness)を1つの共有SRVディスクリプタヒープに並べ、描画時に
    `materialIndex*3`オフセットでディスクリプタテーブルを切り替える。未指定テクスチャは
    1×1フォールバック(`ModelRenderer`と同じ「常に本物のSRVをバインドする」方針)。
  - `FrameCB`(b0)にカメラ位置・ディレクショナルライト方向/色・アンビエント色・フォグ
    パラメータを追加、`ObjectCB`(b1)にラフネス/メタリックファクターを追加。各`Set*`
    メソッドは自分の担当バイト範囲のみを書くため呼び出し順序に依存しない。
  - シェーディングはGGX Cook-Torrance(NDF+Smith height-correlated可視性+Schlick
    Fresnel)+Lambert拡散、タンジェント空間ノーマルマッピング、簡易Exponential Height Fog
    (基準高度Y=0固定)。sRGB/ガンマ補正は導入していない(既存のバックバッファ/
    `ModelRenderer`と同じ非対応のまま)。
  - WARPアダプタでの実描画テストにより、地面+ボックスが両方描画されること・Resize後も
    壊れないこと(既存)に加え、2マテリアルを持つ2オブジェクトがそれぞれ正しい
    ディスクリプタテーブルオフセットで描画されること、`SetDirectionalLight`/
    `SetAmbientLight`の呼び出しが実際にレンダリング結果を変えることを確認済み。
- Conformance: Matches
- Known deviations: sRGB/ガンマ補正非対応(意図的、ADR-0018参照)。
- Next action: なし。

## F-L3-014 — 旧L9 runtime component/system/resource stage(削除済み)

- Layer: L3 — ECS / runtime resources
- Status: Removed(2026-08、`40f08e4`)
- Verification: NotApplicable(削除前はTestVerified / RuntimeVerified)
- Removal evidence: 旧L9アプリケーションの`Components.cs`、`SceneLoader.cs`、
  `TransformHierarchyRuntime.cs`、`Systems/Systems.cs`、`AudioRuntime.cs`と専用テストプロジェクトは
  `40f08e4`で削除済み。
- Successor: 現行のアプリケーション側構成は`UserEcsProject/Components.cs`、
  `UserEcsProject/StaticMeshSceneData.cs`、`UserEcsProject/Systems/RuntimeSystem.cs`、
  `UserEcsProject/Scenes/FirstPersonStageScene.cs`。旧機能のうち手書きの階層同期・移動・距離減衰付き
  複数音源は同等移植されていないため、旧FeatureをImplementedとは扱わない。
- Tests: 現行後継は`tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDslGeneratorEndToEndTests.cs`と
  `tests/CSharpGameEngine.Windows.Tests/Runtime/RenderServerTests.cs`で検証する。
- Conformance: NotApplicable(旧実装は存在しない)
- Known deviations: 旧機能と現行Scene DSLランタイムの機能集合は一致しない。
- Next action: 必要なゲームプレイ機能は現行`IScene`/`ISystem`契約上で別Featureとして再設計する。

## F-L0-007 — StageRenderer directional shadow, HDR, ACES and spatial bloom

- Layer: L0 — Platform / D3D12 renderer
- Status: Implemented
- Verification: TestVerified / RuntimeVerified
- Implementation evidence:
  - src/CSharpGameEngine.Windows/Graphics/StageRenderer.cs (`RenderShadowPass`, HDR target, fullscreen post-process pass, resize/dispose paths)
  - assets/Shaders/Stage.hlsl (`ShadowMap`, `ComputeDirectionalShadow`, `FullscreenVS`, `PostProcessPS`, `ToneMapAces`)
- Tests:
  - tests/CSharpGameEngine.Windows.Tests/Hlsl/HlslCompilerTests.cs (`Compile_StagePostProcessShaders_Succeeds`)
  - tests/CSharpGameEngine.Windows.Tests/Graphics/StageGpuIntegrationTests.cs (6/6, including shadow/light output, resize,
    per-frame-slot stability, and shadow-casting)
- Result: Scene lighting is rendered into `R16G16B16A16_FLOAT`; imported PBR base color, normal,
  metallic/roughness, and emissive factors reach the shader. A thresholded 3x3 HDR spatial blur is
  added and the result is ACES tone-mapped into the existing swap-chain format. The public clear
  color is preserved via an inverse-ACES HDR clear value.
- Known deviations: Bloom is intentionally a compact single-pass 3x3 blur, not a multi-resolution separable bloom chain.
- Bug fix (2026-08): the `ShadowViewProjection` field added to `FrameCB` grew the struct to 560 bytes
  while the double-buffered per-frame-slot stride stayed at the unrelated `ConstantAlignment=512`,
  so `SetDirectionalLight`'s per-frame write silently corrupted/overran the adjacent frame slot every
  other frame (visible as scrambled/garbled geometry in `UserEcsProject`). Separately,
  `UserEcsProject/Systems/RuntimeSystem.cs`'s `LevelLoad` only called `Renderer.SetCamera` once at
  load instead of every frame like the sibling light systems, so one of the two frame slots stayed
  pinned to the constructor-time default camera. Fixed by introducing a `FrameConstantsStride`
  derived from `FrameConstantsSize` (so any future `FrameCB` growth keeps the slot width in sync),
  moving `SetCamera` into `LevelLoad`'s per-frame path, and giving the shadow pass its own
  `VSShadowMain` entry point (removing an incidental, previously latent, out-of-bounds constant read
  for `FrameIndex=1` in the shadow pass's unused `ShadowPosition` varying). Verified via 2 new GPU
  regression tests and a real `UserEcsProject.exe` launch + window capture.

## F-L0-003 — `ProceduralMeshFactory`(手続き的メッシュ生成)

- Layer: L0 — Platform / Native Backends
- Parent capability: ECS駆動ステージ
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0016-Ecs-Driven-Stage-And-ThirdPerson-Controller.md
- Implementation evidence:
  - src/CSharpGameEngine.Core/Geometry/ProceduralMeshFactory.cs
- Tests:
  - tests/CSharpGameEngine.Core.Tests/Geometry/ProceduralMeshFactoryTests.cs
- Reverse-engineered behavior:
  - `CreatePlane`はY=0固定・法線+Yの矩形、`CreateBox`は24頂点(面ごとに法線分離)の
    軸整列ボックスを生成する。このリポジトリで初めての手続き的メッシュ生成コード
    (それ以前は全てのメッシュがglTF/FBXインポート経由だった)。
- Conformance: Matches
- Known deviations: カプセル形状は未実装(ADR-0016参照)。
- Next action: なし。

## F-L0-004 — `Win32Window`キーボード/マウス入力

- Layer: L0 — Platform / Native Backends
- Parent capability: ECS駆動ステージ
- Status: Implemented
- Verification: RuntimeVerified
- Intended specification: Docs/Decisions/ADR-0016-Ecs-Driven-Stage-And-ThirdPerson-Controller.md
- Implementation evidence:
  - src/CSharpGameEngine.Windows/Platform/Win32Window.cs
- Tests: なし(Win32メッセージハンドリング自体の単体テストは無く、実機確認に委ねている。
  ADR-0016参照)。
- Reverse-engineered behavior:
  - `IsKeyDown`はポーリング方式(`WM_KEYDOWN`/`UP`で`bool[256]`を更新)。マウスは
    カーソルセンタリング方式(`GetCursorPos`/`SetCursorPos`/`ClipCursor`/`ShowCursor`)。
  - `WM_KILLFOCUS`でキー状態全クリア+マウスキャプチャ解除、`WM_SETFOCUS`で再キャプチャする。
  - (2026-08-05追記、M0009で発見・修正)フォーカス再取得直後の`ConsumeMouseDelta`が、OSにより
    `WM_KILLFOCUS`時点で暗黙に解除された`ClipCursor`の間にカーソルがどこへ動いていても、
    古い`_capturedCenterScreenPoint`との差分をそのまま1フレーム分の巨大なlook deltaとして
    返してしまい、一人称/三人称カメラのPitchが一発でクランプ値まで飛ぶ不具合を発見した
    (リモートスクリーンショット取得ツールの`SetForegroundWindow`呼び出しがフォーカス再取得を
    誘発したことで顕在化、実際のAlt+Tab操作でも起こりうる)。`_skipNextMouseDelta`フラグを追加し、
    `SetMouseCaptured(true)`直後および`WM_SETFOCUS`での再キャプチャ直後の**次の1回の
    `ConsumeMouseDelta`呼び出し**だけは(内部の再センタリングは通常どおり行いつつ)常に(0,0)を
    返すよう修正した。修正前後で同一の`SetForegroundWindow`連打シナリオを実際に再現し、
    修正前は毎回Pitchが極端な角度まで飛んでいたのに対し、修正後は2回連続で水平な視点のまま
    安定することをスクリーンショットで確認した。
- Conformance: Matches
- Known deviations: None
- Next action: マウス/キーボードの対話的操作感そのものはユーザー自身の実機確認が必要
  (この環境からは入力を注入できないため)。フォーカス再取得直後の1フレームを常に破棄する
  という対策は、根本原因(`ClipCursor`のOSによる暗黙解除)の正確なタイミングを完全には
  再現・特定できていない状態での防御的な修正であり、より厳密な原因特定が必要になった場合は
  再検討する。

## F-L9-001 — 旧L9手書きゲームループ(削除済み)

- Layer: L9 — Game / Application
- Parent capability: ECS駆動ステージ
- Status: Removed(2026-08、`40f08e4`)
- Verification: NotApplicable(削除前はRuntimeVerified)
- Intended specification: Docs/Decisions/ADR-0016-Ecs-Driven-Stage-And-ThirdPerson-Controller.md
- Removal evidence: 旧L9アプリケーションの`Program.cs`とプロジェクトは`40f08e4`で削除済み。
- Successor: `UserEcsProject/UserEcsProject.csproj`は手書き`Main`を持たず、
  `src/CSharpGameEngine.CodeAnalysis/GameSystemEntryPointEmitter.cs`が
  `GeneratedEntryPoint.Main`と`GeneratedGameLoop.Run`を生成する(F-L7-015)。
- Historical behavior:
  - `Stopwatch`ベースのdelta time計測(スパイク対策で0.1秒にクランプ)、`World.Create<Player>()`/
    `world.Create<StaticProp>()`で作成したエンティティをローカル変数として直接操作する。
  - 削除前の旧実行ファイルを4秒間起動し、スクリーンショットで
    空・地面・キャラクターが意図通り描画されることを確認済み。
- Conformance: NotApplicable(旧実装は存在しない)
- Known deviations: 現行ループは生成コードであり、旧手書きループと同一ではない。
- Next action: なし。後継はF-L7-015で管理する。

## F-L9-002 — ThirdPersonキャラクターコントローラー(`CharacterMotion`)

- Layer: L9 — Game / Application
- Parent capability: ECS駆動ステージ
- Status: Removed(2026-08、`40f08e4`)
- Verification: NotApplicable(削除前はTestVerified)
- Intended specification: Docs/Decisions/ADR-0016-Ecs-Driven-Stage-And-ThirdPerson-Controller.md
- Removal evidence: 旧L9アプリケーションの`PlayerController.cs`と`CharacterMotionTests.cs`は
  `40f08e4`で削除済み。旧実装のコードは残っていない。
- Historical behavior:
  - `CharacterMotion.Compute`はWorld/Window/GPUに一切依存しない純粋関数。カメラ基準の
    WASD移動、重力減算、Y<=0での接地クランプ、接地時のみJump可、を行う。`LerpAngle`は
    最短経路で角度を補間する。
- Conformance: NotApplicable(旧実装は存在しない)
- Known deviations: 衝突判定・本格物理エンジンは無し(Y<=0の簡易接地クランプのみ)。
- Next action: なし。後継はF-L5-006(ADR-0067、`Runtime.Character`)として現行`ISystem`上へ新規実装済み。

## F-L8-001 — `OpenGameCommand`

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: ECS駆動ステージ
- Status: Implemented
- Verification: CodeVerified
- Intended specification: Docs/Decisions/ADR-0016-Ecs-Driven-Stage-And-ThirdPerson-Controller.md
- Implementation evidence:
  - tools/CSharpGameEngine.VSExtension/OpenGameCommand.cs
  - tools/CSharpGameEngine.VSExtension/OpenViewerCommandPackage.vsct
  - tools/CSharpGameEngine.VSExtension/CSharpGameEngineViewerPackage.cs
- Tests: なし(`OpenViewerCommand`と同様、`ProcessStartInfo`+`Process.Start`のみの薄い
  配線コードのため専用テストは無い)。
- Reverse-engineered behavior:
  - `OpenViewerCommand`(ADR-0006)と同一パターンで、Toolsメニューから
    `dotnet run --project UserEcsProject`を別プロセス・別トップレベルウィンドウとして
    起動する。VS本体への埋め込みは行わない。
- Conformance: Matches
- Known deviations: 「ゲームドッキングウィンドウ」という当初の要望に対しては、埋め込みではなく
  別ウィンドウ方式である(ADR-0016「未解決事項」参照、ユーザー確認済みの意図的な選択)。
- Next action: VSドッキングパネルへの本当の埋め込みが必要になった時点で、`SetParent`再親化
  またはDXGI共有テクスチャ方式を再検討する。

## F-L0-005 — `GltfImporter.ImportScene`(ノードグラフ/PBRマテリアル/ライトのインポート)

- M0098: `materials[].extras.CGE_world_grid` is read into `ImportedPbrMaterial.WorldGrid` (invalid values are reported and dropped), and a texture bound to a UV set other than TEXCOORD_0 is reported and left out instead of being sampled through TEXCOORD_0 (the engine has one UV set per vertex). GltfWorldGridImportTests.

- M0089: base-colour/emissive textures use sRGB views, and `KHR_texture_transform` is baked into `TEXCOORD_0` (base colour wins; a differing channel is reported; source tangents are re-derived when the transform rotates or mirrors). GltfTextureTransformImportTests.

- M0088: ImportedLight is color-only with directional lux and point/spot cd variants, nullable range and lossless half-radian cones. GltfImporterSceneTests and UnrealLightSceneEmitterTests verify typed output; zero-inner imported cones need explicit authored resolution.

- Layer: L0 — Platform / Native Backends
- Parent capability: UE5.8 FirstPersonステージ移植
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0017-UE-Level-Export-Pipeline.md、
  Docs/Decisions/ADR-0018-Textured-Stage-And-Ecs-Systems.md、
  Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.md(軸変換、ADR-0017の該当宣言を上書き)
- Implementation evidence:
  - src/CSharpGameEngine.Import/Gltf/GltfImporter.cs(`ImportScene`メソッド)
  - src/CSharpGameEngine.Import/Gltf/ImportedScene.cs
  - src/CSharpGameEngine.Import/Gltf/ImportedSceneNode.cs
  - src/CSharpGameEngine.Import/Gltf/ImportedMeshData.cs
  - src/CSharpGameEngine.Import/Gltf/ImportedPbrMaterial.cs
  - src/CSharpGameEngine.Import/Gltf/ImportedLight.cs
  - src/CSharpGameEngine.Import/Gltf/ImportedLightDirection.cs(ADR-0027で新規)
  - src/CSharpGameEngine.Import/Gltf/GltfAxisConversion.cs(ADR-0027で新規)
  - src/CSharpGameEngine.Import/Gltf/GltfSceneImportResult.cs
- Tests:
  - tests/CSharpGameEngine.Import.Tests/Gltf/GltfImporterSceneTests.cs
- Reverse-engineered behavior:
  - 既存の`Import(string)`(先頭プリミティブのみ)とは独立した追加APIで、`Import`自体は
    無改修。`Scene.VisualChildren`/`Node.VisualChildren`を再帰的に歩き、メッシュまたは
    ライトを持つノードを`ImportedSceneNode`(既に`Node.WorldMatrix`で解決済みの
    ワールド変換)として列挙する。実際のエクスポート結果でトップレベル以外にネストした
    ノードが存在することを確認したため、トップレベルのみではなく全階層を辿る実装にした。
  - 1つのglTFメッシュが複数プリミティブを持つ場合、同じ名前/変換を共有する複数の
    `ImportedSceneNode`に分解する(1描画オブジェクト=1メッシュ+1マテリアルという
    `StageRenderer`側の契約に合わせるため)。
  - TANGENTアクセサが無いプリミティブはLengyel法(UV空間の三角形ごとのタンジェント/
    バイタンジェント蓄積→法線に対するGram-Schmidt直交化→ハンドネス判定)で生成する。
  - `KHR_lights_punctual`の`PunctualLight.LocalDirection`は静的定数(常に(0,0,-1))である
    ことをリフレクションで確認し、実際のワールド方向はノードの`WorldMatrix`から導出する。
  - (ADR-0027追記)`node.WorldMatrix`読み取り直後に`GltfAxisConversion.ConvertWorldTransform`
    (Translation符号反転+Rotationミラー行列共役変換`R'=C·R·C`)を適用し、メッシュローカル
    Positions/Normals/Tangentsも同様に変換、`indices`のWindingを反転する。これにより
    `ImportScene`の公開API出力は全てエンジン(`UnityCoordinateConvention`、左手系)空間になる
    (以前はglTFの生右手系のまま無変換だった)。
- Conformance: Matches
- Known deviations: `Matrix4x4.Decompose`が失敗する退化行列(負スケール等)は無変換のまま
  フォールバックする(ADR-0027参照)。
- Next action: なし。

## F-L3-004 — `World.CollectViews<T>`(削除済み)

- Layer: L3 — ECS / Job Runtime
- Parent capability: UE5.8 FirstPersonステージ移植
- Status: Removed
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0018-Textured-Stage-And-Ecs-Systems.md
- Implementation evidence:
  - src/CSharpGameEngine.Ecs/World.cs(`CollectViews`メソッド)
  - src/CSharpGameEngine.Ecs/ArchetypeStorage.cs(`GetEntity`メソッド)
- Tests:
  - tests/CSharpGameEngine.Ecs.Tests/WorldSpec.cs
- Reverse-engineered behavior:
  - `T`の正規化済みコンポーネント集合に完全一致するアーキタイプストレージのみを対象にし、
    存在しない場合は新規作成せず(`TryGetValue`のみ)何もしない。複数アーキタイプに
    またがるフィルタ・マッチングは行わない(クエリ基盤ではない)。
  - 呼び出す度に渡された`List<T>`へ追記する(内部で新しいリストに置き換えない)。
    `Destroy`によるswap-backが発生した後でも、次回`CollectViews`呼び出し時の列挙結果は
    常に現在のストレージ内容を正しく反映する。
- Conformance: Matches
- Known deviations: None
- Next action: 削除済み(ADR-0029、M0009)。後継はF-L3-009(`EntityQuery`、複数アーキタイプに
  またがる差分スキャン方式のQuery基盤で、当時の「再検討する」次アクションに対応)。

## F-L7-005 — UEレベル取り込みパイプライン

- M0098 / ADR-0075: `M_PrototypeGrid`系の材質は焼き付けない。書き出しがマニフェストの`materialGrids`へパラメーターを記録し(対応外の構成は失敗)、`CSharpGameEngine.Unreal.SceneImporter worldgrid`(`WorldGridGlbConverter`)がGLBの材質を`baseColorFactor`+`extras.CGE_world_grid`へ書き換える(ジオメトリは複製、冪等)。ThirdPersonテンプレートとFirstPersonの両方へ適用済み。`inspect_prototype_grid.py`はUE側の材質の実測用。WorldGridGlbConverterTests。

- Layer: L7 — Static Compiler / Bake
- Parent capability: UE5.8 FirstPersonステージ移植
- Status: Implemented
- Verification: RuntimeVerified
- Intended specification: Docs/Decisions/ADR-0017-UE-Level-Export-Pipeline.md
- Implementation evidence:
  - tools/UnrealExport/export_lvl_firstperson.py
  - tools/UnrealExport/run_export.cmd
  - tools/UnrealExport/README.md
  - UserEcsProject/Assets/Stage/Lvl_FirstPerson/Meshes/*.glb
  - UserEcsProject/Scenes/FirstPersonStageAssets.cs
  - UserEcsProject/Scenes/FirstPersonStageScene.cs
- Tests: なし(UEエディタが無い環境では実行できない開発時専用ツールのため、C#テストスイートの
  対象外。現行のScene DSL生成・描画側は`SceneDslGeneratorEndToEndTests`/
  `RenderServerTests`で検証している)。
- Reverse-engineered behavior:
  - UEレベルを読み取り、StaticMeshActorごとの共有メッシュGLBと、配置・Light・SkyLight・Fog・
    PlayerStartを表すC# Scene DSLを一度だけ生成する。ランタイムは一体型レベルGLBや
    `scene_meta.json`を読まず、生成後はC#を正本としてUEとの同期を行わない。
  - `unreal.GLTFExporter.export_to_gltf`のPythonバインディングは実際には4引数までしか
    受け付けず、戻り値は`GLTFExportMessages`単体になる(ADR-0017参照、実行時エラーから逆算)。
  - 現行受け入れ基準はStaticMeshActor 55体、DirectionalLight 1体、SkyLight/Fog/PlayerStart各1体。
    座標照合用の一体型GLBとmanifestは`.artifacts/UnrealExport/Lvl_FirstPerson/`だけに置き、
    Gitおよびランタイム対象外とする。
- Conformance: Matches
- Known deviations: `ExponentialHeightFogComponent`の色プロパティの厳密な特定は未実施
  (固定のニュートラルグレーで代替、ADR-0017参照)。
- Next action: より大規模なUEレベルを移植する際、World Partitionのセル強制ロード
  (`WorldPartitionEditorSubsystem`)が必要になる可能性がある。

## F-L9-003 — 旧`SceneLoader`とステージスポーン(削除済み)

- Layer: L9 — Game / Application
- Parent capability: UE5.8 FirstPersonステージ移植
- Status: Removed(2026-08、`40f08e4`)
- Verification: NotApplicable(削除前はTestVerified、RuntimeVerified)
- Intended specification: Docs/Decisions/ADR-0017-UE-Level-Export-Pipeline.md、
  Docs/Decisions/ADR-0018-Textured-Stage-And-Ecs-Systems.md、
  Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.md(PlayerStart軸変換)
- Removal evidence: 旧L9アプリケーションの`SceneDocument.cs`、`SceneLoader.cs`、`Program.cs`と
  専用テストは`40f08e4`で削除済み。
- Successor: `UserEcsProject/Scenes/FirstPersonStageScene.cs`の宣言を
  `SceneInstantiateEmitter`がECS生成コードへ変換し、`UserEcsProject/Systems/RuntimeSystem.cs`の
  `LevelLoad`が`StaticMeshSceneData`と`RenderServer`へ接続する。旧JSON loaderとは異なるFeatureである。
- Historical behavior:
  - `GltfImporter.ImportScene`の結果と`scene_meta.json`を合成し、`StageRenderer`入力
    (`StageMeshCreateInfo[]`/`StageMaterialCreateInfo[]`/`StageObjectCreateInfo[]`)と
    ECSスポーン情報(位置/回転/スケール/mobility)を1回の`Load`呼び出しで返す。
  - Mobilityは`scene_meta.json`の`mobilityOverrides`をノード名(アクタラベル)で引き、
    見つからない場合は既定で`"static"`になる。
  - `Program.cs`はロード結果を元に`StageRenderer.Create`→mobility別に`StageProp`/
    `MovableStageProp`をスポーン→`SunLight`/`AmbientLight`/`FogVolume`をスポーン→
    `World.CollectViews`で1回だけビューリストを収集し、以後は毎フレームそのリストを
    再利用する(`CollectViews`を毎フレーム呼び直さない)。
  - 削除前の旧L9アプリケーションを実際に起動し、プロセスが`Responding=True`のまま
    10秒以上生存しCPU時間が蓄積すること(=シーンロード・レンダラー初期化・メインループが
    クラッシュせず実際に動作していること)を確認した。
  - (ADR-0027追記)`TryImportDirectionalLight`の独自`-Vector3.UnitZ`変換を削除し
    `ImportedLightDirection.WorldForward`(`+Vector3.UnitZ`、`WorldTransform`が既にエンジン空間の
    ため)へ統一した。`scene_meta.json`の`PlayerStart`(`export_lvl_firstperson.py`が既に
    UE→glTF変換を適用した値であることをスクリプトのコメントで確認済み)にも
    `GltfAxisConversion.ConvertWorldTransform`と同じ経路を適用し、位置Z成分と純Y軸回転である
    Yaw角の両方を変換する(Yaw角は共役変換の結果、符号が反転することを導出・実測確認した)。
    旧L9アプリケーションを再実行し15秒以上クラッシュしないこと、
    スクリーンショット(BitBlt直接キャプチャ)で空・床・チェッカーグリッド壁が破綻なく
    描画されることを確認した(変更前との比較用スクリーンショットは無いため、鏡像になっていない
    か等の完全な視覚的確証はユーザー自身の実機確認が必要)。
  - (ADR-0029追記)`Program.cs`のスポーンコードを`World.Create<T>()`ファサード代入から
    `world.CreateEntity(archetype)` + `world.GetRef<T>(entity) = value`(F-L3-007)へ書き換えた。
    `StageProp`/`MovableStageProp`という宣言済みアーキタイプ型は無くなり、`EntityArchetype`を
    `world.CreateArchetype<...>()`で都度組み立てる。`SunLight`/`AmbientLight`/`FogVolume`という
    シングルトン用のアーキタイプ型宣言も同様に無くなった。挙動は等価(実機描画確認は
    F-L9-004のADR-0029追記を参照)。
- Conformance: NotApplicable(旧実装は存在しない)
- Known deviations: 現行宣言型Sceneへの視覚的同等性は本Featureでは保証しない。
- Next action: 現行の取り込み経路はF-L7-005、Scene DSL経路はF-L7-012/F-L7-015で管理する。

## F-L9-004 — 旧ECS入力抽象化とゲームプレイSystems群(削除済み)

- Layer: L9 — Game / Application
- Parent capability: UE5.8 FirstPersonステージ移植
- Status: Removed(2026-08、`40f08e4`)
- Verification: NotApplicable(削除前はTestVerified、RuntimeVerified)
- Intended specification: Docs/Decisions/ADR-0018-Textured-Stage-And-Ecs-Systems.md、
  Docs/Decisions/ADR-0028-Ecs-Gameplay-Ruf-Boundary.md(RUF境界配置)
- Removal evidence: 旧L9アプリケーションの`Components.cs`、`Systems/Systems.cs`、旧分割System群と
  専用`PlayerControlSystemTests.cs`は`40f08e4`までに削除済み。
- Successor: `UserEcsProject/Systems/RuntimeSystem.cs`はWindow、LevelLoad、Transform/Light/Audio同期を
  現行`ISystem`契約で実装するが、旧`InputState`/`CharacterMotion`/Jump/PlayerControlは移植していない。
- Historical behavior:
  - `InputState`は独立したシングルトンアーキタイプ(`InputHub`)ではなく`Player`
    アーキタイプに直接持たせた(要件「入力抽象化はプレイヤーに結び付ける」に合わせた設計)。
    `InputSystem.Update`のみが`Win32Window`に触れ、以降の全システムは`InputState`という
    データ構造のみを見る。
  - `PlayerControlSystem`は`InputState`+`CameraRig.Yaw`から移動基準ベクトルを作り、
    既存の`CharacterMotion.Compute`(無改修)に渡す。一人称のため、プレイヤーの
    `Rotation`は常に`CameraRig.Yaw`と一致させる(第三者視点用の「移動方向への
    最短補間回転」ロジックは使わない)。
  - `MovingPropSystem`は経過時間から直接計算する連続回転のみを行い、フレーム間で
    ベースライン位置を記憶する必要が無い(浮動小数点ドリフトが発生しない設計)。
  - システム間にスケジューラは無く、`Program.cs`内の呼び出し順序がそのまま更新順序になる
    (ADR-0016の`CharacterMotion`と同じ設計方針を複数ファイルへ分割しただけ)。
  - (ADR-0028追記)`PlayerControlSystem.Update`のflatForward/flatRight/身体の向きQuaternionと
    `CameraSystem.Update`のEyeHeight加算を`RufVector`/`RufQuaternion`経由の計算に書き換えた
    (`Camera.GetFlatForward`/`GetFlatRight`、ADR-0026と同じ実装パターン)。ECSコンポーネント
    (`Position`/`Rotation`等のバラfloatフィールド)は無改修。`UnityCoordinateConvention`が
    恒等写像のため、既存`PlayerControlSystemTests.cs`は無改修のまま成功する。
  - (ADR-0029追記)全Systemsを`[GameSystem]`静的クラス+`[Update]`静的メソッドへ書き換え、
    手書きの`Update(Player, ...)`/`Update(List<T>, ...)`という呼び出しシグネチャと
    `Program.cs`内の手動呼び出し順序を廃止した(F-L7-009/F-L7-010が代わりにQuery推論・SoA
    ループ・依存順序を生成する)。`InputSystem.Poll`/`PlayerControlSystem.Control`/
    `CameraSystem.SyncCamera`/`MovingPropSystem.Spin`/`RenderSyncSystem.SyncProp`等は全て
    `ref`/`in`パラメータのみから成る独立した静的メソッドになった。`Camera`
    (`CSharpGameEngine.Graphics.D3D12`の素の`struct`)をプレイヤーEntity自身のComponentにした
    (別のシングルトンEntityにすると`CameraSystem`がPlayerとCameraの2Entityにまたがる読み書きに
    なり、1メソッド=1Archetype族のSoAループというモデルに合わないため)。`Player`のComponent数が
    `CreateArchetype`の型引数上限8に達したため、どのシステムからも読み書きされていなかった
    `Velocity`をPlayerの構成から外した(`MovableStageProp`側は上限に余裕があるため残した)。
    `RenderSyncSystem.SyncProp`は`Position`+`Rotation`+`Scale`+`RenderObjectRef`という
    Query条件だけで`StageProp`/`MovableStageProp`の両Archetypeに自然に一致するため、旧実装が
    2つ必要だった`SyncProps`/`SyncMovableProps`は1メソッドに統合された。`wasJumpKeyDown`は
    Program.cs側のループ変数から`InputState.WasJumpKeyDown`フィールドへ移した(各`[Update]`
    メソッドは独立した実行単位で、フレームをまたぐ状態を保持する場所がComponentしか無いため)。
    削除前の旧L9アプリケーションを実行し、W前進キー(`keybd_event`で模擬入力)で
    プレイヤーが移動した後のスクリーンショットで空・タイル張りの床・PBRシェーディングされた
    影付きプロップが破綻なく描画されることを実機確認した(検証手順の詳細はM0009参照。
    マウスルック自体は別途発見した`Win32Window`の未修正の既知の問題によりこの確認では
    無効化して検証、詳細はM0009「未解決事項」参照)。
- Conformance: NotApplicable(旧実装は存在しない)
- Known deviations: 一人称でのマウス/キーボード操作感そのものは自動検証していない
  (ADR-0016から引き継いだ制約)。
- Next action: 入力・キャラクター制御が再び必要になった時点で現行`ISystem`向けFeatureを起票する。

## F-L8-002 — VSIX glTFアウトオブプロセスブリッジ(`ExternalGltfLoader`/`ExternalBridgeProcess`)

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: シーンダブルクリックプレビューとヒエラルキー選択連動
- Status: Implemented
- Verification: TestVerified、RuntimeVerified
- Intended specification: Docs/Decisions/ADR-0019-VSIX-Gltf-OutOfProcess-Bridge.md、
  Docs/Decisions/ADR-0010-VSIX-Fbx-OutOfProcess-Bridge.md(追記: ブリッジ起動の事前ビルド済みexe
  直接化)、Docs/Decisions/ADR-0023-Bridge-Process-Residency.md(常駐サーバー化、F-L8-008参照)
- Implementation evidence:
  - tools/CSharpGameEngine.Import.Gltf.Cli/Program.cs
  - tools/CSharpGameEngine.Import.Gltf.Cli/GltfBridgeDocument.cs
  - tools/CSharpGameEngine.Import.Gltf.Cli/GltfBridgeWorker.cs(ADR-0023で`Program.cs`から抽出)
  - tools/CSharpGameEngine.VSExtension/ExternalBridgeProcess.cs(`BridgeExecutableLocator`含む)
  - tools/CSharpGameEngine.VSExtension/ExternalGltfLoader.cs
  - tools/CSharpGameEngine.VSExtension/BridgeDocument.cs
  - tools/CSharpGameEngine.VSExtension/MeshLoader.cs
- Tests:
  - tests/CSharpGameEngine.VSExtension.Tests/BridgeExecutableLocatorTests.cs(`BridgeExecutableLocator`
    の候補探索・Debug/Release/RIDサブフォルダの優先順位を一時フォルダで検証、実プロセス起動なし)
  - ブリッジCLI自体・`MeshLoader`のキャッシュ挙動は引き続き専用ユニットテスト無し(`Process.Start`
    薄いラッパー/実際のブリッジ起動を伴うため。`dotnet run`実行での実地確認に留める)。
  - 常駐サーバー関連のテストはF-L8-008参照。
- Reverse-engineered behavior:
  - `.glb`/`.gltf`はもともと`SimpleGltfLoader`(SharpGLTF.Core直接参照・in-process)で読んで
    いたが、`.fbx`(`ExternalFbxLoader`、ADR-0010)とは異なる方式だった非対称性をユーザー指摘により
    解消し、両形式とも`ExternalBridgeProcess`経由の同一のアウトオブプロセス方式に統一した。
  - `ExternalFbxLoader`/`ExternalGltfLoader`はどちらも「ブリッジを起動しJSONを読む」処理を
    共有の`ExternalBridgeProcess.RunAndReadJson`に委譲し、デシリアライズ先も共有の`BridgeDocument`
    (`ToSimpleMesh()`変換込み)1つに統合した。
  - `SimpleGltfLoader.cs`は削除、`CSharpGameEngine.VSExtension.csproj`から`SharpGLTF.Core`
    パッケージ参照も除去した(このプロジェクトはもうAssimpNetもSharpGLTF.Coreも直接参照しない)。
  - `dotnet run --project tools/CSharpGameEngine.Import.Gltf.Cli -- assets/TestFixtures/Gltf/Box.glb <tmp>.json`
    を実際に実行し、終了コード0・24頂点/36インデックスの正しいJSONが生成されることを確認した。
  - **(ADR-0010追記)** シーンタブが体感で重いというユーザー報告を受け、`ExternalBridgeProcess.
    RunProcess`が`BridgeExecutableLocator.FindExecutable`でブリッジの事前ビルド済みapphost
    `.exe`(`bin/{Release,Debug}/net10.0[/win-x64]/<フォルダ名>.exe`、最終更新日時が新しいもの)を
    探し、見つかればそれを直接起動するよう変更(未ビルド時のみ`dotnet run --project`に
    フォールバック)。`Box.glb`を対象に実測したところ`dotnet run --project`が約1,985〜2,570ms
    かかったのに対し、直接起動は約152〜178msで、ブリッジ1回あたり約13〜15倍高速化した。
    `MeshLoader`にも絶対パス+最終更新時刻キーの`ConcurrentDictionary`キャッシュ(プロセス
    生存期間)を追加し、`ViewerHwndHost`/`AssetPreviewHwndHost`/`SceneHwndHost`全てが再読込
    コストを避けられるようにした。
  - **(ADR-0023)** `ExternalBridgeProcess.RunProcess`が常駐Named Pipeサーバー経由の起動を
    最優先で試すようになった(詳細・実測値はF-L8-008参照)。`RunAndReadJson`は
    `useResidentServer`引数が必須になり、`ExternalGltfLoader`は`true`を指定する。
- Conformance: Matches
- Known deviations: None
- Next action: VS実機で「シーンタブ・シェイプパレット追加時の体感速度が実際に改善したか」を
  ユーザーに確認してもらう(ビルド・テスト・実測タイミングは確認済みだが、ライブVSでの体感は
  未確認)。

## F-L2-001 — `CSharpGameEngine.Scene`(`SceneNode`/`Transform`/`SceneGraph`/`SceneNodeTree`)

- Layer: L2 — Type Schema / Identity
- Parent capability: シーンダブルクリックプレビューとヒエラルキー選択連動
- Status: **Removed**(ADR-0034 / M0014)
- Verification: N/A
- Intended specification: Docs/Decisions/ADR-0020-Scene-Preview-And-Hierarchy-Selection-Sync.md、
  Docs/EditorSpec.md §3(データモデル。旧仕様書、2026-08削除。後継:
  `Docs/シーンエディタの仕様.md` / [[ADR-0041-Scene-Editor-Interaction-Architecture]])
- Removal rationale: Scene Authoringが[[ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth]]/
  [[ADR-0031-Scene-Definition-3D-Preview]]でC#正本方式(`[SceneDefinition]`+`GameSystemGenerator`)へ
  移った後、このJSON方式実装には**生きた利用者が1つも残らなかった**(参照は自分自身のテストのみ)。
  [[ADR-0034-Project-Consolidation]]で`src/CSharpGameEngine.Scene`と
  `tests/CSharpGameEngine.Scene.Tests`をディレクトリごと削除した。
- Superseded by: F-L3-xxx系のScene Authoring(C#正本)。VSIXが`*.scene`を読むパーサーは
  `tools/CSharpGameEngine.VSExtension/SceneFile.cs`としてVSIX内に存続していたが、M0020 Phase 0で
  削除した(読み取り経路はライブRoslyn`SceneModel`へ完全移行済み、F-L8-010)。当時の記録として(F-L8-003)、
  エディタ機能そのものは失われていない。
- Implementation evidence(削除済み。履歴はgitの`f17fbd5`以前を参照):
  - ~~src/CSharpGameEngine.Scene/{Transform,SceneNode,SceneGraph,SceneNodeTree,SceneJson}.cs~~
  - ~~旧L9アプリケーションの`DefaultScene.scene`~~(`40f08e4`で削除)
  - ~~旧L9アプリケーションの`Assets/DefaultScene/Box.glb`~~(`40f08e4`で削除)
- Tests: 8件のうち7件は削除対象そのもののテストなのでカバレッジ損失なし。残る1件
  (出荷されていた旧`DefaultScene.scene`を読む唯一のテスト)も`40f08e4`で削除済み:
  - ~~tests/CSharpGameEngine.VSExtension.Tests/DefaultSceneFileTests.cs~~
- Reverse-engineered behavior(削除時点の記録として保持):
  - EditorSpec §3のJSONスキーマ(`schemaVersion`/`nodes[].{id,name,parentId,transform,
    meshAssetPath,components}`)と完全一致する往復シリアライズを、`StableId`/`Vector3`/
    `Quaternion`用の`JsonConverter`を書かず素の文字列/`float[]`との明示的な相互変換で実装。
  - 型名は仕様書の`Scene`ではなく`SceneGraph`(名前空間`CSharpGameEngine.Scene`の末尾
    セグメントと同名になることを避けるため)。
  - `SceneNodeTree.Build`は「到達済みノード数 != 全ノード数」を循環参照/解決不能参照の検出条件に
    使う - 各ノードの親は高々1つなので、真の循環を構成するノード群はルートとして拾われず
    構造的に到達不能になるという性質を利用している。
  - 旧`DefaultScene.scene.json`はRoot+`Cube_A`/`Cube_B`(いずれも
    `Assets/DefaultScene/Box.glb`を参照、X軸上で左右に配置)の最小3ノードシーン。
    `assets/TestFixtures/Gltf/Box.glb`のコピーを新規アセットとして追加。
- Conformance: N/A(削除済み)
- Known deviations: 旧L9アプリケーションから`CSharpGameEngine.Scene`への`ProjectReference`は
  ついに追加されなかった。ランタイム統合はEditorSpec Phase 4のスコープだったが、その前に
  Scene AuthoringがC#正本方式へ移ったため、このJSON実装は一度も本番経路に載らずに終わった。
- Next action: 無し。(2026-08追記)旧EditorSpec Phase 2/Phase 4相当のランタイム統合は、その後
  `IScene`/`[StartupScene]`方式(ADR-0038/0040)として実装済み。EditorSpec.md自体も2026-08に削除し、
  生き残り項目は[[M0020-Scene-Editor-Interaction]]へ移設した。

## F-L8-003 — シーンダブルクリックプレビューとヒエラルキー選択連動

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: シーンダブルクリックプレビューとヒエラルキー選択連動
- Status: Removed(ADR-0040 Phase 9 / M0020 Phase 0)
- Verification: NotApplicable(削除前はTestVerified / RuntimeVerified)
- Intended specification: Docs/Decisions/ADR-0020-Scene-Preview-And-Hierarchy-Selection-Sync.md
- Removal evidence:
  - 削除済み: `SceneFile.cs`、`SceneFileEditorFactory.cs`、`SceneWindowPane.cs`、
    `SceneWindowControl.xaml(.cs)`、`SceneFileTests.cs`
  - 後継F-L8-010/F-L8-011で存続・再利用:
  - tools/CSharpGameEngine.VSExtension/SceneHwndHost.cs
  - tools/CSharpGameEngine.VSExtension/SceneRenderer.cs
  - tools/CSharpGameEngine.VSExtension/SceneEditorHub.cs
  - tools/CSharpGameEngine.VSExtension/HierarchyToolWindowPane.cs
  - tools/CSharpGameEngine.VSExtension/HierarchyToolWindowControl.xaml(.cs)
  - tools/CSharpGameEngine.VSExtension/ShowHierarchyToolWindowCommand.cs
  - tools/CSharpGameEngine.VSExtension/CSharpGameEngineViewerPackage.cs
  - tools/CSharpGameEngine.VSExtension/OpenViewerCommandPackage.vsct
- Historical behavior:
  - `SceneFileEditorFactory`/`SceneWindowPane`/`SceneHwndHost`は`GlbFbxPreviewEditorFactory`/
    `AssetPreviewWindowPane`/`AssetPreviewHwndHost`(ADR-0013)と構造的に同一のDocView==DocData
    パターン。VSIXはnet10.0の`CSharpGameEngine.Scene`を参照できないため(TFM非互換)、
    `SceneFile.cs`という独立したVSIX専用パーサーを持つ(F-L2-001とは別実装)。
  - `SceneRenderer`は`VorticeRenderer`(単一メッシュ)を改修せず新規に追加した複数オブジェクト
    対応レンダラー。オブジェクトごとに頂点/インデックスバッファ、定数バッファは256バイト
    アライン済みスロットをオブジェクト数分持つ1つのアップロードバッファ。選択ハイライトは
    `Selected`定数によるオレンジ系タイント加算のみ(移動/回転/スケールのギズモではない)。
  - `SceneEditorHub`(static)が`SceneHwndHost`(シーンオープン時に`PublishSceneOpened`)と
    `HierarchyToolWindowControl`(`SceneOpened`購読・`TreeView.SelectedItemChanged`で
    `PublishSelection`)を仲介する。選択連動はヒエラルキー→シーンの一方向のみ。
  - `HierarchyToolWindowPane`の表示タイミングはADR-0021で「常時表示」から「初回起動時のみ
    自動表示、以後はVS標準の前回状態復元に任せる」に変更された(`GameEngineLayout`、
    F-L8-005参照)。
  - **(ADR-0010追記)** `SceneHwndHost.LoadIntoSurface`は元々ノードを1つずつ順に`MeshLoader.Load`
    していたが、シーンタブの体感速度問題(ADR-0010追記、F-L8-002参照)を受け、重複排除した
    メッシュパス集合を`MeshLoader`(プロセス生存期間キャッシュ導入済み)経由で並列に事前ロード
    してから描画オブジェクトを組み立てるよう変更した。並列待機は`Task.WhenAll(...).GetAwaiter()
    .GetResult()`ではなく`ThreadHelper.JoinableTaskFactory.Run(...)`を使用- `LoadIntoSurface`は
    常にUIスレッドで呼ばれるため、生の同期待機はVSTHRD002ビルドエラーになるだけでなく、本ADR
    (0021追記)で実際に踏んだのと同種のデッドロックを再発させかねない。
  - **(ADR-0031追記)** `LoadIntoSurface`の`MeshAssetPath is not null`フィルタを、メッシュ有り→
    `MeshLoader.Load`/メッシュ無し→`GizmoMeshFactory.Create()`(F-L8-009)という分岐へ拡張した。
    メッシュを持つノードの挙動は完全に不変。副作用として、メッシュ0件のシーンが従来の
    `ArgumentException`即エラーではなくギズモ表示になる(退行ではなく改善)。
- Conformance: NotApplicable(`.scene`ダブルクリック経路は存在しない。現行経路はF-L8-010/F-L8-011)
- Known deviations: **ADR-0021で判明・修正済み**: `.scene.json`という複合拡張子は
  `ProvideEditorExtension`で単一の拡張子として認識されず、ダブルクリックしても何も開かない
  不具合だった(実機検証で確認)。拡張子を単一の`.scene`に変更して解決した(スキーマ自体は
  無変更)。
- Next action: なし。ライブRoslyn SceneModel経路へ移行済み。
- **(2026-08、ADR-0040 Phase 9で置き換え)** `.scene`ダブルクリックによるドキュメントタブ方式の
  プレビューは廃止した。`SceneFileEditorFactory`/`SceneWindowPane`/`SceneWindowControl`は削除、
  `SceneHwndHost`/`SceneRenderer`は流用しつつデータソースを`.scene`ファイルからライブRoslynの
  `SceneModel`へ差し替えた。後継はF-L8-010を参照。このエントリは歴史的記録として残す。
- **(2026-08、M0020 Phase 0で完全撤去)** 上記時点では「`SceneFile.cs`のパース自体は
  `SceneNodeAdder`の書き込み側でPhase 10まで存続」と記録していたが、Phase 10を待たず
  `SceneFile.cs`/`SceneNodeAdder.cs`/`SceneFileTests.cs`ごと削除した(シェイプパレットと
  ヒエラルキーのドラッグ&ドロップも同時に撤去、F-L8-006/F-L8-007参照)。したがってこのエントリの
  旧`.scene`専用ファイルは現存しない
  (`SceneFileEditorFactory.cs`/`SceneWindowPane.cs`/`SceneWindowControl.xaml(.cs)`はPhase 9時点で、
  `SceneFile.cs`はPhase 0時点で削除)。`SceneHwndHost`/`SceneRenderer`/`SceneEditorHub`/
  Hierarchy関連ファイルはライブRoslyn経路へ転用され、現存する。リポジトリ内に`*.scene`ファイルは無い。

## F-L2-002 — `CSharpGameEngine.Geometry`(`ProceduralMeshFactory`・6プリミティブ追加)

- Layer: L2 — Type Schema / Identity
- Parent capability: GameEngineレイアウトとシェイプパレット
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0021-Editor-Layout-And-Shape-Palette.md、
  Docs/Decisions/ADR-0026-Camera-Geometry-Ruf-Integration.md(RUF内部実装統合)
- Implementation evidence:
  - src/CSharpGameEngine.Core/Geometry/ProceduralMeshData.cs
  - src/CSharpGameEngine.Core/Geometry/ProceduralMeshFactory.cs
- Tests:
  - tests/CSharpGameEngine.Core.Tests/Geometry/ProceduralMeshFactoryTests.cs
- Reverse-engineered behavior:
  - `src/CSharpGameEngine.Graphics.D3D12/ProceduralMeshFactory.cs`(`CreatePlane`/`CreateBox`のみ)
    から移設。移設前の時点で`Graphics.D3D12`本体のどこからも参照されておらず(唯一の呼び出し元は
    `StageGpuIntegrationTests.cs`)、`Graphics.D3D12`への`ProjectReference`は追加せず
    当時の`tests/CSharpGameEngine.Graphics.D3D12.Tests.csproj`から直接参照する「テスト専用エッジ」
    とした(同csprojの`Import.Gltf`直接参照と同じ前例パターン)。
  - (ADR-0034追記)上記の**テスト専用エッジは解消した**。`Geometry`が
    `CSharpGameEngine.Core`に統合され、`Graphics.D3D12`が属する`CSharpGameEngine.Windows`は
    もともと`Core`を参照しているため、`StageGpuIntegrationTests.cs`(現
    `tests/CSharpGameEngine.Windows.Tests/Graphics/`)は追加の参照なしに
    `ProceduralMeshFactory`へ到達できる。名前空間`CSharpGameEngine.Geometry`は無変更。
  - `CreateSphere`は極を単一頂点にしてファンする方式(素朴なUV球のように極の列を複製して
    quad化する方式ではない)で実装しており、これにより退化三角形が構造的に発生しない。
    `CreateCylinder`/`CreateCone`/`CreateDisk`は共有`AddRing`/`AddQuadBand`ヘルパーで
    リング状の頂点列とバンドの三角形分割を統一的に処理する。`CreateCapsule`は半球リング生成
    (`AddHemisphereRings`)+円柱側面+半球リングという3段構成。
  - 全8種(Box/Plane含む)についてretry`tests/CSharpGameEngine.Core.Tests`で頂点/インデックス
    範囲・法線単位長・退化三角形の不在(三角形面積>1e-6の実測値チェック)を検証、30件全て初回
    実行で成功。
  - (ADR-0026追記)全8種の頂点/法線計算を`RufVector`ベースへ書き換え、
    `Spatial<UnityCoordinateConvention>.ToXyz`で出力直前に`Vector3`へ変換する構成にした。
    `UnityCoordinateConvention`が恒等写像であるため、既存30件のテスト(法線の厳密一致チェック含む)
    が無改修のまま成功することを確認済み。
- Conformance: Matches
- Known deviations: None
- Next action: サイズ/セグメント数のパラメータ化(現在は固定値)が必要になった時点で再検討する
  (Inspectorパネル実装時が候補)。

## F-L8-005 — GameEngine既定レイアウト(`GameEngineLayout`/`ApplyGameEngineLayoutCommand`)

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: GameEngineレイアウトとシェイプパレット
- Status: Implemented
- Verification: TestVerified(ビルド・VSCTコンパイルのみ。実機でのレイアウト適用は未確認)
- Intended specification: Docs/Decisions/ADR-0021-Editor-Layout-And-Shape-Palette.md
- Implementation evidence:
  - tools/CSharpGameEngine.VSExtension/GameEngineLayout.cs
  - tools/CSharpGameEngine.VSExtension/ApplyGameEngineLayoutCommand.cs
  - tools/CSharpGameEngine.VSExtension/CSharpGameEngineViewerPackage.cs
  - tools/CSharpGameEngine.VSExtension/OpenViewerCommandPackage.vsct
- Tests: なし(VS UIそのものへの副作用が中心のため、既存の類似コマンド群と同様専用テストは無い)。
- Reverse-engineered behavior:
  - VS SDKには名前付きウィンドウレイアウト(「ウィンドウ > ウィンドウ レイアウトの適用」)を
    登録する公開APIが存在しないことを実装前に調査確認した(実装は`Microsoft.VisualStudio.Platform.WindowManagement.dll`
    という非公開の内部アセンブリの`internal ILayoutManagementServiceInternal`にのみ存在)。
    そのため独自のTools-menuコマンド`ApplyGameEngineLayoutCommand`で代替した。
  - `[ProvideToolWindow]`属性の`Style`/`Window`/`Orientation`/`DockedWidth`は、そのVSハイブで
    ツールウィンドウが初めて生成される時だけ効く既定値であり、以後はVSが`.winprf`に保存した
    実レイアウトが優先される(実機のExpハイブの`.winprf`で確認済み)。そのため
    `IVsWindowFrame.SetFramePos`(`VSSETFRAMEPOS.SFP_fDock | SFP_fDockLeft`/`SFP_fDockRight`、
    `ToolWindowGuids80.SolutionExplorer`基準)による実行時の強制配置を併用する。
  - `.vsct`にツールバーを既定表示させる宣言的フラグは存在しない(`DefaultDocked`はドッキング/
    フローティングの区別であって表示/非表示ではない)。`EnvDTE.DTE.CommandBars`
    (`System.Object`で返るため`Microsoft.VisualStudio.CommandBars.CommandBars`への明示キャストが
    必要)を経由した`CommandBar.Visible = true`のみが機能する。
  - 初回起動時のみ適用する仕組みは`SVsSettingsManager`のユーザースコープ設定ストア
    (コレクション`CSharpGameEngine`、プロパティ`InitialLayoutApplied`)で判定する。2回目以降は
    何もせず、VS標準の「前回開いていた状態を復元」に委ねる。
  - **ADR-0021追記で修正**: 当初`GameEngineLayout.ApplyAsync`(`ShowToolWindowAsync`でのツール
    ウィンドウ初回生成を含む)を`TryAutoOpenUserEcsProjectAsync`からインライン`await`で
    呼んでおり、`InitializeAsync`自身の非同期チェーンにネストされたまま実行されていたため、
    ユーザーの実機F5デバッグで実際にデッドロックした(`WindowManagerService.CreateContentPane`の
    内部同期待機が`InitializeAsync`自身の完了待ちで自己参照)。修正として
    `TryApplyInitialLayoutAsync`を独立メソッドに分離し、`InitializeAsync`から
    `JoinableTaskFactory.RunAsync(...)`で**待機せず**起動するよう変更、ユーザーが実機で動作
    確認済み。
- Conformance: Matches(初回起動時のデッドロックはユーザーの実機確認により解消を確認済み。
  ツールバー表示・パネル既定配置そのものの見た目は未確認)。
- Known deviations: ツールバー表示・パネル既定配置の見た目そのものの実機確認が未実施
  (デッドロックの解消は確認済みだが、それとは別の確認項目)。
- Next action: VS実験用インスタンスで初回起動時のツールバー表示・パネル配置の見た目と
  「GameEngine レイアウトを適用」コマンドの動作を実機確認する。

## F-L8-006 — シェイプパレット(`ShapePaletteToolWindowPane`/`PrimitiveSpawner`)

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: GameEngineレイアウトとシェイプパレット
- Status: **Removed**(M0020 Phase 0)
- Verification: N/A(削除済み。削除前の最終状態はRuntimeVerifiedだったが、ADR-0040 Phase 9以降は
  下記のとおり機能しなくなっていた)
- Intended specification: Docs/Decisions/ADR-0021-Editor-Layout-And-Shape-Palette.md
- Removal rationale: 配置先が`.scene`ファイルであり、ADR-0040 Phase 9で読み取り経路がライブRoslynの
  `SceneModel`へ移った時点で`SceneEditorHub.ActiveScene`の発行元が消滅した。以後このパレットは
  押しても「シーンファイル(.scene)を開いてください」という案内ダイアログを出すだけのUIであり、
  ユーザー決定によりPhase 10を待たず全面削除した。**プリミティブ生成そのものは
  `tools/CSharpGameEngine.Primitive.Cli`として存続**し、Scene デザイナーの`PrimitiveMesh`描画
  (`SceneModelRenderObjectBuilder.ResolveViaBridge`)が利用している。
- Superseded by: 宣言の直接編集(`SceneEditOperation`、M0020 Phase 3以降)+ ヒエラルキー編集
  マイルストーン。
- Implementation evidence(削除済み。履歴はgitの`e2033ed`以前を参照):
  - tools/CSharpGameEngine.Primitive.Cli/Program.cs(**存続** — 描画側が利用)
  - ~~tools/CSharpGameEngine.VSExtension/ShapePaletteToolWindowPane.cs~~
  - ~~tools/CSharpGameEngine.VSExtension/ShapePaletteControl.xaml(.cs)~~
  - ~~tools/CSharpGameEngine.VSExtension/ShowShapePaletteToolWindowCommand.cs~~
  - ~~tools/CSharpGameEngine.VSExtension/PrimitiveSpawner.cs~~
  - ~~tools/CSharpGameEngine.VSExtension/SceneNodeAdder.cs~~(ADR-0022で`PrimitiveSpawner`から抽出)
  - tools/CSharpGameEngine.VSExtension/ExternalBridgeProcess.cs(`RunProducingFile`は**存続** —
    消費者が`SceneModelRenderObjectBuilder.ResolveViaBridge`へ移った)
  - ~~tools/CSharpGameEngine.VSExtension/SceneFile.cs~~
  - tools/CSharpGameEngine.VSExtension/SceneEditorHub.cs(`ReloadRequested`イベントを削除)
  - tools/CSharpGameEngine.VSExtension/OpenViewerCommandPackage.vsct(コマンドID`0x0107`を退役 —
    `0x0101`/`0x0108`と同じ作法でコメントのみ残し、IDは再利用しない)
  - tools/CSharpGameEngine.VSExtension/InitialLayoutToolWindowPlan.cs、GameEngineLayout.cs、
    CSharpGameEngineViewerPackage.cs(初回レイアウトからシェイプパレットの列挙子・型マップ・
    `[ProvideToolWindow]`登録を削除)
- Tests: なし(VSIX側のクリックハンドラ自体は専用テストが無い - `SceneFile`の`Save`往復は
  F-L2-001/F-L8-003のテストでカバー)。
- Reverse-engineered behavior:
  - 8ボタン(ボックス・球形・シリンダー・カプセル・コーン・トーラス・矩形・ディスク)のみ
    `Click`ハンドラを持ち、残り10ボタンは`IsEnabled="False"`のプレースホルダー
    (対話的ギズモ操作が前提でEditorSpec §6のスコープ外。EditorSpec.mdは2026-08に削除され、
    ギズモ操作は[[ADR-0041-Scene-Editor-Interaction-Architecture]] / M0020のスコープになった)。
  - `PrimitiveSpawner.SpawnAsync`: `UserEcsProject/Assets/Primitives/<Name>.glb`が無ければ
    `tools/CSharpGameEngine.Primitive.Cli`をアウトオブプロセスで起動して生成(2回目以降は
    キャッシュされたファイルを再利用)、`SceneEditorHub.ActiveScene`が無ければ案内メッセージを
    出して終了、新規ノードを`SceneFile.AddNode`+`Save`で書き戻し、
    `SceneEditorHub.RequestReload`で「シーン」タブとヒエラルキーの再描画をトリガーする。
    シーンJSONスキーマ自体は無変更 - スポーンされたプリミティブは他の`meshAssetPath`付き
    ノードと区別されない通常のシーンノードとして扱われる。
  - net472の`Path`には`GetRelativePath`が存在しない(.NET Core 2.0+/netstandard2.1専用のため)
    ことをビルドエラーで確認し、`Uri.MakeRelativeUri`を使った旧来の.NET Framework向け
    ワークアラウンドで代替した。
  - VS threading analyzer(VSTHRD010)が`ThreadHelper.ThrowIfNotOnUIThread()`の明示呼び出しを
    要求することを実際のビルドエラーで確認し、`SetStatusBarText`等のCOM呼び出しを行う各所に
    追加した。
- Conformance: N/A(削除済み)。削除前は「ユーザーの実機確認により一連の流れが動作」だったが、
  ADR-0040 Phase 9以降は書き込み先を失って恒久エラーだった。
- Known deviations: N/A(削除済み)。
- Next action: なし。プリミティブのクリック配置を復活させる場合は`SceneEditOperation`ベースで
  再設計する([[ADR-0041-Scene-Editor-Interaction-Architecture]]、ヒエラルキー編集マイルストーン)。

## F-L8-007 — ヒエラルキーへのドラッグ&ドロップ配置(`SceneNodeAdder`)

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: ヒエラルキーへのドラッグ&ドロップ配置(旧EditorSpec Phase 2前半。
  EditorSpec.mdは2026-08削除、生き残り項目は[[M0020-Scene-Editor-Interaction]]へ移設)
- Status: **Removed**(M0020 Phase 0)
- Verification: N/A(削除済み。削除前もTestVerified=ビルドのみで、ソリューションエクスプローラーから
  の実ドラッグ操作は一度も実機確認されないまま終わった)
- Intended specification: Docs/Decisions/ADR-0022-Hierarchy-Drag-Drop-Asset-Placement.md
- Removal rationale: F-L8-006と同一の理由。書き込み先の`.scene`と`SceneEditorHub.ActiveScene`の
  発行元がADR-0040 Phase 9で消滅し、ドロップしても常に案内ダイアログを出すだけだった。
  `AllowDrop`ごと削除したので「ドロップしても何も起きない」ではなく「ドロップ自体を受け付けない」
  状態になる(機能的損失はゼロ、UIアフォーダンスの消失のみ)。`SceneNodeAdder.GetRelativePath`
  (net472用の`Path.GetRelativePath`ポリフィル)も他の消費者が無いため一緒に落とした — ヒエラルキー
  編集マイルストーンで必要になったら書き直す。
- Superseded by: `SceneEditOperation`ベースのソース挿入(ヒエラルキー編集マイルストーン)。
- Implementation evidence(削除済み。履歴はgitの`e2033ed`以前を参照):
  - ~~tools/CSharpGameEngine.VSExtension/SceneNodeAdder.cs~~
  - tools/CSharpGameEngine.VSExtension/HierarchyToolWindowControl.xaml(.cs)(`AllowDrop`と
    `DragEnter`/`DragOver`/`Drop`束縛、および対応する5メソッドを削除。TreeView本体は存続)
  - ~~tools/CSharpGameEngine.VSExtension/PrimitiveSpawner.cs~~
- Tests: なし(削除前も専用テストは無く、`SceneFile`往復テスト経由の間接カバーのみだった)。
- Reverse-engineered behavior:
  - `PrimitiveSpawner`(ADR-0021)が持っていた「現在開いているシーンにノードを追加・保存・
    再読み込みトリガー」ロジックを`SceneNodeAdder.TryAddAssetNode`として抽出し、
    `PrimitiveSpawner`と本機能の両方が共有する。
  - `HierarchyToolWindowControl`の`TreeView`に`AllowDrop="True"`+`DragEnter`/`DragOver`/`Drop`を
    追加。VSのソリューションエクスプローラーからのドラッグは`System.Windows.DataFormats.FileDrop`
    (標準`CF_HDROP`)で実ファイルパスを受け取れる想定(多くのVS拡張が依存する一般的パターン、
    ただし本プロジェクトでは実機未確認)。拡張子が`.glb`/`.gltf`/`.fbx`のいずれでもない場合は
    `DragDropEffects.None`でドロップを拒否する。
  - シーンJSONスキーマは無変更 - ドラッグ配置されたノードもシェイプパレット由来のノードと
    区別されない通常のノードとして扱われる。位置は原点固定、親はシーンのルートノード。
- Conformance: N/A(削除済み)。
- Known deviations: N/A(削除済み)。削除時点でも「ソリューションエクスプローラーからの実ドラッグで
  `DataFormats.FileDrop`が得られるか」は未検証のままだった — 再実装時に改めて確認が必要。
- Next action: なし。再実装は`SceneEditOperation`ベースで行う
  ([[ADR-0041-Scene-Editor-Interaction-Architecture]]、[[M0020-Scene-Editor-Interaction]]の
  「意図的に除外したこと」)。

## F-L8-008 — ブリッジプロセスの常駐化(`CSharpGameEngine.Bridge.Hosting`/`ResidentBridgeClient`)

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: シーンダブルクリックプレビューとヒエラルキー選択連動
- Status: Implemented
- Verification: TestVerified、実測タイミング検証済み(RuntimeVerifiedはVS実機確認待ち)
- Intended specification: Docs/Decisions/ADR-0023-Bridge-Process-Residency.md
- Implementation evidence:
  - tools/CSharpGameEngine.Bridge.Hosting/BridgeServerHost.cs
  - tools/CSharpGameEngine.Bridge.Hosting/BridgeWireMessage.cs
  - tools/CSharpGameEngine.Import.Gltf.Cli/Program.cs、GltfBridgeWorker.cs
  - tools/CSharpGameEngine.Import.Fbx.Cli/Program.cs、FbxBridgeWorker.cs(`SemaphoreSlim`による
    AssimpNetの`LogStream`排他制御含む)
  - tools/CSharpGameEngine.Primitive.Cli/Program.cs、PrimitiveBridgeWorker.cs
  - tools/CSharpGameEngine.VSExtension/ResidentBridgeClient.cs
  - tools/CSharpGameEngine.VSExtension/ExternalBridgeProcess.cs(3段フォールバックの調停役)
- Tests:
  - tests/CSharpGameEngine.Bridge.Hosting.Tests/BridgeWireMessageTests.cs(`MemoryStream`相手の
    フレーミング往復)
  - tests/CSharpGameEngine.Bridge.Hosting.Tests/BridgeServerHostTests.cs(実際のNamed Pipe、
    同一プロセス内・子プロセス起動なし: 単発往復・8件並行リクエストの無混線・ハンドラ例外後の
    継続動作・アイドルタイムアウトでのループ終了)
  - tests/CSharpGameEngine.VSExtension.Tests/ResidentBridgeClientTests.cs(`ComputePipeName`の
    決定性・repoRoot/ブリッジ/所有者違いでの別名、`--parent-process-id`付きの起動引数)
  - tests/CSharpGameEngine.Bridge.Hosting.Tests/BridgeServerHostTests.cs(2026-10-01: 所有者の
    終了・最初から存在しない所有者で、アイドルタイムアウトを待たずに終了する。`--serve`の引数解釈)
- Reverse-engineered behavior:
  - 1リクエスト=1接続(パイプの多重化なし)。`BridgeServerHost`は接続ごとに独立した`Task`へ
    ディスパッチし、`SceneHwndHost`の並列メッシュロードにそのまま対応できる。
  - `ResidentBridgeClient`は`--serve`常駐サーバーへの接続を最初に試み、失敗時のみ`Mutex`
    (`Global\...`)経由で新規起動する。パイプ名は`ComputePipeName`が`repoRoot`+ブリッジ
    フォルダ名+所有者のVSのプロセスIDからSHA256で決定的に生成する(`GetHashCode()`はプロセス
    ごとにランダム化されるため使用不可)。常駐ブリッジは`--parent-process-id`の所有者が終了すると
    終わる(拡張フォルダーをロックしたまま残らない、ADR-0023追記)。
  - **設計変更を要した実機検証結果**: 応答待機に当初`ThreadHelper.JoinableTaskFactory.Run`
    (VS threading analyzer VSTHRD002が推奨する、Task同期待機の標準的な回避策)を使ったところ、
    xunitテストから実行した際に毎回約10秒かかり常駐パスが常に失敗してtier2(直接exe起動)に
    フォールバックしていることが実測で判明した - VSホストが存在しない環境では
    `JoinableTaskFactory`が正しく機能しなかった。生の`Thread`+`ManualResetEventSlim`による
    待機に置き換えて解決した(VSTHRD002の対象にもならず、VSホスト依存も無い)。
  - `FbxImporter.Import`がAssimpNetの`LogStream.Attach`/`Detach`(プロセス全体で共有される
    ネイティブ状態)を呼び出しているため、`FbxBridgeWorker`のみ`SemaphoreSlim(1, 1)`で
    インポート呼び出し自体を排他制御している。
  - 実測(`Box.glb`、`ExternalBridgeProcess.RunAndReadJson`を同一プロセス内で3回連続呼び出し):
    1回目(コールド、サーバー新規起動)約222ms、2回目(再利用)約2ms、3回目(再利用)約1ms -
    ADR-0010追記の直接exe起動(~150-180ms)と比べてさらに約100〜150倍、当初の
    `dotnet run --project`(~2,000-2,570ms)と比べて約1,000〜2,000倍高速化した。
  - 常駐サーバーが1つも起動していない状態から6件の呼び出しを完全並行実行したところ、全件成功し
    生成された常駐サーバープロセスは1つだけだった(`Mutex`による二重起動防止が実際に機能する
    ことを確認)。
- Conformance: Matches
- Known deviations: None
- Next action: VS実機でシーンタブ・シェイプパレットの体感速度を確認してもらう
  (Next actionはF-L8-002と共通)。`Import.Fbx.Cli`の常駐化除外は「一行で切り替え可能」な形の
  まま保留してあり、実運用でクラッシュ頻度が問題になった場合に改めて検討する。

## F-L3-005 — `AosEntityStore<TRow>` / `EntityAllocator`(削除済み)

- Layer: L3 — ECS / Job Runtime
- Parent capability: Generated Entity Storage(AoS）
- Status: Removed
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0024-Generated-Entity-AoS-SoA.md、
  Docs/Specifications/Generated-Entity-Storage.md
- Implementation evidence:
  - src/CSharpGameEngine.Ecs/AosEntityStore.cs
  - src/CSharpGameEngine.Ecs/EntityAllocator.cs
- Tests:
  - tests/CSharpGameEngine.Ecs.Tests/AosEntityStoreSpec.cs
  - tests/CSharpGameEngine.Ecs.Tests/EntityAllocatorSpec.cs
- Reverse-engineered behavior:
  - `AosEntityStore<TRow>`は`TRow[]`の密配列+`Entity[]`+Location Table(`int[]`、`Entity.Id`で
    Index)を持ち、`Entity`は行配列の直接Indexとして使わない。削除はswap-backで行い、
    移動したEntityのLocationだけを更新する。
  - `EntityAllocator`は`World`の既存private確保ロジック(free-list+Generation増分)と同じ
    考え方だが、意図的に別実装として複製している(`World`の既存挙動への回帰リスクを避けるため)。
- Conformance: Matches
- Known deviations: None
- Next action: 削除済み(ADR-0029、M0009)。SoA一本化に伴いAoSバックエンドの選択肢自体が
  無くなった。後継はF-L3-007(`ComponentColumn<T>`によるSoA列)。

## F-L3-006 — `World.TryGetArchetypeChunk<T>` / `ArchetypeChunk`(削除済み)

- Layer: L3 — ECS / Job Runtime
- Parent capability: Generated Entity Storage(SoA）
- Status: Removed
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0024-Generated-Entity-AoS-SoA.md、
  Docs/Specifications/Generated-Entity-Storage.md
- Implementation evidence:
  - src/CSharpGameEngine.Ecs/World.cs(`TryGetArchetypeChunk<T>`)
  - src/CSharpGameEngine.Ecs/ArchetypeChunk.cs
  - src/CSharpGameEngine.Ecs/ArchetypeStorage.cs(`GetEntitiesSpan`/`GetColumnSpan<T>`)
- Tests:
  - tests/CSharpGameEngine.Ecs.Tests/ArchetypeChunkSpec.cs
- Reverse-engineered behavior:
  - 既存の`CollectViews<T>`と同じく、正規化済み構成に完全一致する1アーキタイプだけを返す
    (複数アーキタイプ横断のQueryではない)。該当アーキタイプのEntityが1件も無ければ`false`を返す。
  - このエンジンのSoAストレージはページ化されたChunkを持たないため、1つの`ArchetypeStorage`
    全体がそのまま1つの`ArchetypeChunk`として返る(物理的な複数Chunk分割は無い)。
  - 正規化後のComponent集合が同じ異なる`IEcsArchetype<T>`型は、`ArchetypeSignature`の
    `Type[]`シーケンス一致により同一の`ArchetypeStorage`(=同一のChunk)を指す
    (`ArchetypeStorageSharingSpec`が証明済みの既存挙動をそのまま継承)。
- Conformance: Matches
- Known deviations: 見本(`AosSoaGeneratedExpansion.SingleFile.cs`)の`ChunkCapacity`ベースの
  物理的な複数Chunk生成とは異なり、ページングを行わない(ADR-0024で文書化済みの意図的な設計)。
- Next action: 削除済み(ADR-0029、M0009)。後継はF-L3-009(`EntityQuery`/`QueryMatches`、
  複数アーキタイプ横断・差分スキャン方式)。

## F-L7-006 — `CSharpGameEngine.Generation.Abstractions`(削除済み)

- Layer: L7 — Static Compiler / Bake
- Parent capability: Generated Entity Storage
- Status: Removed
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0024-Generated-Entity-AoS-SoA.md
- Implementation evidence:
  - src/CSharpGameEngine.Generation.Abstractions/CSharpGameEngine.Generation.Abstractions.csproj
  - src/CSharpGameEngine.Generation.Abstractions/EntityStorageKind.cs
  - src/CSharpGameEngine.Generation.Abstractions/GenerateEntityAttribute.cs
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GenerateEntityGeneratorTestHelper.cs
    (このアセンブリを参照するテストコンパイルを通じて間接的に検証)
- Reverse-engineered behavior:
  - net10.0、PackageReferenceなし、Roslynにも`CSharpGameEngine.Ecs`にも依存しない
    純粋な契約アセンブリ。`EntityGenerationSettings`は同梱せず、ユーザーが自分の
    プロジェクトへ書く慣習として扱う。
  - (M0006で更新)`EntityStorageKind`は`[Flags]`であり`Aos = 1`/`EcsSoa = 2`。
    両方を指定すると1つの宣言から両バックエンドが生成される。
- Conformance: Matches
- Known deviations: None
- Next action: 削除済み(ADR-0029、M0009)。`[GameSystem]`方式ではComponent型はユーザーが
  書く独立したstructで十分になり、専用の属性契約アセンブリは不要になった
  (`[GameSystem]`/`[Update]`等の属性はF-L3-007の一部として`CSharpGameEngine.Entities`本体に定義)。

## F-L7-007 — `GenerateEntityGenerator`(削除済み)

- Layer: L7 — Static Compiler / Bake
- Parent capability: Generated Entity Storage
- Status: Removed
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0024-Generated-Entity-AoS-SoA.md、
  Docs/Specifications/Generated-Entity-Storage.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/GenerateEntityGenerator.cs
  - src/CSharpGameEngine.CodeAnalysis/GenerateEntityAnalyzer.cs
  - src/CSharpGameEngine.CodeAnalysis/GenerateEntityModel.cs
  - src/CSharpGameEngine.CodeAnalysis/GenerateEntityCommonEmitter.cs
  - src/CSharpGameEngine.CodeAnalysis/GenerateEntityAosEmitter.cs
  - src/CSharpGameEngine.CodeAnalysis/GenerateEntitySoaEmitter.cs
  - src/CSharpGameEngine.CodeAnalysis/GenerateEntityEmitterSupport.cs
  - 消費側(いずれも削除済み): samples/AosSoaGeneratedExpansion/、旧L9アプリケーションのプロジェクト
    (`CSharpGameEngine.Generation.Abstractions`への通常参照を追加、`[GenerateEntity]`の
    実利用は未追加)
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GenerateEntityGeneratorEmissionTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GenerateEntityGeneratorConstSwitchTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GenerateEntityAosRuntimeTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GenerateEntitySoaRuntimeTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GenerateEntityConformanceTests.cs
- Reverse-engineered behavior:
  - `ForAttributeWithMetadataName("CSharpGameEngine.Generation.GenerateEntityAttribute", ...)`で
    対象構造体を検出する。属性引数の`EntityStorageKind`は`AttributeData.ConstructorArguments`
    経由で取得し、ソース側が`const`フィールド参照であってもRoslynが解決済みの値を渡すことを
    確認済み。
  - Generator内部の分岐(選択Backendに応じて`GenerateEntityAosEmitter`または
    `GenerateEntitySoaEmitter`を呼ぶ)は出力ソースには一切現れない。選択されなかった
    Backendの`AddSource`は呼ばれない。
  - `{Type}`(Facade)/`{Type}CreateInfo`は`public`、`{Type}Store`も`public`
    (見本通りの`public static Character Spawn(CharacterStore store, ...)`という
    公開静的ファクトリを書けるようにするため)。`{Type}GeneratedMetadata`/
    `{Type}AosGeneratedData`/`{Type}SoaGeneratedArchetype`/`{Type}ChunkView`は`internal`。
  - Component順序は「宣言順」(ユーザー向け生成メンバー)と「正規化順(FQN Ordinal)」
    (`NormalizedSignature`・列Index)の2系統で管理し、`World.GetComponentRef<T>`へ渡す
    `columnIndex`には正規化順を使う。
  - (M0006で追加)`[Flags]`で両方を指定した場合、Common(Facade/CreateInfo/GeneratedMetadata)は
    1回だけ出力され、Facadeは`{Type}AosStore`に束縛される。Store/ChunkViewは
    `{Type}Aos*`/`{Type}Soa*`へ改名され、SoA側の`Create`は`Entity`を返す(Facadeを持たないため)。
    単一指定時の型名・アクセシビリティ・本文は`[Flags]`化前と完全に同一。
  - (M0006で変更)`Store.Get{Name}Ref`は`internal`から`public`へ。Facadeの`{Name}Ref`が既に
    同じ参照をpublicへ公開しており隠蔽している情報が無いこと、および両指定モードのSoA側で
    唯一の単体アクセス経路になることによる。
- Conformance: Matches
- Known deviations: None(見本との差異はF-L3-006/M0005/M0006に記載の設計上の意図的な違いのみ)
- Next action: なし(旧L9アプリケーションでの実利用はM0006で追加後、M0009で削除済み)。

## F-L9-001(重複ID、旧台帳の既知の不整合) — Particle Entityベンチマーク(旧L9、削除済み)

- Layer: L9 — Game / Application
- Parent capability: Generated Entity Storage
- Status: Removed
- Verification: TestVerified、RuntimeVerified(Releaseビルドで実測)
- Intended specification: Docs/Decisions/ADR-0024-Generated-Entity-AoS-SoA.md、
  Docs/Milestones/M0006-Generated-Entity-Benchmark.md
- Removal evidence: 旧L9アプリケーションの`Entities/Particle.cs`、`ParticleComponents.cs`、
  `ParticleMotion.cs`、`ParticleSystems.cs`、`BenchmarkHarness.cs`、`ParticleStorageBenchmark.cs`、
  `Program.cs`の`benchmark`分岐と専用テストはM0009までに削除済み。
- Reverse-engineered behavior:
  - 1つの`[GenerateEntity(Aos | EcsSoa)] partial struct Particle`宣言から
    `ParticleAosStore`と`ParticleSoaStore`が生成され、同一プロセスで比較計測できる。
  - 各行に適用する計算はAoS/SoAとも同じ`ParticleMotion`を呼ぶため、計測しているのは
    メモリレイアウトの差のみ。
  - 実測(500,000件・51試行・中央値、.NET 10.0.10 Release): 一部コンポーネントのみを走査する
    バッチでSoAが1.59〜1.76倍速く、ほぼ全コンポーネントに触るバッチでは差が消え(1.06倍)、
    CreateはAoSが1.69倍速く、ランダム単体アクセスは実質同等(1.03倍)。詳細はM0006。
  - 両バックエンドは10フレーム分のバッチ更新後も合計値が完全一致する
    (ストレージ方式の切り替えが観測可能な結果を変えないことの実証)。
- Conformance: Matches
- Known deviations: 自前Stopwatchハーネスのため統計的な外れ値検出は行っていない
  (試行回数が少ないと中央値が揺れる。M0006に明記)。このエントリはF-L9-001
  (旧L9ゲームループのエントリ)とID重複した既存の台帳不整合であり、ID自体は
  再割当てせずそのまま記録する。
- Next action: 削除済み(ADR-0029、M0009)。AoS/SoA両バックエンドの選択肢自体がSoA一本化で
  無くなったため、再実行可能な比較ベンチマークは失われた(ユーザー確認済みの許容されたトレードオフ、
  ADR-0029参照)。実測値自体はM0006に記録として残る。

## F-L7-008 — `GenerateEntityGenerator`診断(ENTGEN001〜015、削除済み)

- Layer: L7 — Static Compiler / Bake
- Parent capability: Generated Entity Storage
- Status: Removed
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0024-Generated-Entity-AoS-SoA.md、
  Docs/Specifications/Generated-Entity-Storage.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/GenerateEntityDiagnostics.cs
  - src/CSharpGameEngine.CodeAnalysis/AnalyzerReleases.Unshipped.md
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GenerateEntityGeneratorDiagnosticsTests.cs
- Reverse-engineered behavior:
  - `ENTGEN001`〜`015`(カテゴリ`CSharpGameEngine.EntityGeneration.CodeGen`、すべて`Error`)。
    診断が1件でもある型はソース生成を完全にスキップする(`EcsArchetypeEmitter`と同じ規約)。
  - `partial`が付いていないが`get`/`set`を両方持つ非static・非indexerプロパティ(Componentの
    つもりで`partial`を付け忘れた形)は`ENTGEN005`として診断する一方、それ以外の通常メソッド・
    staticメンバー・constは一切Componentとして数えず診断もしない
    (`ValidCharacterSource_ProducesZeroDiagnostics`で確認)。
- Conformance: Matches
- Known deviations: None
- Next action: 削除済み(ADR-0029、M0009)。後継はF-L7-011(SYSGEN001〜020)。

## F-L1-001 — RUF/XYZセマンティック座標コア型と`ICoordinateConvention`契約

- Layer: L1 — Core Kernel
- Parent capability: RUFセマンティック座標系
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/RUF-Semantic-Coordinate-Specification-Prompt.md、
  Docs/Decisions/ADR-0025-RUF-Semantic-Coordinates.md、Docs/Specifications/Spatial-RUF.md
- Implementation evidence:
  - src/CSharpGameEngine.Core/Spatial/RufVector.cs
  - src/CSharpGameEngine.Core/Spatial/XyzVector.cs
  - src/CSharpGameEngine.Core/Spatial/ICoordinateConvention.cs
  - src/CSharpGameEngine.Core/Spatial/CoordinateHandedness.cs
  - src/CSharpGameEngine.Core/Spatial/PhysicalAxis.cs
  - src/CSharpGameEngine.Core/Spatial/UnitConversion.cs
  - src/CSharpGameEngine.Core/Spatial/CoordinateBasisValidator.cs
  - src/CSharpGameEngine.Core/Spatial/SpatialConfigurationException.cs
  - src/CSharpGameEngine.Core/Spatial/Spatial.cs
- Tests:
  - tests/CSharpGameEngine.Core.Tests/Spatial/BasisTests.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/RoundTripTests.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/HandednessTests.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/CoordinateBasisValidatorTests.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/UnitConversionTests.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/ImportAxisConversionTests.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/DeterminismTests.cs
- Reverse-engineered behavior:
  - `RufVector`(R/U/F)と`XyzVector`(X/Y/Z)は共にblittable・unmanaged・heap割当なしの
    `readonly struct`。`XyzVector`から`System.Numerics.Vector3`への変換は`explicit`演算子のみ
    (暗黙変換を許さない、ECS Chunk/SIMD/Renderer内部との境界を明示する設計)。
  - `ICoordinateConvention`は`static abstract`メンバーのみを持ち、`Spatial<TConvention>`が
    コンパイル時に単相化して呼び出す(Runtime分岐・Reflection・Boxing・Virtual dispatchなし)。
  - `CoordinateBasisValidator.Validate<T>()`はAuthoring/Build時専用で、非有限値・非単位長・
    非直交をそれぞれ独立に検出する。ただし「重複軸」専用チェックは直交性チェックが先に走るため
    到達不能(重複した単位軸同士の内積は必ず1になり非直交として先に例外化される) —
    エラーにはなるがメッセージは「非直交」になる(`CoordinateBasisValidatorTests.
    DuplicateAxes_ThrowsViaOrthogonalityCheck`で確認)。
  - `UnitConversion`はm/cm・rad/degの純粋なスカラー変換のみで、軸変換(`ICoordinateConvention`)とは
    独立した処理段階であり順序を入れ替えても結果が変わらない(`ImportAxisConversionTests`で確認)。
- Conformance: MatchesWithDocumentedExtension(重複軸チェックの到達不能性は既存コードの挙動として
  記録済み、修正はスコープ外)
- Known deviations: `CoordinateBasisValidator`の重複軸専用エラーメッセージは実際には出力されない
  (直交性エラーが先に発生するため。ADR-0025参照)。
- Next action: なし。

## F-L1-002 — `RufQuaternion`/`ITransformConvention`/`StandardTransformConvention`

- Layer: L1 — Core Kernel
- Parent capability: RUFセマンティック座標系
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/RUF-Semantic-Coordinate-Specification-Prompt.md、
  Docs/Specifications/Spatial-RUF.md
- Implementation evidence:
  - src/CSharpGameEngine.Core/Spatial/RufQuaternion.cs
  - src/CSharpGameEngine.Core/Spatial/ITransformConvention.cs
  - src/CSharpGameEngine.Core/Spatial/StandardTransformConvention.cs
- Tests:
  - tests/CSharpGameEngine.Core.Tests/Spatial/RufQuaternionTests.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/TransformConventionTests.cs
- Reverse-engineered behavior:
  - `ITransformConvention`は`ICoordinateConvention`と独立した契約で、`StandardTransformConvention`は
    Scale→Rotation→Translationの順で合成し(row-vector規約、`scale*rotation*translation`)、
    Parent/Child合成は`local*parentWorld`(child-then-parent)。
  - `TransformNormal`は逆転置行列を使う専用実装で、`TransformDirection`とはコードパスが異なる
    (非一様スケール下でも法線が変換後の面に垂直なまま保たれることを
    `TransformConventionTests.TransformNormal_StaysPerpendicularToSurfaceUnderNonUniformScale`で
    実測確認)。
  - `RufQuaternion`はR/U/Fの3成分をConvention非依存の内部的な右手系三つ組として扱う。
    `operator *`は`parent*child`で「childを先に適用しparentを後に適用する」合成
    (`RufQuaternionTests.Multiply_AppliesChildThenParent`で確認、既存コードのXMLコメントが
    この未実装テストの存在を前提に書かれていた)。
  - `LookRotation`はForward/Upが平行・ほぼ平行、またはUpがゼロ/非有限の場合に決定的な
    フォールバック軸へ切り替わり、NaNを生成しない。Forwardがゼロ長・非有限の場合は
    `SpatialConfigurationException`を投げる。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L1-003 — Unity/Unreal/Blenderの具体的Convention実装

- Layer: L1 — Core Kernel
- Parent capability: RUFセマンティック座標系
- Status: Implemented
- Verification: TestVerified、RuntimeVerified
- Intended specification: Docs/RUF-Semantic-Coordinate-Specification-Prompt.md「初期プリセット」
  「Golden Values」、Docs/Decisions/ADR-0025-RUF-Semantic-Coordinates.md、
  Docs/Specifications/Spatial-RUF.md
- Implementation evidence:
  - src/CSharpGameEngine.Core/Spatial/Conventions/UnityCoordinateConvention.cs
  - src/CSharpGameEngine.Core/Spatial/Conventions/UnrealCoordinateConvention.cs
  - src/CSharpGameEngine.Core/Spatial/Conventions/BlenderWorldConvention.cs
- Tests:
  - tests/CSharpGameEngine.Core.Tests/Spatial/BasisTests.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/RoundTripTests.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/HandednessTests.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/ImportAxisConversionTests.cs
- Reverse-engineered behavior:
  - 3実装とも`ToXyz`/`FromXyz`は成分の固定並べ替えのみ(heap割当・分岐なし)。
  - `samples/SpatialPresetDemo`をPreset別に実行し、`RufVector.Forward`が物理XYZへ変換された値が
    仕様書のGolden Valuesと完全に一致することを実測確認した: Unity=(0,0,1)、Unreal=(1,0,0)、
    Blender=(0,1,0)。
  - Unity/Unreal(LeftHanded)は`Spatial<T>.Cross(Right,Up)==Forward`、Blender(RightHanded)は
    `Spatial<T>.Cross(Right,Up)==Backward`になることを3 Presetすべてで確認
    (`HandednessTests`)。
  - Blenderは`BlenderWorldConvention`(ワールド座標のGolden Values)のみを実装し、
    `BlenderGltfExportConvention`/`BlenderFbxExportConvention`への分割は見送った
    (Blender用Import/Exportパイプライン自体が未着手のため、ADR-0025参照)。
- Conformance: Matches
- Known deviations: None
- Next action: Blender用のImport/Exportパイプラインが具体化した時点で、Export別Conventionへの
  分割を再検討する。

## F-L1-004 — コンパイル時Preset選択(`SpatialPresetSelection.props`)と`ProjectSpatial`ファサード例

- Layer: L1 — Core Kernel
- Parent capability: RUFセマンティック座標系
- Status: **Removed**(ADR-0034 / M0014)
- Verification: N/A
- Intended specification: Docs/RUF-Semantic-Coordinate-Specification-Prompt.md
  「コンパイル時プリセット選択」「型ベース呼び出し」、Docs/Decisions/ADR-0025-RUF-Semantic-Coordinates.md
- Removal rationale: この機構の利用者は`samples/SpatialPresetDemo`だけであり、しかもそれは
  `CSharpGameEngine.slnx`へ**意図的に未登録**だったため(下記Reverse-engineered behavior参照)、
  **通常ビルドで一度も実行されていなかった**。エンジン本体は
  [[ADR-0026-Camera-Geometry-Ruf-Integration]]で`UnityCoordinateConvention`に固定済みで、
  `Spatial.Unreal`/`Spatial.Blender`の本番利用はゼロだった。
  [[ADR-0034-Project-Consolidation]]で`SpatialPresetSelection.props`と`samples/`を削除した。
- 失われた範囲: **「コンパイル時に1つだけリンクする」機構だけ**である。Convention型
  (`UnityCoordinateConvention`等)は`CSharpGameEngine.Core`内に名前空間ごと残っており、
  実行時に選択して使うことは引き続きできる(F-L1-002/F-L1-003は`Implemented`のまま)。
- Implementation evidence(削除済み。履歴はgitの`f17fbd5`以前を参照):
  - ~~src/CSharpGameEngine.Spatial.Abstractions/SpatialPresetSelection.props~~
  - ~~samples/SpatialPresetDemo/{SpatialPresetDemo.csproj,ProjectSpatial.cs,Program.cs}~~
- Tests: なし(MSBuildレベルのBuild Error検証はxunitで表現しづらいため、`dotnet build`/
  `dotnet run`の実行結果で直接検証していた。実測値はADR-0025の検証結果へ転記済みで、
  証跡そのものは失われていない)。
- Reverse-engineered behavior(削除時点の記録として保持):
  - `$(SpatialPreset)`が`Unity`/`Unreal`/`Blender`のいずれか1つの場合のみ対応するConvention
    プロジェクトへの`ProjectReference`と`DefineConstants`(`SPATIAL_PRESET_*`)が設定される。
  - `SpatialPreset`未指定、または`Unity`/`Unreal`/`Blender`のいずれでもない値
    (`Bogus`等)を指定すると、`ValidateSpatialPresetSelection`ターゲットの`<Error>`により
    確実にBuild Errorになる(`dotnet build samples/SpatialPresetDemo/SpatialPresetDemo.csproj`と
    `-p:SpatialPreset=Bogus`の両方を実行し確認、ADR-0025参照)。
  - `ProjectSpatial.cs`は`#if SPATIAL_PRESET_UNITY/UNREAL/BLENDER`によりコンパイル時のみ分岐し、
    `Spatial<TConvention>`を1つの型引数に固定して転送する(Runtime分岐なし)。
  - `samples/SpatialPresetDemo`は`CSharpGameEngine.slnx`に**意図的に未登録**
    (登録すると`SpatialPreset`未指定で`dotnet build CSharpGameEngine.slnx`全体が失敗することを
    実際に確認したため。既存ビルドを壊さないための判断、ADR-0025参照)。
- Conformance: N/A(削除済み)
- Known deviations: 仕様書が想定する`dotnet test -p:SpatialPreset=Unity`のようなPreset別テスト
  実行は、旧`CSharpGameEngine.Core.Tests`(現`CSharpGameEngine.Core.Tests`の`Spatial/`)自体が
  3 Preset全てを1回のテスト実行内で検証する設計のため該当しない(F-L1-003参照)。
  この設計であるがゆえに、Preset機構を削除してもRUF側のテストカバレッジは一切減っていない。
- Next action: 無し。実際のゲームプロジェクトがRUFを採用する際は、Convention型を実行時に
  選択する形(`Spatial<UnityCoordinateConvention>`等)を使う。コンパイル時Preset選択が
  再び必要になった時点で、実利用者を伴う形で設計し直す。

## F-L2-003 — `CameraBasis`/`DirectionalLightBasis`/`SpotLightBasis`

- Layer: L2 — Type Schema / Identity
- Parent capability: RUFセマンティック座標系
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/RUF-Semantic-Coordinate-Specification-Prompt.md「Object Basis」、
  Docs/Specifications/Spatial-RUF.md
- Implementation evidence:
  - src/CSharpGameEngine.Core/Spatial/CameraBasis.cs
  - src/CSharpGameEngine.Core/Spatial/DirectionalLightBasis.cs
  - src/CSharpGameEngine.Core/Spatial/SpotLightBasis.cs
- Tests:
  - tests/CSharpGameEngine.Core.Tests/Spatial/CameraBasisTests.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/LightBasisTests.cs
- Reverse-engineered behavior:
  - `CameraBasis`のコンストラクタは`Forward`を正規化するが`Up`はそのまま保持する(`Up`は
    正規化せず呼び出し元の値を保つ設計、`CameraBasisTests`で確認)。
  - `DirectionalLightBasis`/`SpotLightBasis`はどちらも`Forward`を「光が進む方向
    (photonの進行方向)」として正規化して保持し、`DirectionToLight`は常に`-Forward`
    (表面から光源へ向かう方向)を返す計算プロパティ。
  - 既存の具体的な`CSharpGameEngine.Graphics.D3D12.Camera`型とは独立(このBasisはRUF空間の
    スキーマであり、Runtime Cameraコントローラーの代替ではない)。両者は本マイルストーンでは
    未接続(Spatial-RUF.md「未確定事項」参照)。
  - Point Lightに対応する型はこのマイルストーンでは追加していない(仕様書「Point Light」の
    「通常はForwardを持たない」に従い、追加不要と判断)。
- Conformance: Matches
- Known deviations: None
- Next action: 既存`CSharpGameEngine.Graphics.D3D12`のCamera/Light型との統合が必要になった時点で
  再検討する(次のマイルストーン候補)。

## F-L0-006 — `Camera.BuildViewMatrix`共有ヘルパー

- Layer: L0 — Platform / Native Backends
- Parent capability: Renderer/Import/ECS内部へのRUF全面統合
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0026-Camera-Geometry-Ruf-Integration.md
- Implementation evidence:
  - src/CSharpGameEngine.Windows/Graphics/Camera.cs(`BuildViewMatrix`静的メソッド)
  - src/CSharpGameEngine.Windows/Graphics/ModelRenderer.cs(`UpdateTransformConstantBuffer`)
- Tests:
  - tests/CSharpGameEngine.Windows.Tests/Graphics/CameraTests.cs(`GetView`経由の間接カバレッジ)
- Reverse-engineered behavior:
  - `Matrix4x4.CreateLookAt`への薄い委譲のみ。`Camera.GetView()`と`ModelRenderer`独自の
    バウンディングスフィア自動フィットeye計算(`Camera`インスタンスを経由しない別機能)の
    両方から呼ばれ、以前は2箇所に独立して存在した同一のLookAt構築コードを1箇所に統一した。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L1-005 — `GltfCoordinateConvention`

- Layer: L1 — Core Kernel
- Parent capability: Renderer/Import/ECS内部へのRUF全面統合
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.md
- Implementation evidence:
  - src/CSharpGameEngine.Core/Spatial/Conventions/GltfCoordinateConvention.cs
- Tests:
  - tests/CSharpGameEngine.Core.Tests/Spatial/BasisTests.cs(`Gltf_MatchesGoldenValues`)
  - tests/CSharpGameEngine.Core.Tests/Spatial/RoundTripTests.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/HandednessTests.cs
  - tests/CSharpGameEngine.Import.Tests/Gltf/GltfImporterSceneTests.cs(実利用側)
- Reverse-engineered behavior:
  - R=+X,U=+Y,F=-Z,RightHanded(glTF仕様: 右手系・Y-up、カメラ/`KHR_lights_punctual`ライトは
    既定でローカル-Z方向)。既存Unity/Unreal/Blender Conventionと同型・同パターンだが、
    Unity/Unreal/Blenderのような「プロジェクトが選ぶPreset」ではなく、
    `CSharpGameEngine.Import.Gltf`が内部でのみ使う固定のImport境界専用Convention
    (`SpatialPresetSelection.props`からは選択不可)。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L3-007 — `CSharpGameEngine.Entities`: Entity / World / 動的Archetype

- Layer: L3 — ECS / Job Runtime
- Parent capability: SoA一本化ECSへの全面移行(`[GameSystem]`/`[Update]`)
- Status: Removed(ADR-0033/M0013で削除・置換。後継は F-L3-012 / F-L7-015 / F-L7-016)
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0029-Dynamic-Archetype-Soa-Ecs.md、
  Docs/Specifications/Ecs-Soa-Systems.md
- Implementation evidence:
  - src/CSharpGameEngine.Entities/Entity.cs
  - src/CSharpGameEngine.Entities/ComponentType.cs(`ComponentType<T>.Id`/`ComponentTypeRegistry`)
  - src/CSharpGameEngine.Entities/ComponentMask.cs
  - src/CSharpGameEngine.Entities/ComponentTypeSet.cs
  - src/CSharpGameEngine.Entities/ComponentColumn.cs、IComponentColumn.cs
  - src/CSharpGameEngine.Entities/Archetype.cs
  - src/CSharpGameEngine.Entities/EntityArchetype.cs
  - src/CSharpGameEngine.Entities/World.cs(`CreateArchetype<T1..T8>`/`CreateEntity`/`Destroy`/
    `IsAlive`/`Has<T>`/`GetRef<T>`)
- Tests:
  - tests/CSharpGameEngine.Entities.Tests/ComponentTypeRegistrySpec.cs
  - tests/CSharpGameEngine.Entities.Tests/ComponentMaskSpec.cs
  - tests/CSharpGameEngine.Entities.Tests/ArchetypeSpec.cs
  - tests/CSharpGameEngine.Entities.Tests/WorldSpec.cs
- Reverse-engineered behavior:
  - Archetypeはコンパイル時に型として宣言せず、実行時の実際のComponent集合から
    `World.GetOrCreateArchetype`が動的に生成する(`Dictionary<ComponentTypeSet, Archetype>`で
    重複生成を防ぐ)。Archetypeリストは**append-only**(生成後は破棄されない) - これに
    F-L3-009のQuery差分スキャンが依存する。
  - `ComponentType<T>.Id`は型`T`ごとの`static readonly`フィールドとして、初回アクセス時に
    `ComponentTypeRegistry.Register(typeof(T), () => new ComponentColumn<T>())`を1回だけ呼ぶ。
    「ID 42の列を作れ」という実行時要求は`MakeGenericType`ではなく、この時点で焼き込まれた
    ファクトリデリゲート配列の添字呼び出しで解決するため、動的Archetype生成でも
    Reflectionを一切使わない。
  - `ComponentMask`は固定256bit(`ulong`×4)で、`ContainsAll`/`OverlapsAny`はO(4)・
    アロケーション無し。可変長`ulong[]`ではなく固定長にしたのは、型登録数が増えるにつれて
    列挙時に長さ不一致バグが混入するのを設計で防ぐため。
  - `World.GetRef<T>`は呼び出しごとに現在のArchetype内で該当列を2分探索してから`ref`を返す
    (スポーン/初期化コードや単発アクセス向けであり、毎フレームの行ループでは使わない設計 -
    そちらはF-L7-009が生成する`EntityQuery`ベースのSoAループを使う)。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L3-008 — 構造変更(Component追加・削除、Archetype間Entity移動、削除済み)

- Layer: L3 — ECS / Job Runtime
- Parent capability: SoA一本化ECSへの全面移行(`[GameSystem]`/`[Update]`)
- Status: Removed(ADR-0033/M0013。後継はF-L3-012)
- Verification: NotApplicable(削除前はTestVerified)
- Intended specification: Docs/Decisions/ADR-0029-Dynamic-Archetype-Soa-Ecs.md、
  Docs/Specifications/Ecs-Soa-Systems.md
- Removal evidence(旧動的Archetype実装、削除済み):
  - src/CSharpGameEngine.Entities/World.cs(`AddComponentDefault`/`AddComponentValue`/
    `RemoveComponent`/`GetArchetypeWithAdded`/`GetArchetypeWithRemoved`/`MoveEntity`/
    `CopySharedColumns`、すべて`internal`)
  - src/CSharpGameEngine.Entities/Archetype.cs(`TryGetAddEdge`/`SetAddEdge`/
    `TryGetRemoveEdge`/`SetRemoveEdge`)
- Historical tests(削除済み):
  - tests/CSharpGameEngine.Entities.Tests/StructuralChangeSpec.cs
- Historical behavior:
  - 旧`CSharpGameEngine.Ecs`(ADR-0015、F-L3-001、削除済み)には実行時のComponent追加・削除は
    無かった(ADR-0015で明示的に非目標と記録されていた)。この機能は新規追加。
  - 移動先の行を`AddRow`で先に確保 → 両Archetypeに存在する列だけを2つの昇順`int[]`の
    マージ走査でO(k)コピー(`CopySharedColumns`)、移動先のみの列は`SetDefault` →
    元をswap-back削除し移動したEntityの位置テーブルを修復、という順序を厳守する
    (コピーを削除より先にしないとswap-backで元データが破壊される)。
  - 「ComponentXを追加したら次はどのArchetypeか」を`Archetype`ごとの`Dictionary<int, Archetype>`
    (add/removeエッジキャッシュ)で記憶する。これは構造変更(`EntityCommandBuffer.Playback`時のみ
    発生、コールドパス)専用のDictionary使用であり、「ホットループにReflection/Type
    Dictionaryを入れない」という要件が禁止するのはフレームごとの行ループ内での使用であって、
    このコールドパスは対象外と判断した。
  - Component追加・削除の即時APIは`internal`であり、システムコード(`[Update]`メソッド)からは
    F-L3-010の`EntityCommandBuffer`経由でしか触れない(Query一致後の行ループ中に列配列が
    `Array.Resize`されて`Span<T>`が無効化される事故を設計で防ぐ)。
- Conformance: NotApplicable(動的なComponent追加・削除は現行閉集合ECSに存在しない)
- Known deviations: None
- Next action: 現行のEntity破棄/Scene unloadはF-L3-012で管理する。

## F-L3-009 — `EntityQuery` / `QueryMatches`(複数Archetype横断・差分スキャン、削除済み)

- Layer: L3 — ECS / Job Runtime
- Parent capability: SoA一本化ECSへの全面移行(`[GameSystem]`/`[Update]`)
- Status: Removed(ADR-0033/M0013。後継はF-L7-015の生成ループ)
- Verification: NotApplicable(削除前はTestVerified)
- Intended specification: Docs/Decisions/ADR-0029-Dynamic-Archetype-Soa-Ecs.md、
  Docs/Specifications/Ecs-Soa-Systems.md
- Removal evidence(削除済み):
  - src/CSharpGameEngine.Entities/EntityQuery.cs
  - src/CSharpGameEngine.Entities/QueryMatches.cs
- Historical tests(専用テストは削除済み):
  - tests/CSharpGameEngine.Entities.Tests/EntityQuerySpec.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorEndToEndTests.cs
    (`Move_ProcessesEntitiesAcrossMultipleArchetypes_UsingSoaSpans`ほか、生成されたExecutor経由の
    実行時検証)
- Historical behavior:
  - `EntityQuery.Update(World)`は`world.ArchetypeCount`(append-onlyなArchetypeリストの件数)を
    前回スキャン時の件数と比較するだけの差分スキャンで、新しいArchetypeが1件も生まれていない
    フレームは`int`比較1回で完了する。新Archetypeを発見した場合のみ、その1回だけ列インデックスを
    2分探索して`matchedColumns`(flat `int[]`、stride=access長)に書き込む。
  - `Create(all, none, access)`の`all`/`none`はSG(コンパイル時にComponent IdをOrdinal順で
    ソートできない)の都合で実行時にソートするが、`access`はパラメータ宣言順を保持する
    (列インデックスの対応順を安定させるため)。
  - `samples/SoaSystemsDemo`の`DemoScenario.Run()`で、既に1回`Update`済みのQueryが、
    frame1とframe2の間に新規生成されたArchetype(既存Queryが要求するComponent集合の
    スーパーセット)を、frame2で正しく検出することを実測確認済み(Query Cacheが
    「新しいArchetypeが生成されたときだけ」更新されるという要件の直接的な証拠)。
- Conformance: NotApplicable(動的Queryキャッシュは現行閉集合ECSに存在しない)
- Known deviations: None
- Next action: なし。現行はコンパイル時に確定したArchetypeへ直接ディスパッチする。

## F-L3-010 — `EntityCommandBuffer`(構造変更の遅延適用、削除済み)

- Layer: L3 — ECS / Job Runtime
- Parent capability: SoA一本化ECSへの全面移行(`[GameSystem]`/`[Update]`)
- Status: Removed(ADR-0033/M0013。限定的後継はF-L3-012の`DestroyDeferred`/`UnloadSceneDeferred`)
- Verification: NotApplicable(削除前はTestVerified)
- Intended specification: Docs/Decisions/ADR-0029-Dynamic-Archetype-Soa-Ecs.md、
  Docs/Specifications/Ecs-Soa-Systems.md
- Removal evidence(削除済み):
  - src/CSharpGameEngine.Entities/EntityCommandBuffer.cs
  - src/CSharpGameEngine.Entities/ComponentValueStore.cs
  - src/CSharpGameEngine.Entities/Entity.cs(`Temporary`/`IsTemporary`/`TemporaryIndex`)
- Historical tests(専用テストは削除済み):
  - tests/CSharpGameEngine.Entities.Tests/EntityCommandBufferSpec.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorEndToEndTests.cs
    (`Commands_StructuralChangesApplyOnlyAfterPlaybackAtEndOfGeneratedLoop`)
- Historical behavior:
  - `Add<T>(Entity)`/`Add<T>(Entity, in T)`/`Remove<T>`/`Destroy`/`CreateEntity`をFIFOの
    `List<Command>`(byte構造体、種別+Entity+ComponentTypeId+Payload)に記録する。値の型消去は
    `typeof(T)==typeof(X)`分岐ではなく`ComponentValueStore<T>`(`ComponentColumn<T>`と同じ
    「ジェネリッククラス+非ジェネリックインターフェース」パターン)で行う。
  - `CreateEntity`は`Entity.Temporary(index)`という負のIdハンドルを返す。`World.IsAlive`が
    `Id>=0`を先に見るため、Playbackで実体化されるまで自動的に「生存していない」扱いになる。
    同一バッファ内で`CreateEntity`直後に`Add<T>(その一時ハンドル, value)`を積める。
  - Playback順序: 既存Component+値なし`Add<T>`は無視(既存値を潰さない)、既存Component+
    値付き`Add<T>`は値だけ上書き(Archetype移動なし)、存在しないComponentへの`Remove<T>`と
    死んでいるEntityへの操作はno-op(例外を投げない)。
  - `samples/SoaSystemsDemo`のシナリオで、行ループ中に`commands.Add<DisabledTag>(entity)`を
    積んでも即座にArchetypeが変わらないこと(=Query一致中の配列を壊さない)、
    `GeneratedGameLoop.Update`の最後で`world.Commands.Playback(world)`が呼ばれた後にのみ
    実際に構造変更が反映されることを実測確認済み。
- Conformance: NotApplicable(汎用Add/Remove/Create command bufferは現行に存在しない)
- Known deviations: None
- Next action: なし。現行の遅延破棄は`Runtime.cs`と`EntityDestroySpec.cs`で管理する。

## F-L7-009 — `GameSystemGenerator`(Analyzer / ExecutorEmitter、引数からのQuery/Read-Write推論)

- Layer: L7 — Static Compiler / Bake
- Parent capability: SoA一本化ECSへの全面移行(`[GameSystem]`/`[Update]`)
- Status: Removed(ADR-0033/M0013で削除・置換。後継は F-L3-012 / F-L7-015 / F-L7-016)
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0029-Dynamic-Archetype-Soa-Ecs.md、
  Docs/Specifications/Ecs-Soa-Systems.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/GameSystemGenerator.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemAnalyzer.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemModel.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemExecutorEmitter.cs
  - src/CSharpGameEngine.Entities/GameSystemAttribute.cs、UpdateAttribute.cs、
    ContextAttribute.cs、CurrentEntityAttribute.cs、CommandsAttribute.cs、WithoutAttribute.cs
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorTestHelper.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorEmissionTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorEndToEndTests.cs
- Reverse-engineered behavior:
  - 推論規則: `ref X`→Query必須+Write、`in X`→Query必須+Read、`in X`(インスタンスフィールド0の
    タグ型)→Query必須のみ・列解決対象外(`X x = default;`をループ外にホイスト)、
    `[Context]`→Query無関係でexecutor引数へそのまま転送(構造体は`in`、参照型は値渡し)、
    `[CurrentEntity]`→`archetype.Entities[row]`、`[Commands]`→`world.Commands`、
    `[Without(typeof(X))]`(メソッド属性)→QueryのNone集合(「引数から推論する」原則の唯一の
    明示的な例外)。
  - Executor型は`{ClassName}_{MethodName}_Executor`という名前空間スコープの独立した型として
    生成し、ユーザーの`[GameSystem]`クラスには何も追加しない。当初`{MethodName}_Executor`
    (クラス名を含めない)で実装したところ、異なる`[GameSystem]`クラスが同名メソッド
    (例: 複数クラスの`Move`)を持つ場合に同一名前空間で型名が衝突するバグを自己レビューで
    発見し、クラス名を含む形に修正した
    (`TwoDifferentGameSystemClasses_WithSameMethodName_CompileTogetherWithoutCollision`で
    実際にコンパイルして回帰確認)。
  - ユーザーメソッド呼び出し(`global::{ClassFqn}.{Method}(...)`)を含む生成コード中の型参照は
    全て`global::`完全修飾で出力する(`GameSystemClassResult.ClassFullyQualifiedName`が
    `global::`を含まない`ToDisplayString()`由来のため、呼び出し箇所でのみ明示的に
    `global::`を前置する)。
  - 生成された`Execute`メソッドの行ループ内に`typeof(`/`Has<`/`.Contains(`/`Dictionary`/
    `GetType()`が一切現れないことをテストでassertしており、「ホットループでComponent有無判定を
    しない」「ReflectionやType DictionaryをホットループにIntroduce入れない」という要件を
    機械的に検証している。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L7-010 — `GameSystemScheduler` / `GameSystemLoopEmitter`(Write→Read依存順序、`GeneratedGameLoop`)

- Layer: L7 — Static Compiler / Bake
- Parent capability: SoA一本化ECSへの全面移行(`[GameSystem]`/`[Update]`)
- Status: Removed(ADR-0033/M0013で削除・置換。後継は F-L3-012 / F-L7-015 / F-L7-016)
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0029-Dynamic-Archetype-Soa-Ecs.md、
  Docs/Specifications/Ecs-Soa-Systems.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/GameSystemScheduler.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemLoopEmitter.cs
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorEmissionTests.cs
    (`GeneratedGameLoop_CallsWriterBeforeReader_EvenWhenReaderIsDeclaredFirst`、
    `GeneratedGameLoop_CallsExecutorsInWriteBeforeReadOrder`、
    `GeneratorIsDeterministic_AcrossTwoIndependentRuns`)
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorDiagnosticsTests.cs
    (`MutualWriteReadDependency_ReportsSysgen019_ButStillEmitsGeneratedGameLoop`)
- Reverse-engineered behavior:
  - エッジ規則: AがComponent CをWrite・BがCをRead(A≠B)ならA→B(生産者が先、唯一の意味的に
    確実な制約)。A・BがともにCをWriteなら`(Order, ClassFullyQualifiedName ordinal,
    MemberIndex, MethodName ordinal)`というbaseline順の小さい方→大きい方(決定的な
    タイブレーク、ファイルパスは使わない)。Read→Writeはエッジを張らない
    (「書き込み前を見たいか後を見たいか」は推論不能で、張ると容易に循環するため)。
  - Kahn法+「利用可能な中でbaseline順最小」を優先するタイブレークで安定トポロジカルソートする。
    実際に、宣言順・クラス名Ordinal順では後に来るはずのWrite側メソッドが、Read側より
    先に実行されることをテストで確認済み(依存関係が宣言順を上書きする実例)。
  - 循環を検出した場合はSYSGEN019をError報告した上で、ループ自体はbaseline順で出力する
    (既存2ジェネレータの「診断が1件でもあれば出力しない」ゲートからの意図的な逸脱 -
    ループを丸ごと省くと呼び出し側で無関係な「型が見つからない」二次エラーが大量発生するため)。
  - `GeneratedGameLoop.Update`のContext引数名は、当初`context0`/`context1`のような不透明な
    連番だったが、旧L9アプリケーション移行で3つのContext型(`StageRenderer`/`Win32Window`/
    `FrameContext`)を同時に使う実例に当たった際、呼び出し側がFQNの辞書順という非直感的な
    規則を暗記しないと正しい引数順序で呼べないという実用上の欠陥に気付き、単純型名の
    先頭を小文字化した名前(衝突時は連番、C#予約語なら`@`エスケープ)へ変更した。これにより
    `Update(world, stageRenderer: r, win32Window: w, frameContext: f)`のように名前付き引数で
    安全に呼べる。
  - `.Collect()`ベースの結合出力(既存2ジェネレータには無い、この結合ループが初のパターン)は
    デフォルトの参照等価性比較により無関係な編集でも再生成されうるが、既存2ジェネレータの
    `ImmutableArray<T>`キャッシュ制約と同種の許容されたトレードオフとして記録する。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L7-011 — `GameSystemGenerator`診断(SYSGEN001〜020)

- Layer: L7 — Static Compiler / Bake
- Parent capability: SoA一本化ECSへの全面移行(`[GameSystem]`/`[Update]`)
- Status: Removed(ADR-0033/M0013で削除・置換。後継は F-L3-012 / F-L7-015 / F-L7-016)
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0029-Dynamic-Archetype-Soa-Ecs.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/GameSystemDiagnostics.cs
  - src/CSharpGameEngine.CodeAnalysis/AnalyzerReleases.Unshipped.md
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorDiagnosticsTests.cs
    (SYSGEN001〜020すべてに1件ずつのテストケース)
- Reverse-engineered behavior:
  - `SYSGEN001`〜`020`(カテゴリ`CSharpGameEngine.Entities.CodeGen`、すべて`Error`)。
    `SYSGEN001`〜`003`は`[GameSystem]`クラス自体の検証(static/非ジェネリック/非ネスト)、
    `SYSGEN004`〜`008`は`[Update]`メソッドの検証(static/void/非ジェネリック/非private/
    Component引数1つ以上)、`SYSGEN009`〜`012`はComponent引数個々の検証(ref/in必須、
    非nullable struct必須、型重複禁止、タグは`ref`禁止)、`SYSGEN013`〜`017`は特殊パラメータ
    (`[Context]`/`[CurrentEntity]`/`[Commands]`)の検証、`SYSGEN018`はメソッド名重複禁止、
    `SYSGEN019`は循環依存(F-L7-010参照)、`SYSGEN020`は`[Without]`引数の妥当性検証。
  - 診断が1件でもあるクラスは、`GameSystemExecutorEmitter`がそのクラス分のExecutor生成を
    完全にスキップする(既存2ジェネレータと同じ診断ゲート規約)。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L9-005 — `samples/SoaSystemsDemo`

- Layer: L9 — Game / Application
- Parent capability: SoA一本化ECSへの全面移行(`[GameSystem]`/`[Update]`)
- Status: Removed(ADR-0033/M0013で削除・置換。後継は F-L3-012 / F-L7-015 / F-L7-016)
- Verification: RuntimeVerified
- Intended specification: Docs/Decisions/ADR-0029-Dynamic-Archetype-Soa-Ecs.md、
  Docs/Specifications/Ecs-Soa-Systems.md
- Implementation evidence:
  - samples/SoaSystemsDemo/SoaSystemsDemo.csproj
  - samples/SoaSystemsDemo/Components.cs
  - samples/SoaSystemsDemo/MovementSystems.cs、HealthSystems.cs
  - samples/SoaSystemsDemo/DemoScenario.cs
  - samples/SoaSystemsDemo/Program.cs
- Tests: なし専用(`DemoScenario.Run()`自身が"OK"/"FAIL:<理由>"という決定的な文字列を返す
  自己検証シナリオで、`dotnet run`で実行して確認する。他サンプル
  (`samples/AosSoaGeneratedExpansion`)と同じ「サンプルはdotnet runで動かす、xUnitプロジェクトは
  持たない」規約に合わせた)。
- Reverse-engineered behavior:
  - ユーザー提示の元サンプル(`MovementSystems.Move`/`HealthSystems.Regenerate`/
    `DisableDeadEntity`、`in MovableTag _`というタグパラメータ名を含む)を、実際の
    `GameSystemGenerator`で動くコードとしてほぼそのまま再現している(型名のみユーザー決定の
    `Entity`/`EntityCommandBuffer`を使用、元サンプルの`EntityId`/`EntityCommands`ではない)。
  - `DemoScenario.Run()`は1シナリオでQuery一致・SoAループ・`[Context]`/`[CurrentEntity]`/
    `[Commands]`・構造変更・Archetype動的生成・Queryキャッシュの差分更新を同時に検証する
    (F-L3-009のReverse-engineered behavior参照)。`dotnet run --project
    samples/SoaSystemsDemo`で実行し"OK"を確認済み。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L1-006 — `FbxCoordinateConvention`

- Layer: L1 — Core Kernel
- Parent capability: Renderer/Import/ECS内部へのRUF全面統合
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.md
- Implementation evidence:
  - src/CSharpGameEngine.Core/Spatial/Conventions/FbxCoordinateConvention.cs
- Tests:
  - tests/CSharpGameEngine.Core.Tests/Spatial/BasisTests.cs(`Fbx_MatchesGoldenValues`)
  - tests/CSharpGameEngine.Core.Tests/Spatial/RoundTripTests.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/HandednessTests.cs
  - tests/CSharpGameEngine.Import.Tests/Fbx/FbxAxisConversionTests.cs(変換関数の単体テスト)
- Reverse-engineered behavior:
  - `GltfCoordinateConvention`と数値的に同一の定義(R=+X,U=+Y,F=-Z,RightHanded)だが、
    AssimpNet公式ドキュメント(`PostProcessSteps.MakeLeftHanded`のXMLコメント: 「既定出力は
    右手系、+Xが右、+Zが視点側、+Yが上」)を根拠に独立した型として実装した
    (`CSharpGameEngine.Import.Fbx`が`CSharpGameEngine.Import.Gltf`に依存しないため)。
  - `FbxImporter.Import`はシーングラフを持たない単一メッシュのみのプレビュー専用APIのため、
    `GltfCoordinateConvention`と異なりWorldTransformの共役変換は不要(Positions/Normals/
    Windingの変換のみ)。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L3-011 — Scene DSL基盤型(`IScene`/`Node`/`Parent`/`SceneInstance`)

- Layer: L3 — ECS / Job Runtime
- Parent capability: Scene Authoring(C#コードをSceneの正本にする、Phase A)
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor.md
- Implementation evidence:
  - src/CSharpGameEngine.Entities/IScene.cs
  - src/CSharpGameEngine.Entities/Node.cs
  - src/CSharpGameEngine.Entities/ISceneDefinition.cs
  - src/CSharpGameEngine.Entities/Parent.cs
  - src/CSharpGameEngine.Entities/SceneNodeTag.cs
  - src/CSharpGameEngine.Entities/SceneInstance.cs
  - src/CSharpGameEngine.Entities/SceneInstanceTable.cs
- Tests:
  - tests/CSharpGameEngine.Entities.Tests/SceneDslTypeSpec.cs
  - tests/CSharpGameEngine.Entities.Tests/SceneInstanceSpec.cs
  - tests/CSharpGameEngine.Entities.Tests/SceneInstanceStressSpec.cs
- Reverse-engineered behavior:
  - `Parent`はUnity DOTSの`Unity.Transforms.Parent`と同じ形の汎用Component(`Entity Value`のみ)。
    現行では`Node.Children`の入れ子からSource Generatorが自動付与する。
  - `IScene.Root`のC#式は宣言専用で実行しない。`Node`コンストラクターは誤実行を即座に検出する
    tripwireで、Runtime生成は`World.InstantiateScene<TScene>()`から生成済み`ISceneDefinition`を呼ぶ。
  - `SceneInstance`/`SceneInstanceTable`が同じScene定義の複数instantiate、所有Entityの追跡、
    unloadと世代検証を担う。安定Node IDはRuntime型ではなくSceneModel側の`NodePath`で管理する。
- Conformance: Matches
- Known deviations: **(2026-08、M0049)** `IScene`/`Node`はファイル自体は削除していないが、
  実際のScene(`UserEcsProject/Scenes/*.json`)はどれもこの型を実装しなくなった
  ([[M0049-Scene-Json-Persistence]])。Scene検知は`*.scene.json`ファイルの存在のみで行う
  (F-L7-019)。ファイルを残した理由は3テストプロジェクトの`.csproj`が直接リンクしていることと、
  VSIX側の解析器(F-L7-012)がM0050までこの型名を前提にしているため。
- Next action: なし。編集時の安定Node IDはF-L8-010/F-L8-011で管理する。

## F-L7-012 — Scene DSL解析・Runtime生成(`SceneDslAnalyzer`/`SceneInstantiateEmitter`)

- **M0093/ADR-0070 (2026-10-01):** Sceneの正本はシーン JSON になった（2026-10-01 からは `Scenes\**\*.json`、それまでは `*.scene.json`）。生成器はJSONからだけSceneを作り、C#のSceneクラスはSCENE206(ビルドエラー、変換コマンドを案内)。`SceneInstantiateEmitter`以降の生成は不変で、C# Scene DSLの解析はScene DesignerがJSONを読む内部表現(`SceneJsonDesignerSource`)としてだけ残る。
- Layer: L7 — Static Compiler / Bake
- Parent capability: Scene Authoring(C#コードをSceneの正本にする、Phase A)
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/SceneDslAnalyzer.cs
  - src/CSharpGameEngine.CodeAnalysis/SceneEmitModel.cs
  - src/CSharpGameEngine.CodeAnalysis/SceneModelBridge.cs
  - src/CSharpGameEngine.CodeAnalysis/SceneComponentSetCollector.cs
  - src/CSharpGameEngine.CodeAnalysis/SceneInstantiateEmitter.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemGenerator.cs
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDslAnalyzerTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDslGeneratorEmissionTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDslGeneratorEndToEndTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneArchetypeIntegrationTests.cs
- Reverse-engineered behavior:
  - `IScene.Root => new Node { ... }`の構文を実行せず解析し、Node名、Component初期化子、Childrenを
    `SceneEmitModel`へ変換する。`SceneInstantiateEmitter`は閉集合Archetypeの直接`Create`呼び出しを生成し、
    非Rootへ`Parent`を、Component無しノードへ`SceneNodeTag`を自動付与する。
  - Scene由来Component集合は`SceneComponentSetCollector`からGameSystemのArchetypeレジストリへ統合し、
    `ISystem`由来集合と同じ連番Archetype空間を使う。ReflectionやRuntime DSL解釈は行わない。
  - 生成partialは`ISceneDefinition`を明示実装し、`World.InstantiateScene<TScene>()`から同じSceneを
    複数回独立して生成できる。
- Conformance: Matches
- Known deviations: DSL宣言はC#としてコンパイルされるが、`Node`をRuntime実行すると意図的に例外になる。
  **(2026-08、M0049)** `GameSystemGenerator`はこの経路(C#構文からの`SceneEmitModel`生成)を
  実際のScene検出には使わなくなった。実運用の経路はF-L7-019(`*.scene.json` → `SceneJsonAnalyzer`)
  に置き換わっている。`SceneDslAnalyzer`/`SceneModelBridge`自体はVSIX側(F-L8-010/F-L8-011)が
  M0050まで使い続けるため削除していないが、Runtimeコンパイルパイプラインからは呼ばれない
  ([[M0049-Scene-Json-Persistence]])。
- Next action: 編集操作はF-L8-011の後続Phaseで管理する。

## F-L7-013 — Scene DSL診断(SCENE100〜118)

- **M0093/ADR-0070 (2026-10-01):** ビルドが報告するのはSCENE114(Scene外での`Node`構築・`Root`読み取り)と、JSONの`startupOrder`重複に対するSCENE118(後のJSONの値の位置)だけになった。ほかのSCENE1xxはScene Designerの内部解析の診断として残り、シーンJSONの誤りはSCENE201〜205が報告する。
- Layer: L7 — Static Compiler / Bake
- Parent capability: Scene Authoring(C#コードをSceneの正本にする、Phase A)
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/SceneDslAnalyzer.cs
  - src/CSharpGameEngine.CodeAnalysis/SceneEmitModel.cs
  - src/CSharpGameEngine.CodeAnalysis/AnalyzerReleases.Unshipped.md
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDslAnalyzerTests.cs
- Reverse-engineered behavior:
  - `SCENE100`〜`118`は`IScene`実装、Root式、Node/Component/Children構文、禁止されたRuntime参照、
    `[StartupScene]`適用先とOrder重複を検証する。カテゴリは`CSharpGameEngine.Entities.CodeGen`で全てError。
  - 解析エラーを持つSceneは`SceneInstantiateEmitter`とStartup合成Systemの生成対象から除外する。
- Conformance: Matches
- Known deviations: 旧SCENE001〜009はADR-0040移行時に廃止し、IDをSCENE100以降へ振り直した。
  **(2026-08、M0049)** SCENE100〜118はC#構文解析(`SceneDslAnalyzer`、VSIX専用に縮退)にのみ残る。
  実運用のJSON経路は別番台のSCENE200〜204(F-L7-019)を使う。
- Next action: なし。

## F-L9-006 — `samples/SceneAuthoringDemo`(削除済み)

- Layer: L9 — Game / Application
- Parent capability: Scene Authoring(C#コードをSceneの正本にする、Phase A)
- Status: Removed(ADR-0040移行時)
- Verification: NotApplicable(削除前はRuntimeVerified)
- Intended specification: Docs/Decisions/ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth.md
- Removal evidence: `samples/SceneAuthoringDemo/`は削除済み。後継の実利用例は
  `UserEcsProject/Scenes/MainScene.cs`と`UserEcsProject/Scenes/FirstPersonStageScene.cs`。
- Historical tests: なし専用(`DemoScenario.Run()`自身が"OK"/"FAIL:<理由>"という決定的な文字列を返す
  自己検証シナリオで、`dotnet run`で実行して確認する。`samples/SoaSystemsDemo`と同じ「サンプルは
  dotnet runで動かす、xUnitプロジェクトは持たない」規約に合わせた)。
- Historical behavior:
  - ユーザーが貼り付けた見本(シーン構築見本.cs)のRoot/MainCamera/Player構成を、
    `LocalTransform`ではなく`Position`/`Rotation`/`Scale`の3分割`Set`呼び出しへ書き換えた上で
    実際の`SceneDefinitionGenerator`で動くコードとして再現している。
  - `DemoScenario.Run()`は`MainScene.CreateRuntime(world)`を実行し、Rootが`SceneNodeTag`のみを
    持ち`Parent`を持たないこと、MainCamera/Playerがそれぞれ`Parent.Value == Root`を持つこと、
    Define本体に書いた各Component値が実際にWorldへ反映されていることを、`EntityQuery`で
    該当Entityを検索した上で検証する(`CreateRuntime`はRootのEntityしか返さないため)。
    `dotnet run --project samples/SceneAuthoringDemo`で実行し"OK"を確認済み。
- Conformance: NotApplicable(旧サンプルは存在しない)
- Known deviations: None
- Next action: なし。現行Scene DSLはF-L7-012と`UserEcsProject`で検証する。

## F-L7-014 — `CSharpGameEngine.SceneAuthoring.Cli`(実行ベースの汎用Worldブリッジ)

- Layer: L7 — Static Compiler / Bake
- Parent capability: `[SceneDefinition]`クラスのコンテキストメニュー3Dプレビュー
- Status: Removed(2026-08)
- Verification: NotApplicable
- Intended specification: Docs/Decisions/ADR-0031-Scene-Definition-3D-Preview.md
  (機構ごと退役。ライブRoslynの`SceneModel`直接描画へ移行したためブリッジ実行自体が不要になった)
- Removal evidence(2026-08時点で実際にディレクトリの有無を確認):
  **`tools/CSharpGameEngine.SceneAuthoring.Cli`ディレクトリ自体が存在しない**。検証に使っていた
  `samples/SceneAuthoringDemo`も削除済み。F-L8-009 / F-L9-007と同じADR-0031系列の退役。
- Implementation evidence(歴史的記録。以下は削除済みのファイル):
  - tools/CSharpGameEngine.SceneAuthoring.Cli/Program.cs
  - tools/CSharpGameEngine.SceneAuthoring.Cli/SceneDefinitionSourceParser.cs
  - tools/CSharpGameEngine.SceneAuthoring.Cli/SceneRuntimeInvoker.cs(ADR-0033で完全リフレクション化)
  - tools/CSharpGameEngine.SceneAuthoring.Cli/WorldInspector.cs(ADR-0033で`WorldSnapshot`消費へ全面書き換え)
  - tools/CSharpGameEngine.SceneAuthoring.Cli/WorldInspector.cs
  - tools/CSharpGameEngine.SceneAuthoring.Cli/ScenePreviewBridgeWorker.cs
  - tools/CSharpGameEngine.SceneAuthoring.Cli/ScenePreviewRequest.cs、ScenePreviewBridgeDocument.cs
  - src/CSharpGameEngine.Entities/CSharpGameEngine.Entities.csproj(`InternalsVisibleTo`追加)
- Tests:
  - tests/CSharpGameEngine.SceneAuthoring.Cli.Tests/SceneDefinitionSourceParserTests.cs
  - tests/CSharpGameEngine.SceneAuthoring.Cli.Tests/WorldInspectorTests.cs
  - tests/CSharpGameEngine.SceneAuthoring.Cli.Tests/SceneRuntimeInvokerTests.cs
    (`samples/SceneAuthoringDemo`の実ビルド出力に対する統合テスト)
- Reverse-engineered behavior:
  - `Define`メソッドの構造・Component値は静的解析(構文解析)ではなく、生成済みの
    `CreateRuntime(World)`をビルド済みアセンブリからCollectible `AssemblyLoadContext`+
    `AssemblyDependencyResolver`でロードし、Reflectionで実際に実行して得る。`Set(...)`の引数は
    SCENE008診断が「new式であること」しか強制しないため、定数参照や計算式を含みうる — 実行して
    結果を読む方式のみが常に正しい値を保証する。ノード**名**だけは実行結果に残らないため、
    `SceneDefinitionSourceParser`による構文のみの別解析(`CSharpSyntaxTree.ParseText`、
    Compilation不要)で宣言順に復元し、`Entity.Id`昇順(=宣言順、`SceneRuntimeEmitter`の
    深さ優先生成順と`World.AllocateId`の単調増加により保証される)で位置対応させる。
  - `WorldInspector`は対象プロジェクトのComponent型をコンパイル時に一切知らないまま、
    `CSharpGameEngine.Entities.csproj`への新規`InternalsVisibleTo`経由で`Archetype`/
    `ComponentTypeRegistry`等の`internal`APIを歩く。`ComponentColumn<T>.Items`が`Span<T>`ではなく
    素の`public T[]`フィールドであることを利用し、`FieldInfo.GetValue`+`Array.GetValue(row)`で
    値を取得する(`Span<T>`は`MethodInfo.Invoke`を跨いで返せないため、この設計が無ければ汎用走査
    自体が成立しなかった)。Position/Rotation/Scaleは型の単純名一致(ADR-0030の規約)、`Parent`は
    型そのもの(`CSharpGameEngine.Entities`を直接参照しているため型が統一される)で判定する。
  - 共有ブリッジ基盤(`ExternalBridgeProcess`/`BridgeServerHost`)が「入力パス1つ+出力パス1つ」
    固定のため、2つの入力(ソース.csパス、対象アセンブリパス)を`ScenePreviewRequest`として
    一時JSONへ書き、そのファイルパス自体を入力パスとして渡す間接化を行っている。
- Conformance: NotApplicable(機構が存在しない)
- Known deviations: None
- Next action: なし。以前挙げていた「Collectible `AssemblyLoadContext.Unload()`の常駐ブリッジ
  サーバー環境での繰り返し安定性の専用ストレステスト」(ADR-0031の残課題)は、ブリッジ実行方式ごと
  退役したため不要になった。
- **(2026-08、Status修正)** F-L8-009 / F-L9-007と同じく、削除済みのプロジェクトを
  `Status: Implemented`のまま記述していたため`Removed`へ修正した。

## F-L8-009 — Scene Definition 3Dプレビュー(コンテキストメニューコマンド、ギズモフォールバック)

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: `[SceneDefinition]`クラスのコンテキストメニュー3Dプレビュー
- Status: Removed(2026-08)
- Verification: NotApplicable
- Intended specification: Docs/Decisions/ADR-0031-Scene-Definition-3D-Preview.md
  (機構ごと退役。後継はADR-0040 / F-L8-010)
- Removal evidence(2026-08時点で実際にファイルの有無を確認):
  - 削除済み: `PreviewSceneDefinitionCommand.cs`、`SceneDefinitionTextDetector.cs`、
    `ProjectOutputAssemblyResolver.cs`、`ExternalScenePreviewLoader.cs`、
    `ScenePreviewSceneFileAdapter.cs`、`ScenePreviewRequest.cs`/`ScenePreviewBridgeDocument.cs`
  - 削除済みテスト: `ScenePreviewSceneFileAdapterTests.cs`、`ProjectOutputAssemblyResolverTests.cs`、
    `ExternalScenePreviewLoaderTests.cs`
  - `.vsct`のコマンド/グループも退役: `0x0108`(PreviewSceneDefinitionCommandId)、
    `0x1023`(SceneDefinitionContextMenuGroup。宣言は2026-08に削除、IDは再利用しない)
  - **存続している部分**: `tools/CSharpGameEngine.VSExtension/GizmoMeshFactory.cs` と
    `tests/CSharpGameEngine.VSExtension.Tests/GizmoMeshFactoryTests.cs` は削除されておらず、
    現在は`SceneModelRenderObjectBuilder`(ライブRoslynの`SceneModel`描画経路、F-L8-010)が
    メッシュ解決に失敗したときのフォールバックとして使っている。**本エントリを`Removed`にしても
    このファイルは生きている**ため、掃除対象と誤認しないこと。
- Reverse-engineered behavior(歴史的記録。以下は退役した実装の説明):
  - 新しいWindowPane/EditorFactoryは作らず、既存の「シーン」プレビュー基盤
    (`SceneFileEditorFactory`/`SceneWindowPane`/`SceneHwndHost`/`SceneEditorHub`、F-L8-003)を
    そのまま流用する。ブリッジ応答を`ScenePreviewSceneFileAdapter`で一時的な合成`.scene`
    ファイルへ変換し、`dte.ItemOperations.OpenFile(...)`(ADR-0013の確立パターン)で開く。
  - `PreviewSceneDefinitionCommand`はこのリポジトリ初のコードエディタコンテキストメニュー配置
    コマンドであり、`BeforeQueryStatus`によるVisible/Enabled動的切替もこのリポジトリ初
    (既存の唯一の`OleMenuCommand`利用例`ToggleFabWatchCommand`はテキスト/Checked切替のみ)。
    可視性判定は`EnvDTE.TextDocument`のライブバッファテキストを`SceneDefinitionTextDetector`
    (正規表現)で判定するだけで、Roslyn/Workspaceは一切使わない。
  - `ProjectOutputAssemblyResolver`は`EnvDTE.Project.FullName`+`OutputPath`/`OutputFileName`
    プロパティから実ビルド出力を解決する(TFM/RID非依存)。旧L9アプリケーションのような
    `net10.0-windows10.0.19041.0`+`win-x64`という非標準TFMでも、フォルダ名を決め打ちする
    `BridgeExecutableLocator`方式とは異なりそのまま解決できることを、実際に
    旧`Scene1.cs`向けのブリッジ実行(F-L9-007参照)で確認した。
  - `GizmoMeshFactory`は`CSharpGameEngine.Geometry.ProceduralMeshFactory.CreateBox`を経由せず、
    同じ頂点/法線構成をプレーンな`System.Numerics.Vector3`で独立に再実装している
    (`ProceduralMeshFactory`はRUF空間抽象化を経由し、net10.0専用でVSIXから参照できないため)。
  - `.vsct`のビルドは`IDM_VS_CTXT_CODEWIN`/`DynamicVisibility`とも初回で正常にコンパイルされた。
- Conformance: NotApplicable(機構が存在しない)
- Known deviations: None
- Next action: なし。
- **(2026-08、Status修正)** 以前このエントリは`Status: Implemented` / `Verification: TestVerified`の
  まま、既に削除済みの実装ファイルを列挙していた(「削除済みコードを実装済みと記述している」状態)。
  M0020の残課題として記録されていたものを`Removed`へ修正した。修正時にファイルの有無を実際に確認し、
  `GizmoMeshFactory`だけは存続して現行の描画経路で使われていることが判明したため上記に明記した
  (残課題の記述は「5ファイルとテスト3件は全て削除済み」としており、この点が不正確だった)。

## F-L9-007 — 旧L9アプリケーションの`Scene1.cs`(削除済み)

- Layer: L9 — Game / Application
- Parent capability: `[SceneDefinition]`クラスのコンテキストメニュー3Dプレビュー
- Status: Removed(2026-08)
- Verification: NotApplicable
- Intended specification: Docs/Decisions/ADR-0031-Scene-Definition-3D-Preview.md
  (機構ごと退役。後継はADR-0040 / M0019)
- Removal evidence(2026-08時点で実際にファイルの有無を確認): 旧`Scene1.cs`は`81c462e`で、
  旧L9アプリケーションの残りは`40f08e4`で削除済み。現行のユーザープロジェクトは`UserEcsProject`。
- Tests: なし(退役)。
- Reverse-engineered behavior(歴史的記録。以下は退役した実装の説明):
  - `Root`→`MainCamera`(Position/Rotation/Scale/CameraRig)/`Player`(Position/Rotation/Scale/
    PlayerTag/Health)/`Lighting`(Positionのみ、Sun/Ambientのグルーピングアンカー)、
    `Lighting`→`Sun`(Position/DirectionalLightData)/`Ambient`(Position/AmbientLightData)という
    3階層6ノード構成。旧`Components.cs`の既存型のみを使い、新規Component型は
    追加していない。`Camera`(D3D12)ではなく`CameraRig`を採用したのは、`Camera`のフィールド
    (ピッチクランプ範囲・軌道半径等)が実行時に更新され続けるライブ状態向けであり、静的な
    初期値としての意味を持たないため。
  - `Program.cs`の実際のゲームループには一切組み込まれていない(`samples/SceneAuthoringDemo/
    MainScene.cs`と同じ、単独のプレビュー確認用フィクスチャという位置づけ)。
  - 旧L9アプリケーションのビルドで生成される`Generated/.../Scene1.SceneNodes.g.cs`/
    `Scene1.SceneRuntime.g.cs`を目視確認し、6ノード全ての階層・Component型引数・GetRef代入が
    Define本体のとおりであることを確認した。ブリッジCLIを実際のビルド済み(TFM/RID付き)dllに
    対して実行し、応答JSONが同じ6ノードの階層・値と完全一致することも確認した(F-L7-014参照)。
- Conformance: NotApplicable(機構が存在しない)
- Known deviations: None
- Next action: なし。
- **(2026-08、Status修正)** F-L8-009と同じく、削除済みのファイルを`Status: Implemented`のまま
  記述していたため`Removed`へ修正した。

## F-L3-012 — `CSharpGameEngine.Entities`(`Runtime.cs`、閉集合Archetypeランタイム)

- Layer: L3 — ECS / Job Runtime
- Parent capability: 静的(閉集合)Archetype ECS
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0033-Static-Archetype-Ecs-Migration.md、
  Docs/Decisions/ADR-0035-Generated-Entry-Point-And-Engine-Objects-As-Components.md、
  Docs/Decisions/ADR-0036-System-Order-Attribute.md、
  Docs/Specifications/Ecs-Soa-Systems.md
- Implementation evidence:
  - src/CSharpGameEngine.Entities/Runtime.cs
  - src/CSharpGameEngine.Entities/Bootstrap.cs
  - src/CSharpGameEngine.Entities/SystemOrderAttribute.cs
  - src/CSharpGameEngine.Entities/CSharpGameEngine.Entities.csproj
- Tests:
  - tests/CSharpGameEngine.Entities.Tests/WorldSpec.cs(生成された`Create`/`MoveArchetype`/
    `PlaybackMoveArchetypes`/swap-back位置表修復)
  - tests/CSharpGameEngine.Entities.Tests/ChunkListSpec.cs(チャンク跨ぎの伸長、`ref`返しの indexer、
    `RemoveAtSwapBack`の「詰め替えが起きたか」の戻り値)
- Reverse-engineered behavior:
  - `ISystem`/`IArchetype`は空のマーカーinterface。`Entity`は`record struct Entity(int Id)`で
    Generation無し(`Create`が`EntityArchetypeIndices.Count`をそのまま割り当てる単調増加ID)。
  - `World`/`State`は`partial struct`。`Runtime.cs`はEntity位置表、Scene instance表、遅延Destroy/
    Scene unloadキューを持つ。Archetypeごとのリスト・`Create`/`MoveArchetype`/`Detach`/`Destroy`/
    `PlaybackMoveArchetypes`はGameSystemGeneratorが同じ`partial struct World`へ生成する。
  - このプロジェクトは`<ProjectReference>`されない**ソース正本**。partial型がアセンブリ境界を
    またげないため、消費側は`<Compile Include Link>`で取り込む。単体`dotnet build`は
    「System 0件でも空の`Archetype` enumと空の`partial struct World`が生成されコンパイルできる」
    ことのスモークテストとして機能する。
  - M0012の`src/CSharpGameEngine.StaticArchetype/Runtime.cs`を、グローバル名前空間から
    `CSharpGameEngine.Entities`名前空間へ移して統合したもの(旧プロジェクトは削除済み)。
  - `Bootstrap`(M0015)は生成されたエントリポイントが作るシードEntityのComponent。`Done`は
    旧一回初期化契約の互換フィールドとして残るが、複数`[StartupScene]`の一回性は共有値ではなく
    各`SceneStartupSystemEmitter`生成Systemのprivate `Done`が担う(F-L7-017)。
  - `SystemOrderAttribute`(M0016)は「属性を一切使わない構造的推論」(ADR-0033決定事項2)からの
    唯一の例外。`ISystem`実装struct自身に直接(そのSystem1つだけの順序)、または`file`スコープの
    マーカークラスに(ファイル既定値)、どちらにも付けられる(ADR-0036)。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L7-015 — `GameSystemGenerator`(Analyzer/Model/IndexAssigner/Types・World・Loop・EntryPoint・Scene各Emitter)

- Layer: L7 — Static Compiler / Bake
- Parent capability: 静的(閉集合)Archetype ECS
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0033-Static-Archetype-Ecs-Migration.md、
  Docs/Decisions/ADR-0035-Generated-Entry-Point-And-Engine-Objects-As-Components.md、
  Docs/Decisions/ADR-0036-System-Order-Attribute.md、
  Docs/Specifications/Ecs-Soa-Systems.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/GameSystemGenerator.cs(`ISystem`+`IScene`の統合エントリポイント)
  - src/CSharpGameEngine.CodeAnalysis/GameSystemAnalyzer.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemModel.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemIndexAssigner.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemTypesEmitter.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemWorldEmitter.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemLoopEmitter.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemEntryPointEmitter.cs(M0015)
  - src/CSharpGameEngine.CodeAnalysis/GameSystemOrderAnalyzer.cs(M0016)
  - src/CSharpGameEngine.CodeAnalysis/GameSystemExecutionOrder.cs(M0016)
  - src/CSharpGameEngine.CodeAnalysis/SceneComponentSetCollector.cs
  - src/CSharpGameEngine.CodeAnalysis/SceneInstantiateEmitter.cs
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemEntryPointTests.cs(出力条件4種、SYSGEN021、
    Bootstrap Archetypeの注入が生成時のみであること、生成された`Main`の実行)
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemOrderTests.cs(ファイル名に逆らう
    `[SystemOrder]`順で生成`Main`を実行、System直接指定がファイル既定値より優先されること、
    既存タイブレークの後方互換回帰、SYSGEN022〜025)
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorEmissionTests.cs(出現順の連番命名、
    `Create`の戻り値、`World.Create`オーバーロード、記述順ディスパッチ、部分集合一致、Spawn除外、
    Contextのスレッディング、WorldSnapshot、決定性)
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorEndToEndTests.cs(部分集合ディスパッチの
    発火回数、フレーム末尾のMoveArchetype適用、Context到達、Spawn宣言Archetypeの生成、WorldSnapshot)
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDslGeneratorEmissionTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDslGeneratorEndToEndTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneArchetypeIntegrationTests.cs
- Reverse-engineered behavior:
  - 属性を一切使わない構造的推論: 先頭値渡し`Entity`=Entityスロット、末尾`ref State`=Stateスロット、
    それ以外の`ref`=Component、それ以外(ref以外)=Context。ref-kindだけで曖昧性なく判別できる。
  - `Update`はディスパッチ対象、`Spawn`はArchetype形状の宣言専用(ディスパッチされない)。
  - Archetype名は出現順の連番(`Archetype0`,`Archetype1`,…)。`ComponentIndices`は最初にその構成を
    宣言したメソッドの引数順で保持され、`World.Create`/`MoveArchetype`のパラメータ順もそれに従う。
    これによりArchetype名は完全に内部識別子になり、ユーザーコードに現れない。
  - 実行順序はソース上の記述順。整列キーは`GameSystemExecutionOrder.Compare`に一本化されており、
    実効値は①`[SystemOrder]`をSystem自身に直接付けた明示値 ②`[SystemOrder]`をfileマーカーへ
    付けたファイル既定値(いずれもADR-0036、既定0、①が優先)、そのうえで③`FilePath` Ordinal
    (既存タイブレーク) ④`SourceSpan.Start`の順。旧`GameSystemScheduler`のWrite→Read自動順序決定は、
    Component引数が全て`ref`になったことで原理的に成立しなくなり削除された。
  - `IScene.Root`のノード形状も同じArchetype空間へ入れるため、Scene DSL生成は
    独立したGeneratorではなくこのパイプラインの一部になっている(ADR-0033決定事項7)。
  - 出力は`CSharpGameEngine.Entities.ISystem`型がコンパイル内に実在する場合のみ(無関係なプロジェクトの
    汚染防止、ADR-0032実装時に確認済みの回帰への対策)。
  - エントリポイント(`GameSystem.EntryPoint.g.cs`)は、①OutputKindがアプリケーション
    ②`Compilation.GetEntryPoint`がnull ③ディスパッチSystemが1件以上 ④Context引数がゼロ、の4条件を
    すべて満たす場合のみ出力する(ADR-0035決定事項2)。`{Bootstrap}`のArchetypeも、出力すると決めた
    ときだけ`additionalComponentSets`へ注入する。④だけ落ちる場合はSYSGEN021で理由を報告する。
- Conformance: Matches
- Known deviations: 実行順序の自動決定を失い、記述順という手動規則に置き換わった(ADR-0033に明記)。
- Next action: なし。

## F-L7-016 — `GameSystemGenerator`診断(SYSGEN001〜025、SYSGEN009は欠番)

- Layer: L7 — Static Compiler / Bake
- Parent capability: 静的(閉集合)Archetype ECS
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0033-Static-Archetype-Ecs-Migration.md、
  Docs/Decisions/ADR-0035-Generated-Entry-Point-And-Engine-Objects-As-Components.md、
  Docs/Decisions/ADR-0036-System-Order-Attribute.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/GameSystemDiagnostics.cs
  - src/CSharpGameEngine.CodeAnalysis/AnalyzerReleases.Unshipped.md
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorDiagnosticsTests.cs(SYSGEN001〜020の
    各1件+正常系1件。SYSGEN009は欠番であることを、`out`引数がSYSGEN018として報告されるテストで記録)
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemEntryPointTests.cs(SYSGEN021)
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemOrderTests.cs(SYSGEN022〜025)
- Reverse-engineered behavior:
  - 属性ベースだった旧SYSGEN001〜020とM0012のSARCH001〜015をいずれも破棄し、構造的推論ベースの規則へ
    全面的に振り直した(`AnalyzerReleases.Shipped.md`が空のため「Removed Rules」管理は不要だった)。
  - SYSGEN009(「Componentパラメータは`ref`でなければならない」)は到達不能のため欠番。「refならComponent、
    ref以外ならContext」と定義した以上、refでないパラメータは壊れたComponentではなく正当なContextになる。
    `out`のような不正な渡し方はContext側の規則SYSGEN018が捕捉する(ADR-0015に同種の前例あり)。
  - SCENE009(1ノードのComponent数8以下)も削除。旧`World.CreateArchetype<T1..T8>`のジェネリック型引数
    上限に由来する規則で、閉集合方式には上限が無い。
  - SYSGEN021(M0015)は「エントリポイントを生成すべき状況(Exe・ユーザーのMain無し・System有り)なのに
    Context引数があるため生成できない」ケース。放置するとCS5001だけが出て原因が分からないため、
    どのSystemのどのContext型が妨げているかを名指しする。
  - SYSGEN022〜025(M0016、ADR-0036)は`[SystemOrder]`の誤用: 022=fileマーカーとして使ったが
    `file`スコープでない、023=1ファイルに複数のマーカーがある(ファイル既定値が定まらない)、
    024=2つ以上の異なるファイルが同じ明示的なファイル既定値を宣言している(ユーザー確認済みで
    ビルドエラーに設定。System単体の明示値同士の重複や、暗黙の0同士が並ぶのは対象外 —
    `ForAttributeWithMetadataName`が明示的に属性を付けた対象しか集めないため)、025=structに
    付けたが`ISystem`を実装していない(値がどこにも効かない)。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L3-013 — `CSharpGameEngine.Entities`(`StartupSceneAttribute`/`IScene`起動契約)

- Layer: L3 — ECS / Job Runtime
- Parent capability: 静的(閉集合)Archetype ECS
- Status: Implemented
- Verification: TestVerified、RuntimeVerified
- Intended specification: Docs/Decisions/ADR-0038-Startup-Scene-Declarative-Api.md、
  Docs/Specifications/Ecs-Soa-Systems.md#startupscenesceneentity宣言合成system
- Implementation evidence:
  - src/CSharpGameEngine.Entities/StartupSceneAttribute.cs
  - src/CSharpGameEngine.Entities/IScene.cs
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDslAnalyzerTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDslGeneratorEndToEndTests.cs
- Reverse-engineered behavior:
  - `StartupSceneAttribute`は`AttributeTargets.Class`、`Order`は`int`の`init`プロパティ(コンストラクタ
    引数ではなく名前付き引数`[StartupScene(Order = 0)]`で渡す設計、`SystemOrderAttribute`の
    コンストラクタ引数方式とは意図的に異なる)。
  - `StartupSceneAttribute`を付けられる現行対象は`IScene`実装classだけ。旧`[SceneEntity]`プロパティ列挙は
    ADR-0040で削除され、起動Entityは`IScene.Root`以下の`Node`宣言から導出する。
  - `StartupSceneAttribute`/`IScene`はいずれも`src/CSharpGameEngine.Entities/`に手書きで置かれ、
    消費側csprojのワイルドカード(`*.cs`)経由で伝播する。
- Conformance: Matches
- Known deviations: **(2026-08、M0049)** `[StartupScene(Order=N)]`属性は実際のScene定義では
  もう使われない。JSONの`"startupOrder"`フィールドが同じ役割を果たす(F-L7-019)。属性自体は
  ファイルとして削除していないが、新規Startup Sceneの起動順はJSON側で宣言する
  ([[M0049-Scene-Json-Persistence]])。
- Next action: なし。

## F-L7-017 — `GameSystemGenerator`(`SceneStartupSystemEmitter`、`IScene`合成System統合)

- Layer: L7 — Static Compiler / Bake
- Parent capability: 静的(閉集合)Archetype ECS
- Status: Implemented
- Verification: TestVerified、RuntimeVerified
- Intended specification: Docs/Decisions/ADR-0038-Startup-Scene-Declarative-Api.md、
  Docs/Specifications/Ecs-Soa-Systems.md#startupscenesceneentity宣言合成system
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/SceneStartupSystemEmitter.cs
  - src/CSharpGameEngine.CodeAnalysis/SceneDslAnalyzer.cs
  - src/CSharpGameEngine.CodeAnalysis/SceneEmitModel.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemGenerator.cs(合成`GameSystemResult`のマージと
    `SystemOrderByFqn`への追加登録)
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDslGeneratorEmissionTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDslGeneratorEndToEndTests.cs(複数Startup Sceneの
    独立instantiate、2フレーム目の非重複、生成`RunFrame`連鎖)
- Reverse-engineered behavior:
  - Roslynの1つのGeneratorは自分自身が生成した構文木を同じ実行内で再検出できないため、
    `[StartupScene]`から`ISystem`実装structのソースだけを生成して既存`GameSystemAnalyzer`による
    再検出に期待する設計は取れない。`SceneStartupSystemEmitter.BuildSyntheticSystemResult`が
    `GameSystemResult`を直接構築し、実際に検出されたSystemと同じ`List`へ合流させてから
    `GameSystemIndexAssigner.Assign`/`GameSystemLoopEmitter.Emit`/`GameSystemEntryPointEmitter`へ
    渡す(いずれも合成か実際に検出されたものかを区別しない)。
  - 合成Systemの実体(`{ClassName}__StartupSystem`、元クラスと同じ名前空間)は
    `SceneStartupSystemEmitter.EmitHiddenSystem`が同じGenerator実行内で生成する。
  - `[StartupScene(Order = N)]`は、実際に検出されたSystemの`SystemOrderByFqn`マップへ合成Systemの
    完全修飾名を追加登録することで、`[SystemOrder]`をSystem自身へ直接付けた場合と同じ優先度として
    扱われる(`GameSystemExecutionOrder`自体は無変更)。
  - Entityの出力Component集合は`AdditionalComponentSets`経由で登録される。実際に検出されたSystemの
    引数集合が先に登録されるため、同じ集合を既存Systemが宣言済みならArchetypeの出現順は変わらない
    (`UserEcsProject`の`{AppHost, Level}`はLevelLoadが登録済みのため、`MainScene`の同集合出力は
    新しいArchetypeを作らない — 生成コードで実際に確認済み)。
  - Scene構文解析/instantiate生成(F-L7-012/013)と起動タイミングを分離し、後者だけを
    `SceneStartupSystemEmitter`が合成Systemとして既存GameSystemループへ統合する。
- Conformance: Matches
- Known deviations: None
- Next action: なし。
- Bug fix (2026-08、ADR-0040 Phase 6): `[StartupScene]`が複数のシーンで共存する場合、生成される
  `{ClassName}__StartupSystem`は全員が唯一共有される`Bootstrap`Entityの`Bootstrap.Done`フィールドを
  直接読み書きしていたため、同一フレーム内で先に実行された1つが`Done=true`にした時点で残り全員が
  「実行済み」と誤認し、自分の`InstantiateScene`を一度も呼ばずに終わっていた(宣言順が2番目以降の
  `StartupScene`が実機で常に無視されるという形で発覚)。`SceneStartupSystemEmitter.EmitHiddenSystem`を、
  共有`Bootstrap.Done`ではなく各Systemインスタンス
  固有のprivate instance field(`Done`)を1回きりの判定に使うよう修正(System構造体インスタンスは
  `Run()`内で1つだけ生成され全フレームを通して同一インスタンスがrefで渡され続けることを利用)。
  `tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDslGeneratorEndToEndTests.cs`に、2つの
  `[StartupScene]`が生成された実際の`RunFrame`連鎖を通してどちらも独立にインスタンス化されることを
  固定する回帰テストを追加。
- **(2026-08、M0049)** `SceneStartupSystemEmitter`自体は無改造で流用しているが、入力の
  `SceneEmitModel.IsStartupScene`/`StartupOrder`は今はJSONの`"startupOrder"`フィールドから
  導出される(`[StartupScene]`属性からではない)。回帰テスト
  (`MultipleStartupScenes_BothInstantiateIndependently_...`)はJSON入力へ書き換えて存続
  ([[M0049-Scene-Json-Persistence]])。

## F-L7-018 — `GameSystemGenerator`診断(旧STARTUP001〜010、削除済み)

- Layer: L7 — Static Compiler / Bake
- Parent capability: 静的(閉集合)Archetype ECS
- Status: Removed(ADR-0040移行。後継はF-L7-013のSCENE117/118)
- Verification: NotApplicable(削除前はTestVerified)
- Intended specification: Docs/Decisions/ADR-0038-Startup-Scene-Declarative-Api.md
- Removal evidence: 旧`StartupSceneDiagnostics.cs`と旧StartupScene専用テストは削除済み。
  現行の診断登録は`src/CSharpGameEngine.CodeAnalysis/AnalyzerReleases.Unshipped.md`、
  実装とテストはF-L7-013を参照。
- Historical behavior:
  - カテゴリ`CSharpGameEngine.Entities.CodeGen`、すべて`Error`(既存SYSGEN/SCENE系と同じ規約)。
  - `[SceneEntity]`がプロパティ以外に付く場合(`AttributeTargets.Property`が言語レベルで拒否)、
    同一クラス内でEntity論理名(プロパティ名)が衝突する場合(C#の重複宣言禁止)、生成型名/Hint Nameが
    衝突する場合(元クラスのFQNから1:1に導出するため構造的に起こり得ない)は、専用診断を設けていない
    (SYSGEN009と同じ「欠番の理由をコードコメントに残す」方針)。
- Conformance: NotApplicable(STARTUP診断IDは存在しない)
- Known deviations: None
- Next action: なし。現行`[StartupScene]`誤用はSCENE117/118で報告する。

## F-L7-019 — Scene JSON永続化(`SceneJsonAnalyzer`、`*.scene.json` → `SceneEmitModel`)

- Layer: L7 — Static Compiler / Bake
- Parent capability: Scene Authoring(JSONをSceneの正本にする、第1段)
- Status: **Removed(2026-08-19、M0051第1段で撤回)** — [[M0051-CSharp-Source-Of-Truth-Restoration]]。
  ユーザーの「C#の型機能・Roslyn警告・コンポーネント補完を使いたい」という要望により、Sceneの正本を
  C#宣言型(ADR-0040)へ戻しJSONは完全撤廃した。撤回の追加根拠として、JSON全文再生成により
  ギズモで1ノードを動かしただけで565行が書き換わる(浮動小数点の表記ノイズ)ことが実測された。
  本エントリ以下の記述は撤回前の歴史的記録である。
- Verification: TestVerified。RuntimeVerified止まり(生成物の新旧比較を実測、実機でのゲーム起動・
  描画確認は未実施)。
- Intended specification: [[M0049-Scene-Json-Persistence]]
- Implementation evidence:
  - src/CSharpGameEngine.SceneModel/SceneJsonModel.cs(`System.Text.Json`用POCO。M0050で
    `src/CSharpGameEngine.CodeAnalysis/`から移設し、VSIX側`SceneJsonModelBuilder`とも共有)
  - src/CSharpGameEngine.CodeAnalysis/SceneJsonAnalyzer.cs(`AdditionalText`+`Compilation`→
    `SceneEmitModel`。値の解釈は宣言型で決める)
  - src/CSharpGameEngine.CodeAnalysis/GameSystemGenerator.cs(`Context.AdditionalTextsProvider`で
    `*.scene.json`を拾う`SceneJsonResults`プロバイダー。旧`SceneDeclarations`(C#構文由来)は削除)
  - src/CSharpGameEngine.SceneModel/SceneDslDiagnostics.cs(SCENE200〜204)
  - src/CSharpGameEngine.Entities/Runtime.cs(`InstantiateScene<TScene>`の制約を
    `where TScene:IScene,ISceneDefinition`から`where TScene:ISceneDefinition`へ緩和)
  - UserEcsProject/UserEcsProject.csproj(`<AdditionalFiles Include="Scenes/**/*.json" />`、2026-10-01からGame.props経由)
  - UserEcsProject/Scenes/MainScene.json、FirstPersonStageScene.json、
    PrimitiveShowcaseBoxScene.json、PrimitiveShowcaseSphereScene.json
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneJsonAnalyzerTests.cs(新規11件。リテラル値の
    宣言型別解決、enumメンバー名解決、複数子ノードの再帰的走査、SCENE200/201/202/203の各診断)
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDslGeneratorEmissionTests.cs、
    SceneDslGeneratorEndToEndTests.cs、SceneArchetypeIntegrationTests.cs、
    GameSystemGeneratorEndToEndTests.cs(JSON入力へ書き換えて存続。`InMemoryAdditionalText`/
    `GameSystemGeneratorTestHelper.RunGeneratorWithSceneJson`/`CompileAndLoadWithSceneJson`を新設)
- Reverse-engineered behavior:
  - Sceneは`*.scene.json`ファイルの**存在だけ**で検知される。手書きC#クラスの宣言は一切不要
    (`IScene`実装は廃止)。`className`/`namespace`はJSON側で初めて決まる。
  - `type`はComponent構造体の完全修飾名(`Compilation.GetTypeByMetadataName`で解決)。旧C# DSLと
    異なり`using`や囲む名前空間経由の解決は行わない — 常に完全修飾名を要求する。
  - 値の解釈は対象フィールド/プロパティの**宣言型**で決める。`enum`は文字列をメンバー名として
    `GetMembers(name)`から`IFieldSymbol{IsConst:true}`を解決、それ以外は`string`/`bool`/
    `float`/`double`/`int`/`long`のJSONネイティブ型をそのまま使う。フィールド解決は
    `public`かつ`const`でも`readonly`でもないものに限る(readonly除外はM0049実装中に発見した
    バグ修正 — 修正前はreadonlyフィールドを誤って受理し、生成コードがCS0191でコンパイル不能に
    なる潜在バグがあった)。
  - `[SceneAssets]`定数は解決せず、参照箇所へ解決済みの文字列を直書きする(インライン化)。
  - `SceneInstantiateEmitter`/`SceneStartupSystemEmitter`/`SceneComponentSetCollector`/
    `SceneEmitModel`はADR-0040で既にシンボルから式を再構築する設計だったため無改造で流用。
  - 生成コードの新旧比較(旧C#構文パイプライン↔新JSONパイプライン)を`git stash`で実機的に
    切り替えて実測: 差分は浮動小数点の書式(float32ビット列は完全一致)と資産定数のインライン化
    (設計通り)のみで、構造・Archetype番号・引数順序に差分は無いことを確認した。
- Conformance: Matches(承認済み計画通り)
- Known deviations:
  - 承認済み計画は`IScene.cs`/`Node.cs`/`StartupSceneAttribute.cs`の削除を指示していたが、
    実装中にテストプロジェクトの直接ファイルリンクとVSIX側解析器(F-L7-012、M0050まで温存)の
    依存を発見し、削除しない判断へ変更した(F-L3-011のKnown deviations参照)。
  - ネスト構造体・コンストラクタ引数・`[SceneFactory]`は今回のJSON形式では非対応
    (SCENE201/204で拒否)。
  - **(2026-08-19、M0050で解消)** VSIXのScene DesignerはM0050でJSON対応した(F-L8-011参照、
    [[M0050-VSIX-Scene-Json-Phase1-2]])。検出・表示・Hierarchy/ギズモ編集・保存まで実装済みだが
    実機確認は未実施。JSON内の正確なテキスト位置追跡(ダブルクリック時の精密ジャンプ)は
    次マイルストーンへ持ち越し。
- Next action: 実機でのゲーム起動・描画確認(FirstPersonStageのステージ・カメラ・ライト等)と
  M0050で実装したVSIX側JSON対応の実機確認(F-L8-011のFollow-up参照)がユーザー確認待ち。

## F-L1-007 — `CSharpGameEngine.Audio.Abstractions`(`IAudioBackend`/`AudioBackend<TBackend,...>`/`ProceduralAudioClip`)

- Layer: L1 — Core Kernel
- Parent capability: Audio再生(AudioSource/AudioListener)
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0039-Audio-Backend-Abstraction.md
- Implementation evidence:
  - src/CSharpGameEngine.Core/Audio/IAudioBackend.cs
  - src/CSharpGameEngine.Core/Audio/AudioBackend.cs
  - src/CSharpGameEngine.Core/Audio/ProceduralAudioClip.cs
- Tests:
  - tests/CSharpGameEngine.Core.Tests/Audio/ProceduralAudioClipTests.cs(サンプル数・振幅範囲・
    引数検証)
  - tests/CSharpGameEngine.Core.Tests/Audio/AudioBackendFacadeTests.cs(ハードウェア非依存のフェイク
    バックエンドで、ジェネリックFacadeが全メンバーを実際に転送することを検証)
- Reverse-engineered behavior:
  - `IAudioBackend<TEngine, TVoice>`は`CSharpGameEngine.Spatial.Abstractions.ICoordinateConvention`
    (ADR-0025)と同じく全メンバーが`static abstract`、`TEngine`/`TVoice`は`struct`制約。
  - `AudioBackend<TBackend, TEngine, TVoice>`は`Spatial<TConvention>`と同型の`static class`
    Facadeで、`TBackend.Method(...)`という型引数への直接静的呼び出しに解決される(JITの
    ジェネリック値型モノモーフィゼーションにより、仮想ディスパッチ・Boxing・Reflectionが原理的に
    存在しない)。
  - `SubmitBuffer`は`void`ではなく更新後の`TVoice`を返す。`TVoice`は不変値型であり、バックエンドが
    (例: 確保したネイティブバッファへのポインタのように)後で覚えておく必要がある情報をハンドル自体
    へ折り込んで返す以外に手段が無いため。
  - `ProceduralAudioClip.GenerateSineWave`はプラットフォーム非依存の純粋関数。リポジトリに音声
    アセット(.wav等)が1件も無いため、ファイルI/O無しで可聴PCMを得る唯一の経路として存在する。
- Conformance: Matches
- Known deviations: None
- Next action: 2つ目の具体的バックエンドが必要になった時点で、`AudioBackendFacadeTests`のフェイク
  実装で検証済みの拡張性を実バックエンドで再確認する。

## F-L4-001 — `CSharpGameEngine.Windows`(`XAudio2Backend`/`XAudio2EngineHandle`/`XAudio2VoiceHandle`)

- Layer: L4 — Domain Servers
- Parent capability: Audio再生(AudioSource/AudioListener)
- Status: Implemented
- Verification: TestVerified、RuntimeVerified
- Intended specification: Docs/Decisions/ADR-0039-Audio-Backend-Abstraction.md
- Implementation evidence:
  - src/CSharpGameEngine.Windows/Audio/XAudio2Backend.cs
  - src/CSharpGameEngine.Windows/Audio/XAudio2EngineHandle.cs
  - src/CSharpGameEngine.Windows/Audio/XAudio2VoiceHandle.cs
  - src/CSharpGameEngine.Windows/Audio/AudioException.cs
  - src/CSharpGameEngine.Windows/Audio/AudioDiagnostics.cs
  - UserEcsProject/AudioBackend.cs(プラットフォーム選択の集中点、`GameAudio`非ジェネリックFacade)
- Tests:
  - tests/CSharpGameEngine.Windows.Tests/Audio/XAudio2BackendTests.cs(実オーディオデバイスに対する
    CreateEngine→CreateVoice→SubmitBuffer→Start→Stop→DestroyVoice→DestroyEngineのスモークテスト、
    `[WindowsOnlyFact]`)
- Reverse-engineered behavior:
  - `IAudioBackend<XAudio2EngineHandle, XAudio2VoiceHandle>`を実装する唯一の具体的バックエンド。
    `readonly unsafe struct`でフィールドを持たず、ネイティブポインタは`XAudio2EngineHandle`/
    `XAudio2VoiceHandle`(いずれも内部に`nint`のみを持つ)の側に閉じ込める。
  - `XAudio2Create`→`IXAudio2::CreateMasteringVoice`→`IXAudio2::CreateSourceVoice`→
    `SubmitSourceBuffer`という`TerraFX.Interop.DirectX`経由のCOM呼び出しは、既存
    `CSharpGameEngine.Graphics.D3D12.GraphicsDevice`/`StageRenderer`と同じ作法(`->`呼び出し、
    `AudioDiagnostics.EnsureSuccess`によるHRESULT検査)。追加のNuGetパッケージは不要
    (`TerraFX.Interop.Windows`は既にD3D12用に参照済みで、XAudio2バインディングも同梱されている)。
  - PCMバッファは`NativeMemory.Alloc`で確保し、`DestroyVoice`まで解放を遅延する(`SubmitBuffer`が
    ハンドルへポインタを折り込んで返す設計、F-L1-007参照)。
  - VoiceはCOM `Release`ではなく`IXAudio2Voice::DestroyVoice()`で破棄する(XAudio2固有の非COM解放
    メソッド)。Engineは標準COM `Release`。
  - `UserEcsProject/AudioBackend.cs`が唯一このバックエンドを名指しする場所(`global using`
    エイリアス2行 + `GameAudio`Facade)。`UserEcsProject/Components.cs`/`Systems/RuntimeSystem.cs`/
    `Scenes/MainScene.cs`はいずれも`CSharpGameEngine.Audio.XAudio2`を直接参照しない。
- Conformance: Matches
- Known deviations: `UserEcsProject`はADR-0039の単一Entity制約を維持する。旧L9ランタイムにあった
  Entityごとの音源、Listener位置、距離減衰は現行へ移植していない。
- Next action: なし。

## F-L4-002 — `CSharpGameEngine.Runtime.Windows`(`PrimitiveMesh`/`PrimitiveType`/`RenderServer`)

- M0088: RenderServer accepts four typed submissions (rectangle includes forward/up), optional shadows and ordered reusable typed frame records with separate emissive payloads. TypedLightFrameTests verifies generated diagnostics, exact WARP recovery pixels and warm-buffer allocation.

- Layer: L4 — Domain Servers
- Parent capability: Scene DSL(ADR-0040 / M0019 Phase 6)
- Status: Implemented
- Verification: TestVerified、RuntimeVerified
- Intended specification: なし(新規ADRは書かず、既存フェーズと同じく`ADR-0040 / M0019 Phase 6`として
  コミット。ADR-0040自体の文書化はPhase 11で行う → 2026-08に完了、
  [[ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor]]改稿・[[M0019-CSharp-Scene-DSL]]参照)
- Implementation evidence:
  - src/CSharpGameEngine.Windows/Runtime/PrimitiveType.cs
  - src/CSharpGameEngine.Windows/Runtime/PrimitiveMesh.cs
  - src/CSharpGameEngine.Windows/Runtime/RenderServer.cs
  - src/CSharpGameEngine.Windows/Graphics/StageRenderer.cs(`ArbitraryTangentFor`を`internal`化して共有)
  - UserEcsProject/Systems/RuntimeSystem.cs(`LevelLoad`に`{Transform,PrimitiveMesh}`用の宣言順
    アキュムレータオーバーロードを追加)
  - UserEcsProject/Scenes/PrimitiveShowcaseScene.cs(デモ、`MainScene.cs`は無変更)
- Tests:
  - tests/CSharpGameEngine.Windows.Tests/Runtime/RenderServerTests.cs(デデュープ、全`PrimitiveType`の
    妥当性、実WARP GPUパイプラインでのBox描画)
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorEndToEndTests.cs(宣言順アキュムレータの
    核心契約、およびScene DSLのChildren由来Archetypeが実System由来の全Archetypeより後に処理される
    ことの固定回帰テスト)
- Reverse-engineered behavior:
  - `PrimitiveMesh{ PrimitiveType Type }`はサイズ指定を持たない単位サイズのプリミティブ
    (Unityの`GameObject.CreatePrimitive`と同じ規約)。配置先Nodeの`Transform.Scale`でサイズを決める。
  - `RenderServer.BuildPrimitiveRoster`は`StageRenderer.Create`が要求するロースター断片を作る
    純粋関数(静的ビルド関数 — このリポジトリに`Server`/`Registry`接尾辞のステートフルクラスの
    先例が無いため)。同じ`PrimitiveType`は1つの`StageMeshCreateInfo`にデデュープする。全プリミティブは
    1つの共有デフォルトマテリアルを使う(個別マテリアル指定はスコープ外)。
  - `StageRenderer.Create`はメッシュ/マテリアル/オブジェクトのロースターをCreate時に一括で要求し、
    構築後に追加するAPIを持たない(このリポジトリの全レンダラーに共通する制約)。そのため
    `LevelLoad`に、`{AppHost,Level}`用オーバーロードより先に宣言した`{Transform,PrimitiveMesh}`用
    オーバーロードを追加し、Scene DSLが撒いたプリミティブを先に集めてから.glbロースターへ連結して
    1回の`StageRenderer.Create`に渡す(同一struct内のオーバーロード宣言順がArchetype登録順・
    したがって1フレーム内の実行順に直結することを利用、ADR-0033決定事項1)。
  - デモの2つのプリミティブは、`MainScene.Root`の`Children`にせず、それぞれ独立した`[StartupScene]`
    (`Order=1`/`Order=2`)の単一Rootノードとして宣言している。Childrenにすると自動付与される`Parent`
    により別Archetype(`{Transform,PrimitiveMesh,Parent}`)になり、Scene DSL由来の追加Archetypeは
    実System由来の全Archetypeより後に登録される(ADR-0038)ため、`LevelLoad`がロースターを確定させた
    後に処理されてしまい描画から漏れる(実際にハマって確認した)。
  - この実装の過程で、`[StartupScene]`の複数シーン共存機能そのものに実バグを発見・修正した
    (F-L7-017の追記参照): 複数の合成Systemが共有`Bootstrap.Done`を直接読み書きしていたため、
    宣言順が2番目以降の`StartupScene`が常に無視されていた。
- Conformance: Matches
- Known deviations: プリミティブごとの色/マテリアル指定は無し。起動後の動的な追加・削除は不可
  (全レンダラー共通の「Create時にロースター確定」という制約を踏襲)。削除済み旧L9側の並行
  コンポーネント体系(`MeshRenderer`/`RenderSync`)とは統合せず、現行Scene DSLへ一本化した。
- Next action: なし。Phase 9(VSIX Scene ViewをSceneModel描画へ)がこの`PrimitiveMesh`語彙を前提として
  実装済み(F-L8-010参照)。

## F-L8-010 — Scene デザイナー: `.scene`からSceneModel(ライブRoslyn)描画への移行

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Scene DSL(ADR-0040 / M0019 Phase 9)
- Status: Implemented
- Verification: TestVerified(実機VS Experimental Instanceでの目視確認はユーザー側で実施予定)
- Intended specification: なし(新規ADRは書かず、既存フェーズと同じく`ADR-0040 / M0019 Phase 9`として
  コミット。ADR-0040自体の文書化はPhase 11で行う → 2026-08に完了、
  [[ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor]]改稿・[[M0019-CSharp-Scene-DSL]]参照)
- Implementation evidence:
  - tools/CSharpGameEngine.VSExtension/SceneModelRenderObjectBuilder.cs(新規)
  - tools/CSharpGameEngine.VSExtension/SceneHwndHost.cs(`.scene`読み込みをSceneModel解決へ全面改修)
  - tools/CSharpGameEngine.VSExtension/SceneRenderer.cs(`SceneRenderObject.NodeId`を`Guid`から
    `NodePath`へ変更)
  - tools/CSharpGameEngine.VSExtension/SceneWorkspaceService.cs(`AllModels`/`AllModelsChanged`を
    追加。アクティブドキュメント内の`IScene`実装を「最初の1件」だけでなく全件収集するよう拡張、
    既存の`CurrentModel`/`ModelChanged`は無改修)
  - tools/CSharpGameEngine.VSExtension/SceneEditorHub.cs(`AllSceneModelsChanged`を追加、`.scene`
    JSON専用だった旧Guidチャンネル(`SceneOpened`/`SelectedNodeChanged`/`PublishSelection`)を削除)
  - tools/CSharpGameEngine.VSExtension/SceneDesignerToolWindowPane.cs、
    SceneDesignerToolWindowControl.xaml(.cs)(新規、`HierarchyToolWindowPane`と同型のToolWindowPane)
  - tools/CSharpGameEngine.VSExtension/OpenSceneDesignerCommand.cs(新規、コードエディタコンテキスト
    メニューコマンド)
  - tools/CSharpGameEngine.VSExtension/SceneDesignerPresentationContracts.cs(複数`IScene`は
    `SceneSelector`コンボボックスから選択)
  - tools/CSharpGameEngine.VSExtension/OpenViewerCommandPackage.vsct(`OpenSceneDesignerCommandId`/
    `SceneDesignerContextMenuGroup`を追加、`0x0108`/`0x1023`は退役のため再利用せず)
  - tools/CSharpGameEngine.VSExtension/CSharpGameEngineViewerPackage.cs(`SceneFileEditorFactory`の
    `[ProvideEditorFactory]`登録を削除、`SceneDesignerToolWindowPane`の`[ProvideToolWindow]`を追加)
  - tools/CSharpGameEngine.VSExtension/GameEngineLayout.cs(既定レイアウトが`.scene`をドキュメント
    タブで開く処理を削除、Scene デザイナー ツールウィンドウのドッキングに置き換え)
  - 削除: SceneFileEditorFactory.cs、SceneWindowPane.cs、SceneWindowControl.xaml(.cs)
- Tests:
  - tests/CSharpGameEngine.VSExtension.Tests/SceneModelRenderObjectBuilderTests.cs(新規、Transform/
    PrimitiveMesh値抽出・回転未指定時のIdentityフォールバック・Gizmoフォールバック・NodePath同一性の
    7件、実解析済み`SceneModel`に対して検証)
  - tests/CSharpGameEngine.VSExtension.Tests/SceneDesignerPresentationContractsTests.cs
- Reverse-engineered behavior:
  - **トリガー方式の決定**: 当初案(`.cs`ファイル用の2つ目の`IVsEditorFactory`+`LOGVIEWID_Designer`)は
    実機での「開く方法」挙動が未検証のまま採用するリスクがあったため、このリポジトリに実績のある
    コードエディタコンテキストメニューコマンド(`IDM_VS_CTXT_CODEWIN`)+`ToolWindowPane`
    (`HierarchyToolWindowPane`と同じ、確実に動く実績パターン)へ変更した。ソリューション
    エクスプローラー/クラスビューのシンボルサブノードへのコマンド追加(当初のユーザー要望)は
    VS SDKの非公開・非対応拡張ポイントと判明したため採用しなかった。
  - `OpenSceneDesignerCommand`は`SceneWorkspaceService`の解析結果(`SceneEditorHub.ActiveAllSceneModels`)
    を再利用するだけで、コマンド自身はRoslynを一切呼ばない(`BeforeQueryStatus`/`Execute`とも
    キャッシュ済みの値を読むだけ)。
  - **(M0020 Phase 0で修正、F-L8-011参照)** `SceneModelRenderObjectBuilder`は
    `global::UserEcsProject.Transform`(プロジェクト固有、ハードコード)と
    `global::CSharpGameEngine.Runtime.Windows.PrimitiveMesh`(エンジン標準、
    Phase 6)の2つのComponent型を完全修飾名で認識していた。**実シーンのTransformは
    `global::CSharpGameEngine.Runtime.Windows.Transform`であり一致しないため、全ノードがIdentityで
    原点に重なって描画される実バグだった**(ユニットテストは`namespace UserEcsProject`の
    フィクスチャを自前宣言してハードコードに合わせていたためグリーンを維持していた)。
    Phase 0で`SceneComponentBindings`表へ置換した。`PrimitiveMesh.Type`(enum)の解決は
    `SceneValue.ReferencedSymbol`(`EnumMember`用)から行う — `SceneValue`には`float`/enum用の
    型付きアクセサが無いため、値抽出ロジックはこのタスクで新規に書いた。
  - プリミティブ形状の実メッシュ生成は、VSIXがnet10.0の`ProceduralMeshFactory`を直接参照できない
    (ADR-0008)ため、既存の`tools/CSharpGameEngine.Primitive.Cli`ブリッジ(Shape Paletteと共有)を
    そのまま再利用し、`UserEcsProject/Assets/Primitives/`の既存キャッシュも共有する
    (シェイプパレットはM0020 Phase 0で削除されたが、ブリッジとキャッシュは描画側が引き続き使う)。
    `PrimitiveType.Plane`はブリッジ側の命名(`"Rectangle"`)と異なるため変換マップを持つ。
  - `SceneHwndHost.Rebuild`は250msデバウンス済みのスナップショット更新のたびに走るが、対象クラスが
    見つからない場合(解析未追従・削除等)は前回の描画をそのまま残す(`Renderer`を破棄しない)。
    **(M0020 Phase 0で変更)** Phase 9時点は`SceneRenderer`がオブジェクト0件で例外を投げるため
    ロースターが空になるケースをホスト側でガードしていたが、Phase 0で例外側を撤廃し
    (グリッド/ギズモのみのフレームを許容するため)、ホスト側のガードも削除した。
  - **(M0020 Phase 0で実装、F-L8-011参照)** Phase 9時点では階層Transform合成を行わず、各ノードの
    ローカルTRSをワールドとして使っていた(ランタイム側も未実装の既存ギャップに合わせていた)。
    Phase 0でpre-order DFSによる`world = local * parentWorld`の畳み込みを実装した。
    `Level`コンポーネント(.glbステージ全体参照)は引き続き描画しない。
- Conformance: Matches(自動テストの範囲。実機VS Experimental Instanceでのコンテキストメニュー
  表示・`SceneSelector`・3D描画・ライブ更新の目視確認はユーザー側で実施予定)。
- Known deviations: ドラッグ&ドロップでの配置(`SceneNodeAdder`、`.scene`書き込み)はこのフェーズ
  完了時点で一時的に機能しなくなった(`SceneEditorHub.ActiveScene`を発行する経路が無くなったため、
  常に「シーンファイルを開いてください」エラーになる) — 既知の受け入れ済み退行だった。
  **M0020 Phase 0で当該機能ごと削除して清算した**(F-L8-006/F-L8-007をRemovedへ)。
- Next action: (2026-08更新)実機VS Experimental Instanceでの目視確認は未消化のまま
  [[M0020-Scene-Editor-Interaction]] Phase 0の完了基準へ統合した。あわせてPhase 0では
  `SceneModelRenderObjectBuilder`のTransform型ハードコード(`global::UserEcsProject.Transform`)に
  起因する「実シーンが全ノードIdentityで原点に固まって描画される」バグの修正と、階層Transform
  合成を行う([[ADR-0041-Scene-Editor-Interaction-Architecture]])。旧`ScenePickerDialog.xaml(.cs)`は
  その後のコミット(`eeaae1b`/`40f08e4`)で
  `SceneDesignerToolWindowControl`内の`SceneSelector`コンボボックスへ置き換えられて削除済み
  (歴史的記録として残す)。

## F-L8-011 — シーンエディタ インタラクション基盤(ナビゲーション・ピッキング・ギズモ・編集トランザクション)

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Scene DSL双方向エディタ(ADR-0040)の操作系
- Status: **Implemented**(M0020 Phase 0〜7+後続Task C実装済み)
- Verification: **TestVerified**(純粋なカメラ数学・入力状態機械・CPU pick・ギズモ形状/ヒット/
  拘束ソルバ・Transform数学・編集トランザクション・Roslyn最小差分・live text世代guard)。
  `SceneHwndHost`/D3D12/Visual Studio live buffer/Undoの製品配線は**CodeVerified**止まりで、
  Experimental Instance目視は未実施([[M0020-Scene-Editor-Interaction]]のPhase別表を参照)。
  [[M0030-SceneView-Gizmo-Transform-Editing]]で受け入れ基準の監査を行い、失敗経路(対象ノード消失・
  外部編集による式化・親world退化・ドラッグイベント重畳)のテストを9件追加して契約を固定した。
  実機確認(2026-08-17)で以下4件のフィードバックを受け修正した: ①既定ActiveToolを
  `Select`→`Move`へ変更(選択即ギズモ)、②コミット直後`CurrentRenderObjects`が再解析待ちで
  ギズモが一瞬旧位置へ戻る不具合を`EditTransactionController.CurrentPreviews`経由の即時パッチで
  解消、③Hierarchy選択時に`IVsTextManager.EnumViews`(非アクティブ化)経由でマーク行を
  エディタ最上部へスクロール、④ソース反映前後でcaret/スクロール位置をsave/restore(対症療法、
  根本原因未特定)。②③④は実機での修正確認が未実施。続けて実機フィードバックで、Hierarchy選択
  ではソースがマーク・スクロールされるがシーンビュー(ビューポート)選択では反映されない非対称
  が報告され、`CSharpGameEngineViewerPackage.OnSelectionChangedForHierarchy`がHierarchy起点以外
  ではマークを明示的に消すだけだった実装(M0020設計時点の意図的制限)を、Viewport起点でも
  `SourceMarkerService.Update`へ実際の選択集合を渡すよう修正(⑤)。続けてMove/Rotate/Scaleを
  排他的な`SceneActiveTool`切替(W/E/R)からBlender/Unity寄りの常時同時表示・操作へ変更(⑥)。
  旧`RotateRingRadius`と`ScaleAxisLength`が完全一致し実際に座標が重なっていた不具合を含め半径帯を
  再設計し、`TransformGizmo.TryHitTransform`で3系統+新設Local/Globalトグルアイコンをレイ距離
  最近傍で統合ヒットテストするようにした。ドラッグ系統は従来ambientな`ActiveTool`ではなく
  ヒットした`GizmoHandleId`から`GetHandleFamily`で導出するよう修正(3系統同時ヒット化で表面化する
  「Handleと系統の食い違い」を解消)。W/E/Rキーと`SceneViewState.ActiveTool`永続状態は不要になり
  削除した。続けてサーフェススナップ(既にM0020 Phase 5で実装済みの位置+法線回転整列)の起動方法を
  `N`キーホールドからShift+Ctrl押下中に現れる専用ハンドル`GizmoHandleId.SurfaceSnapPlane`
  (中心の平面四角、輪郭はfill色のRGB反転)のドラッグへ変更(⑦、頂点スナップ`V`キーはそのまま)。
  マウス静止中でも1フレーム以内に武装表示が反映されるようRenderingティックで`GetKeyState`を
  ポーリングする方式(既存のFly Fast/Precisionポーリングと同型)を新設し、新規WndProc/WPFキー
  配線は追加していない。`CanEditTransform`にHandle優先分岐を追加したことで、ドラッグ開始時に
  同じ条件を別途チェックしていた`CanSurfaceSnapSelection`は不要になり削除した。続けて視認性
  改善3件(⑧): `ScaleUniform`キューブの基準色が既に純白でhover時のlerpが無効化されていた
  実バグを修正(濃いグレー既定+hoverで明転)、hover/Active時に白黒二重ワイヤーフレーム輪郭を
  追加、`AxisStart`(0.12→0.26)でMove矢印を`ScaleUniform`キューブから引き離し、`AddRing`へ
  内外半径を持つ平帯Triangleを追加してRotateリングに面積を持たせた(半幅は既存の当たり判定
  許容量`RotateRingThickness`を流用)。続けてシーンビューのCtrl+Z/Y高速化(⑨): ADR-0040の
  「C#ソースの`ITextUndoTransaction`だけが唯一の編集履歴」という設計は維持したまま、
  ①`RenderHostGrid.PreviewKeyDown`でCtrl+Z/Y/Ctrl+Shift+Zを横取りし対象.csファイルの
  `ITextUndoHistory`を直接呼ぶことでフォーカス起因の無反応を解消、②M0030の即時パッチ手法
  (`ApplyFinalTransformsToRenderObjects`)をUndo/Redoにも転用し、直近のTransformコミット
  (Move/Rotate/Scale/SurfaceSnap限定)のbefore/afterを`RecentTransformCommits`/
  `RecentTransformUndone`スタックへ記録してRoslyn再解析を待たず3Dビューを即時に巻き戻す/
  やり直す。どこにも書き込みを行わない表示専用の先読みキャッシュで、Property編集・複製・
  別Scene切替時は破棄する。実機目視が残っているため`Verified`へは上げていない。続けて実機で
  ⑨のCtrl+Z修正が効かないと報告され(M0037)、真因を再調査した: VSシェルのキーバインド解決が
  Ctrl+Zを`Edit.Undo`(`guidVSStd97`/`cmdidUndo`)コマンドとして消費し、そもそもWPFの
  `PreviewKeyDown`まで到達していなかった(⑨の対症療法は前提が誤りだった)。
  `WindowPane`(`ToolWindowPane`の基底)の`IOleCommandTarget`はexplicit+sealedでoverride不可だが
  内部でペインの`IMenuCommandService`へ委譲するため、`SceneDesignerToolWindowPane.OnToolWindowCreated`
  で`VSStd97CmdID.Undo/Redo/MultiLevelUndo/MultiLevelRedo`を`OleMenuCommandService`へ登録し、
  `BeforeQueryStatus`でビューポートのキーボードフォーカス+ピン留め有無を見て`Supported`を切り替える
  方式(⑩)へ置き換えた。WPFの`PreviewKeyDown`配線は削除。副次的に、`SceneWorkspaceService.TryUndo/
  TryRedo`がRDT未登録ファイル(.csタブを閉じた後)で無言失敗する潜在バグ(`FindAndLockDocument`は
  RDT_NoLockで未登録でも成功HRESULTを返す)も`EnsureDocumentOpen`で解消。併せて実機フィードバックで
  Move矢印を`AxisStart 0.26→0.40`/`AxisLength 1.35→1.60`/`RotateViewRingRadius 1.5→1.85`へ拡大し(⑪)、
  UE5風のドラッグ中HUD(⑫)を追加した: D3D12パイプラインにテクスチャ/文字描画能力が皆無で
  ネイティブHWNDのairspace制約でWPFテキストも重ねられないため、`GizmoDragHud.cs`で数字を
  ベクターフォント(線分ストローク)としてオーバーレイに直接焼き込む。Move中は開始点の黄色マーカーと
  現在位置までの黄色破線+移動量数値、Rotate中は開始角の黄色マーカー・半透明の扇+輪郭線・
  マウス方向の黄色破線・角度数値(`Atan2`の(-π,π]折り返しをまたいでも破綻しないよう独自の
  角度累積関数で連続値を保持)、Scale中はハンドルに応じて1軸なら1値/Uniformなら3値の倍率数値を表示する
  (面(2軸)スケールは今回スコープ外)。既存の`MarqueeOverlay.ToWorld`と同じ手法でスクリーン座標を
  ピクセル一定サイズのワールド座標へ逆投影し、`SceneRenderer.SetOverlay`の頂点上限(4096)超過時は
  ギズモ本体を守るためHUDのみを落とす防御を追加。実機目視が残っているため`Verified`へは
  上げていない。続けて実機で「まだ編集の速度が遅い」と報告され(M0038)、⑨⑩を含むこれまでの
  高速化が**いずれも見た目の巻き戻りを隠していただけ**で待ち時間自体を短縮していなかったことが
  判明した。実測の主因は(a)コミット完了まで次の操作がブロックされること、(b)1操作あたり
  プロジェクト全体のコンパイル取得が2回、(c)250msデバウンス。対策として`SceneModelEditApplier`
  (⑬)を新設し、編集後の`SceneModel`をメモリ上で関数的に予測して
  `SceneEditorSessionService.TryPublishPrediction`で pin中snapshotとして即時配信する。
  `SceneDocumentSnapshot`がRenderer/Hierarchy/Inspector/ソースマーカーの唯一の配信点なので、
  1回のpublishでUI全体が下流無改造で更新される。予測が配信できた時点でトランザクションを`Idle`へ
  戻し、ソース書き戻しは背後で直列に継続する(ADR-0040のC#テキスト単一正本は維持、予測は
  M0030/M0036と同じ表示専用の先読みで、再解析が真実で上書きするため外れても自己修復する)。
  対応操作は`SetTransformOperation`と`SetComponentMemberOperation`(リテラル式のみ)で、構造編集は
  今回対象外(扱えない操作が混ざれば丸ごと諦めて従来経路へ落とす)。副次的に生じた危険2件も同時に
  塞いだ: 書き戻しの並行実行による「直前の編集の打ち消し」を`PendingCommitWrite`チェーンの直列化で、
  書き戻し中のCtrl+Zによる「テキストUndoと先読みUndoの食い違い」を
  `SceneDesignerUndoCommandPolicy.CanExecute`(`Supported`は落とさず`Enabled`のみ落とす — 落とすと
  Ctrl+Zが別ドキュメントへ流れるため)で抑止。実機目視が残っているため`Verified`へは上げていない。
  出荷直後に実機で「Undoが直前の1回しかできない」と報告され(M0039)、⑬の予測配信が2つの構造欠陥を
  持ち込んでいたことが判明した: (a)予測が実解析の`DocumentVersion`連番を消費し、
  `SceneEditConflictMerger`のfast path判定を機能不全にしていた、(b)ローカル完了とソース書き戻し
  成功の対応関係が保証されておらず、直列書き戻しチェーンの途中で1件でも失敗すると、それより後ろの
  書き込みが「実際には起きていない編集」の上に積み上がり、`RecentTransformCommits`(先読みUndo
  スタック)と実際の`ITextUndoHistory`が分岐していた。(a)は予測snapshotの`DocumentVersion`を
  元のまま据え置く(`AnalysisGeneration`のみ新値を払い出す)ことで修正、(b)は書き戻しチェーンを
  `Task<bool>`(健全性フラグ)化して前段が不健全なら以降を中断・失敗として伝播し、チェーンで
  最初に不健全化した箇所だけ`SceneEditorSessionService.RefreshPinnedSnapshotFromSourceAsync`
  (新設、Roslynへ直接再解決してpin中snapshotを真実で強制上書き)と
  `SceneHwndHost.ResyncAfterCommitFailure`(新設、先読みUndo/Redoスタックを丸ごと破棄 —
  既存のProperty編集/複製/別Scene切替時のClearと同じidiom)を呼ぶことで修正した。続けて実機で
  「選択直後はソースが正しくマークされるが、編集を確定すると無関係な行(ファイル末尾付近等)へ
  再スクロールしてしまう」と報告され(M0040)、M0020/M0032時点からの既存不具合が判明した:
  編集確定後の再解析着地イベント(`SceneEditorHub.SceneModelChanged`)の購読先だけが、
  Hierarchyパネル専用の`SceneEditorHub.HierarchySelection`(Hierarchy起点のクリックでしか
  更新されない静的フィールド)を参照しており、Viewport起点の選択・編集ではこれが一切更新
  されないため、セッション中最後にHierarchyパネルでクリックした無関係な位置へ再解析のたびに
  再スクロールしていた。統一選択サービス(`SceneEditorSessionService.Selection.
  CurrentSelection`)から今の選択を都度解決するよう修正し、解決ロジックを
  `SceneSelectionPathResolver`(新設、VS SDK非依存の純粋関数)へ切り出してViewport/Hierarchy
  両経路で共有した。実機で「直ってない」と再報告され(M0041)、M0040とは別の真因が判明した:
  `SourceMarkerService`は選択が実際に変わらない限りスクロールしないため(消去法で確認)、
  単純なTransform編集ではM0040の修正経路も含めソース側は一切スクロールを起こさないはずだった。
  実際にスクロール位置を触っていたのは`SceneWorkspaceService.ApplyTextAsync`内の
  `CaptureViewPositions`/`RestoreViewPositions`(ビューポート先頭行の保存・復元、M0036由来)
  だけで、M0038が書き戻しを非同期化・背後直列化したことで捕獲タイミングが「コミットした瞬間」
  から「書き戻しチェーンが自分の番に到達した瞬間」まで遅れ、捕獲時点でズレていたスクロール
  位置をそのまま書き戻し後に復元してしまっていた。ビューポート先頭行の保存・復元を撤去し、
  caret位置の保護だけを残してスクロール位置の決定権を`SourceMarkerService`へ一本化した。
  さらにM0041の実機検証で「移動するとソースの先頭(Root宣言位置)が表示される」と報告され、
  M0042で調査したところマーカー機構自体は無罪で、実際には`StaticMesh`/`PrimitiveMesh`を
  持たないノード(Root自身・整理用の空グループノード)が`GizmoMeshFactory`のプレースホルダー
  立方体(ワールド原点付近に描画)としてピッキング対象へ紛れ込み、誤選択・誤ドラッグされて
  いたことが判明した。以前保留にしていた「SkySphereが空クリック判定に紛れ込む」問題も同種の
  穴だったため、`SceneRenderObject`へ`IsPickable`フラグ(既存の`IncludeInBounds`と同じ
  パターン)を新設し、メッシュ未宣言ノードとSky domeの両方を`ScenePicker`の
  `TryPick`/`PickMarquee`/`TryVertexSnapQuery`/`TrySurfaceSnapQuery`から除外して両方まとめて
  解消した。Hierarchyパネル経由の選択は`ScenePicker`を経由しないため無影響(メッシュ無し
  ノードも引き続きHierarchyからは選択・編集できる)。それでもなお「編集するとマーク位置が
  スクロールで見えなくなる」症状が残ったため、M0043で**原因特定を諦めて要件を構造で保証する
  方針へ切り替えた**: M0040/M0041/M0042はいずれも実在するバグだったが決定打ではなく、飛び先も
  毎回変わっていた。修正前の`SceneSourceMarkerService.Update`は「選択が変わったか」を
  `BufferChanged||!LastScrolledPaths.SequenceEqual(Paths)`から推測していたが、`BufferChanged`は
  `MarkedBuffer==null`(=`Clear()`後)でも必ずtrueになり、`Clear()`は編集サイクル中の過渡状態
  (解析エラー・文書未オープン・pin不一致)で普通に走る——つまり過渡状態を一度経由すると次の
  正常な再解析が選択変更と誤認してスクロールする構造だった。スクロール可否を`Update`内部の
  推測から**呼び出し側が明示する`SceneSourceMarkerScrollBehavior`**へ移し、判定を
  `SceneSourceMarkerScrollPolicy.ShouldScroll`(新設、純粋static)へ委譲した。再解析の着地
  (`OnSceneModelChangedForSourceMarkers`)は必ず`PreserveScroll`を通るため、過渡状態が何で
  あってもスクロールが起きえなくなった。それでも「一瞬最上部へ飛んでから戻る」ちらつきが残った
  ため、M0044では推測をやめて先に実測した: `SceneTextChangePlanner.Plan(SourceText,SourceText)`は
  1リテラルの変更でも**ファイル全体(18,531文字)を覆う1個のTextChange**を返していた
  (`SourceText.GetTextChanges`は`WithChanges`由来でない無関係なテキスト同士では接頭辞・接尾辞を
  切り詰めない。`CommitAsync`が毎回`SourceText.From(Edited.Root.ToFullString(),…)`でまっさらな
  SourceTextを作っていたため常にこの経路だった)。つまりバッファ全体が削除・再挿入され、ビューの
  レイアウト基準点ごと作り直されていた。`Edited.Root`は`Context.Root`へReplaceNodeした結果で
  green nodeを共有しているため、`SyntaxTree.GetChanges`による構文ツリー差分へ切り替えた
  (実測で10文字/離れた2箇所でも独立2変更・計11文字)。併せて変更ごとの`Buffer.Replace`を
  `ITextEdit`の一括`Apply`へまとめ、バッファ変更・TextChangedイベント・レイアウトを1回にした
  (VSに描画抑制の公開APIは無く、これが等価な手段)。ただしITextEdit化の直後に実機で
  「ギズモで移動してもY座標が書き換わらない(成功扱いのまま無言で消える)」退行が出たため、
  適用部だけ実績のあるBuffer.Replaceループへ戻した。ツリー差分側は本番と同じ経路
  (注釈付きApplyBatch・同型兄弟20件)を再現した一時テストで健全性を確認できており、残る新規
  コードはVS SDK依存でテストできないITextEdit適用部だけだったための切り分け。併せて、ツリー
  差分を適用した結果が編集後ツリーの全文と一致するかをCommitAsyncで毎回検算し、一致しなければ
  従来の全文差分へ落とすフォールバックを入れた。副次的に`ToFullString()`による全文文字列化も
  消え、M0038の遅さ調査で挙げた項目の1つが解消した。実機目視が残っているため`Verified`へは
  上げていない。続けて実機で「1,2,3(スクロール位置・ちらつき・ソース反映)はOKだが、Ctrl+Zで
  戻るのは1段階だけ。ツールバーの「元に戻す」▼履歴も登録されない」と報告され(M0045、
  [[M0045-Undo-Multi-Step-Native-Key-Detection]])、M0037で追加した標準コマンド登録自体が
  真因と判明した。AskUserQuestionでの切り分けにより、C#ソースへの書き込みは3回連続で正しく
  反映される(書き込み自体は健全)一方、コードエディタへフォーカスを移してCtrl+Zを連打しても
  1段階も戻らないことが確定した——`SceneDesignerUndoCommandPolicy.ShouldHandle`はビューポートの
  WPFフォーカスが無ければ`Supported=false`を返すはずで、ペインが無関係なはずのコードエディタ
  側でも症状が出ることは、`VSStd97CmdID.Undo`/`Redo`/`MultiLevelUndo`/`MultiLevelRedo`を
  `GUID_VSStandardCommandSet97`としてツールウィンドウの`OleMenuCommandService`へ登録すること
  自体が、ペインの表示・フォーカス状態に関わらずVS全体のUndo管理(ツールバー・ドロップダウン・
  コードエディタのCtrl+Z)の接続を壊していることを示す決定的な証拠だった(加えて
  `MultiLevelUndo`の`BeforeQueryStatus`は`Supported`/`Enabled`の真偽値しか設定せず、
  ドロップダウンが要求する`OLECMDTEXT`を一切埋めていなかった——空履歴の直接原因)。
  `SceneTextBufferWriter.TryApply`は標準的な`ITextUndoHistoryRegistry`経由の
  `CreateTransaction`/`Buffer.Replace`/`Transaction.Complete`というVSエディタの正規のUndo機構を
  使っており、そちら自体には問題が無かった。M0045では4つの標準コマンド登録を全廃し、代わりに
  WASDQE Fly移動やShift/Ctrl修飾検出が既に使っている`CompositionTarget.Rendering`ティックごとの
  `NativeMethods.GetKeyState`ポーリング(`SceneHwndHost.TickFlyIfNeeded`と同じ経路)でCtrl+Z/
  Ctrl+Yを検知する`TickUndoRedoIfNeeded`(新設)へ置き換えた。`GetKeyState`はメッセージの
  到達自体を必要とするが、VSのアクセラレータテーブルによる「コマンドとしての消費」は
  メッセージを破棄するわけではなく届いた後の扱いを分岐させるだけなので、Ctrl+ZがEdit.Undoとして
  消費されたかに関わらず物理的な押下状態を正しく反映する——これにより標準コマンドテーブルへ
  一切触れずにシーンビューでのCtrl+Z/Ctrl+Yを実現した。押しっぱなし連射を防ぐ立ち上がりエッジ
  判定は`SceneUndoRedoKeyPolicy.IsRisingEdge`(新設、純粋関数)へ切り出し、エッジ追跡自体は
  フォーカスと独立させてフォーカスは発火可否にのみ使う(ビューポート外で押しっぱなしのまま
  フォーカスが戻ったときの誤発火防止)。実際のUndo/Redo実行経路(`Session.TryUndo()`/
  `Session.TryRedo()`/`Host.TryFastUndo()`/`Host.TryFastRedo()`/
  `SceneDesignerUndoCommandPolicy`)は無改造のまま流用した。しかしM0045投入後も実機で
  「Undo履歴が全く残らない」ままであり、加えて「ソースエディタで素早く入力している間はいいが、
  1秒ほど入力を止めるとフォーカスがソースエディタからヒエラルキーへ移る」と報告された(M0046、
  [[M0046-Hierarchy-Focus-Steal-Fix]])。この新症状が決定的な手がかりであり、**Undo症状の
  原因でもあった**: `HierarchyToolWindowControl.Tree_SelectedItemChanged`が無条件に
  `this.Tree.Focus()`を呼んでおり、このハンドラは矢印キーでのツリー内移動の受け口である一方、
  再解析による`ItemsSource`差し替えの後にWPFが**遅延生成**した`TreeViewItem`へスタイルSetter
  (`IsSelected`のTwoWayバインド)経由で選択が入ったタイミングでも発火する。
  `ItemContainerGenerator`はコンテナを遅延生成するため、これは`Populate`の同期ブロックが
  終わった後の測定・配置パスで起きる——つまり`SuppressSelectionPublish`ガードは同期スコープで
  しか張られていないのに守るべきイベントは非同期で来るため、構造的にすり抜けていた(副次的欠陥
  として`ApplySelectionToTree`の`finally`が`SuppressSelectionPublish=false`を無条件代入し、
  呼び出し元`Populate`のガードを黙って解除してもいた)。再解析は入力の250msデバウンス後に走る
  ため「1秒ほど止める」とちょうど着地し、そのたびにフォーカスがヒエラルキーへ移ることで、
  報告されていた4症状すべて——ツールバー▼履歴が空(アクティブなVSウィンドウがUndo管理を持たない
  ツールウィンドウになる)、コードエディタでCtrl+Zが効かない(押した時点でフォーカスが抜けている)、
  ギズモ移動直後に履歴ボタンが0.5秒で消える、Undoが1段階しか効かない(最初の1回だけ強奪前に
  間に合う)——が説明できた。クラスのdocコメント自身が「source focus is never changed by those
  requests」と書いており、ADR-0041の「ツールウィンドウはフォーカスを奪わない」原則にも反する、
  実装が自分の文書化した契約に違反していた事例だった。M0046ではフォーカスを「奪う」のをやめ
  「既に持っていること」を選択発行の条件へ反転させ(`HierarchyFocusPolicy.
  ShouldPublishSelectionChange`、新設・純粋関数)、`Tree_SelectedItemChanged`から`Tree.Focus()`を
  削除した。マウス左/右クリックの2箇所はユーザーがツリーを直接クリックした本物のジェスチャー
  なので`Tree.Focus()`を残し、`ApplySelectionToTree`のガードは保存・復元へ変更した。
  M0045の標準コマンド登録の全廃自体は維持する(`OLECMDTEXT`未実装は実在の欠陥であり、
  ツールウィンドウのペインへ標準コマンドIDを登録する設計も筋が悪い)が、**M0045は真因では
  なかった**。実機目視が残っているため`Verified`へは上げていない。
  M0046対応後も実機で「3,成功したが遅い」「7,構造編集がCtrl+Zで取り消せない」と報告され、
  ユーザーからアーキテクチャの再考(「UE5/Unity/Blenderのようにきびきび動かして。C#ソースを正と
  する考え方も辞めてよい」)を求められた。M0038(予測レイヤー)は編集ループにRoslyn往復
  (250msデバウンス+全解析+全UI再構築)が残ったままの中途半端な対処だったと判断し、M0047
  ([[M0047-Inmemory-Edit-Model]])でセッション中の正本をメモリ内`SceneModel`へ移した
  (C#ソースへは保存操作でのみ書き込む。将来のJSON保存化は次段、優先順位はユーザー指定で
  「エディタの即応性を先に出す」)。`SceneModelEditApplier`(M0038時点は値編集のみ予測)へ
  構造操作6種(Add/Rename/Remove/Reparent/Reorder/Duplicate)を追加し、新設の`SceneEditDocument`
  (immutableな`SceneModel`のスナップショット列だけで無制限Undo/Redoが成立し、`IsDirty`は
  保存時点モデルとの参照比較で判定)をセッションの正本にした。実装中、`SceneProjectionReconciler`
  が同じ親の兄弟間でしか同一性を追跡しない設計(既存、ドキュメント記載どおり)のため、親を跨ぐ
  Reparentで移動したNodeへ新しいIDが振られてしまう不具合を発見し、移動前後のPathを並走させて
  IDを差し戻す専用の補正パスで解消した。保存は新設`SceneSourceEditor.ApplyForSave`が
  Documentの全履歴を実C#構文へ一度だけ再生する — 既存の`ApplyBatch`は全操作対象が呼び出し時点の
  投影に既に存在することを要求するため、セッション中に追加したNodeを後続の操作(Add→Rename等)が
  参照する一般的な流れに対応できない制約があり、Annotation登録をNode作成時点まで遅延させ、
  実在Nodeは最初の構造編集でSpanがずれる前に全て事前Annotateすることで解決した。この過程で
  `CreateNodeCreation`が"new"と"Node"の間にtriviaを持たない構文を生成し、再解析すると
  "newNode"という1トークンに読めて追加したNodeが消える既存の潜在バグを発見・修正した
  (`NormalizeWhitespace()`追加。既存の`AddNode`テストは再解析までは確認しておらず気付けなかった)。
  `SceneEditorSessionService`の非同期直列コミットチェーン(`CommitAsync`/`TryPublishPrediction`/
  `RefreshPinnedSnapshotFromSourceAsync`、M0038〜M0046)と`SceneDesignerToolWindowControl`の
  `PendingCommitWrite`直列化を撤去し、`Apply`(同期)・`SaveAsync`(明示保存)・Document経由の
  `TryUndo`/`TryRedo`へ置き換えた。保存トリガーはビューポートフォーカス時のCtrl+S
  (M0045と同じ`GetKeyState`ポーリング、VS標準コマンドテーブルには触れない)のみで、コードエディタ
  側Ctrl+Sでの自動フラッシュ(`IVsRunningDocTableEvents`)は実装せずリスクを避けた
  (既知の制限として明記)。外部でC#が変化した場合、未保存編集が無ければDocumentを黙って
  実解析へ追従させ、未保存編集があれば確認ダイアログを出す。VSExtension.Tests 596/596
  (既存568+新規28、リグレッションなし)。実機検証で「ヒエラルキーの改名・削除・親変更・
  並べ替えができない」「Undo後にRedoできない」「Ctrl+Sで保存されずC#ソースから再読み込み
  される」と報告され、M0048([[M0048-Hub-Sync-And-Spurious-Reanalysis-Fix]])で2件の根本原因を
  特定・修正した。(a)`HierarchyToolWindowControl`は`SceneEditorHub.SceneModelChanged`
  (実Roslyn再解析だけで更新される静的Hub)を表示に使い続けていた——M0047でDesignerビューポートは
  `Session.PinnedSnapshotChanged`(Document更新)を直接購読するよう切り替えたが、Hierarchyは
  この配線から取り残されており、編集のたびにビューポートだけ更新されヒエラルキーの表示が
  固まっていた(「シーンビューは消えるがノードは消えない」と一致)。`CSharpGameEngineViewer
  Package`が`Session.PinnedSnapshotChanged`を購読し`SceneEditorHub.PublishSceneModel`へ
  橋渡しすることで解消した。(b)`OnWorkspaceModelsChanged`は`!Document.IsDirty`を「失うものが
  無い」の代理指標として`Document.ResetTo`(Undo/RedoスタックをClear)の可否判定に使っていたが、
  `IsDirty`は「保存時点との差分の有無」であって「Redo履歴の有無」ではない——ギズモ移動→Undoで
  保存時点まで戻ると`IsDirty=false`になる一方RedoStackにはまだ履歴が残っており、そこへVSの
  背景解析(実際のテキスト変更とは無関係に発火しうる)が来ると無条件にResetToが呼ばれRedo履歴が
  消えていた。同じ設計の裏返しで、無関係な再解析が未保存編集中に来ると誤って外部変更確認
  ダイアログを出してしまい、これが「Ctrl+Sで再読み込みされる」の実体だったと考えられる。
  再解析イベントの発火自体ではなく、直前直後の実Projectionを`SceneDiffEngine.Compare`で比較して
  「本当に内容が変わったか」を判定するよう`SceneExternalChangePolicy.Evaluate`(新設、純粋関数)へ
  切り出し、変化が無ければDocumentへ一切触れないようにした。VSExtension.Tests 601/601
  (既存596+新規5、リグレッションなし)。実機検証は未実施。
- Intended specification: `Docs/シーンエディタの仕様.md`(要求仕様)、
  Docs/Decisions/ADR-0041-Scene-Editor-Interaction-Architecture.md(意思決定)、
  Docs/Specifications/Scene-Editor-Design.md(詳細設計)
- Implementation evidence(Phase 0〜7+後続Task C):
  - tools/CSharpGameEngine.VSExtension/SceneComponentBinding.cs(新規。`SceneComponentRole`/
    `SceneComponentBinding`/`SceneComponentBindings`。FQNハードコードを宣言的な表へ置換)
  - tools/CSharpGameEngine.VSExtension/SceneCameraFraming.cs(新規。視点ポリシーと境界球計算を
    `SceneRenderer`から移設した純粋static)
  - tools/CSharpGameEngine.VSExtension/SceneModelRenderObjectBuilder.cs(FQN表化、
    `ExtractWorldTransform`→`ExtractLocalTransform`改名、`EnumerateNodes`のフラット列挙を
    `ParentWorld`を運ぶpre-order再帰へ置換)
  - tools/CSharpGameEngine.VSExtension/SceneRenderer.cs(`SetCamera`+`ConstantsDirty`追加、
    `ComputeViewMatrix`/`ComputeProjectionMatrix`/`ComputeSceneBounds`/`BoundsCenter`/`BoundsRadius`
    削除、空リストの`throw`撤去と定数バッファ幅クランプ、更新経路を`RenderFrame`冒頭の1本へ統一)
  - tools/CSharpGameEngine.VSExtension/SceneHwndHost.cs(`ApplyCamera()`新設と毎フレーム・リサイズ・
    再生成時の配線、`Count==0`ガード削除、`800,600`ハードコード撤去)
  - 削除: SceneFile.cs、SceneNodeAdder.cs、PrimitiveSpawner.cs、ShapePaletteControl.xaml(.cs)、
    ShapePaletteToolWindowPane.cs、ShowShapePaletteToolWindowCommand.cs(F-L8-006/007参照)
  - Phase 1〜2: `SceneViewState`/`SceneViewCamera`/`SceneInputStateMachine`、`ScenePicker`、
    `SceneSelectionService`とViewport↔Hierarchy選択配線、FocusSelection、永続設定/Options。
  - Phase 3: tools/CSharpGameEngine.VSExtension/SceneTransformMath.cs、TransformGizmo.cs、
    EditTransactionController.cs(純粋なTRS/ギズモ/トランザクション層)。
  - Phase 3: SceneRenderer.cs(深度無効line/solid overlay PSO、固定upload buffer、
    `UpdateObjectTransform`)、SceneHwndHost.cs(ギズモ優先hit、preview、W/Esc、snapshot保留)。
  - Phase 3: SceneSourceEditor.cs(`SetTransform`+Position限定member mask+batch atomicity)、
    SceneWorkspaceService.cs/SceneEditorSessionService.cs/SceneTextBufferWriter.cs
    (同一Roslyn世代の再解決、live buffer、1 Undo、text競合時1回だけ再試行、失敗通知)。
  - Phase 3の軸/Camera判断: Docs/Decisions/ADR-0042-Scene-Editor-Gizmo-Axis-Handedness.md。
  - 後続Task C: SceneInputStateMachine.cs(`MarqueeSelecting`)、ScenePicker.cs(`PickMarquee`)、
    MarqueeOverlay.cs(Phase 3 overlay PSO再利用)、MarqueeSelectionPolicy.cs、SceneHwndHost.cs/
    SceneDesignerToolWindowControl.xaml.cs(`SceneSelectionOrigin.Viewport`での複数選択発行)。
  - Phase 4: TransformGizmoPhase4.cs(Rotate/Scale overlay・解析hit・拘束ソルバ・増分スナップ)、
    EditTransactionController.cs(shared Pivotのworld回転/Local基底scale previewとmember mask)、
    SceneHwndHost.cs(W/E/R、Ctrl、World/Local、既存transaction/overlayへの配線)。
  - Phase 4: SceneDesignerToolWindowControl.xaml(.cs)(World/Localセレクター)、SceneSourceEditor.cs
    (複数既存assignment置換時に現在nodeをindexから再取得)、SceneEditorSessionService.cs(mask伝播)。
  - Phase 5: SceneInteractionPhase5.cs(DuplicateNodePlanner、path再マップ/選択解決、
    SceneViewFollowSnapshot)、ScenePicker.cs(VertexSnapQuery/SurfaceSnapQuery)、NativeMethods.cs(V/N)。
  - Phase 5: SceneRenderer.cs(倍容量constant buffer、一時object handle追加/更新/破棄)、
    SceneHwndHost.cs(Alt優先、一時duplicate preview、V/N/Shift配線、cancel復元)、
    SceneDesignerToolWindowControl.xaml.cs(再解析後のduplicate Selection復元)。
  - Phase 5: EditTransactionController.cs(surface Position+Rotation preview、duplicate commit request)、
    SceneSourceEditor.cs(DuplicateNodeOperation、trivia clone、Name一意化、同一親要求順batch)、
    SceneEditorSessionService.cs(duplicate operation列を1 live text Undoへ変換)。
  - Phase 6: Docs/Decisions/ADR-0043-Scene-Editor-Light-Component-Vocabulary.md(L9 Binding採用)、
    PropertyGizmo.cs(PropertyGizmoFrame/Binding、FrustumGizmo、3ライトoverlay/hit、Compound合成、
    modifier表、値clamp/snap、source operation生成)。
  - Phase 6: SceneComponentBinding.cs(Camera/Directional/Point/Spot Role)、
    SceneHwndHost.cs(Transform+Property同時表示、Alt除外、drag/commit/cancel配線)。
  - Phase 6: EditTransactionController.cs(direct operation transaction)、
    SceneSourceEditor.cs(省略Member追記、AddComponentOperation/RemoveComponentOperation)、
    SceneEditorSessionService.cs(direct operation列を既存live text 1 Undoへ変換)。
  - Phase 7: SceneIncrementalUpdate.cs(SceneDiff分類、mesh再利用、全階層world/bounds再計算)、
    SceneHwndHost.cs(表示世代guard、incremental/rebuild分岐)、FrameResourceTracker.cs/
    SceneRenderer.cs(2 slot allocator・object/overlay upload領域・fence、slot再利用待機)。
  - Phase 7: SceneTransformChange.OriginalLocal、SetComponentMemberOperation.OriginalValueExpression、
    SceneEditConflictMerger.cs(開始値差分を最新値へ合成)、SceneEditorSessionService.cs(commit配線)、
    SceneInputStateMachineTests.cs(Esc全7状態終端)。
- Tests(Phase 0〜7+後続Task C):
  - tests/CSharpGameEngine.VSExtension.Tests/SceneComponentBindingTests.cs(5件。実FQNの認識、
    退役FQNを認識しないこと、Role照合、`Enum.GetValues`による「消費者のないRole宣言」の機械的検出)
  - tests/CSharpGameEngine.VSExtension.Tests/SceneCameraFramingTests.cs(9件。境界球計算4件、
    FOV導出距離、旧3.0*R比で5%以内、視点方向、境界球がFOVに収まることのNDC投影検証、退化入力)
  - tests/CSharpGameEngine.VSExtension.Tests/SceneModelRenderObjectBuilderTests.cs(+7件。親子/孫の
    合成、Transform無しの子の継承、回転した親の下での子配置=乗算順の固定、pre-order順序、
    SkySphereのフレーミング除外、Root無しで空リスト)。フィクスチャの名前空間を
    `UserEcsProject`→`CSharpGameEngine.Runtime.Windows`へ移した(これを忘れると2件が落ちる)
  - 削除: SceneFileTests.cs(9件)
  - Phase 1〜2のナビゲーション/ピッキング/選択テストはM0020の各検証節に記録。
  - Phase 3で24件追加: SceneTransformMathTests(3)、TransformGizmoTests(9)、
    EditTransactionControllerTests(3)、SceneSourceEditorTests(+4)、SceneInputStateMachineTests(+3)、
    SceneTextChangePlannerTests(+2)。Phase 3時点は808件成功・失敗0。
  - 後続Task Cで16件追加: SceneInputStateMachineTests(+7)、ScenePickerTests(+4)、
    MarqueeSelectionPolicyTests(4)、TransformGizmoTests(+1)。Task C時点は824件成功・失敗0。
  - Phase 4で14件追加: TransformGizmoTests(+7)、EditTransactionControllerTests(+3)、
    SceneSourceEditorTests(+2)、SceneTransformMathTests(+2)。最終全体は838件成功・失敗0
    (VSExtension.Tests 346件)。
  - Phase 5で16件追加: SceneInputStateMachineTests(+1)、ScenePickerTests(+4)、
    SceneViewStateTests(+2)、EditTransactionControllerTests(+2)、SceneSourceEditorTests(+3)、
    DuplicateNodePlannerTests(4)。最終全体は854件成功・失敗0(VSExtension.Tests 362件)。
  - Phase 6で19件追加: SceneComponentBindingTests(+5)、PropertyGizmoTests(11)、
    EditTransactionControllerTests(+1)、SceneSourceEditorTests(+2)のうちTheory caseを含む実行件数19。
    最終全体は873件成功・失敗0(VSExtension.Tests 381件)。
  - Phase 7で8件追加: ScenePhase7Tests(7)、SceneInputStateMachineTests(+1)。
    SceneDiff増分/rebuild、mesh再利用と全階層world、frame slot fence、Transform/Property競合差分、
    Esc全状態を固定した。最終全体は881件成功・失敗0(VSExtension.Tests 389件)。
- Follow-up(2026-08-16): `HierarchySourceSync.cs`がCtrl/Shift範囲選択・primary・アンカー、選択Nodeの`SceneNode.Location.Span`併合、明示移動要求を純粋層で扱う。`SceneSourceMarkerService.cs`はRDTで既に開いたC# bufferだけを取得し、`TextMarkerTag`で非選択マークする。通常クリックは非フォーカス、double-click/Enterだけがprimaryの`Span.Start`へ移動する。Viewport/Inspector起点はHierarchy表示だけを更新し、ソース同期を起動しない。
- Follow-up tests: `HierarchySourceSyncTests.cs`(5件)と`SceneNodeViewModelTests.cs`のXAML公開プロパティ回帰を追加。`dotnet test tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj --no-restore`は395件成功・失敗0。
- Follow-up(2026-08-16): ソリューションエクスプローラー起点の起動では、Pin済み`SceneKey.ProjectId`からSceneSelectorカタログを解決する。アクティブなコード文書が無くても対象Sceneを表示し、カタログ未取得・失敗は既存`SceneHwndHost`を空状態へ戻さない。
- Reverse-engineered behavior:
  - 階層合成・preview・source write-backはVSIX純粋層`SceneTransformMath`のrow-vector式へ統一した。
    net472からnet10.0 Runtime型は参照せず、`world * inverse(parentWorld)`をTRSへ分解してローカル値へ戻す。
  - Moveは複数選択のうち選択済み祖先を持たないrootだけへ同じworld差分を適用し、Positionメンバーのみ
    書き戻す。欠落リテラルはInvariantCultureの`f`形式で追記し、既存RHS triviaと他メンバーは保持する。
  - ドラッグ中の同一Scene snapshot更新は最新1件だけ保留する。MouseUp時に最新Compilationへ再解決し、
    live textが直前に変わった場合だけ1回再試行する。対象消滅/解析エラー/2回目競合では破棄+通知する。
  - ギズモ軸は実際のワールド軸で、既定視点の`+X`はゲーム本体と同じく画面左へ投影される(ADR-0042)。
  - 矩形選択は空間から4px以上ドラッグした場合だけ開始する。通常は置換、Ctrlは加算。
    Viewport/Inspector起点はHierarchyのprimary表示だけを更新する。一方Hierarchy操作は選択集合・primary・アンカーを保持し、選択集合をSceneSelectionServiceへ発行する。
  - Rotate/ScaleはMoveと同じframe/overlay/transaction経路を使う。Rotateは3軸+viewリング、Scaleは
    3軸+中心均一ハンドル。CtrlはMove=1.0、Rotate=15°、Scale=0.1へ丸める。
  - World/Localは上部セレクターと既存設定で同期する。複数選択はprimaryの位置/回転をPivot/Local基底とし、
    selection rootへ同じworld deltaを適用する。RotateはPosition+Rotation、Scaleは全TRSを書き戻すため、
    行列分解で変化しうる全メンバーが編集可能な場合だけ操作できる。
  - Alt+handleはOrbitより優先してduplicate dragを開始し、元objectを動かさず予測NodePathの
    一時renderer objectだけを更新する。clone+Name一意化+最終TransformはDuplicateNodeOperation列として
    同一親内の選択順でChildren末尾へ追加し、1 live text Undoで確定する。全予測path解決後に複製rootを選択する。
  - Shift Follow Viewは開始cameraへのtotal deltaでMove/Rotateだけを追従し、cancel/失敗でcameraも戻す。
    V/N snapはMove中だけ有効で、selection rootと全子孫を除外する。Vは8px内頂点へPositionだけ、
    Nは最近面へPositionを合わせprimaryローカル+Yを幾何法線へ整列する。成功優先順位はN→V→Ctrl。
  - PropertyGizmoはactive TransformGizmoを置換せずCompoundGizmoで同一overlayへ合成する。
    Cameraは投影別にFOVまたはOrthographicSizeとNear/Far、現行L9ライトはADR-0043 Bindingで
    Direction/Range/Coneを編集する。Ctrlは単位snap、Shift/AltはPropertyへ適用しない。
  - Property編集はdirect operation列としてTransformと同じsnapshot保留・cancel・live text 1 Undo経路を
    通る。宣言済みでinitializerから省略されたMemberはassignmentを追記し、Add/RemoveComponentは
    ComponentsListLocationとSceneComponent.Locationに限定して他triviaを保持する。
  - 通常snapshot更新はSceneDiffをNone/Incremental/Rebuildへ分類する。Transform/非mesh Memberだけなら
    既存SimpleMesh/SceneRendererを再利用して全階層worldとboundsを再計算し、mesh/構造変更または表示世代の
    飛びはlatest-only全再構築へ戻す。
  - SceneRendererは2 frames-in-flightで、allocator・object定数・overlay定数/頂点領域・fence値を
    slot別に持つ。RenderFrame末尾の全GPU待機はなく、slot再利用時だけ待つ。resize/資源破棄/disposeは
    全体待機を維持する。
  - Transform/Property dragは開始値をoperationへ運び、Position/Property加算、Rotation Quaternion差分、
    Scale倍率をcommit時点の最新値へ合成する。対象/Component消失、非editable式、非有限値では破棄+通知する。
  - `SceneNode.Children`は構文木の入れ子から構築されるツリーなのでサイクルガードは不要
    (深さは解析器が`MaxRecursionDepth`=64で打ち切る)。
  - 初期フレーミングは`Distance = Radius/sin(FovY/2)*1.1`(真の境界球半径)。旧固定値`3.0*Radius`と
    4%差で、near/farは旧値(0.05 / `Radius*20`)を維持 — `Camera.CreateBoundsFit`の`far=D+4R`では
    `IncludeInBounds=false`で除外しているSky domeがクリップされるため。
  - 退化半径では`far`が`near`を下回り`CreatePerspectiveFieldOfView`が例外を投げるバグを退化入力
    テストが検出し、`far`に下限ガードを追加した。
- Conformance: **MatchesWithDocumentedExtension**(Phase 0〜7+後続Task Cの純粋層とコードレビュー範囲。
  Phase 4のRoslyn複数メンバー置換例外、Phase 5の同一親duplicate追加順逆転、
  Phase 6の省略Member source追記、Phase 7の表示世代飛びguardもテスト/コードレビューで固定)。
  VS/D3D12/WPF/UndoのObservedは
  Experimental Instance未実施のため**Unknown**。
- Known deviations: 入れ子`Children`を持つシーンがリポジトリに1つも無いため、**階層Transform合成の
  実シーン証跡が存在しない**(ユニットテストが証跡)。Phase 4〜7の実HWND/D3D12/WPF/
  live buffer/VS UndoはExperimental Instance未確認。SurfaceSnapの法線整列はPhase 5でtrue固定であり、
  falseへ切り替える設定UIは将来拡張として未実装。Camera Aspect、ライトSourceRadius/SourceSize、
  RectLightもPhase 6の明示的対象外。
- Next action: Phase 0〜7+後続Task CのExperimental Instance確認表をユーザー側で実施。

- Follow-up(2026-08-16, VS テーマ Hierarchy / Component icon, RuntimeVerified): `HierarchyToolWindowControl`は公開
  `ThemedDialogTreeViewStyleKey` / `ThemedDialogTreeViewItemStyleKey` と `CrispImage` 展開アイコンを使い、
  active/inactive selection、全複数選択、primary 左アクセントを表示する。展開操作は選択やソース同期を
  発行しない。
- Follow-up(2026-08-16, Component icon authoring, RuntimeVerified): 公開
  `SceneComponentIconAttribute(string projectRelativePath)`、共通 `SceneVisualIcon`、SVG/PNG のプロジェクト
  相対パス検証、SharpVectors/BitmapImage renderer、C# glyph margin のアンカー選択メニューを追加した。
  属性更新は `SceneTextBufferWriter` 経由の既存 buffer 1 Undo で行い、他文書を開かない。lock overlay は
  将来用予約であり、編集 lock 機能は追加していない。
- Follow-up(2026-08-16, standard hierarchy source marker, RuntimeVerified): 独自 tagger を廃止し、
  `ITextMarkerProviderFactory` の共有 `SimpleTagger<TextMarkerTag>` へ拡張所有の
  `TrackingTagSpan` だけを登録する方式へ変更した。書式は
  `MarkerFormatDefinition/CSharpGameEngine.SceneHierarchySource`、半透明青紫背景・前景未指定である。
  RDT/buffer/tagger/tag 数と失敗は ActivityLog に記録し、寿命終端では所有 tag だけを解除する。
- Follow-up tests: `ComponentIconAuthoringTests.cs` はパス正規化・候補列挙・属性の追加/置換/削除・partial
  宣言を、`SceneHierarchyIconTests.cs` は Hierarchy の moniker/公開 binding を、
  `SceneSourceMarkerTaggerTests.cs` は標準 marker format/provider 契約を固定する。
- Runtime verification(2026-08-16): Experimental Instanceで、Hierarchyのactive/inactive表示・展開・
  Ctrl/Shift複数選択、SVG/PNG pickerと1 Undo、青紫source markerの表示/解除、通常選択時の非フォーカス、
  Enter/double-click移動、未表示ソースを開かないことを確認した。
- Follow-up(2026-08-16, Hierarchy structural editing, TestVerified): SceneSourceEditorにAddChild/Rename/Remove/Reparent/Reorderを追加し、HierarchyのContextMenu・F2/Delete・DragDropから1つのVisual Studio Undo単位で適用する。親子を含む削除だけ確認し、Root・生成ソース・自己/子孫への移動は安全に拒否する。`FirstPersonStageScene.cs`は変更していない。`SceneSourceEditorTests`を含むVS拡張Debug/Releaseテスト431件が成功。
- Follow-up(2026-08-16, Runtime/User component icons, TestVerified): RuntimeのTransform/StaticMesh/PrimitiveMesh/CameraとUserEcsProjectのScene-addable structへ`SceneComponentIconAttribute`を付与し、宣言プロジェクト配下のSVGを主形式、PNGを互換形式として解決する。Hierarchy/glyph marginはVS TreeViewフォントサイズへ連動し、無効・欠落・metadataは既定iconへフォールバックする。
- Follow-up(2026-08-16, ギズモ編集の受け入れ監査と深度精度, TestVerified —
  [[M0030-SceneView-Gizmo-Transform-Editing]]): ギズモTransform編集の新規実装依頼を受けたが、
  該当機能はM0020 Phase 3〜7で実装済みだったため、依頼仕様の受け入れ基準に対する監査へ切り替えた。
  未達だったのは失敗経路のテストと残課題R1の2点。
  - `SceneEditConflictMergerTests.cs`(新規6件)/`EditTransactionControllerTests`(+2件)/
    `SceneInputStateMachineTests`(+1件)で、対象ノード消失・外部編集による式化・SceneKey不一致・
    親world退化・ドラッグイベント重畳の各失敗経路を固定した。
  - 残課題R1(深度精度)を解消。`SceneCameraFraming.ComputeNearPlane`で
    `Near = max(0.05, Far/10000)`のfar連動式にし、`SceneViewState`側の`NearPlane`二重定義を削除した。
    正射投影は深度が線形なので対象外。深度バッファは`D32_Float`。回帰テスト8件。
  - 依頼仕様が提案した`HierarchyEditKind.TranslateNode`/`SceneTransformEdit`は**採用しない**。
    構造編集(`HierarchyEditKind`)と値編集(`EditTransactionController`経路)を2系統化するため、
    ADR-0041の編集トランザクション設計と衝突する。
  - VS拡張テストは431件→448件、ソリューション全体942件が成功。実機目視は未実施。
- Follow-up(2026-08-19, M0050 Phase 1-2: `.scene.json`検出・表示・編集・保存、TestVerified —
  [[M0050-VSIX-Scene-Json-Phase1-2]]): M0049でRuntimeパイプラインがJSON専用になった結果、
  この一式が対象Sceneを一切検出できず使用不能になっていた(`SceneWorkspaceService.
  AnalyzeAllAsync`が`AdditionalDocument`扱いの`.scene.json`で`GetDocument`から`null`を受け取り
  例外落ちしていた点、`GetProjectSceneCatalogAsync`の生成コード除外フィルタがJSON由来の生成
  クラスを構造的に締め出していた点の2つが直接原因)。`SceneJsonModelBuilder`(新規、VSIX側)が
  Runtime側`SceneJsonAnalyzer`と同じ値解釈ロジックで既存の`SceneModel`系型(シンボル系プロパティ
  は元々nullable)を直接構築するようにし、`AnalyzeAllAsync`/`GetProjectSceneCatalogAsync`/
  `OnWorkspaceChanged`にAdditionalDocument分岐を追加した。編集モデル
  (`SceneEditDocument`/`SceneModelEditApplier`/`EditTransactionController`)はNodePathベースで
  Roslyn非依存だったため無改造で動作。保存は`SceneSourceEditor.ApplyForSave`のC#構文木部分差分
  書き換えとは別に`SceneJsonSourceWriter`(新規)を作り、`Document.Model`から`*.scene.json`を
  丸ごと再構築してから既存の粗い全文diff経路で書き戻す方式にした。コンテキストメニューへ
  「JSON で表示」(新規`OpenSceneJsonCommand`)を追加し、「シーン デザイナーで開く」の拡張子判定
  も`.scene.json`対応させた。JSON内の正確なテキスト位置追跡(ダブルクリックでの精密ジャンプ)は
  次マイルストーンへ送った(現状は`SceneSourceLocation.FromJsonFile`でファイルパスのみ保持、
  ジャンプはファイル先頭に留まるがクラッシュはしない)。新規テスト11件。ソリューション全体
  1117件が成功。**実機確認は未実施**——特に`WorkspaceChangeKind.AdditionalDocument*`が実際に
  発火するか、`GetDocumentIdsWithFilePath`がAdditionalDocumentのIDを返すかは、リフレクションで
  API自体の存在を確認しただけで、実機のVS Workspaceでの挙動は未検証。

- Follow-up(2026-08-19, M0051第1段: C#正本復帰と書き戻しの往復検証, TestVerified —
  [[M0051-CSharp-Source-Of-Truth-Restoration]]): M0049/M0050で導入したJSON形式を撤回し、Sceneの
  正本をC#宣言型(ADR-0040)へ戻した。VSIX側のJSON対応(`SceneJsonModelBuilder`/
  `SceneJsonSourceWriter`/`OpenSceneJsonCommand`/`SceneDiagnosticsErrorListSync`)は全て削除し、
  C#経路(`SceneSourceEditor.ApplyForSave`)が再び唯一の書き戻し経路になった。
  **あわせて書き戻しの往復検証を新設**した: 書き戻した構文木を`SceneDeclarationAnalyzer`で読み直し、
  `SceneModelStructuralSignature`(ID・ソース位置に依存しない正規化署名、新規)で編集内容と比較し、
  不一致ならテキストへ一切書き込まずに中止する。M0049の動機だった「C#に反映されていない」を
  「壊れたら必ず判る」へ変えるための関門である。
  **この検証を入れた過程でM0047から存在していた既存バグ2件を実際に発見・修正した**:
  (1) `ApplySetTransform`が`Transform.Location.Span`でのみ構文を探しており、同一Nodeへ
  先にRenameしてから移動するとSpanがずれて保存が失敗していた(「移動→Rename」は成功するという
  非対称な壊れ方)。(2) `ApplyRename`がNameの右辺をSpanで探しており、同一Nodeを2回Renameすると
  同じ理由で失敗していた。どちらもAnnotation経由でNodeを特定してから内側を引く方式へ変更し、
  操作順序4パターンの回帰テストを追加した。さらに第2段(タブ切替同期)の土台として
  `ApplyForSaveSteps`(履歴エントリごとの中間構文木を返す)を追加した。新規テスト17件、全1112件成功。
  **実機確認は未実施**。
- Follow-up(2026-08-19, M0051 Step 1-5: 保存後のUndoとHierarchyのUndo, TestVerified —
  [[M0051-CSharp-Source-Of-Truth-Restoration]]): 実機報告2件に対応した。
  (1) **HierarchyでUndoできない**: Ctrl+Z/Yの門番が`SceneHwndHost`と
  `SceneDesignerUndoCommandPolicy`の2箇所ともビューポート限定だった(編集履歴自体はラベル付きで
  正しく積まれており、欠けていたのは入力経路だけ)。新規`SceneDesignerInputRoutingPolicy`へ
  Undo/Redo/Saveの範囲判定を集約し3つとも揃えた。Step 1-4の「M0037の回帰があるため広げない」は
  誤った判断だったと確定(回帰の原因は`OleMenuCommandService`への標準コマンド登録という手段であり、
  その全廃は維持したまま門番だけ広げた)。`TextBox`入力中はCtrl+Z/Yのみ除外する。
  (2) **保存後にUndoできない**: 保存の着地で`SceneEditDocument.ResetTo`が履歴を消していた。
  `SceneEditDocument`は逆操作を持たず`ApplyForSaveSteps`は前方向再生なので、保存点より前へ戻せるのは
  C#テキストの履歴だけ — メモリ内スタックが空のときテキストUndoへ委譲する
  (`SceneTextUndoDelegationPolicy`、承認済み第2段の履歴設計の前倒し)。委譲先を誤らないよう
  `SceneUndoTransactionNaming`の接頭辞で自分が積んだトランザクションだけを対象にする。
  あわせて保存を**1操作=1 VS Undo単位**にし(`ApplyTextStepsAsync`/`SceneTextWriteStep`)、
  `AwaitingOwnSaveReanalysis`が無関係なSceneの履歴まで消していた潜在バグを`SceneKey`照合へ修正した
  (`SceneOwnSaveLandingPolicy`)。新規テスト29件、全1161件成功。
  **実機確認は未実施 — 特にVS標準`Edit.Undo`との二重発火の有無は未確認のまま残っている。**
- Follow-up(2026-08-19, M0051 Step 1-6: 保存後のUndo/Redoを保存前と同じ体感にする, TestVerified —
  [[M0051-CSharp-Source-Of-Truth-Restoration]]): 実機報告「メモリ上のctrl+z,ctrl+yは早い。保存すると
  遅くなる」への対応。Step 1-5のテキスト委譲は表示を更新せず追従をRoslyn再解析に任せていたため、
  保存の前後で体感が変わっていた。
  **Stage A**: `AnalyzeAllAsync`の対象がアクティブ文書ひとつだけで、pin中Sceneの`.cs`がアクティブで
  ないと**再解析が一切来ない**(表示が永久に追いつかない)不具合を`SceneAnalysisTargetPolicy`で是正。
  `ScheduleAnalysis`が張り直しのたびに待ち時間をリセットしてデバウンスが際限なく延びる問題も
  `SceneAnalysisDebouncePolicy`(上限750ms)で頭打ちにした。
  **Stage B**: `SceneEditDocument.TextWatermark`を導入し、メモリ内スタックをセッション全体で保持する。
  Undo/Redoは常にメモリ内が駆動し、テキストへの反映は副作用へ格下げ。保存は部分列だけを書き出す。
  `IsDirty`は参照比較からウォーターマークとの距離へ置き換え(`SceneSaveSuffixPolicy`)。
  保存の着地では`ResetTo`を呼ばずDocumentを維持するが、`SceneDocumentLineagePolicy`が投影を比較して
  食い違えば従来どおり縮退する — **検証が無いと、区別できない兄弟がいるSceneで後続の保存が
  「対象Nodeの構文を特定できません」で失敗する**(追加したテストが実際に再現した)。
  `SceneExternalChangePolicy`から`IsOwnSaveLanding`/`TrustOwnSave`を分離し、台帳は
  `SceneTextSyncPolicy`へ縮小した。全1191件成功。
  **効果範囲**: 消えるのは「保存の前後で体感が変わること」であって「全部が即時になること」ではない。
  構造編集(ノード追加/削除、親変更、Mesh系)は保存前のメモリ内Undoでもbridgeプロセス往復が入るため、
  即時にはならない — その高速化は別課題。**実機確認は未実施。**
- Follow-up(2026-08-19, M0051 Step 1-7: Step 1-6が持ち込んだ回帰2件の修正, TestVerified —
  [[M0051-CSharp-Source-Of-Truth-Restoration]]): Step 1-6の実機確認で2件出た。
  (1) **保存後のCtrl+ZでC#が戻らない**: リネージ一致判定に`SceneDiffEngine.Compare(...).IsEmpty`を
  使っていたが、`SceneDiffEngine`はComponentの`EmitExpressionText`を比較する一方
  `SceneModelEditApplier`の予測モデルはそれを古いまま引き継ぐため、**`KeepDocument`は実機で
  一度も返らずStage Bの高速経路そのものが死んでいた**(Step 1-3で一度踏んで
  `SceneModelStructuralSignature`で回避した罠の再発)。判定を`SceneLandingDecision`へ独立させ、
  ID対応の全単射(`SceneProjectionIdentityComparer`)と構造署名の論理積にした。解析エラー中は
  第3の結果`Defer`で触らない。
  (2) **Hierarchyのインラインリネームが確定しない**: `Populate`がビューモデルを作り直すため
  リネーム中のTextBoxごと消える。門番はDocumentがdirtyのときしか止めないが、確定前はdirtyでない。
  Stage Aで再解析が確実に届くようになるまでは「解析が来ない」欠陥が偶然守っていた。
  `HierarchyPopulatePolicy`でSkip(内容署名による冪等化)とDefer(リネーム中の保留)の2段構えにした。
  再発防止として、判定材料の生成ごと検証する結合テスト(`SceneLandingDecisionTests`)と、
  実装の逆戻りを止めるアーキテクチャテスト(`SceneLineageArchitectureTests`)を追加。
  全1216件成功。**実機確認は未実施。**
- Follow-up(2026-08-19, M0051 Step 1-8: 解析が「解析中」に張り付く不具合の修正, TestVerified —
  [[M0051-CSharp-Source-Of-Truth-Restoration]]): 実機で「シーンが開けなくなった」。右クリックメニューが
  「シーン デザイナーで開く(シーンを解析中)」でグレーアウトし、Hierarchyも空。VS再起動で直る。
  Step 1-6 Stage Aで入れたデバウンス上限のバグで、**保留窓を解析の完走時にしかクリアしていなかった**
  ため、解析が一度も完走しないまま上限を超えると以後ずっと待ち時間0になり、
  「即発火 → 即キャンセル」を繰り返してデバウンスが消滅していた。引き金は`ScheduleAnalysisNow`自身
  (Ctrl+Z/Yの同期と保存の書き戻しが直前にバッファを書き換えるため、その即時解析は必ず追い越される)。
  状態遷移を`SceneAnalysisDebouncePolicy.OnScheduled`/`OnStarted`として純粋関数にし、
  **窓を閉じるのを「完走」から「開始」へ変えた**。時刻源も単調時計へ変更。
  副因だった`AppendPinnedSceneModelsAsync`(キャンセル可能区間をほぼ倍にし、結果はメニューに
  使われないのにブロッカーになっていた)は**Stage Aごと撤回**した — 表示・履歴・保存の正しさは
  Step 1-6/1-7の仕組みが担保しており、失うのは非アクティブ時のリネージ検証と外部変更検知の遅延だけ。
  再発防止として遷移列をシミュレートするテストを追加(旧意味論では100回中92回が即発火することを
  実測で確認)。全1220件成功。**実機確認は未実施。**

## F-L8-012 — シーンエディタ インスペクター(選択ノードのComponent member数値編集)

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Scene DSL双方向エディタ(ADR-0040)の操作系。F-L8-011の編集トランザクション経路
  (`EditTransactionController`ではなく`SetComponentMemberOperation`の直接commit)を再利用する。
- Status: **Implemented**([[M0031-Inspector-Member-Editing]])
- Verification: **TestVerified**(値種別判定・数値/真偽値/Enum/文字列の書式化とパース・範囲外数値の
  拒否・commit要求の組み立て・WPFバインド対象プロパティのpublicアクセシビリティ)。実運用配線
  (`InspectorToolWindowPane`/`InspectorToolWindowControl`/`CSharpGameEngineViewerPackage`の
  ProvideToolWindow登録)は**CodeVerified**止まりで、Experimental Instance目視は未実施。
- Intended specification: M0020が「意図的に除外したこと」として残した項目
  ([[M0020-Scene-Editor-Interaction]])。「数値編集UIは`SetComponentMember`配線(Phase 3)後に
  小さく足せる状態になるが、本マイルストーンには含めない」の実装。
- Implementation evidence:
  - tools/CSharpGameEngine.VSExtension/SceneInspectorModel.cs(既存の`SceneInspectorModelBuilder`を
    拡張。`InspectorEditorKind`判定、Enum候補列挙、`SceneInspectorValueFormat`(型別の数値パース/
    書式化)、`SceneInspectorCommitPlanner`(`OriginalValueExpression`を常にnullで渡す — 絶対値指定の
    タイプ入力にドラッグ用の差分リベースを適用しないため))
  - tools/CSharpGameEngine.VSExtension/InspectorMemberViewModel.cs(新規。WPF行ViewModel、
    バインド対象プロパティは全てpublic)
  - tools/CSharpGameEngine.VSExtension/InspectorToolWindowPane.cs、InspectorToolWindowControl.xaml(.cs)
    (新規。`SceneDesignerToolWindowPane`のSessionProviderパターンを踏襲)
  - tools/CSharpGameEngine.VSExtension/InspectorVisualConverters.cs(新規。EditorKind→Visibility変換)
  - tools/CSharpGameEngine.VSExtension/ShowInspectorToolWindowCommand.cs、OpenViewerCommandPackage.vsct
    (コマンドID 0x010A — 0x0109はOpenSceneDesignerCommandIdが既に使用)、
    CSharpGameEngineViewerPackage.cs(ProvideToolWindow登録・SessionProvider配線)、
    InitialLayoutToolWindowPlan.cs/GameEngineLayout.cs(HierarchyのSFP_fDockLeftに対し
    SFP_fDockRightで初回レイアウトへ追加)
- Known deviations:
  - 入れ子`ObjectCreation`(`new Vector3(...)`等)の分解編集は対象外。読み取り専用として表示する。
  - `EnumMember`候補の選択照合は`ConstantValue`ではなく`SourceExpression`の末尾識別子(名前)で行う
    — flags enumの値重複による取り違えを避けるため。`Default`種別のEnum(`default`のまま)は
    初期選択が付かない。
  - CheckBox/ComboBoxはOneWayバインド+`Click`/`DropDownClosed`でcommitする(`Checked`/
    `SelectionChanged`は初期のデータバインド代入自体で発火するため、それらを起点にすると
    「確定→再構築→再バインド→再確定」の無限ループになる)。
  - ソース未記載メンバー(型のデフォルトのまま)を新規に書き込む「Add Member」的なUIは対象外。
    `Component.Members`は初期化子に実際に書かれたメンバーだけを列挙するため、Inspectorは
    既に値を持つメンバーだけを表示・編集する。
- Next action: Experimental Instanceでの目視確認(ノード選択→行表示、Enter/フォーカス喪失での確定と
  Undo、Enum/CheckBoxの単発commit、編集中の他ウィンドウ由来の再解析で入力が消えないこと)。

# 2026-08 UE FirstPerson宣言型Scene移行

- 旧L9アプリケーションと一体型`Lvl_FirstPerson.glb`ランタイムを廃止し、`UserEcsProject`へ統合した。
- UE 5.8の55 StaticMeshActorを9個の共有描画資産へ分解し、配置・Mobility・DirectionalLight・SkyLight・Fog・PlayerStartを`FirstPersonStageScene.cs`で宣言する。
- 実行時とScene Designerは同じ`StaticMesh.AssetPath`を解決し、C# Sceneを配置の正本とする。

# M0052 Componentパレットと、Inspectorのグルーピング/展開

- Status: CodeVerified(実機確認は未実施)
- Scope: `SceneComponentCatalog`→`SceneModelEditApplier`→`SceneSourceEditor`の3層は先行実装済みで、
  今回はそこへ繋がるUI(Inspectorのグルーピング/展開、Componentパレット、ドラッグ&ドロップ)。
- Inspector: `SceneInspectorModelBuilder.BuildGroups`でComponent単位の階層を返し、既存の`Build`は
  その平坦化として委譲する(既存テストは無改変)。XAMLは`ItemsControl`→`Expander`→`ItemsControl`の
  入れ子で、`InspectorEditorKindVisibilityConverter`の4コントロール重ね配置は維持。
- 再構築は`InspectorRefreshPolicy`+`InspectorContentSignature`のSkipを土台にした。従来の
  `IsKeyboardFocusWithin`だけのガードでは、マウスだけで操作するExpanderの開閉を守れない。
- 展開状態は`CollapsedKeys`(既定は展開、畳んだものだけ記憶)。キーはNode+Component完全修飾名。
  `Expander.Expanded`/`Collapsed`は購読せず、`IsExpanded`のTwoWayバインドを再構築直前に読み戻す。
- パレット: `ComponentPaletteToolWindowPane`(CommandId=0x010B、`Orientation=Bottom`で
  Inspectorとタブ結合させない)。候補は`SceneWorkspaceService.GetProjectComponentCatalogAsync`が
  `Project.Version`キャッシュ付きで返す。
- 適用は`SceneEditorHub.RequestComponentAdd`→`HandleComponentAddAsync`の1経路に統一し、
  Undoラベルと追加後のReveal発行をそこへ閉じ込めた。
- Known deviations:
  - **Hierarchyの`DragOver`プレビューはbest-effort**。Hierarchyは`ActiveSceneModel`(実解析)しか
    持たず、Documentの予測モデルを見られないため、未保存の追加が絡む一瞬だけ重複判定が甘くなる。
    Inspectorは予測投影を見るので適用側と必ず一致する。最終判断は常に`TryApplyAddComponent`で、
    拒否理由はステータスバーに出る。
  - 重複型をパレット候補から外さない(ドロップ先が事前に確定しないため)。`DragOver`の不可カーソルで伝える。
  - Expanderヘッダに削除(×)ボタンを実装した。`ComponentRemoveRequest`/`RequestComponentRemove`
    (`SceneEditorHub.cs`)を新設し、`ComponentAddRequest`→`HandleComponentAddAsync`と同じ
    単一適用点パターンで`HandleComponentRemoveAsync`(`CSharpGameEngineViewerPackage.cs`)へ配線した。
    `EnsurePinned`を経由するため、Scene デザイナー未オープンでも無言失敗しない。
    **実機報告で発覚し追加修正**: 既定のButtonクロムでは"×"がダークテーマ背景でほぼ見えず、
    当たり判定も不明瞭だったため、`Border`+`TextBlock`の自前`ControlTemplate`
    (`InspectorRemoveButtonStyle`)へ差し替えた。削除実行前にHierarchyと同じ`MessageBox.Show`
    (Yes/No、Warning)で確認するようにし、Componentヘッダ行を選択した状態での`Key.Delete`からも
    同じ確認→削除経路(`RequestRemoveComponent`共通メソッド)を通せるようにした。
  - **ノード並べ替えドラッグの挿入位置プレビュー**を追加した。挿入位置(前/後/子として取り込む)の
    判定を`HierarchyInsertionPolicy.Resolve`(新規`HierarchyInsertionPolicy.cs`)へ純粋関数として
    切り出し、`Tree_DragOver`(プレビュー)と`Tree_Drop`(実適用)の両方から同じ関数を呼ぶことで
    食い違いを無くした(`ComponentDropPolicy`と同じ設計判断)。対象は`FindNodeContainer`で解決した
    `TreeViewItem`コンテナの実座標・高さを基準にする(従来の`Args.OriginalSource`基準から変更、
    行によって当たる子要素が違う不安定さを解消)。視覚表示は`ReorderInsertionLine`(前/後、水平線)
    /`ReorderIntoHighlight`(子として取り込む場合、対象行を囲む枠線)の2つのオーバーレイ
    (`HierarchyToolWindowControl.ReorderDrop.cs`、`ComponentDropFeedback`と同じ「コードから位置決め」
    の作法)。`HierarchyInsertionPolicyTests`で境界値(25%/75%ちょうど、高さ0以下)を含めて
    ユニットテスト済み。実機確認は未実施。
  - 追加直後のComponentはmemberが空で、Roslyn再解析が着地するまで値を設定できない。その間は
    「解析待ち…」を表示し、`PendingRevealKey`を保持し続ける。
- **未検証の前提**: カスタムWPFツールウィンドウ間の`DragDrop.DoDragDrop`はこのリポジトリに前例も
  検証記録も無い(ADR-0022は削除済みで、それ自身「実機確認は未実施」だった)。Step 2-3を独立させ、
  Step 2-2でダブルクリック/Enter追加を先に出荷したのはこのため。
- **実機報告で発覚し追加修正した3件**:
  (1) `ComponentPaletteEntry`の`DisplayName`/`Namespace`/`Icon`が`internal`のままXAMLでバインドされ、
  WPFが無言で失敗してアイコンだけ既定値・文字が空になっていた。`public`へ変更しテストで固定。
  (2) `SceneEditorSessionService.Apply`はpin済み(=シーン デザイナーを開いた)Sceneにしか効かないため、
  Hierarchyで選択してもシーン デザイナーを一度も開いていないとInspectorへ選択が届かず、
  Hierarchyの構造編集・Component追加も無言失敗していた。共通ヘルパー`EnsurePinned`で、
  Hierarchy選択・Hierarchy編集・Component追加の3経路すべてを「未pinなら暗黙にpinする」に統一した。
  (3) Expanderヘッダ・行ラベルに明示`Foreground`が無くダークテーマで黒く判読できなかった。
  `WindowTextKey`を明示して解決。同一原因が`ComponentPaletteToolWindowControl.xaml`の型名にも
  残っていたため、続けて修正した。
- **ネストしたObjectCreation値の展開表示**(実機フィードバックを受けた追加機能): `SceneInspectorRow`
  (`SceneInspectorMemberRow`/`SceneInspectorNestedRow`)を新設し、`new Vector3(1,2,3)`のような
  値をExpanderの子行として展開する。**表示専用** — `SceneModelEditApplier.TryApplySetMember`が
  Componentのトップレベルでしか名前一致を探さず、見つからなければ黙って新規メンバーを生やすため、
  ネストの葉は`SceneValue.IsEditable`によらず強制的に`ReadOnly`にする(安全装置、省略不可)。
  位置引数のパラメータ名は`Value.Type`のコンストラクタをアリティ一致で探索して解決(1つに絞れなければ
  `[0]`等の合成ラベル)。XAMLは`DataType`ベースの暗黙`DataTemplate`で再帰させ、展開状態キー・
  内容署名もパス込みで再帰対応させた。編集対応は実データが出てから独立した段階として検討する。
- Next action: Experimental Instanceでの目視確認(Docs/Milestones/M0052-Component-Palette.md の
  「Next action」節に段階別のチェック項目)。

# M0053 実行時状態のComponent分離と、パレット追加時の既定値埋め

- Status: CodeVerified(実機確認は未実施)
- Scope: Component構造体が混在させていた「作者が設定する値」と「エンジンが実行時に埋めるハンドル」を
  型ごと分離し、パレット追加時に全作者フィールドを明示する式を書くようにした([[ADR-0044-Runtime-Component-Separation]])。
- **既定値はコンストラクタで表現する。`required`は採用しない** — `Transform`が既に明示コンストラクタで
  `RotationW=1f,ScaleX/Y/Z=1f`を設定しており、全required化するとこの設計意図が到達不能になるため。
- `IRuntimeComponent`/`IRuntimeComponent<TAuthored>`(新規、`CSharpGameEngine.Entities`)。
  generic版は随伴先を宣言し、非generic版は単独生成される実行時Componentを表す。
  `ISystem`が構造的推論の入口である前例に倣い、**作者が書く型の定義を汚さない**。
- 随伴付与は`SceneInstantiateEmitter.BuildEffectiveComponents`(`Parent`/`SceneNodeTag`と同じ1箇所)で行う。
  対応表`SceneRuntimeComponentMap`は`Collect`と`EmitNode`の両方へ**引数で**渡すので、
  渡し忘れがコンパイルエラーになり「登録と生成のズレ」が構造的に防がれる。
- 分離した型: `AppHostRuntime`/`LevelRuntime`/`AmbientLightRuntime`/`HeightFogRuntime`/
  `DirectionalLightRuntime`/`PointLightRuntime`/`SpotLightRuntime`/`AudioSourceRuntime`。
  `Renderable`/`FramePresent`は作者が宣言しない型と判明したので分割せず非generic版を実装させ、
  パレットから除外した。
- `SceneComponentInitializerPolicy`(新規、純粋関数)がパレットの`new T { … }`を組み立てる。
  **パラメータなしコンストラクタ本体の`this.X=<定数>;`を構文で読み、そのメンバーはコンストラクタの値を
  写す** — 素朴に全フィールドへ`default`を撒くと`Transform`の回転・スケールが0になるため。
  単純代入の並びとして読めないコンストラクタは`new T()`へ安全に縮退する。
- 新診断: SCENE119(作者が実行時Componentを明示した)、SYSGEN027(同一作者型を2つの実行時Componentが主張)。
- Known deviations:
  - **`Renderer is null`の判別ロジックは消えていない。** 随伴付与により作者側Entityも`*Runtime`を
    持つため、`LevelLoad`の判別が`Runtime.Renderer is null`へ移っただけ。得たのは編集面の正しさで
    あって実行時ロジックの単純化ではない。
  - 型シンボルを解決できないときだけ「解析待ち…」へ縮退する(Compilationが取れない等)。
    通常は追加した直後から編集できる。
  - `Level.Path`/`Sun`/`PointLight`/`SpotLight`/`Listener`/`Source`は宣言側・実装側とも未使用の
    死んだフィールドだったので削除した。`Level`の作者面は`ShaderPath`のみになった。
- **実機報告による追加修正4件**:
  (1) **ドロップするとノードの選択が外れる** — `SceneProjectionReconciler`の照合キーが
  「ノード名 + Component型名の連結」なので、Component増減でキーが変わり`EditorNodeId`が
  振り直されていた。`PreserveIdentityAfterMove`を`PreserveNodeIdentity`へ改名し、
  Pathが変わらない`AddComponent`/`RemoveComponent`でもIDを引き継ぐようにした。
  (2) **追加直後は「解析待ち…」で編集できない** — `AddComponentOperation`が文字列2つしか
  運ばず適用側に型シンボルが無かったため`Members`を空にしていた(設計上の必然ではなく実装の限界)。
  `ResolveComponentTypeAsync`で型を解決し、`CreatePlan`が式とメンバーを同じ列挙から作って
  `AddComponentOperation.Members`で運ぶ。予測と再解析後の真実が一致することを契約テストで固定した。
  (3) **シーン デザイナーで開いているのにHierarchyが空** — `SceneWorkspaceService`が文書切替のたびに
  発行する`ModelChanged(null)`を、pin中でも未編集なら素通りさせていた。`SceneHubPublicationPolicy`の
  判定軸を「未保存の編集を守るか」から「pin中Sceneの表示を守るか」へ組み替え、pin中はnullも別Sceneも
  流さないようにした(同一Scene+未保存編集ありを塞ぐM0051 Step 1-4の分岐は不変)。
  (4) **既定値コンストラクタの整備** — `AppHost`/`AmbientLight`/`HeightFog`/`DirectionalLight`/
  `PointLight`/`SpotLight`/`AudioSource`へ「追加した瞬間に見える/聞こえる」値を入れた
  (`Transform`のパターンの横展開)。`Camera`は既に完備していた。
- Evidence: 全1325件成功。ソリューション全体ビルドは0警告0エラー。生成コードで随伴付与を確認済み。
  Component型は18→20種で`MaxComponentTypes=64`に余裕あり。`FirstPersonStageScene.cs`は無変更。
- Next action: Experimental Instanceでの目視確認と、**`UserEcsProject`の起動確認**
  (Archetype構成が変わったため描画・音響の全経路を見る)。詳細は
  Docs/Milestones/M0053-Runtime-Component-Separation.md の「Next action」節。


## F-L3-015 - Entity loose octree

- Layer: L3 - ECS / Job Runtime
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Specifications/Spatial-Partitioning.md
- Implementation evidence: src/CSharpGameEngine.Entities/SpatialPartitioning.cs
- Tests: tests/CSharpGameEngine.Windows.Tests/Runtime/SpatialPartitioningTests.cs
- Conformance: Matches

## F-L3-016 - World global spatial snapshot

- Layer: L3 - ECS / Job Runtime
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Specifications/Spatial-Partitioning.md
- Implementation evidence: src/CSharpGameEngine.Entities/SpatialWorld.cs; src/CSharpGameEngine.CodeAnalysis/SpatialWorldEmitter.cs
- Tests: tests/CSharpGameEngine.CodeAnalysis.Tests/SpatialWorldGeneratorEmissionTests.cs
- Conformance: Matches

## F-L3-017 - Scene View render modes and global octree overlay

- Layer: L3 - Editor / Runtime Diagnostics
- Status: Implemented
- Verification: TestVerified (automated); UserEcs smoke pending
- Implementation evidence: tools/CSharpGameEngine.VSExtension/SceneRenderer.cs; SceneHwndHost.cs; src/CSharpGameEngine.Entities/SpatialDebug.cs
- Tests: tests/CSharpGameEngine.VSExtension.Tests/SpatialDebugOverlayTests.cs

## F-L5-001 — Shared Windows ECS Runtime

- M0089: Runtime.Lighting also owns the ADR-0069 environment Components (SkyAtmosphere, SkyLight, PostProcessVolume, ExponentialHeightFog, GlobalIlluminationVolume); Runtime.Windows collects them in EnvironmentCollect/EnvironmentCollection. EnvironmentComponentTests round-trip the snapshot codec.

- M0088: portable Runtime.Lighting owns the four typed analytic components and optional LightShadow; Contracts owns validated physical units, Windows owns extraction. The old Windows light contracts are removed. See ADR-0064 and Docs/Specifications/Typed-Lighting.md.

- Layer: L5 — Runtime Feature Modules
- Status: Implemented
- Verification: TestVerified (Debug/Release solution build and test, UserEcsProject build, Windows light contract, VSIX binding, analyzer migration)
- Intended specification: Docs/Decisions/ADR-0048-Shared-Windows-Ecs-Runtime.md
- Implementation evidence: `src/CSharpGameEngine.Runtime.Windows/CSharpGameEngine.Runtime.Windows.shproj`, `src/CSharpGameEngine.Runtime.Windows/CSharpGameEngine.Runtime.Windows.projitems`
- Contract: shared source owns Windows ECS components/systems, XAudio2 facade, fixed hierarchy resolution, and stage data; UserEcsProject retains scenes/gameplay/precision selection.
- Known deviations: none for the shared Runtime migration.

## F-L3-018 — System-major fixed simulation phases

- Layer: L3 — ECS / Job Runtime
- Status: Implemented
- Verification: TestVerified (fixed-tick E2E and Debug/Release stage evidence)
- Intended specification: `Docs/Specifications/Ecs-Runtime.md#tick-state-and-structural-changes`
- Stage: `ecs.fixed-tick` (`Executable`); existing `rigid.physics` remains executable physics evidence
- Depends on: F-L7-030
- Current delta: none for the fixed-tick scope. Solver, cooking, bake-host, and VSIX physics UI remain outside this milestone.
- Evidence: `FixedTickScheduler.cs`, `GameSystemLoopEmitter.cs`, `GameSystemGeneratorEndToEndTests.cs`, `FixedTickSchedulerTests.cs`
- Conformance: Matches for the 60 Hz/maximum-eight, retained-backlog, pause/step, interpolation, and generated phase-loop scope.
- Next action: continue with the separately scoped Physics Solver and content milestones.

## F-L5-002 — Shared deterministic Simulation Runtime

- Layer: L5 — Runtime Feature Modules
- Status: Implemented
- Verification: TestVerified
- Evidence: `src/CSharpGameEngine.Runtime.Simulation/`, fixed tick and message tests.
- Conformance: Matches for integer tick, deterministic envelopes, cache identity, and next-tick bridge.

## F-L5-003 — Deterministic local rigid-body Physics Runtime

- Layer: L5 — Runtime Feature Modules
- Status: Implemented
- Verification: TestVerified in Debug and Release for LogicalScalar domain-local simulation, rotated analytic primitive contacts, support-mapped GJK/EPA, cooked static-mesh BVH, four-point manifolds, 8/3 sequential impulses, warm start, sleep/wake, trigger/event lifecycle, angular integration, ContinuousStatic CCD, overflow fault isolation, deterministic insertion order, and zero managed allocation in warmed contact ticks.
- Evidence: `src/CSharpGameEngine.Runtime.Physics/`, `tests/CSharpGameEngine.Windows.Tests/Physics/`.
- Conformance: Matches the required local rigid-body scope. Dynamic-dynamic CCD, joints, deformables, GPU/parallel solvers, and runtime destruction remain separate Deferred features.

## F-L6-002 — Physics Cooking and Simulation Cache artifacts

- Layer: L6 — Runtime Artifacts
- Status: Implemented
- Verification: TestVerified in Debug and Release for canonical convex/static-mesh artifacts, deterministic BVH, immutable cook identity, atomic publish/reuse, concurrent convergence, corruption and path-boundary rejection, strict cache serialization, transactional playback, explicit realtime handoff, engine-owned sequential spool lifecycle, and Q64.64 offline Voronoi prefracture artifacts. Prefracture coverage includes deterministic seeds, 2-16 fragments, 1/16-1024 scale, volume conservation, bond connectivity, closed consistently-wound single-shell meshes, mesh/collision identity, strict serialization, atomic publication, corrupt immutable entry rejection, and real GLB CLI cooking.
- Evidence: PhysicsCooking.cs, PhysicsCache.cs, PrefractureCooking.cs, PhysicsBakeTests.cs, tools/CSharpGameEngine.Physics.Cli/.
- Conformance: Matches. The playback artifact contains the closed render mesh, local convex collision shape, center, mass, stable fragment index, and ordered adjacency bonds for every fragment. Runtime topology mutation remains the separately Deferred destruction domain rather than an L6 artifact responsibility.

## F-L7-020 — Physics CLI and Bake Host

- Layer: L7 — Static Compiler / Bake
- Status: Implemented
- Verification: TestVerified in Debug and Release. The `rigid.physics` Stage passes 48/48 tests in both configurations; the Release CLI build and real GLB static-mesh/convex/prefracture cook smoke pass.
- Evidence: `GeneratedSceneBakeHostEmitter.cs`, `GameSystemLoopEmitter.cs`, `PhysicsCache.cs`, `PhysicsWorldIntegration.cs`, `EngineHost.cs`, `tools/CSharpGameEngine.Physics.Cli/` (`GameBake.cs`), `GeneratedSceneBakeHostTests.cs`, `PhysicsBakeTests.cs`, and `EngineLayerSpec.cs`.
- Conformance: Matches. The source generator emits an exact fully-qualified C# Scene switch and a headless Fixed/Physics/PostPhysics tick path. The host streams canonical frames through bounded host-owned scratch storage, rejects cancellation/unknown scenes/early loop termination, and the CLI publishes through a same-directory temporary file so failure preserves the prior cache. M0096 G: the CLI references no game; `bake --assembly <game.dll>` loads the game build into a collectible load context, points the game's content root at that build, and calls its generated `TryBake`. VSIX Bake commands remain the separate F-L8-014 scope.

## F-L8-014 — Scene View Physics control and debug overlay

- Layer: L8 — Visual Studio Authoring Environment
- Status: Implemented
- Verification: TestVerified in Debug and Release for protocol-v2 validation, bounded body/contact/tree/bond overlay generation, canonical `global::` Destructible discovery, exact-host identity, source-closure hashing, target-local cache paths, live Scene commit before disk Bake, variable-argument versioned requests, timeout and client-disconnect cancellation, and Bake button wiring. Experimental Instance visual certification remains manual evidence rather than an implementation dependency.
- Evidence: `PhysicsDebugClient.cs`, `PhysicsDebugOverlay.cs`, `PrefractureDebugService.cs`, `PhysicsSceneBakeService.cs`, `SceneHwndHost.cs`, `SceneDesignerToolWindowControl.xaml`, `ResidentBridgeClient.cs`, `BridgeServerHost.cs`, `PhysicsDebugOverlayTests.cs`, `PrefractureDebugServiceTests.cs`, `PhysicsSceneBakeServiceTests.cs`, and `BridgeServerHostTests.cs`.
- Conformance: Matches. AABB/velocity/sleep/COM/contact/tree/fracture bond overlays are available. M0097: the editor world registers the bodies but does not advance time, so the overlay matches the drawn scene; the simulate/pause/step/reset controls were removed. Bake Selected/All commits only requested Scene edits to their C# authoring source, saves only the requested open source files, builds/reuses an engine-fingerprinted exact host and, per scene project, a source-fingerprinted game build that the host loads (M0096 G), publishes atomically, and propagates cancellation through the resident connection to the host handler.

## F-L8-015 — VSIX English/Japanese localization

- Layer: L8 — Visual Studio Authoring Environment
- Status: Implemented
- Verification: TestVerified; Debug/Release Solution build and 1,564 tests per configuration passed. `/LCID 1041` selected `ja-JP` and the installed/build DLL hashes matched. `/LCID 1033` was passed to devenv, but this Visual Studio installation has no 1033 resource directory and fell back to `ja-JP`; both LCID visual sweeps remain pending.
- Intended specification: `Docs/Decisions/ADR-0050-Vsix-English-Japanese-Localization.md`, `Docs/Milestones/M0056-Vsix-Localization.md`
- Implementation evidence: `VsixLocalization.cs`, `VsixStrings.resx`, `VsixStrings.ja-JP.resx`, localized VSCT/VSPackage resources, `ja-JP/Extension.vsixlangpack`, Culture 1033/1041 Scene templates.
- Tests: 958 VSIX tests per configuration cover resource culture fallback/parity/placeholders, intentional-English allowlist, XAML key and expanded UI-sink scans, VSCT structural parity, debug reason formatting, VSIX satellite/langpack/template payloads, and byte identity with the current build VSIX.
- Conformance: Matches automated scope. RuntimeVerified/Verified promotion is blocked on the 1033 language pack plus both LCID visual/layout checks.

## F-L8-016 - Scene View Physics sidebar

- Layer: L8 - Visual Studio Authoring Environment
- Status: Implemented
- Verification: TestVerified (layout and settings tests; VSIX build pending)
- Intended specification: Docs/Specifications/Scene-Editor-Design.md#physicsパネルの右端折りたたみ
- Implementation evidence: `tools/CSharpGameEngine.VSExtension/SceneDesignerToolWindowControl.xaml`; `SceneDesignerToolWindowControl.xaml.cs`; `SceneEditorSettings.cs`
- Contract: Physics controls are hosted in a persisted 280 DIP right sidebar. A collapsed 24 DIP chevron opens it and viewport-focused `N` toggles it. Runtime and Editor Preview physics data paths are unchanged.
- Known deviations: Experimental Instance visual verification remains pending.
- Next action: Run VSIX Debug/Release build and confirm open/close, N shortcut, and viewport resize in the Experimental Instance.
## F-L8-017 — Scene Designer common Escape handling

- Layer: L8 — Visual Studio Authoring Environment
- Status: Implemented
- Verification: TestVerified (Debug/Release VSIX and full-solution test runs); Experimental Instance verification pending
- Intended specification: `Docs/Specifications/Scene-Editor-Design.md#43-共通escルーティング`
- Implementation evidence: `SceneEditorEscapeRouter.cs`, `SceneEditorEscapeRoutingPolicy.cs`, `SceneDesignerToolWindowControl.xaml.cs`, `HierarchyToolWindowControl.xaml.cs`, `InspectorToolWindowControl.xaml.cs`, `ComponentPaletteToolWindowControl.xaml.cs`, `SceneSelectionService`.
- Tests: `SceneEditorEscapeRoutingPolicyTests.cs`, `SceneEditorSessionContractsTests.cs`, and existing `SceneInputStateMachineTests.cs` cancellation coverage.
- Contract: focused Scene Designer surfaces consume Esc before Visual Studio routing. They cancel transient UI/viewport work first, otherwise issue exactly one empty shared Scene selection; no-op Esc remains consumed.
- Conformance: Matches automated scope. Visual Studio Experimental Instance confirmation that Esc no longer activates another document tab remains pending.

## F-L7-021 — Autonomous Feature Runner

- Layer: L7 — Static Compiler / Bake
- Parent capability: Autonomous verification
- Status: Implemented
- Verification: TestVerified (Debug/Release: 11 tests per configuration; parallel full solution: 1,587 tests per configuration)
- Intended specification: `Docs/Feature-Stages.md`, `Docs/Decisions/ADR-0051-Autonomous-Feature-Runner-And-Stage-Packs.md`
- Implementation evidence: `tools/CSharpGameEngine.FeatureRunner/FeatureStageCatalog.cs`, `FeatureRunnerApplication.cs`, `FeatureStageExecutor.cs`, `FeatureRunResultStore.cs`, `RepositoryInspection.cs`
- Tests: `tests/CSharpGameEngine.FeatureRunner.Tests/`
- Reverse-engineered behavior: Feature/Stage IDs are validated at startup; CLI supports list/run/run-feature/suite/resume; process results include Git provenance, seed/ticks, hashes, assertions and typed status in a versioned JSON artifact. Explicitly selected deferred features return `NotImplemented` and a non-success code.
- Conformance: Matches ADR-0051 for the runner foundation.
- Known deviations: domain solver bodies remain deferred and are tracked by their F-L5 Feature IDs.
- Next action: retain catalog/result compatibility while domain Features replace deferred Stages.

## F-L9-008 — Feature Stage Packs

- Layer: L9 — Game / Application
- Parent capability: Autonomous verification
- Status: Implemented
- Verification: RuntimeVerified in Debug/Release for all 10 independent Pack executables; parallel full solution passed 1,587 tests per configuration
- Intended specification: `Docs/Feature-Stages.md#stage-pack分離`
- Implementation evidence: `FeatureStagePack` taxonomy and `stage-packs.contract` in `tools/CSharpGameEngine.FeatureRunner/FeatureStageCatalog.cs`; shared host in `stages/CSharpGameEngine.FeatureStages.Shared/`; independent Foundation/Rendering/Rigid/Deformable/Fluid/Destruction/Celestial/ProbabilityQuantum/Coupling/VSIX executable projects under `stages/`
- Tests: `FeatureStageCatalogTests.EveryExistingFeatureHasAnExecutableAutomatedStage`, `EveryDeferredSimulationFeatureHasAnExplicitNotImplementedStage`, `EveryVisualStageRequiresWarpAndAllowsOptionalHardware`, `EveryVisualStageHasACompiledSceneDefinition`, and `stage-packs.contract`
- Reverse-engineered behavior: existing 48 features are covered by executable grouped stages. The 10 independent Pack processes compile C# `IScene` definitions, instantiate generated ECS entities, verify entity count/root ownership/unload, and emit stable state hashes. Future solver features retain explicit deterministic Oracles and return `NotImplemented`.
- Conformance: Matches the Stage Pack infrastructure contract. Domain solver correctness remains owned by each deferred F-L5 Feature and is not inferred from the Pack-host pass.
- Next action: replace representative Pack scenes with solver-specific execution as each Feature is implemented.

## F-L3-020 — Extended static ECS component mask

- Layer: L3 — ECS / Job Runtime
- Parent capability: ECS Runtime / Planetary World
- Status: Implemented
- Verification: Automated; `foundation.extended-ecs-mask` executes the 64/65/128 boundaries and 129-type diagnostic
- Intended specification: `Docs/Feature-Stages.md#基盤stageと64型上限`
- Stage: `foundation.extended-ecs-mask` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L3-021
- Current behavior: `GameSystemIndexAssigner.MaxComponentTypes` is 128. Generator-time subset selection uses only the low word for 1–64 types and low/high words for 65–128; generated Runtime dispatch remains direct Archetype loops with no runtime mask matching.
- Target behavior: preserve the 1–64 low-word path, execute 65–128 without high/low aliasing, and emit SYSGEN026 for 129 or more.
- Conformance: Matches the required 128-type planetary workload; 65 and 128 compile and execute high-component queries in the automated oracle.
- Next action: include the executable Stage in clean Debug/Release and stress closure.

## F-L1-009 — Canonical LogicalScalar and float boundary

- Layer: L1 — Core Contracts
- Parent capability: World Scale
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/World-Scale-Runtime.md#canonical-numeric-model`
- Stage: `world.logical-scalar` (`Executable`, Headless / Foundation, 900s)
- Depends on: None
- Current delta: None in the canonical numeric contract. Engine-owned authoritative values use Q64.64 `LogicalScalar`; relative rendering/UI values use `float`. Framework-required `double` is confined to a marked method/property or an external interface signature and is validated/projected to `float` without engine-owned storage. Type/class-wide adapter exemptions are rejected.
- Adversarial corrections: decimal parsing now rejects every integer magnitude above the signed Q64.64 range before the 64-bit scale shift, preventing `2^64`-class inputs from wrapping to an unrelated raw value. The numeric analyzer no longer lets a class-level adapter marker or `DependencyObject` inheritance hide forbidden fields, and an external `double` member may not remain in an inferred `var`; WPF/UI Automation sites project to `float` at their boundary.
- Conformance: `CSharpGameEngine.Numerics.Tests` (10), `CSharpGameEngine.CodeAnalysis.Tests` (146), Debug/Release `world.logical-scalar`, full Debug solution tests (2,038), and full Release solution tests (2,037).
- Next action: Keep the analyzer and property oracle in every Debug/Release closure while migrating later subsystems.

## F-L2-006 — Persistent world identity contracts

- Layer: L2 — Asset Schemas
- Parent capability: World Scale
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/World-Scale-Runtime.md#identity-and-source-of-truth`
- Stage: `world.identity` (`Executable`, Headless / Foundation, 900s)
- Depends on: None
- Current delta: None. Runtime `Entity(Index,Generation)` is recyclable and process-local, while frame, cell, asset, and authored entity identities are typed persistent `StableId` values.
- Conformance: `WorldIdentityTests` and `CellUnloadAtPlaybackInvalidatesRuntimeAndPersistentHandles` verify canonical round-trip and slot-reuse separation.
- Next action: keep persistent IDs as the only cross-cell and serialized reference representation.

## F-L4-013 — Engine host resource ownership

- Layer: L4 — Domain Servers
- Parent capability: World Scale
- Status: Implemented
- Verification: TestVerified (generated runtime build, ownership/clock/streaming tests and executable Stage)
- Intended specification: `Docs/Specifications/World-Scale-Runtime.md#ownership-and-lifecycle`
- Stage: `world.host-ownership` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L2-006
- Current delta: None. Async loading policy and spatial active-set selection remain F-L5-037/F-L4-006 responsibilities, not ownership gaps.
- Evidence: `EngineHost` owns one `WorldOwner`, Core fixed-tick clock, ReferenceFrameGraph, optional WorldCellStreamingService, per-frame physics runtimes, audio server/mixer, renderer, input, and window. Generated runtime and preview entrypoints borrow this tree; host disposal unloads/releases streaming data and invalidates the World lease.
- Conformance: Matches. Generator tests, real UserEcsProject build, World/clock/streaming ownership tests, Component reflection scan, and `world.host-ownership` prove one owner and reverse-order release.
- Next action: Preserve this ownership boundary while later render visibility and shipping-content Features consume the completed active-cell snapshot.

## F-L5-036 — Hierarchical reference frame graph

- Layer: L5 — Integrated Runtime
- Parent capability: World Scale
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/World-Scale-Runtime.md#reference-frame-graph`
- Stage: `celestial.hierarchical-frames` (`Executable`, Visual / Celestial, 1800s)
- Depends on: F-L1-009, F-L2-006, F-L5-035
- Current delta: None for the bounded-depth frame tree, command-buffered mutations, and immutable fixed-tick snapshots.
- Conformance: `ReferenceFrameGraphTests` cover cycle and missing-parent rejection, depth bounds, reparenting, deterministic command application, and snapshot publication.
- Next action: consume the immutable snapshots through F-L5-027 frame-relative rendering, physics, and audio extraction.

## F-L6-008 — World cell partition artifacts

- Layer: L6 — Runtime Artifacts
- Parent capability: World Scale
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/World-Scale-Runtime.md#world-cell-artifacts`
- Stage: `world.cell-artifacts` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L2-006, F-L5-036, F-L6-005
- Current delta: None for the canonical sparse cell schema and deterministic artifact envelope. Runtime activation is implemented by F-L5-037.
- Conformance: `WorldCellArtifactTests` cover deterministic round-trip, exact Q64.64 bounds, closure validation, corruption rejection, and canonical dependency ordering.
- Next action: compile authoring definitions incrementally through F-L7-026 without changing the runtime artifact schema.

## F-L5-023 — Kepler frame pose provider

- Layer: L5 — Integrated Runtime
- Parent capability: World Scale
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/World-Scale-Runtime.md#kepler-frame-provider`
- Stage: `celestial.kepler` (`Executable`, Visual / Celestial, 1800s)
- Depends on: F-L1-009, F-L5-036
- Current delta: None. Immutable orbital elements and integer/domain time deterministically produce Q64.64 frame pose commands and velocity without entering the rigid-body solver.
- Conformance: `KeplerFramePoseProviderTests` cover one-period closure, independent specific-energy error, high eccentricity, full-`ulong` ticks, domain time scale, command publication, explicit failure, and zero allocation.
- Next action: consume published orbital frame snapshots through F-L5-027 frame-relative subsystem extraction.

## F-L5-027 — Hierarchical celestial coordinates

- Layer: L5 — Integrated Runtime
- Parent capability: World Scale
- Status: Implemented
- Verification: TestVerified (Debug/Release subsystem tests and Celestial/WARP Stage)
- Intended specification: `Docs/Specifications/World-Scale-Runtime.md#reference-frame-graph`
- Stage: `celestial.frame-relative-runtime` (`Executable`, Visual / Celestial, 1800s)
- Depends on: F-L5-036
- Current delta: None. Visibility partitioning and cell selection are owned by F-L5-037/F-L4-006, not this coordinate contract.
- Evidence: Runtime render and audio extraction compose source/listener/camera affines through the immutable host snapshot, stop at the least common ancestor, and convert only the final relative result to `float`. Physics uses host-owned per-frame solver/command/query state and rejects cross-domain bodies before mutation.
- Conformance: Matches. Remote-root overflow, listener/camera-relative extraction, cross-domain rejection, and solver-state isolation are covered by Debug/Release subsystem tests and `celestial.frame-relative-runtime`.
- Next action: Preserve the `LogicalScalar` authoritative/frame-relative `float` boundary while active-cell and visibility systems consume these snapshots.

## F-L5-037 — Active-set world cell streaming

- Layer: L5 — Integrated Runtime
- Parent capability: World Scale
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/World-Scale-Runtime.md#cell-state-model`
- Stage: `world.cell-streaming` (`Executable`, Headless / Foundation, 1800s)
- Depends on: F-L3-021, F-L6-008
- Current delta: None. Frame-local selection, canonical CAS resolution/retention, bounded dependency-wave prefetch, generated C# Scene payload activation, persistent cell ownership, rollback, unload, and last-known-good failure behavior are implemented.
- Conformance: `ContentAddressedStoreTests`, `WorldCellRuntimeTests`, `WorldCellStreamingTests`, `WorldCellSpatialIndexTests`, `WorldCellEntityPayloadTests`, `CellUnloadAtPlaybackInvalidatesRuntimeAndPersistentHandles`, Scene generator E2E, and `GeneratedWorldCellActivationTests`.
- Next action: consume the published active-cell snapshot from F-L4-006 visibility and package the reachable CAS closure in F-L6-006.

## F-L5-038 — Radial (logarithmic) depth camera

- Layer: L5 — Integrated Runtime
- Parent capability: Rendering
- Status: Implemented
- Verification: TestVerified, RuntimeVerified (WARP)
- Intended specification: `Docs/Specifications/Omni.md`, `Docs/Decisions/ADR-0076-Native-Radial-Depth-Mapping.md`
- Stage: `rendering.radial-depth` (`Executable`, Visual / Rendering, 1800s)
- Depends on: F-L4-014, F-L0-009
- Milestone: `Docs/Milestones/M0099-Radial-Depth.md`
- Implementation evidence: `src/CSharpGameEngine.Rendering/RadialDepth.cs` (`RenderDepthMapping`, `RadialDepthCurve`, `RadialDepthProjection`); Stage ABI `stage-v10` with `RadialVSMain`/`RadialPSMain`/`PreviewRadialPSMain`/`PreviewCleanRadialPSMain`/`RadialObserverVSMain` in `StagePresentationShaderSource`, `FrameCB.DepthMapping0/1` and `ObjectCB.RadialRasterDepth` (`radial_depth_2026_10_04.py`, `radial_object_depth_2026_10_04.py`, `radial_object_depth_frame_2026_10_04.py`); `Graphics/Camera.DepthMapping`/`RadialTransitionMeters`; `StageRenderer` (radial pipeline family, `WriteDepthMapping`, `InvertibleViewProjection`, per-preview mapping, `ReadDepthForTesting`); `RenderCameraPreview.DepthMapping`/`Radial`/`RadialCenter`.
- Current delta: radial depth is the default of the scene `Camera` component (`SceneCameraTests`, `SceneJsonRuntimeValuesTests.Camera_ReadsTheDepthMappingAndIsRadialWhenAbsent`) and of the render camera factories (`Camera.CreateDefault`/`CreateFirstPerson`/`CreateBoundsFit`; `CameraTests.Factories_ChooseRadialDepth`); standard is an explicit choice (M0100). Scene `Camera.DepthMapping`/`RadialTransitionMeters` reach the render camera through `LevelLoad.ApplyProjection`, in the game and in the editor runtime view (whose clip range then covers both the editor's and the scene camera's). They are read on every frame with the exposure, so value edits show on the next frame; an unusable value keeps the last usable projection (`LevelRuntime.InvalidCameraProperty`). The isolated preview (protocol 8) draws perspective views radial from the editor's matrix (`SetViewProjection` with `RadialViewParameters`) and camera thumbnails with their scene camera's mapping (`RenderPreviewRenderSessionTests.WARP_RadialCameraDrawsTheStandardImageWithRadialDepth`, `RenderPreviewCameraPoseTests`, `SceneCameraPreviewPolicyTests.Build_ReadsTheDepthMappingAndIsRadialByDefault`). The radial main pass keeps early depth rejection through a conservative raster depth (`RadialVSMain`, `SV_DepthGreaterEqual`); clipped triangles may differ from standard by one 8-bit level.
- Conformance: `RadialDepthTests`; `ShaderGraphCompilerTests` (14 Stage entries; the radial main pixel entries declare `SV_DepthGreaterEqual`, the thumbnail entry `SV_Depth`); `RadialDepthGpuIntegrationTests` (standard pixels in legacy and environment scenes, coplanar pairs at 2e4/1e15/5e18 m, depth equals the CPU encoding, extreme far planes, frame slots, radial thumbnails, invalid input, device recovery); `ShaderArtifactHotReloadTests.RenderServer_DebugHotReload_RebuildsTheRadialPipelines`. Standard frames stay pinned by `StandardModeGoldenFrameTests`.
- Next action: none required. Performance evidence: `RadialDepthPerformanceEvidence` (on demand; 24 front-to-back layers on an RTX 4060 Ti: 0.171 ms radial against 0.133 ms standard at 2–48 m, 0.123 against 0.091 ms at 100–1020 m with the per-object tangent). Precision evidence: `RadialDepthPrecisionEvidence` (on demand; within 4 ulps of a double oracle in the middle of the view on WARP and the RTX 4060 Ti, after replacing the hardware `log2`, which was up to 520,000 ulps off at 4 µm); `StagePresentationShaderSourceTests` keeps transcendental intrinsics out of the presentation entries.

## F-L5-039 — Radial observer view

- Layer: L5 — Integrated Runtime
- Parent capability: Rendering
- Status: Implemented
- Verification: TestVerified, RuntimeVerified (WARP)
- Intended specification: `Docs/Specifications/Omni.md#observer-view`
- Stage: `rendering.radial-depth` (`Executable`, Visual / Rendering, 1800s)
- Depends on: F-L5-038
- Milestone: `Docs/Milestones/M0099-Radial-Depth.md`
- Implementation evidence: `RadialObserver` (`RuntimeComponents.cs`), `RadialObserverSync` and `RadialObserverView` (`RuntimeSystems.cs`), `EngineHost.AddRadialObserverPreview` and its submission in `SubmitEnvironment`, `RadialObserverVSMain`, the observer pipeline states and `PrepareObserverMarkers` in `StageRenderer`, `RenderCameraPreview.MarkRadialCenter`.
- Current delta: game runtime only (not in the editor runtime view, whose previews belong to the editor); at most three insets; picking and gizmos inside the inset and a full-screen observer view are non-goals. Interpolation inside the inset is perspective in the compressed space (an approximation for visualization).
- Conformance: `RadialObserverTests` (radial-only, inset placement, centre at the inset middle, far plane beyond the compressed world, unusable values, one inset in the game and none with a standard camera or an editor camera), `RadialDepthGpuIntegrationTests.ObserverPreview_DrawsTheCompressedWorldAndMarksTheCentre` (the sphere appears where `T·G(r)` puts it, the main view outside the inset is unchanged, the marker is drawn).
- Next action: none planned; picking inside the inset and a full-screen observer view stay non-goals until a game needs them.

## F-L5-006 — Character Controller

- Layer: L5 — Integrated Runtime
- Parent capability: Rigid
- Status: Implemented
- Verification: TestVerified, RuntimeVerified (WARP)
- Intended specification: `Docs/Decisions/ADR-0067-Kinematic-Character-Movement-And-Spring-Arm-Camera.md`
- Stage: `rigid.character-controller` (`Executable`, Visual / Rigid, 1800s)
- Depends on: F-L5-003, F-L4-010, F-L3-018
- Implementation evidence: `src/CSharpGameEngine.Runtime.Character/{CharacterComponents,CharacterMotor,CharacterSystems,PlayerCamera,PlayerCameraComponents}.cs`, `src/CSharpGameEngine.Runtime.Physics/{StaticMeshCollision,PhysicsNarrowPhase}.cs`, `templates/CSharpGameEngine.Templates.ThirdPerson/{Scenes/ThirdPersonCharacter,Input/ThirdPersonInput,Gameplay/ThirdPersonControls}.cs`, `tools/CSharpGameEngine.FeatureRunner/ThirdPersonCharacterVerification.cs`
- Current delta: Walking and falling only; there is no swimming, flying, crouching, moving-platform basing, root motion, or network prediction. Collision uses the rendered triangles of StaticMesh colliders, not Unreal's simple collision. The spring arm has no camera lag. The template character is Unreal's Quinn, animated by F-L4-015 (ADR-0066).
- Conformance: CharacterMotorTests, SpringArmTests, PhysicsCapsuleContactTests, StaticMeshCollisionTests, InputServerTests, Win32InputEventAdapterTests, Win32WindowMouseCaptureTests, RuntimeAutomationTests, and ThirdPersonCharacterVerificationTests. The ThirdPerson template runs on WARP with scripted W then Space; see [M0090](Milestones/M0090-ThirdPerson-Character-Movement.md).
- Next action: drive the Quinn skeletal mesh and locomotion animation from `CharacterMovementState` (ADR-0066).

## Deferred Simulation/Physics features with designed Stages

All entries below remain `Deferred / Unverified`. Their intended behavior and deterministic Oracle are specified in `Docs/Feature-Stages.md`; implementation evidence does not yet exist, and explicit execution returns `NotImplemented`.

| Feature ID | Capability | Stage ID | Conformance |
|---|---|---|---|
| F-L5-004 | Joints | `rigid.joints` | Unknown |
| F-L5-005 | Ragdoll | `rigid.ragdoll` | Unknown |
| F-L5-007 | Vehicle | `rigid.vehicle` | Unknown |
| F-L5-008 | Dynamic-Dynamic CCD | `rigid.dynamic-ccd` | Unknown |
| F-L5-009 | Parallel Solver | `rigid.parallel-solver` | Unknown |
| F-L5-010 | GPU Solver | `rigid.gpu-solver` | Unknown |
| F-L5-011 | Rollback | `rigid.rollback` | Unknown |
| F-L5-012 | PBD / XPBD | `deformable.xpbd` | Unknown |
| F-L5-013 | Cloth | `deformable.cloth` | Unknown |
| F-L5-014 | Rope | `deformable.rope` | Unknown |
| F-L5-015 | Soft Body | `deformable.soft-body` | Unknown |
| F-L5-016 | FEM | `deformable.fem` | Unknown |
| F-L5-017 | MPM | `deformable.mpm` | Unknown |
| F-L5-018 | SPH / PBF | `fluid.sph-pbf` | Unknown |
| F-L5-019 | FLIP / APIC | `fluid.flip-apic` | Unknown |
| F-L5-020 | Eulerian Grid | `fluid.eulerian-grid` | Unknown |
| F-L5-021 | Runtime Voronoi / Boolean | `destruction.runtime-topology` | Unknown |
| F-L5-022 | Voxel / SDF destruction | `destruction.voxel-sdf` | Unknown |
| F-L5-024 | Direct N-body | `celestial.direct-nbody` | Unknown |
| F-L5-025 | Barnes–Hut | `celestial.barnes-hut` | Unknown |
| F-L5-026 | FMM | `celestial.fmm` | Unknown |
| F-L5-028 | Gameplay probability Domain | `probability.gameplay` | Unknown |
| F-L5-029 | Wavefunction Grid | `probability.wavefunction-grid` | Unknown |
| F-L5-030 | Monte Carlo scientific quantum Domain | `probability.monte-carlo-quantum` | Unknown |
| F-L5-031 | Cross-domain coupling | `coupling.multidomain` | Unknown |

## Deferred Simulation infrastructure and specified non-physics overrides

F-L0-008 and F-L3-019 are implemented non-physics Roadmap Features; the remaining rows in this table remain Deferred simulation infrastructure.

| Feature ID | Capability | Stage ID | Status |
|---|---|---|---|
| F-L0-008 | D3D12 compute/readback | `foundation.d3d12-compute` | Implemented |
| F-L3-019 | Deterministic Job Runtime | `foundation.deterministic-jobs` | Implemented |
| F-L4-003 | Simulation backend contracts/adapters | `foundation.simulation-adapters` | Deferred |
| F-L6-003 | Field/graph artifacts | `foundation.field-artifacts` | Deferred |
| F-L5-032 | Animation/Physics pose bridge | `deformable.pose-bridge` | Deferred |
| F-L5-033 | Volume/field renderer | `fluid.volume-renderer` | Deferred |
| F-L5-034 | Thermal field Domain | `coupling.thermal-field` | Deferred |

## F-L7-022 — Rendering baseline conformance

- Layer: L7 — Static Compiler / Bake
- Parent capability: Rendering Roadmap
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Specifications/Rendering.md#scope-and-current-baseline
- Stage: `rendering.baseline` (Executable, Visual / Rendering, 1800s)
- Depends on: None
- Current delta: Triangle, Model, Stage, lights, shadows, HDR, bloom, and ACES run from Shader Artifacts and retain WARP golden/readback and debug-layer oracles.
- Conformance: Matches
- Next action: Keep golden tolerances and resource/debug diagnostics stable as later Render Graph work lands.
## F-L4-004 — Engine-owned Render Graph

- Layer: L4 — Domain Servers
- Parent capability: Rendering Roadmap
- Status: Implemented
- Verification: TestVerified (topology, hazards, barriers, queue ownership, alias lifetime, invalid paths)
- Intended specification: `Docs/Specifications/Rendering.md#target-public-contracts`
- Stage: `rendering.render-graph` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L7-022
- Current delta: none for the L4 graph compile contract. Existing D3D12 renderers still require migration to the graph executor under later rendering features.
- Evidence: `src/CSharpGameEngine.Rendering/RenderGraph.cs`, `tests/CSharpGameEngine.Rendering.Tests/RenderGraphTests.cs`.
- Conformance: Matches. Typed handles, stable topological order, RAW/WAR/WAW dependencies, resource transitions, cross-queue ownership, cycle/invalid-use rejection, and non-overlapping transient aliases are compiled without native types.
- Next action: make the L0 D3D12 backend consume compiled transitions while implementing snapshot visibility in F-L4-006.

## F-L0-008 — D3D12 compute and readback

- Layer: L0 — Platform / Native Backends
- Parent capability: Rendering Roadmap
- Status: Implemented
- Verification: TestVerified (WARP fixed-ABI compute/readback and D3D12 debug layer)
- Intended specification: `Docs/Specifications/Rendering.md#stage-oracle-matrix`
- Stage: `foundation.d3d12-compute` (`Executable`, Visual / Rendering, 1800s)
- Depends on: F-L7-022
- Current delta: none for the private compute/readback primitive.
- Evidence: `assets/Shaders/ComputeVector.hlsl`, Compute `CGESHDR1` ABI/compiler path, `D3D12ComputeReadback`, and WARP tests.
- Conformance: Matches. Runtime loads offline DXIL, uses upload/UAV/readback buffers and a completion fence, exposes only float arrays, rejects a wrong artifact before device creation, and reports zero debug-layer errors.
- Next action: consume the compiled L4 Render Graph transitions from the D3D12 executor while migrating material and texture ownership.

## F-L6-004 — Shader artifact compiler and PSO provenance

- Layer: L6 — Runtime Artifacts
- Parent capability: Rendering Roadmap
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Specifications/Rendering.md#data-flow and Docs/Specifications/Shader-Graph.md
- Stage: `rendering.shader-artifacts` (Executable, Headless / Foundation, 1800s)
- Depends on: F-L7-022, F-L7-049
- Current delta: CGESHDR1 records fixed target ABI, semantic/dependency hashes, compiler provenance, nine entry DXIL payloads, reflection, PSO key, and checksum. Runtime DXC is removed.
- Conformance: Matches
- Next action: Extend dependency hashes to external production Subgraph files without changing the runtime container contract.
## F-L4-005 — Material and texture runtime

- Layer: L4 — Domain Servers
- Parent capability: Rendering Roadmap
- Status: Implemented
- Verification: TestVerified (canonical envelopes, dependency hashes, CPU sampling, runtime bake, WARP image, residency, corruption/lifecycle paths)
- Intended specification: `Docs/Specifications/Rendering.md#target-public-contracts`
- Stage: `rendering.materials` (`Executable`, Visual / Rendering, 1800s)
- Depends on: F-L6-004
- Current delta: none for synchronous material/texture resolution. Asynchronous retain/load/release handles remain F-L4-008.
- Evidence: `RenderArtifactCodec`, `RenderMaterialCatalog`, immutable RGBA8 resources, source-hash runtime bake adapter, CPU reference shading, Stage/Model raw-byte API removal, content-hash GPU dedup, and WARP red/blue material readback.
- Conformance: Matches. Content envelope checksum and exact dependency hashes are validated before L0; invalid encoded images fail explicitly, canonical negative zero has one representation, duplicate texture content has one CPU/GPU residency owner, and upload failure releases partial native resources.
- Next action: Preserve artifact identity while the later shared Render Server consumes F-L4-006 batches.

## F-L4-006 — Visibility, LOD, and instancing

- Layer: L4 — Domain Servers
- Parent capability: Rendering Roadmap
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Rendering.md#data-flow`
- Stage: `rendering.visibility` (`Executable`, Visual / Rendering, 1800s)
- Depends on: F-L4-004, F-L4-005
- Current delta: None for the platform-independent instance visibility contract. Stable-ID snapshots, finite camera-relative bounds, frustum selection, hysteretic LOD state, deterministic instance batches, and caller-buffer execution are implemented.
- Conformance: `RenderWorldSnapshotTests` covers immutable canonical extraction, duplicate/invalid rejection, camera movement without stale visibility, both LOD hysteresis directions, canonical batch grouping, invalid frusta/state overlap, and warmed 100,000-instance zero allocation.
- Next action: The later shared Render Server consumes F-L4-007 quality plans without changing this snapshot contract.

## F-L4-007 — Rendering quality profiles

- Layer: L4 — Domain Servers
- Parent capability: Rendering Roadmap
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Rendering.md#visual-and-quality-policy`
- Stage: `rendering.quality-profiles` (`Executable`, Visual / Rendering, 1800s)
- Depends on: F-L4-006
- Current delta: None for the platform-independent quality policy. Immutable profiles deterministically add or omit shadow cascade, IBL, bloom, ACES, FXAA, and exposure topology in the Render Graph.
- Conformance: `RenderQualityProfileTests` verify the compatibility baseline, Balanced/Cinematic scaling, all-features-disabled graph, deterministic independent builds, bounded one-stop EV100 behavior, and invalid cascade/resolution/exposure/AA rejection.
- Next action: F-L8-018 moves the VSIX preview onto the same recovered Render Server boundary.

## F-L4-014 — Physically based environment lighting

- Layer: L4 — Domain Servers
- Parent capability: Rendering Roadmap
- Status: Implemented
- Verification: TestVerified, RuntimeVerified (WARP)
- Intended specification: `Docs/Specifications/Rendering.md#environment-lighting`, `Docs/Decisions/ADR-0069-Physical-Sky-Exposure-Shadows-And-Dynamic-GI.md`
- Stage: `rendering.environment-lighting` (`Executable`, Visual / Rendering, 1800s)
- Depends on: F-L4-007, F-L0-008
- Implementation evidence: `src/CSharpGameEngine.Windows/Graphics/{StageAtmosphere,StageGlobalIllumination,StageAmbientOcclusion,StageAutoExposure,DirectionalShadowCascades,EnvironmentLighting}.cs`, `StageRenderer.cs`, `assets/Shaders/{Atmosphere,GlobalIllumination,AmbientOcclusion,LuminanceHistogram}.hlsl`, the Stage graph (ABI `stage-v7`), `src/CSharpGameEngine.Runtime.Lighting/EnvironmentComponents.cs`, `src/CSharpGameEngine.Runtime.Windows/EnvironmentCollection.cs`
- Current delta: volumetric clouds, reflection probes and texture mipmaps are not implemented; local lights do not contribute to GI; probes inside geometry are not relocated.
- Conformance: AtmosphereGpuIntegrationTests, DirectionalShadowCascadesTests, DirectionalShadowGpuTests, GlobalIlluminationGpuIntegrationTests, GlobalIlluminationSettingsTests, AmbientOcclusionGpuIntegrationTests, AutoExposureGpuIntegrationTests and EnvironmentComponentTests on WARP; the ThirdPerson template capture is compared with Unreal in [M0089](Milestones/M0089-Physically-Based-Environment-Lighting.md).
- Next action: texture mipmaps for baked Unreal atlases; volumetric clouds if they are brought into scope.

## F-L4-015 — Skeletal meshes and animation

- Layer: L4 — Domain Servers
- Parent capability: Rendering Roadmap
- Status: Implemented
- Verification: TestVerified, RuntimeVerified (WARP)
- Intended specification: `Docs/Specifications/Rendering.md#skeletal-meshes-and-animation`, `Docs/Decisions/ADR-0066-Skeletal-Meshes-And-Animation.md`
- Stage: `rendering.skeletal-animation` (`Executable`, Visual / Rendering, 1800s)
- Depends on: F-L4-007, F-L0-008
- Implementation evidence:
  - `src/CSharpGameEngine.Import/Gltf/{ImportedSkin,ImportedAnimationClip,GltfImporter}.cs`
  - `src/CSharpGameEngine.Windows/Graphics/StageSkinning.cs` and `assets/Shaders/Skinning.hlsl`
  - `src/CSharpGameEngine.Runtime.Animation/`
  - `src/CSharpGameEngine.Runtime.Windows/StageSceneDataBuilder.cs`
  - the ThirdPerson template's `Mesh` node
- Current delta:
  - Only linear blend skinning.
  - No retargeting, root motion, additive layers, IK, montages, or skinned-bound culling.
  - The locomotion graph is the fixed ABP_Unarmed shape, not a general animation graph.
- Conformance: GltfSkinAnimationImportTests, AnimationRuntimeTests and SkinningGpuIntegrationTests. The template shows Quinn idling and walking; see [M0091](Milestones/M0091-Skeletal-Animation.md).
- Next action: direction-aware blending for strafing controls, and a general animation graph if more templates need one.

## F-L0-009 — D3D12 device recovery

- Layer: L0 — Platform / Native Backends
- Parent capability: Rendering Roadmap
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Rendering.md#device-and-preview-state-machines`
- Stage: `rendering.device-recovery` (`Executable`, Visual / Rendering, 1800s)
- Depends on: F-L4-004, F-L4-007
- Current delta: `RenderDeviceLifecycle` is the provider-neutral state source. `RenderServer` owns one D3D12 generation, an exact shader/mesh/material/roster artifact closure, and reusable completed/pending frame snapshots. Minimize/restore and real `ID3D12Device5.RemoveDevice` rebuild from the applied last-known-good package and completed snapshot without ECS resubmission; invalid or partial closures never publish Ready.
- Conformance: Eight lifecycle tests cover every legal/illegal state and closure replacement. The WARP integration removes the real device, corrupts the disk artifact, recreates generations, reproduces the pixel oracle without command resubmission, and reports zero debug-layer errors. Runtime ownership reflection rejects Render Server or native resource fields in ECS Components.
- Next action: F-L8-018 reuses this server through the versioned Preview Host and removes the VSIX renderers.

## F-L8-018 — Shared isolated rendering preview

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Rendering Roadmap
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Rendering.md#device-and-preview-state-machines`
- Stage: `vsix.rendering-preview` (`Executable`, Visual / VSIX, 1800s)
- Depends on: F-L0-009, F-L8-022
- Current delta: None in the automated shared-preview contract. Asset Preview and Scene View are clients of one out-of-process Windows x64 Preview Host that owns common recovered `RenderServer` sessions. The provider-neutral protocol carries bounded scene/frame state, selection weights, Lit/BaseColor/Normal/Wireframe mode, transparent overlay geometry, up to three camera previews, stable correlation, monotonic sequences, current device generation/fence diagnostics, and opt-in capture. A WPF container retains its HWND while a failed process generation is replaced together with its DXGI child surface, after which source-derived state is replayed once.
- Implementation evidence: `src/CSharpGameEngine.Contracts/RenderPreviewHostContracts.cs`; `src/CSharpGameEngine.Windows/Runtime/RenderServer.cs`; `tools/CSharpGameEngine.EditorPreview.Host/`; `tools/CSharpGameEngine.VSExtension/RenderPreviewHostClient.cs`; `RenderPreviewSceneBuilder.cs`; `RenderSurfaceHostWindow.cs`; `AssetPreviewHwndHost.cs`; `SceneHwndHost.cs`; `CSharpGameEngine.VSExtension.csproj`.
- Tests: `RenderPreviewHostProtocolTests`; `RenderPreviewRenderSessionTests`; `RenderPreviewHostProcessTests`; `RenderPreviewHostPipeTests`; `RenderPreviewHostSessionTableTests`; `RenderPreviewHostClientTests`; `RenderPreviewSceneBuilderTests`; `RenderSurfaceHostWindowTests`; `PackagedRenderPreviewHostTests`; `VsixManifestTests`; `RenderServerTests`.
- Conformance: Matches for the automated F-L8-018 contract. The built VSIX contains the same-configuration framework-dependent apphost closure and offline Stage shader artifact, its apphost renders a real WARP frame with the D3D12 debug layer, and package/source tests reject `SceneRenderer`, `VorticeRenderer`, `NativeEvent`, Vortice, and SharpGen. Debug and Release VSIX suites pass 1,017 tests; focused common Render Server/Preview Host suites pass 13 in each configuration. Experimental Instance Scene Designer observation remains Pending, so this Feature is not `Verified` or `RuntimeVerified`.
- Next action: F-L7-024 consumes the completed rendering performance reports in the scoped release closure. F-L8-023 owns executable transform/WARP/rebase/Undo conformance without reintroducing a renderer; the separate Experimental Instance `scene-designer` row remains Pending manual evidence.
- **M0100 (2026-10-04):** protocol 8 carries a camera's depth mapping and transition length. The isolated preview draws every perspective view radial from the editor's matrix (orthographic views stay standard) and each camera thumbnail with its scene camera's mapping (radial by default). `RenderPreviewHostProtocolTests.ProtocolV8_ValidatesRadialCameras`, `RenderPreviewRenderSessionTests.WARP_RadialCameraDrawsTheStandardImageWithRadialDepth`.
- **M0099 (2026-10-04):** the runtime view follows a radial scene camera (F-L5-038, the default since M0100): the editor camera keeps its pose and field of view but takes the scene camera's depth mapping and, for radial depth, a clip range covering both its own and the scene camera's NearZ/FarZ (`EditorCamera_OnARadialSceneCamera_SeesBothItsOwnRangeAndTheScenes`). Picking stays the CPU physical ray. `EditorRuntimeViewTests.EditorCamera_FollowsARadialSceneCameraAndRendersWhatItRendersFromTheSamePose`. A value edit of the scene camera (exposure, depth mapping, transition length, a radial clip range) shows on the next frame instead of waiting for a reload; an unusable value keeps the last usable projection (`EditorCamera_TakesAValueEditOfTheSceneCameraOnTheNextFrame`, `SceneCamera_KeepsTheLastUsableProjectionWhileAnEditIsInvalid`).
- **M0093/ADR-0070 (2026-10-01):** protocol 6 adds the runtime view. For a JSON scene whose game is built, the Scene View opens a `RenderPreviewRuntimeSession` that runs the game's own world and renderer, and streams the live scene JSON so value edits show without a build; a structural edit returns `BuildRequired`. A new build reopens the view, and an unbuilt game still gets the scene preview. Tests: `RenderPreviewRuntimeSessionTests`, `EditorRuntimeViewTests`, `EditorRuntimeViewBridgeTests`, `EditorCameraConventionTests`, `RenderPreviewCameraPoseTests` and `PackagedRuntimeViewTests`.

## F-L7-023 — Rendering performance evidence

- Layer: L7 — Static Compiler / Bake
- Parent capability: Rendering Roadmap
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Rendering.md#performance-and-release-evidence`
- Stage: `rendering.performance` (`Executable`, Visual / Rendering, 7200s)
- Depends on: F-L4-007, F-L0-009
- Current delta: None in the automated performance contract. One isolated TestHost process records exactly five warm-ups and thirty samples for a hidden 1280x720 D3D12 frame and 100,000-instance visibility/batching. Its runtime mode disables Tiered JIT so compilation transitions cannot contaminate measured samples; the raw artifact records adapter, runtime mode, debug-layer errors, pixel verification, and per-sample allocation/time before two canonical SHA-bound performance reports are published.
- Conformance: WARP is the required correctness pass and must reproduce the triangle pixel oracle with zero debug-layer errors. Debug timing is recorded but only correctness and allocation are gated because non-optimized code generation is not a shipping-performance oracle. Release visibility gates median at 6 ms, nearest-rank p95 at 8.33 ms, and allocation p95 at zero. WARP elapsed time is not used as a hardware performance claim; an optional Release hardware pass gates 1280x720 presentation at 16.67 ms.
- Next action: F-L7-024 binds current Debug/Release reports, WARP evidence, shader/artifact provenance, and dependency closure into the rendering release manifest.

## F-L7-024 — Rendering release evidence

- Layer: L7 — Static Compiler / Bake
- Parent capability: Rendering Roadmap
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Rendering.md#performance-and-release-evidence`
- Stage: `rendering.release` (`Executable`, Headless / Foundation, 7200s)
- Depends on: F-L7-023, F-L8-018
- Current delta: None in the automated Rendering track closure. The Stage re-executes `rendering.performance` and `vsix.rendering-preview` under dedicated release profiles, accepts only recent configuration/Git/WARP-matched complete results, reopens the raw capture and both SHA-bound performance reports, and publishes one canonical report over the raw path/hash/schema/adapter/runtime/pixel/debug state, exact Shader Artifact identity, and fixed Rendering source/toolchain closure.
- Conformance: The report is self-validating, Windows x64 and machine-bound, and fails for missing, ordinary-profile, stale, modified, escaped, reparse, non-canonical, partial, or extra evidence. It is a bounded Rendering input to F-L7-048, not a package, installer, signature, SBOM, or competing product release manifest.
- Next action: Product-wide package, compatibility, RC, and immutable manifest stages consume this completed Rendering track closure.

## F-L2-004 — Asset identity and canonical metadata

- Layer: L2 — Type Schema / Identity
- Parent capability: Asset / Content Pipeline
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Asset-Content-Pipeline.md#identity-and-canonical-metadata`
- Stage: `content.identity` (`Executable`, Headless / Foundation, 900s)
- Depends on: None
- Current delta: None in the scoped identity contract. AssetId, SHA-256 SubAssetId, canonical asset URI, and deterministic UTF-8 JSON metadata are implemented.
- Conformance: `CSharpGameEngine.Content.Tests.AssetIdentityTests`
- Next action: Consume canonical metadata from the incremental compiler without adding a legacy reader.

## F-L7-025 — Importer conformance

- Layer: L7 — Static Compiler / Bake
- Parent capability: Asset / Content Pipeline
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Asset-Content-Pipeline.md#import-boundary-and-artifact-envelope`
- Stage: `content.importer-conformance` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L2-004
- Current delta: None for mesh import conformance. glTF and FBX adapters converge on immutable engine-owned geometry/material IR and provider-neutral diagnostics.
- Conformance: `CSharpGameEngine.Import.Tests.ProviderNeutralImportTests`
- Next action: Make the incremental compiler consume this IR and publish canonical artifacts.

## F-L6-005 — Content artifact envelope

- Layer: L6 — Runtime Artifacts
- Parent capability: Asset / Content Pipeline
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Asset-Content-Pipeline.md#import-boundary-and-artifact-envelope`
- Stage: `content.artifact-schema` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L7-025
- Current delta: None in the envelope contract. Fixed little-endian schema, provenance, ordered dependencies, payload hash, checksum, and bounded corruption rejection are implemented.
- Conformance: `CSharpGameEngine.Content.Tests.ContentArtifactTests`
- Next action: Publish validated envelopes atomically through the incremental compiler.

## F-L7-026 — Incremental content compiler

- Layer: L7 — Static Compiler / Bake
- Parent capability: Asset / Content Pipeline
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Asset-Content-Pipeline.md#data-flow-and-state`
- Stage: `content.incremental-cook` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L6-005
- Current delta: None in the compiler core. Reachable closure selection, cycle/missing-dependency rejection, dependency-wave concurrency bounds, source/options/tool/dependency-address fingerprints, canonical envelope validation, CAS publication, and all-or-nothing state publication are implemented.
- Conformance: `IncrementalContentCompilerTests.ColdWarmAndLeafChangeRebuildExactlyTheReachableDirtyClosure`, `IndependentNodesCompileWithinTheDeclaredConcurrencyBound`, and `InvalidGraphDoesNotCompileOrPublishState`.
- Next action: Consume the published immutable index through F-L4-008 runtime asset handles.

## F-L7-027 — Content cache recovery and GC

- Layer: L7 — Static Compiler / Bake
- Parent capability: Asset / Content Pipeline
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Asset-Content-Pipeline.md#data-flow-and-state`
- Stage: `content.cache-recovery` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L7-026
- Current delta: None in the recovery contract. Failed/cancelled compilation preserves exact state bytes, restart removes only temporary publications, corrupt state stops before compilation, and root-safe GC atomically publishes retained state before recoverably quarantining unreachable validated CAS envelopes.
- Conformance: `IncrementalContentCompilerTests.FailedDependentKeepsLastKnownGoodStateAndOrphanIsRecoverablyQuarantined`, `RestartRecoveryRemovesOnlyIncompleteTemporaryPublications`, and `CorruptCompilerStateDoesNotCompileOrReplaceTheState`.
- Next action: Feed the immutable shipping manifest into F-L8-019 authoring and F-L7-028 performance evidence.

## F-L4-008 — Asynchronous runtime asset handles

- Layer: L4 — Domain Servers
- Parent capability: Asset / Content Pipeline
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Asset-Content-Pipeline.md#data-flow-and-state`
- Stage: `content.runtime-load` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L7-027
- Current delta: None. `RuntimeAssetStore` resolves the active immutable manifest, shares typed loads and dependency artifact ownership, bounds concurrent reads, detaches caller cancellation, releases the final lease exactly once, rejects handles after host teardown, and atomically retains the last-known-good manifest when a candidate fails or is cancelled.
- Conformance: `RuntimeAssetStoreTests` covers joined retains, final release, dependency closure, cancellation isolation and eviction, manifest failure/cancellation/success, unsupported codecs, decode failure, IO concurrency bounds, and teardown/load races.
- Next action: Consume the same manifest/handle boundary from F-L8-019 and record F-L7-028 performance evidence.

## F-L6-009 — Engine SDK package and game project context

- Layer: L6 — Content / Packaging
- Parent capability: Asset / Content Pipeline
- Status: Implemented
- Verification: TestVerified, RuntimeVerified (WARP)
- Intended specification: `Docs/Decisions/ADR-0065-Engine-Sdk-Package-And-Project-Context.md`
- Stage: `vsix.project-template` (`Executable`, Visual / VSIX, 2400s)
- Depends on: F-L6-006
- Implementation evidence:
  - `packaging/CSharpGameEngine.Sdk/` (package layout and NuGet entry points)
  - `build/CSharpGameEngine.Game.props` and `build/CSharpGameEngine.ShaderGraph.targets` (Source and Package modes)
  - `tools/CSharpGameEngine.VSExtension/CgeProjectContext.cs`
  - the VSIX `Bridges/` payload
  - `BridgeExecutableLocator`
- Current delta:
  - Physics Scene Bake and the legacy model viewer still need the engine repository.
  - The package version is fixed at 0.1.0 until a release process assigns versions.
- Conformance: `CgeProjectContextTests`, `BridgeExecutableLocatorTests` and `VsixManifestTests`. The Stage builds a project generated outside the repository from the package alone, with no restore or build input pointing into the repository, and plays it on WARP.
- Next action: a dynamic-assembly Physics Bake Host for PackageMode projects, and versioned releases of the package.

## F-L6-006 — Shipping content manifest

- Layer: L6 — Runtime Artifacts
- Parent capability: Asset / Content Pipeline
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Asset-Content-Pipeline.md#performance-and-shipping-rules`
- Stage: `content.package` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L4-008
- Current delta: None. `ShippingContentManifestBuilder` derives only the exact root-reachable closure from published references and validated CAS envelopes. Canonical manifest bytes include hashes, addresses, provenance, kind, length, and dependencies. `ShippingContentPackage` atomically publishes deduplicated address objects and rejects missing, extra, corrupt, cyclic, non-canonical, or reparse-point package state.
- Conformance: `ShippingContentManifestTests` covers deterministic round-trip, exact closure, missing dependency, deep non-recursive traversal, cycle rejection, CAS-address deduplication, atomic package open, and missing/extra/corrupt artifact rejection.
- Next action: Use the canonical manifest as the package/SBOM input for F-L7-029 after F-L7-028 and F-L8-019.

## F-L8-019 — VSIX asset authoring

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Asset / Content Pipeline
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Asset-Content-Pipeline.md#stage-oracle-matrix`
- Stage: `vsix.asset-authoring` (`Executable`, Visual / VSIX, 1800s)
- Depends on: F-L6-006, F-L8-022
- Current delta: None. The net472 VSIX sends typed correlated inspect/save requests to one resident-or-one-shot net10 host. `AssetAuthoringService` alone validates source identity, canonical metadata, importer ownership, immutable `AssetId`, and optimistic SHA-256 concurrency before same-directory atomic publication. The VSIX displays the returned identity and diagnostics and cannot parse or write `.meta` itself.
- Conformance: `AssetAuthoringProtocolTests`, `AssetAuthoringServiceTests`, `AssetAuthoringBridgeWorkerTests`, `AssetAuthoringPresentationTests`, and `BakeIntegrationArchitectureTests` cover bounded/correlated DTOs, missing/create/refresh, canonical encoding, conflict/invalid-source byte preservation, worker transport, thin-client ownership, and UI failure projection; the Visual Stage adds shared Preview Host WARP evidence.
- Next action: Record F-L7-028 content performance evidence without adding another authoring or metadata path.

## F-L7-028 — Content pipeline performance evidence

- Layer: L7 — Static Compiler / Bake
- Parent capability: Asset / Content Pipeline
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Asset-Content-Pipeline.md#performance-and-shipping-rules`
- Stage: `content.performance` (`Executable`, Headless / Foundation, 7200s)
- Depends on: F-L6-006
- Current delta: None. `ContentPerformanceProbe` runs the real `IncrementalContentCompiler` for five warm-ups plus thirty cold 32-root samples and thirty one-leaf-change 33-node samples. Every iteration asserts the exact rebuilt/reused closure; reports include median, nearest-rank p95, MAD, allocation p95, explicit budgets, machine fingerprint, git provenance, payload SHA-256, and independently recomputed statistics/verdicts.
- Conformance: `verify-content-performance`, `PerformanceMeasurementFrameworkTests`, and `IncrementalContentCompilerTests` reject wrong sample counts, invalid statistics, corrupt reports, budget failures, and non-incremental rebuild behavior.
- Next action: Keep its configuration-qualified reports bound into the F-L7-029 release closure.

## F-L7-029 — Content pipeline release evidence

- Layer: L7 — Static Compiler / Bake
- Parent capability: Asset / Content Pipeline
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Asset-Content-Pipeline.md#performance-and-shipping-rules`
- Stage: `content.release` (`Executable`, Headless / Foundation, 7200s)
- Depends on: F-L7-028, F-L8-019
- Current delta: None. `ContentReleaseEvidence` publishes one seven-kind canonical runtime-loadable `ShippingContentPackage`, then binds its exact file closure and manifest fingerprint together with the owned metadata/artifact/manifest schema sources, a bounded project/toolchain SBOM input set, matching Git provenance, and recent passing configuration-matched cold/incremental performance reports into one canonical SHA-256 report. Validation reopens the package, recomputes every byte length and hash, and rejects path escape, reparse points, missing, extra, stale, relocated, non-canonical, or modified evidence. SPDX generation, signing, install/run/uninstall, and the formal immutable product release manifest remain owned by F-L7-045 and F-L7-048.
- Conformance: `verify-content-release`, `ContentReleaseEvidenceTests`, and `ShippingContentManifestTests` cover package reopen, exact closure, source/SBOM input mutation, performance mutation or absence, report canonicality, and extra evidence rejection.
- Next action: Keep this content-scoped evidence as an input to F-L7-045 without turning it into a second product release manifest.

## F-L8-020 — VSIX manual and Experimental Instance baseline

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Editor / VSIX
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Editor-VSIX.md#manual-and-automated-evidence`
- Stage: `vsix.experimental-smoke` (`Executable`, Headless / VSIX, 3600s)
- Depends on: None
- Current delta: The versioned ledger and false-pass validator are implemented and executable; legacy manual checks remain explicitly Pending rather than false Passed.
- Conformance: Matches
- Next action: Attach fresh evidence before changing an individual ledger item to Passed.

## F-L8-021 — VSIX Experimental Instance automation

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Editor / VSIX
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Editor-VSIX.md#manual-and-automated-evidence`
- Stage: `vsix.experimental-automation` (`Executable`, Headless / VSIX, 3600s)
- Depends on: F-L8-020
- Current delta: The CgeAutomation-only runner, provenance checks, stable AutomationIds, evidence serialization, InfrastructureError mapping, and owned-PID shutdown are implemented. Debug and Release 1041 runs passed 19/19 assertions with normal shutdown.
- Conformance: Matches
- Next action: Preserve the isolated-hive and owned-PID contract when extending future VSIX scenarios.

## F-L8-022 — Versioned authoring host lifecycle

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Editor / VSIX
- Status: Implemented
- Verification: TestVerified (shared netstandard2.0 protocol, real named pipe, terminal-state/session and net472 client tests)
- Intended specification: `Docs/Specifications/Editor-VSIX.md#host-boundary-and-protocol`
- Stage: `vsix.host-lifecycle` (`Executable`, Headless / VSIX, 1800s)
- Depends on: F-L8-021
- Current delta: Live multi-message progress streaming is intentionally not owned by this Level; the terminal result carries the last validated progress and F-L8-026 owns UI presentation.
- Evidence: `AuthoringHostRequest`/`Progress`/`Result` are shared by net472 and net10 through Contracts; request ID, protocol, timeout, diagnostics, bounded framing, active-request drain, cancellation, crash, and mismatch are executable contracts.
- Conformance: Matches. Only pre-submission unavailability permits a fresh one-shot request; timeout, disconnect, and protocol mismatch after submission are terminal and cannot duplicate an authoring mutation.
- Next action: Preserve this single protocol while F-L8-026 adds Bake/import progress presentation and actionable UI diagnostics.

## F-L8-023 — Transform tool visual conformance

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Editor / VSIX
- Status: Implemented
- Verification: TestVerified (2,047/2,047 Debug; 2,046/2,046 Release; Debug/Release solution builds and executable Stage passed)
- Intended specification: `Docs/Specifications/Editor-VSIX.md#editing-contracts`
- Stage: `vsix.transform-tools` (`Executable`, Visual / VSIX, 1800s)
- Depends on: F-L8-022
- Current delta: none for the automated contract. Move, Rotate, Scale, local/world mode, numeric/vertex/surface snap, and repeated drag previews remain float-relative derived UI state and do not touch C# until one final commit. Canonical Transform edits change only local Logical position/rotation/scale members and preserve `ReferenceFrameId`; conflict landing either keeps the predicted document after equivalent Roslyn reparse or explicitly rebases. The common Preview Host/Render Server now has an isolated WARP pixel oracle proving overlay appearance and removal without a VSIX-owned renderer.
- Evidence: `TransformGizmo.cs`; `TransformGizmoPhase4.cs`; `EditTransactionController.cs`; `SceneTransformMath.cs`; `SceneEditConflictMerger.cs`; `SceneSourceCommitPlanner.cs`; `SceneSourceEditor.cs`; `SceneTextBufferWriter.cs`; `RenderPreviewSceneBuilder.cs`; `RenderPreviewRenderSession.cs`; `VsixReferenceScene.cs`.
- Tests: `TransformGizmoTests`; `SurfaceSnapGizmoTests`; `EditTransactionControllerTests`; `SceneTransformMathTests`; `SceneEditConflictMergerTests`; `SceneSourceCommitPlannerTests`; `SceneSourceEditorTests`; `SceneSaveVerificationTests`; `RenderPreviewSceneBuilderTests`; `SceneDesignerUndoCommandPolicyTests`; `RenderPreviewRenderSessionTests`; `FeatureStageCatalogTests`.
- Conformance: Matches for the automated F-L8-023 contract. Repeated preview updates produce one final source change, one history step is routed through the single `ITextUndoTransaction` writer, canonical frame identity survives edits, reparse/rebase decisions are explicit, and an actual D3D12 WARP frame changes only while overlay geometry exists. The Experimental Instance `scene-designer` observation remains Pending and is not represented as `RuntimeVerified`.
- Next action: Keep the completed gesture boundary in `vsix.transform-tools`; F-L8-025 now owns the source-commit convergence contract.

## F-L8-024 — Typed inspector and component palette

- M0088: LightValueContract and TypedLightEditingTests cover exact constructor-symbol matching, physical quantities/RGB/cone/size, field-initializer defaults and atomic light/Transform/shadow palette batches. Verification is recorded in Docs/Milestones/M0088-Typed-Lighting.md.

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Editor / VSIX
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Editor-VSIX.md#editing-contracts`
- Stage: `vsix.inspector-contracts` (`Executable`, Headless / VSIX, 1800s)
- Depends on: F-L8-022
- Current delta: none. Inspector control selection is derived from the declared component-member type; enum and compatible immutable `[SceneAssets]` members use one typed choice contract, canonical `float` and integer members retain validated numeric editing, and noncanonical `double`/`decimal` members are read-only. Invalid or unchanged choices create no source operation. Existing vector, add, remove, reorder, component-initializer, and reparse behavior is covered by the same executable Stage.
- Conformance: Matches
- Next action: Keep `vsix.inspector-contracts` in changed/full/VSIX suites; F-L8-025 now commits its typed operations through the common source transaction.

## F-L8-025 — Scene document synchronization

- M0094/ADR-0071 (2026-10-01): the Scene Designer's document is the Scene Editor, opened by double-clicking a scene JSON (`Scenes\**\*.json`; `*.scene.json` until the same day). Its Design and JSON views share the JSON text buffer as the document data, so this synchronization contract is unchanged; the code-editor/Solution Explorer context menus and the external `.cgescene` virtual document are removed. Tests: `SceneJsonEditorTests`, `VsixMenuPlacementTests`.

- M0088: 12 source-only ConvertLightOperation paths retain Node and shadow ownership, preserve local flux, require explicit directional quantities, reject source/default-contract conflicts and pass canonical verification through one native text write. No private Undo or runtime kind replacement.

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Editor / VSIX
- Status: Implemented
- Verification: TestVerified (M0093 C3b: complete VSIX suite in Debug; the M0070 C# evidence below is historical)
- Intended specification: `Docs/Specifications/Editor-VSIX.md#authoring-model`
- Stage: `vsix.source-sync` (`Executable`, Headless / VSIX, 1800s)
- Depends on: F-L8-023, F-L8-024
- Current delta: interactive use in Visual Studio (JSON editor and Scene Designer side by side) is a pending manual check. Each completed gesture resolves the latest Roslyn context, explicitly rebases or rejects stale input, applies the immutable operation batch to the scene model, writes the model as canonical scene JSON with its node ids kept, re-reads it through the designer view, and writes only the changed lines to the live buffer once (ADR-0070 decision 5). The published predicted snapshot is the canonical re-read result, with the operation projection used only as an identity basis for newly created nodes. Save persists the live `*.scene.json` document and never replays editor history.
- Source of truth and ownership: the scene JSON (`Scenes\**\*.json`) buffer and Visual Studio native text history are the only authoring and Undo truths. `SceneEditorSessionService` owns only immutable real/predicted projections, a transient pending-landing identity basis keyed by Scene and canonical semantic signature, and one in-flight write guard per Scene. It owns no editable document, mirrored Undo stack, text watermark, or save-time history.
- Conflict and lifecycle: transform deltas rebase onto the latest source; stale member edits without an observed original value reject instead of overwriting; live-buffer mismatch writes nothing; cancellation/failure clears pending landing state; an older analysis cannot consume another generation's identity; a completed mismatching landing is discarded as a superseding external edit; post-write closure is non-cancellable; Undo/Redo delegates to native text history and schedules Roslyn analysis.
- Evidence: `SceneSourceCommitPlanner.cs`; `SceneJsonCommitPlanner.cs`; `SceneJsonWorkspaceSource.cs`; `SceneEditorSessionService.cs`; `SceneWorkspaceService.cs`; `SceneTextBufferWriter.cs`; `SceneEditConflictMerger.cs`; all Hierarchy/Inspector/Viewport callers. Removed legacy includes `SceneEditDocument`, `SceneTextUndoLedger`, text watermark policies, external landing policy, own-save landing policy, multi-step save writer, and their implementation-detail tests; M0093 removed the C# scene writer (`SceneSourceEditor`) and `SceneSaveVerification`.
- Tests: `PendingSourceLandingTests`; `SceneJsonCommitPlannerTests`; `SceneJsonWorkspaceSourceTests`; `SceneSourceTruthArchitectureTests`; `SceneEditConflictMergerTests`; `SceneModelEditApplierTests` (predictions checked by writing and re-reading scene JSON); `SceneTextChangePlannerTests`; `SceneUndoRoutingPolicyTests`; `SceneDesignerUndoCommandPolicyTests`; `SceneUndoRedoKeyPolicyTests`; `SceneSavePolicyTests`; `SceneProjectionIdentityTests`; `HierarchySourceSyncTests`; complete VSIX suites.
- Conformance: Matches. One gesture maps to at most one native Undo transaction; consecutive gestures before/after Roslyn landing retain NodeId and avoid self-rebase; external stale input is rebased or rejected; source, model, selection identity, and Hub projection converge without a second authoring state.
- Next action: Keep `vsix.source-sync` in changed/full/VSIX suites while F-L8-026 consumes this source and diagnostic boundary.

## F-L8-026 — Bake, asset, and diagnostic UI

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Editor / VSIX
- Status: Implemented
- Verification: TestVerified (963/963 VSIX and 71/71 FeatureRunner in Debug/Release; shared host 22/22 and registration 3/3 in both; executable Stage passed in both configurations)
- Intended specification: `Docs/Specifications/Editor-VSIX.md#editing-contracts`
- Stage: `vsix.bake-integration` (`Executable`, Headless / VSIX, 1800s)
- Depends on: F-L8-025
- Current delta: none for the automated contract. Import and Bake expose one immutable request-correlated UI projection; Physics Bake reports monotonic progress, supports cancellation, keeps completed outputs after a later failure/cancel, and opens a selected artifact from the result projection. Asset Preview presents Import and recovered Render Server status inline.
- Source of truth and ownership: the versioned `AuthoringHostRequest`/`Progress`/`Result`/`Diagnostic` envelope and each operation's terminal result are the only execution truth. `AuthoringOperationSnapshot` is an immutable validated UI projection, not another job, build, artifact, or retry state. C# source remains owned by F-L8-025 and exact host/artifact execution remains owned by the existing bridge and Bake services.
- Error and lifecycle contract: request/protocol mismatch, non-monotonic or non-finite progress, invalid terminal progress, nonzero exit, and error diagnostics cannot become success. Cancelling ignores late progress until the terminal result. Host diagnostic codes remain typed through resident client and bridge exceptions. Partial output paths survive failure/cancel; the UI never infers completion from file names or button state.
- Evidence: `AuthoringOperationPresentation`; typed `ResidentBridgeClient.TryRunDetailed` and `AuthoringHostOperationException`; `PhysicsSceneBakeService`; Scene Designer Bake progress/cancel/diagnostic/artifact surfaces; Asset Preview inline progress/diagnostic surface; focused presentation/orchestration/architecture/localization and shared host tests.
- Conformance: Matches F-L8-026 without adding another source writer, build implementation, mutable operation Manager, retry layer, or renderer.
- Next action: Keep `vsix.bake-integration` in changed/full/VSIX suites.

## F-L8-028 — VSIX compatibility matrix

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Editor / VSIX
- Status: Implemented
- Verification: TestVerified in Debug and Release
- Intended specification: `Docs/Specifications/Editor-VSIX.md#manual-and-automated-evidence`
- Stage: `vsix.compatibility` (`Executable`, Headless / VSIX, 3600s)
- Depends on: F-L8-026
- Current delta: `Docs/Verification/VSIX-Compatibility-Matrix.json` is the single versioned compatibility contract. The validator checks the net472 VSIX and automation projects, amd64 Visual Studio 17.14+ installation/prerequisite manifest, .NET Framework 4.7.2 dependency, pinned VS SDK/Roslyn versions, neutral en-US plus ja-JP satellite/template packaging, CgeFixed development hive, isolated CgeAutomation evidence hive, Debug/Release CI cells, exact Experimental checks/owners, and the aggregate manual entry.
- Evidence semantics: pre-release validation accepts a structurally valid explicit Pending state and never turns it into compatibility success. It also accepts a structurally valid Passed row whose observation has aged past its expiry, because the expiry window is a time-based approximation of source drift rather than a measurement of it, and an ordinary build must not be blocked by it. Passed automated rows additionally prove configuration, locale, CgeAutomation suffix, Visual Studio minimum version, four SHA-256 provenance fields, all-passing assertions, and observation. `--require-passing-evidence` fails every required Pending/Failed/expired row and the manual summary, so freshness is a release requirement; F-L8-029 must use that mode.
- Evidence: repository/mutation/release-fail-closed tests in `VsixCompatibilityMatrixValidatorTests`, existing manifest/package/localization/layout/Experimental automation tests, and build-time validator execution. Full FeatureRunner tests pass 76/76 and full net472 VSIX tests pass 970/970 in both Debug and Release. `vsix.compatibility` passed Debug (`20260901T114505804Z-vsix-a650a39f789646f1b6369d30e7bd6c87`) and Release (`20260901T114546489Z-vsix-609836b366cc4b7c8a3d67cae87be3fd`).
- Conformance: Matches the F-L8-028 matrix/validation boundary without claiming that currently Pending human Experimental/DPI/accessibility observations have passed.
- Next action: Run Debug/Release `vsix.compatibility`, then consume its strict release mode from F-L8-029 after actual Experimental/manual evidence is recorded.

## F-L8-029 — VSIX release evidence

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Editor / VSIX
- Status: Implementing
- Verification: TestVerified for the report/lifecycle contract; RuntimeVerified for current-package 1041 and isolated install/uninstall; 1033 and six manual observations remain incomplete
- Intended specification: `Docs/Specifications/Editor-VSIX.md#stage-oracle-matrix`
- Stage: `vsix.release` (`Executable`, Headless / VSIX, 7200s)
- Depends on: F-L8-028
- Current delta: The canonical exact-package report, strict observation binding, and isolated CgeRelease lifecycle are implemented. The required 1033 host observation cannot pass until the en-US Visual Studio language pack is installed with elevation; six manual rows are still Pending and the Stage therefore fails closed.
- Evidence: `tools/CSharpGameEngine.FeatureRunner/VsixReleaseEvidence.cs`, `tools/CSharpGameEngine.VSExtension/Capture-CgeVsixReleaseLifecycle.ps1`, `tests/CSharpGameEngine.FeatureRunner.Tests/VsixReleaseEvidenceTests.cs`; current exact-package 1041 evidence under `.artifacts/VsixExperimental/20260901-163257-d99bdf74/1041/` and zero-residue lifecycle under `.artifacts/VsixReleaseLifecycle/20260901T163427704Z-e484c31087294d19ad8e6074794d30c0/` share package SHA-256 `1e89e9f29011d83c7c337259b501aa024c57622c8ef8bfe4887e93539429fe00`.
- Conformance: Partial. Source/package/install/uninstall ownership now Matches; launch/localization/manual release closure remains incomplete and cannot produce a passing report.
- Next action: install the en-US Visual Studio language pack with user-approved elevation, capture 1033 against the unchanged package, record the six current-package manual observations, then run Debug/Release outer `vsix.release` evidence without rebuilding the VSIX.

## F-L7-030 — ECS runtime baseline conformance

- Layer: L7 — Static Compiler / Bake
- Parent capability: ECS / Runtime
- Status: Implemented
- Verification: TestVerified (Entities and CodeAnalysis suites, sequential Debug/Release stage runs)
- Intended specification: `Docs/Specifications/Ecs-Runtime.md#scope-and-observed-baseline`
- Stage: `ecs.baseline` (`Executable`, Headless / Foundation, 900s)
- Depends on: None
- Current delta: none for the closed-set Archetype, Scene generation/lifecycle, system-major ordering, and delayed structural-change baseline.
- Evidence: `src/CSharpGameEngine.Entities/`, `src/CSharpGameEngine.CodeAnalysis/`, `tests/CSharpGameEngine.Entities.Tests/`, `tests/CSharpGameEngine.CodeAnalysis.Tests/`
- Conformance: Matches for the implemented baseline contract; unified command replay and other future ECS features remain separately specified.
- Next action: preserve the executable oracle while implementing the next ECS roadmap feature.

## F-L5-035 — Portable shared Transform module

- Layer: L5 — Runtime Feature Modules
- Parent capability: ECS / Runtime
- Status: Implemented
- Verification: TestVerified (portable contract, render/physics/audio consumption)
- Intended specification: `Docs/Specifications/Ecs-Runtime.md#runtime-boundaries-and-shared-transform`
- Stage: `ecs.runtime-boundary` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L7-030
- Current delta: none for the portable frame-local Transform contract.
- Evidence: `src/CSharpGameEngine.Entities/Transform.cs`, `src/CSharpGameEngine.Entities/FixedTransform.cs`, and render/physics/audio extraction tests.
- Conformance: Matches. Authoritative fields are `ReferenceFrameId`, `LogicalVector3`, and `LogicalQuaternion`; float exists only after domain/camera/listener-relative projection.
- Next action: preserve this boundary while adding change tracking and deterministic jobs.

## F-L3-021 — Unified structural command buffer

- Layer: L3 — ECS / Job Runtime
- Parent capability: ECS / Runtime
- Status: Implemented
- Verification: TestVerified (global ordering, mutation guard, scene/cell ownership, stale generation)
- Intended specification: `Docs/Specifications/Ecs-Runtime.md#tick-state-and-structural-changes`
- Stage: `ecs.structural-changes` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L3-018, F-L5-035
- Current delta: none for the single-thread structural barrier contract.
- Evidence: `EntityCommandBuffer` is the only ordered stream for Create/Move/Destroy/Scene/Cell changes; generated playback applies global enqueue order and direct mutation is rejected during system execution.
- Conformance: Matches for Level 3. Add/remove are generated full-archetype Move payloads rather than a second mutation path.
- Next action: extend command production to deterministic worker partitions in F-L3-019 without introducing another source of truth.

## F-L3-022 — Enable masks and change versions

- Layer: L3 — ECS / Job Runtime
- Parent capability: ECS / Runtime
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Ecs-Runtime.md#generated-storage-and-scheduling`
- Stage: `ecs.change-tracking` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L3-021
- Current delta: None. Every generated archetype stores entity and component enable bits plus per-row component versions. Create, move, enable transitions, and generated system ref access advance one World-owned wrap-safe version source; caller-span queries visit only archetypes containing the selected component.
- Conformance: `ChangeTrackingSpec`, generated dispatch E2E, 65/128 high-word E2E, and the exactly-64 single-word emission boundary.
- Next action: Preserve the single clock while snapshots serialize only completed barriers.

## F-L3-019 — Deterministic job runtime

- Layer: L3 — ECS / Job Runtime
- Parent capability: ECS / Runtime
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Ecs-Runtime.md#generated-storage-and-scheduling`
- Stage: `foundation.deterministic-jobs` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L3-022
- Current delta: None. Fixed partitions run through a bounded worker set, unmanaged job/state isolation prevents hidden shared mutable state, and outputs/events publish only after a stable successful merge.
- Conformance: Debug/Release `DeterministicJobRuntimeSpec` proves single/scheduled state-event hash identity, worker bounds, deterministic lowest-partition failure, invalid-mode rejection, cancellation, and unchanged caller input.
- Next action: Consume only completed job results when F-L6-007 snapshots are produced.

## F-L7-031 — Generated schedule diagnostics

- Layer: L7 — Static Compiler / Bake
- Parent capability: ECS / Runtime
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Ecs-Runtime.md#generated-storage-and-scheduling`
- Stage: `ecs.schedule-diagnostics` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L3-019
- Current delta: None. Explicit read/write declarations generate static low/high masks, predecessor lists, and dependency waves. Missing targets, phase contradictions, cycles, and unordered write conflicts fail generation before execution.
- Conformance: `GameSystemScheduleTests` covers read-only dispatch/change stamping, same-wave reads, ordered waves, missing dependencies, cycles, conflict messages, and reflection/dictionary-free output.
- Next action: Preserve the generated schema and schedule hash in F-L6-007 snapshots.

## F-L6-007 — Schema-fingerprinted ECS snapshots

- Layer: L6 — Runtime Artifacts
- Parent capability: ECS / Runtime
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Ecs-Runtime.md#snapshot-artifact`
- Stage: `ecs.snapshot` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L3-022, F-L3-019
- Current delta: None. Generated archetype/Component schemas, canonical checksummed cell deltas, completed-tick capture, atomic loaded-cell restore, tombstones, and unloaded-cell pending deltas are implemented without a legacy reader.
- Conformance: `WorldSnapshotSpec` covers exact round-trip, corruption/schema rejection, canonical-payload validation, tombstones, unloaded-cell merge/pending restore, transient derived links, archetype/entity mismatch, and structural-barrier atomicity. `SnapshotGeneratorTests` fixes the generated boundary including zero-archetype compilations, and `GeneratedWorldCellActivationTests` validates the shipping Scene/cell path and `IRuntimeComponent` exclusion.
- Next action: Bind the stable schema/source hashes and ECS performance reports into F-L7-033 release closure.

## F-L7-032 — ECS runtime performance evidence

- Layer: L7 — Static Compiler / Bake
- Parent capability: ECS / Runtime
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Ecs-Runtime.md#performance-and-release-evidence`
- Stage: `ecs.performance` (`Executable`, Headless / Foundation, 7200s)
- Depends on: F-L7-031, F-L6-007
- Current delta: None. Independent 1, 64, 65, and 128 Component compilations execute the generated query over 100,000 active entities for exactly five warm-ups and thirty samples with Tiered JIT disabled. Raw and common reports bind the exact one-word/two-word source shape, generated-source SHA-256, deterministic state SHA-256, query, worker count, timing, and zero allocation.
- Conformance: Debug records diagnostic timing without treating non-optimized code as a shipping oracle. Release gates median at 6 ms, p95 at 8.33 ms, and allocation p95 at zero; the separately compiled 64-type single-word path additionally fails if its median or nearest-rank p95 regresses more than 20 percent from the one-type direct path. Missing, duplicate, extra, malformed, aliased, or tampered boundary evidence fails closed.
- Evidence: current Debug Stage `20260901T155813468Z-ecs-release-performance-920d1c8ed90d477288a1349f15c72996`; current Release Stage `20260901T160004976Z-ecs-release-performance-9358908023fa4e8386adbfd9b7b67e3b`. Release 1/64/65/128 medians are 484,600/526,750/638,900/563,850 ns, p95 values are 645,700/695,600/929,600/646,500 ns, and allocation p95 is zero for every boundary.
- Next action: Use these four raw captures and reports in F-L7-033 canonical ECS release evidence.

## F-L7-033 — ECS runtime release evidence

- Layer: L7 — Static Compiler / Bake
- Parent capability: ECS / Runtime
- Status: Specified
- Status: Implemented
- Verification: Automated
- Intended specification: `Docs/Specifications/Ecs-Runtime.md#performance-and-release-evidence`
- Stage: `ecs.release` (`Executable`, Headless / Foundation, 7200s)
- Depends on: F-L7-032
- Current delta: None. The Stage reruns deterministic replay, generated schedule diagnostics, schema-fingerprinted snapshot, and four-boundary performance under dedicated profiles, then emits one canonical ECS-track report over their complete Stage results, four raw captures, four common performance reports, twenty-two ordered source/toolchain inputs, Windows x64 machine identity, and Git provenance.
- Conformance: The validator reopens and hashes every bound Stage/raw/performance/input file, recomputes Stage artifacts, performance statistics/verdicts, machine fingerprint, generated source shape/hash, deterministic final state, and report payload hash. Stale, ordinary-profile, wrong-configuration/Git/adapter, incomplete, modified, escaped, reparse, noncanonical, or extra evidence fails closed without leaving a partial directory. This report owns no package, SBOM, installer, signature, or product manifest.
- Evidence: final-specification Debug Stage `20260901T155824288Z-ecs-release-closure-final-docs-9decb27cb9594bab977e17954337c348` with report SHA-256 `6c5e522848b335b271653ddd3550a6de097ddb13268f993608499e257f2b0ead`; final-specification Release Stage `20260901T160015524Z-ecs-release-closure-final-docs-79075f8a369741f0b9895e55380cadfb` with report SHA-256 `adae41f5bded563b12f6ac447b48f12237f80a5a8ffe2ae155fe2bec490bb02c`. Focused performance/release contracts pass 8/8 and report validation passes after deleting derived generated `obj`.
- Next action: Consume the canonical ECS track report in F-L7-047/F-L7-048 product release aggregation.

## F-L1-008 — Platform event and timestamp contracts

- Layer: L1 — Core Kernel
- Parent capability: Audio / Input / Platform
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Audio-Input-Platform.md#l1-value-contracts`
- Stage: `platform.boundaries` (`Executable`, Headless / Foundation, 900s)
- Depends on: None
- Current delta: Native-free monotonic timestamp, window/device/context/action IDs, controls, values, raw events, action phases, release reasons, and deterministic name-derived IDs are implemented in Core.
- Conformance: Matches
- Next action: Preserve native-type isolation while extending later audio contracts.

## F-L0-010 — Win32 window and input binding

- Layer: L0 — Platform / Native Backends
- Parent capability: Audio / Input / Platform
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Audio-Input-Platform.md#server-data-flow`
- Stage: `platform.window-input` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L1-008
- Current delta: Win32 keyboard, mouse button/wheel, raw relative mouse, XInput pad 0 (Unreal key names), device notification, QPC timestamp, repeat suppression, and forced release on focus/capture/device/window loss are implemented behind `DrainInputEvents()`. Mouse-look capture clips the cursor only while the window has focus. Automation can inject scheduled keys (ADR-0067).
- Conformance: Matches
- Next action: Preserve QPC and forced-release behavior while adding future device kinds.

## F-L4-009 — Audio device lifecycle server

- Layer: L4 — Domain Servers
- Parent capability: Audio / Input / Platform
- Status: Implemented
- Verification: TestVerified (state/mode transitions, silent fallback, explicit recovery, host ownership, XAudio2 smoke)
- Intended specification: `Docs/Specifications/Audio-Input-Platform.md#audio-contract-and-recovery`
- Stage: `audio.lifecycle` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L1-008
- Current delta: none for device creation and ownership. Voice restoration after device loss is implemented by F-L4-012 without adding another device owner.
- Evidence: `AudioDeviceServer`, lazy `EngineHost.EnsureAudioDevice`, fault-injection tests, and real XAudio2 lifecycle tests.
- Conformance: Matches. State and backend mode are separate; failed or invalid native creation becomes Ready/Silent or Faulted/None, explicit recovery never retains a stale native handle, and disposal releases one engine generation once.
- Next action: Preserve the one-generation ownership contract through platform compatibility evidence in F-L7-034.

## F-L4-010 — Deterministic input action server

- Layer: L4 — Domain Servers
- Parent capability: Audio / Input / Platform
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Audio-Input-Platform.md#input-contract`
- Stage: `input.actions` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L0-010
- Current delta: Stable raw-event ordering, context priority, chord evaluation, 1D/2D dead-zone, reserved actions, collision-checked rebind, context/device cancellation, and deterministic action frames are implemented. `ReadValue` sums an action's bindings after ordered Negate/Swizzle modifiers, and relative mouse axes accumulate per processed frame (Enhanced Input semantics, ADR-0067).
- Conformance: Matches
- Next action: Preserve deterministic trace/action identity while adding future composite bindings.

## F-L4-011 — Audio mixer and offline oracle

- Layer: L4 — Domain Servers
- Parent capability: Audio / Input / Platform
- Status: Implemented
- Verification: TestVerified (fixed PCM hash, bus/gain/pan, loop, voice limit, underrun policy, ownership, allocation)
- Intended specification: `Docs/Specifications/Audio-Input-Platform.md#audio-contract-and-recovery`
- Stage: `audio.mixer` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L4-009
- Current delta: none for offline mixing. Immutable decoded stream artifacts and device-recovery restoration are implemented by F-L4-012.
- Evidence: portable `AudioMixer`, immutable owned `PcmAudioClip`/`PcmAudioStreamArtifact`, EngineHost/device-server ownership, fixed little-endian PCM16 SHA-256 oracle, and zero-allocation steady-state loop test.
- Conformance: Matches. Mixer state owns buses and voice cursors; voice limit rejects without implicit stealing, loop bounds require available data, and underrun uses explicit `Silence` or `Stop` policy with typed evidence.
- Next action: Preserve the offline oracle while later content work adds asynchronous artifact loading outside the mixer.

## F-L4-012 — Spatial audio and recovery

- Layer: L4 — Domain Servers
- Parent capability: Audio / Input / Platform
- Status: Implemented
- Verification: TestVerified (listener-local attenuation/pan, boundary rejection, artifact ownership, logical-frame restore, typed stop, failed-recreate preservation, frame-relative runtime extraction)
- Intended specification: `Docs/Specifications/Audio-Input-Platform.md#audio-contract-and-recovery`
- Stage: `audio.spatial` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L4-010, F-L4-011
- Current delta: none. Spatial inputs are derived only from listener-relative float coordinates, decoded PCM artifacts own their samples and canonical `AssetArtifactReference`, and the mixer remains the sole voice/cursor truth across device generations.
- Evidence: `AudioSpatializer`, `PcmAudioStreamArtifact`, mutable per-voice derived gain/pan, `AudioVoiceRecoveryReport`, `AudioDeviceSnapshot.LastRecovery`, runtime listener-relative extraction, and fault-injection tests.
- Conformance: Matches. Successful XAudio2 or Silent recreation restores only explicitly eligible artifact voices at a defined logical frame and stops non-eligible voices with typed decisions; failed recreation preserves logical state for retry and never retains a native pointer.
- Next action: Preserve the immutable audio/device recovery snapshot contract through F-L7-034 compatibility evidence.

## F-L7-034 — Platform compatibility evidence

- Layer: L7 — Static Compiler / Bake
- Parent capability: Audio / Input / Platform
- Status: Implemented
- Verification: TestVerified (versioned matrix, CI/workflow agreement, executable prerequisite closure, optional hardware ledger separation, Debug/Release Stage)
- Intended specification: `Docs/Specifications/Audio-Input-Platform.md#performance-and-compatibility`
- Stage: `platform.compatibility` (`Executable`, Headless / Foundation, 3600s)
- Depends on: F-L4-012
- Current delta: none. `Platform-Compatibility-Matrix.json` binds the exact Input and Audio prerequisite Stages to mandatory Windows x64 Debug/Release solution, Windows-TFM, and WARP CI cells without copying their dynamic result state.
- Evidence: `PlatformCompatibilityMatrixValidator`, CI/workflow bidirectional validation, focused synthetic/silent runtime tests, explicit `hardware-audio`/`hardware-d3d12` Manual Evidence references, and passing Debug (`20260901T134331089Z-full-eda58875a7414b48b8919b4233ae5ceb`) / Release (`20260901T134337227Z-full-22bf326925d14baea1832fce1751c8cf`) Stage results.
- Conformance: Matches. Hardware observations remain supplemental and cannot replace synthetic Input, Silent Audio, or WARP correctness; missing, duplicated, promoted, mismatched, or non-executable references fail closed.
- Next action: Aggregate configuration, adapter, device, and Stage provenance into F-L7-035 platform release evidence.

## F-L7-035 — Platform release evidence

- Layer: L7 — Static Compiler / Bake
- Parent capability: Audio / Input / Platform
- Status: Implemented
- Verification: TestVerified (canonical report/hash, exact source closure, matching compatibility result, environment/mode/manual provenance, Debug/Release Stage)
- Intended specification: `Docs/Specifications/Audio-Input-Platform.md#stage-oracle-matrix`
- Stage: `platform.release` (`Executable`, Headless / Foundation, 7200s)
- Depends on: F-L7-034
- Current delta: none. One Platform-scoped report binds a recent configuration/Git-matched F-L7-034 result, exact source and verification inputs, Windows target, machine/timestamp fingerprint, required modes, and optional hardware ledger snapshot.
- Evidence: `PlatformReleaseEvidence`, canonical report validation, tamper/extra/missing/wrong-profile prerequisite regressions, Debug Stage `20260901T135955088Z-full-dbd3216597ae47fc9accafb6fa8d5bf9` with report SHA `822f1feb7633f7ce2fcd9ef74ee0a45f27f0f28106788e70b6c23f9bbb1f9901`, and Release Stage `20260901T135955011Z-full-a0dae41606384a91a49b3fc0d67a9752` with report SHA `2b26dc75d215bf15f36dc89c790c0bb9b897767a66337b8a7160592e63bfe4d1`.
- Conformance: Matches without creating a product package, signing format, SBOM, or second release manifest.
- Next action: Consume this bounded track report from the later product-wide F-L7-045/F-L7-048 release closure.

## F-L7-036 — Feature coverage and catalog validation

- Layer: L7 — Static Compiler / Bake
- Parent capability: Testing / CI / Release
- Status: Implemented
- Verification: TestVerified in Debug through `RepositoryVerificationTests` and the `verify-coverage` CLI
- Intended specification: `Docs/Specifications/Testing-CI-Release.md#catalog-and-feature-metadata`
- Stage: `verification.coverage` (Executable, Headless / Foundation, 900s)
- Depends on: F-L7-021
- Current delta: None at Level 1. The validator rejects missing Feature entries, Status/Stage/dependency disagreement, invalid Catalog graphs, and missing Visual Scene bindings.
- Conformance: Matches the Level 1 coverage contract.
- Next action: Keep the validator in the build gate while later Features move from Specified to Implemented.

## F-L7-037 — Manual verification ledger

- Layer: L7 — Static Compiler / Bake
- Parent capability: Testing / CI / Release
- Status: Implemented
- Verification: TestVerified in Debug through `RepositoryVerificationTests` and the `verify-manual-ledger` CLI
- Intended specification: `Docs/Specifications/Testing-CI-Release.md#ci-matrix-and-evidence-separation`
- Stage: `verification.manual-ledger` (Executable, Headless / Foundation, 900s)
- Depends on: F-L7-036
- Current delta: None at Level 1. `Docs/Verification/Manual-Evidence.json` records owner and explicit state; Passed evidence requires an existing link and future expiry, and `--release` rejects Pending or Failed entries.
- Conformance: Matches the Level 1 manual evidence contract.
- Next action: Replace Pending rows with time-bounded Passed evidence only when the corresponding Level 6/7 manual run is performed.

## F-L7-038 — Changed-path stage selection

- Layer: L7 — Static Compiler / Bake
- Parent capability: Testing / CI / Release
- Status: Implemented
- Verification: TestVerified (path-boundary matching, global build closure, rename/copy source and destination, literal spaces, executable/profile filtering, runner fallback)
- Intended specification: `Docs/Specifications/Testing-CI-Release.md#catalog-and-feature-metadata`
- Stage: `verification.changed-selection` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L7-036
- Current delta: none. `ChangedStageSelector` is the single selection policy; Git porcelain parsing provides normalized old/new rename paths without shell quoting ambiguity.
- Evidence: selector/parser contract tests and FeatureRunner changed-suite integration.
- Conformance: Matches. Only executable stages declaring the `changed` profile are selected, path prefixes require a segment boundary, global build inputs select the full changed closure, and no match runs only `runner.contract`.
- Next action: Use the exact changed closure as an input to hermetic Debug/Release verification in F-L7-039.

## F-L7-039 — Hermetic clean-build verification

- Layer: L7 — Static Compiler / Bake
- Parent capability: Testing / CI / Release
- Status: Implemented
- Verification: TestVerified (isolated restore, no-incremental Debug/Release builds, no-build Debug/Release full tests, artifact counts/hashes, cleanup, stale-output regression)
- Intended specification: `Docs/Specifications/Testing-CI-Release.md#ci-matrix-and-evidence-separation`
- Stage: `verification.clean-build` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L7-038
- Current delta: none. Each run owns a unique `.artifacts/CleanBuild/work` subtree and isolated NuGet package/artifact roots; normal `bin/obj` outputs are neither cleaned nor consumed.
- Evidence: versioned clean-build JSON, restore/build/test output hashes, managed assembly count, Debug/Release full-suite results, owned-root cleanup, and the isolated-output VSIX regression fix.
- Conformance: Matches. Restore, no-incremental Debug/Release solution build, and no-build Debug/Release full tests run in one isolated graph. The first adversarial run exposed a stale normal-bin VSIX copy; the test project now selects the active `ArtifactsPath`, and the repeated full matrix passes.
- Next action: Apply the same provenance contract to governed golden updates in F-L7-040.

## F-L7-040 — Golden artifact policy

- Layer: L7 — Static Compiler / Bake
- Parent capability: Testing / CI / Release
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Testing-CI-Release.md#golden-performance-and-stress-policy`
- Stage: `verification.golden-policy` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L7-039
- Current delta: none. `Docs/Verification/Golden-Artifacts.json` is the only registry; conventional golden paths fail closed when unregistered.
- Evidence: repository and synthetic-manifest contract tests cover unmanaged discovery, exact-byte registration, payload/source/review/diff hash drift, stale review rejection, deterministic generator metadata, reviewable diff metadata, and tolerance ceiling rejection.
- Conformance: Matches. The build invokes `verify-goldens`; all governed payloads must pin source and review provenance, and numeric tolerance is compared as canonical decimal text without introducing engine-owned `double` state.
- Next action: Reuse the versioned evidence/report conventions in the F-L7-041 performance measurement framework.

## F-L7-041 — Performance measurement framework

- Layer: L7 — Static Compiler / Bake
- Parent capability: Testing / CI / Release
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Testing-CI-Release.md#golden-performance-and-stress-policy`
- Stage: `verification.performance` (`Executable`, Headless / Foundation, 7200s)
- Depends on: F-L7-040
- Current delta: none. One shared integer measurement/statistics/gate implementation owns the sample and report contract.
- Evidence: deterministic statistics, synthetic regression, missing-measurement, execution-count, report tamper, and fingerprint contract tests plus versioned Debug/Release probe reports.
- Conformance: Matches. Exactly 5+30 executions produce median, nearest-rank p95, MAD, process-wide allocation p95, workload/configuration, Git provenance, and machine fingerprint; report payload/statistics/fingerprint integrity is revalidated, absolute and baseline verdicts remain independent, and unavailable measurement fails closed.
- Next action: Make the required Windows/TFM/WARP cells explicit and executable in F-L7-042.

## F-L7-042 — Explicit CI matrix

- Layer: L7 — Static Compiler / Bake
- Parent capability: Testing / CI / Release
- Status: Implemented
- Verification: RuntimeVerified
- Intended specification: `Docs/Specifications/Testing-CI-Release.md#ci-matrix-and-evidence-separation`
- Stage: `verification.ci-matrix` (`Executable`, Headless / Foundation, 7200s)
- Depends on: F-L7-041
- Current delta: none. One manifest owns eight required windows-2022 x64 cells and the checked-in workflow is its executable projection.
- Evidence: repository contract, missing required Release cell, workflow field drift, continue-on-error rejection, and complete Visual Stage selection tests.
- Conformance: Matches. Debug/Release solution, Windows TFM, net472 VSIX, and WARP visual workloads are explicit; the build and workflow self-validate matrix agreement, WARP cannot be replaced by hardware, and external artifact publication remains outside this feature.
- Next action: Apply explicit failure classes, one infrastructure-only retry, and expiring quarantine in F-L7-043.

## F-L7-043 — Failure, retry, and quarantine policy

- Layer: L7 — Static Compiler / Bake
- Parent capability: Testing / CI / Release
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Testing-CI-Release.md#retry-quarantine-and-failure-policy`
- Stage: `verification.failure-policy` (`Executable`, Headless / Foundation, 900s)
- Depends on: F-L7-042
- Current delta: none. `FeatureStageExecutor` owns retry classification and `Docs/Verification/Quarantines.json` is the only quarantine ledger.
- Evidence: persistent infrastructure failure, one-attempt recovery, oracle non-retry, empty repository ledger, valid scoped entry, expiry, duration, owner/evidence/signature, and Stage identity tests.
- Conformance: Matches. Only declared infrastructure exits and process exceptions retry once; timeout, cancellation, oracle failure, and NotImplemented do not. Quarantines are scoped, owned, linked, limited to fourteen days, validated at build, and never mutate result status.
- Next action: Reuse the terminal failure envelope and fixed time limits for F-L7-044 stress evidence.

## F-L7-044 — Stress contract

- Layer: L7 — Static Compiler / Bake
- Parent capability: Testing / CI / Release
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Testing-CI-Release.md#golden-performance-and-stress-policy`
- Stage: `verification.stress-contract` (`Executable`, Headless / Foundation, 28800s)
- Depends on: F-L7-043
- Current delta: none. One bounded execution framework and one versioned report own deterministic stress and fault-injection evidence.
- Evidence: plan-bound validation, repeated-seed aggregate equality, first-failure termination, invalid workload contract, deadline, report tamper, and Debug/Release executable Stage probes.
- Conformance: Matches. Seed, ticks, workload/fault metadata, configuration, adapter, Git provenance, machine fingerprint, first-failure boundary, reproduction command, and rolling state/event hashes are bound into an atomically written report under the 28,800-second maximum.
- Next action: Reuse the same provenance and integrity rules for F-L7-045 package and SBOM validation after its required packaging inputs exist.

## F-L7-045 — Package install and SBOM validation

- Layer: L7 — Static Compiler / Bake
- Parent capability: Testing / CI / Release
- Status: Implemented
- Verification: RuntimeVerified
- Intended specification: `Docs/Specifications/Testing-CI-Release.md#artifact-retention-and-package-gate`
- Stage: `release.packages` (Executable, Headless / Foundation, 7200s)
- Depends on: F-L7-044
- Current delta: none. `ReleasePackageEvidence` builds one exact Windows x64 archive from the published viewer CLI, the sole packable analyzer NuGet, the prebuilt exact VSIX, its localized templates and Preview Host, and the native Assimp payload. It writes an exact file manifest, canonical SPDX 2.3 file/dependency SBOM, and signing-input closure; safely extracts into a unique isolated directory, executes one hidden WARP readback smoke run, deletes the installation on success or failure, and records zero residue.
- Evidence: nine focused Debug/Release tests cover the exact archive/SPDX/signing closure, archive mutation, missing native payload, missing ja-JP template, canonical host-tool/publish boundaries, isolated Stage test builds, successful isolated execution, oracle failure cleanup, and unowned-directory rejection. Frozen-input Debug Stage `20260901T172840773Z-full-87f7e27773a1446190e6dd29b01ecffd` and Release Stage `20260901T172932938Z-full-f538f59fd0674eecb58162a02999158d` each closed 17 restored NuGet dependencies and completed the extracted WARP readback with zero residue.
- Conformance: Matches. Validation recomputes the archive, manifest, SBOM, installed byte closure, restored NuGet package bytes, repository inputs, signing inputs, install evidence, and complete evidence-directory closure. Missing, extra, changed, escaped, reordered, non-reproducible, development-path, noncanonical, partial, or stale evidence fails closed.
- Next action: Consume the canonical Debug/Release package reports from F-L7-046 compatibility aggregation and the later F-L7-047/F-L7-048 product release closure.

## F-L7-046 — Release compatibility matrix

- Layer: L7 — Static Compiler / Bake
- Parent capability: Testing / CI / Release
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Testing-CI-Release.md#ci-matrix-and-evidence-separation`
- Stage: `release.compatibility` (Executable, Headless / Foundation, 7200s)
- Depends on: F-L7-045
- Current delta: none. `Release-Compatibility-Matrix.json` records one Windows 10 2004+ x64/.NET 10/net472/WARP product scope by reference: eight required CI cells, six required Platform cells, eight required VSIX release checks, two supplemental hardware cells, and four executable contract Stages. It stores no Passed/Pending state, evidence path, observation time, or expiry.
- Evidence: `ReleaseCompatibilityMatrixValidator` delegates each source to its existing validator, then requires exact bidirectional aggregate references and executable Catalog Stages. Four focused tests cover the repository contract, missing/duplicate/promoted hardware references, source drift, and the no-copied-status invariant. Debug Stage `20260901T173856314Z-full-1602a39885a44e6db299e37d3b812804` passed with artifact SHA-256 `10bcefd6f08e66c8c5f991cc98cdbc1fdcfd7ec9ceadfb181cce4f82eccff80c`; Release Stage `20260901T173928467Z-full-cfaf190a66164a62a8d9a17f4b0f80a1` passed with artifact SHA-256 `b0ed419887e0f0b6b8274bed89de1fb213756a4a3b0d7b072f16f7f33ee49756`.
- Conformance: Matches. CI, Platform, VSIX, manual evidence, and Feature Stage results remain independent sources of truth; F-L7-046 owns only product-scope membership.
- Next action: Consume this static scope from F-L7-047 while evaluating actual configuration-qualified Stage results.

## F-L7-047 — Release candidate gate

- Layer: L7 — Static Compiler / Bake
- Parent capability: Testing / CI / Release
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Testing-CI-Release.md#release-scope-and-level-gates`
- Stage: `release.candidate` (Executable, Headless / Foundation, 7200s)
- Depends on: F-L7-046
- Current delta: Catalog-derived product track/Level/dependency closure, recent Release/WARP/current-Git prerequisite selection, canonical SHA-bound RC report, and fail-closed revalidation are implemented. Formal RuntimeVerified evidence remains pending on F-L8-029 and the resulting complete prerequisite run.
- Conformance: Matches the implemented contract.
- Next action: Complete F-L8-029, execute `run-release-candidate --configuration Release --level 7`, and record the final Stage evidence.

## F-L7-048 — Immutable release manifest

- Layer: L7 — Static Compiler / Bake
- Parent capability: Testing / CI / Release
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Specifications/Testing-CI-Release.md#artifact-retention-and-package-gate`
- Stage: `release.manifest` (Executable, Headless / Foundation, 7200s)
- Depends on: F-L7-047
- Current delta: The formal release publisher binds one passing RC Stage/report, the validated Rendering/Content/ECS/Platform/VSIX reports, and the exact package archive/signing/package-manifest/SPDX identities into an atomic SHA-256 content-addressed directory. The FormalRelease retention class is immutable and non-expiring; repeated publication is byte-preserving and an invalid existing identity is never overwritten.
- Conformance: Matches the implemented contract.
- Next action: Complete F-L8-029, execute the full RC prerequisite closure, and publish/revalidate the final formal release.

## F-L2-005 — Shader Graph canonical schema

- Layer: L2 — Asset Schemas
- Parent capability: Shader Graph
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Specifications/Shader-Graph.md#canonical-assets
- Stage: `shadergraph.schema` (Executable, Headless / Foundation, 900s)
- Depends on: F-L7-022
- Current delta: Canonical UTF-8 JSON, stable IDs, typed ports, semantic hashing, internal/external Subgraphs, recursive dependency identity/hash, and first-class If/ForRange/Return region ownership, branches, nesting, and validation are implemented.
- Conformance: Matches
- Next action: Preserve schema-v1 compatibility while extending future node families.

## F-L7-049 — Shader Graph compiler

- Layer: L7 — Static Compiler / Bake
- Parent capability: Shader Graph
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Specifications/Shader-Graph.md#compiler-cli
- Stage: `shadergraph.compiler` (Executable, Headless / Foundation, 1800s)
- Depends on: F-L2-005
- Current delta: Typed syntax-node graphs and recursive external production Subgraphs deterministically regenerate production HLSL, while high-level If/Else, statically bounded ForRange, LoopIndex, and typed Return lower with source mapping. All nine fixed-ABI entries compile and dependency-identical artifacts are reused.
- Conformance: Matches
- Next action: Extend the typed node catalog only with explicit ABI-compatible nodes.

## F-L8-032 — ThirdPerson Visual Studio project template

- M0098: the template's floor, walls and steps (the four `MI_PrototypeGrid_Gray` meshes) now draw Unreal's 1 m grid with 20 cm sub lines in world space instead of the baked textures that rendered as scrambled triangles (ADR-0075). Observed by launching the template on WARP and comparing the spawn view with the Unreal reference capture.

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: Editor / VSIX Roadmap
- Status: Implemented
- Verification: TestVerified, RuntimeVerified (WARP); interactive VS dialog creation is a manual check
- Intended specification: `Docs/Decisions/ADR-0068-ThirdPerson-Project-Template.md`
- Stage: `vsix.project-template` (`Executable`, Visual / VSIX, 2400s)
- Depends on: F-L6-009, F-L8-031
- Implementation evidence:
  - `tools/CSharpGameEngine.ProjectTemplates/GameProjectGenerator.cs`
  - `tools/CSharpGameEngine.VSExtension/GameProjectTemplateWizard.cs`
  - `tools/CSharpGameEngine.VSExtension/ProjectTemplates/CSharp/GameEngine/ThirdPerson/`
  - the VSIX `ProjectTemplateSources/` and `Packages/` payloads
  - `tools/CSharpGameEngine.FeatureRunner/TemplatePackageVerification.cs`
  - `THIRD_PARTY_NOTICES.md`
- Current delta:
  - Only the base Third Person template.
  - No Combat, Platforming or SideScroller variants and no touch controls.
  - Creation from the dialog has not been observed in an Experimental instance yet.
- Conformance: `GameProjectGeneratorTests` and `VsixManifestTests`. The Stage generates "My ThirdPerson" in %TEMP%, builds it from the package, and plays it on WARP; see [M0092](Milestones/M0092-Sdk-Package-And-Project-Template.md).
- Next action: record the interactive creation in the Experimental instance, and consider more template variants.

## F-L8-031 — Visual Shader Graph editor

- Layer: L8 — Tools / Visual Studio
- Parent capability: Editor / VSIX
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Specifications/Shader-Graph.md#visual-studio-editor
- Stage: `vsix.shader-graph` (Executable, Headless / VSIX, 1800s)
- Depends on: F-L7-049, F-L8-021
- Current delta: Editor Factory, WPF node canvas, built-in/prototype palette, exact typed connections, If/ForRange/Return region authoring, edit commands, properties, diagnostics, save/build, Subgraph validate-only save, external-change handling, stable AutomationIds/TransformPattern, external Subgraph navigation, localized templates, dependency-bundle copying, and target wizard are implemented and experimentally verified.
- Conformance: Matches
- Next action: Preserve the fixed ABI and evidence contract while adding future node families.

## F-L9-009 — Engine Showcase and live feature progress

- Layer: L9 — Cross-Domain Orchestration
- Parent capability: Verification / visible runtime evidence
- Status: Implemented
- Verification: TestVerified
- Intended specification: `Docs/Milestones/M0064-Visible-Engine-Showcase.md`
- Stage: `showcase.visible-progress` (`Executable`, Visual / Rendering, 900s)
- Depends on: F-L7-030, F-L3-018, F-L7-022, F-L6-004, F-L4-010, F-L5-003
- Current delta: The default UserEcsProject starts a fixed-camera ECS/physics/rendering gallery with a 360 DIP localized Win32 dashboard, catalog-derived Feature/Stage progress, deterministic Input Actions, Pause/Step controls, machine-readable child title, and WARP evidence automation.
- Conformance: Matches
- Next action: Optional showcase playback may consume the canonical `.cgephyscache` output; the editor-triggered Bake path itself is complete under F-L8-014.
