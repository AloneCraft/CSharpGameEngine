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
  - UserGameProject/Components.cs
  - UserGameProject/Archetypes.cs
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/EcsArchetypeGeneratorEmissionTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/EcsArchetypeGeneratorEndToEndTests.cs
- Reverse-engineered behavior:
  - `ForAttributeWithMetadataName("CSharpGameEngine.Ecs.EcsArchetypeAttribute", ...)`でアーキタイプ
    構造体を検出し、コンポーネントプロパティを完全修飾名のOrdinal文字列でソートして正規化する。
    `UserGameProject`を実際にビルドし、`Player`の生成コード(`ComponentTypes`配列・列インデックス)が
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

## F-L9-001 — `UserGameProject`ゲームループ

- Layer: L9 — Game / Application
- Parent capability: ECS駆動ステージ
- Status: Implemented
- Verification: RuntimeVerified
- Intended specification: Docs/Decisions/ADR-0016-Ecs-Driven-Stage-And-ThirdPerson-Controller.md
- Implementation evidence:
  - UserGameProject/Program.cs
  - UserGameProject/UserGameProject.csproj
- Tests: なし(統合的なゲームループ自体は`tools/CSharpGameEngine.Extension`と同様、実機確認に
  委ねている)。
- Reverse-engineered behavior:
  - `Stopwatch`ベースのdelta time計測(スパイク対策で0.1秒にクランプ)、`World.Create<Player>()`/
    `world.Create<StaticProp>()`で作成したエンティティをローカル変数として直接操作する。
  - 実際に`UserGameProject.exe`を起動し4秒間クラッシュしないこと、スクリーンショットで
    空・地面・キャラクターが意図通り描画されることを確認済み。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L9-002 — ThirdPersonキャラクターコントローラー(`CharacterMotion`)

- Layer: L9 — Game / Application
- Parent capability: ECS駆動ステージ
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0016-Ecs-Driven-Stage-And-ThirdPerson-Controller.md
- Implementation evidence:
  - UserGameProject/PlayerController.cs
- Tests:
  - tests/UserGameProject.Tests/CharacterMotionTests.cs
- Reverse-engineered behavior:
  - `CharacterMotion.Compute`はWorld/Window/GPUに一切依存しない純粋関数。カメラ基準の
    WASD移動、重力減算、Y<=0での接地クランプ、接地時のみJump可、を行う。`LerpAngle`は
    最短経路で角度を補間する。
- Conformance: Matches
- Known deviations: 衝突判定・本格物理エンジンは無し(Y<=0の簡易接地クランプのみ)。
- Next action: なし。

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
    `dotnet run --project UserGameProject`を別プロセス・別トップレベルウィンドウとして
    起動する。VS本体への埋め込みは行わない。
- Conformance: Matches
- Known deviations: 「ゲームドッキングウィンドウ」という当初の要望に対しては、埋め込みではなく
  別ウィンドウ方式である(ADR-0016「未解決事項」参照、ユーザー確認済みの意図的な選択)。
- Next action: VSドッキングパネルへの本当の埋め込みが必要になった時点で、`SetParent`再親化
  またはDXGI共有テクスチャ方式を再検討する。

## F-L0-005 — `GltfImporter.ImportScene`(ノードグラフ/PBRマテリアル/ライトのインポート)

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

## F-L7-005 — UEレベルエクスポートパイプライン

- Layer: L7 — Static Compiler / Bake
- Parent capability: UE5.8 FirstPersonステージ移植
- Status: Implemented
- Verification: RuntimeVerified
- Intended specification: Docs/Decisions/ADR-0017-UE-Level-Export-Pipeline.md
- Implementation evidence:
  - tools/UnrealExport/export_lvl_firstperson.py
  - tools/UnrealExport/run_export.cmd
  - UserGameProject/Assets/Stage/Lvl_FirstPerson/Lvl_FirstPerson.glb
  - UserGameProject/Assets/Stage/Lvl_FirstPerson/scene_meta.json
- Tests: なし(UEエディタが無い環境では実行できない開発時専用ツールのため、C#テストスイートの
  対象外。出力物(GLB/JSON)の消費側は`GltfImporterSceneTests`/`SceneLoaderTests`で検証している)。
- Reverse-engineered behavior:
  - `UGLTFExporter::ExportToGLTF`にUWorldを渡しレベル全体を1GLBとしてエクスポートすることで、
    UE自身に座標変換・マテリアルオーバーライドのベイク・ライト変換を行わせる。Pythonが
    自前で行うのはSkyLight/Fog/PlayerStart/Mobilityタグの4つのみ。
  - `unreal.GLTFExporter.export_to_gltf`のPythonバインディングは実際には4引数までしか
    受け付けず、戻り値は`GLTFExportMessages`単体になる(ADR-0017参照、実行時エラーから逆算)。
  - 実行結果、レベルは55 StaticMeshActor+1 DirectionalLightで構成され(バイナリ文字列頻度からの
    事前概算とは内訳が異なっていた)、マテリアルの大半(21/23)は実テクスチャにベイクされ、
    残り2件はフラットカラーファクターだった。
- Conformance: Matches
- Known deviations: `ExponentialHeightFogComponent`の色プロパティの厳密な特定は未実施
  (固定のニュートラルグレーで代替、ADR-0017参照)。
- Next action: より大規模なUEレベルを移植する際、World Partitionのセル強制ロード
  (`WorldPartitionEditorSubsystem`)が必要になる可能性がある。

## F-L9-003 — `SceneLoader`とステージスポーン

- Layer: L9 — Game / Application
- Parent capability: UE5.8 FirstPersonステージ移植
- Status: Implemented
- Verification: TestVerified、RuntimeVerified
- Intended specification: Docs/Decisions/ADR-0017-UE-Level-Export-Pipeline.md、
  Docs/Decisions/ADR-0018-Textured-Stage-And-Ecs-Systems.md、
  Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.md(PlayerStart軸変換)
- Implementation evidence:
  - UserGameProject/SceneDocument.cs
  - UserGameProject/SceneLoader.cs
  - UserGameProject/Program.cs
- Tests:
  - tests/UserGameProject.Tests/SceneDocumentTests.cs
  - tests/UserGameProject.Tests/SceneLoaderTests.cs
- Reverse-engineered behavior:
  - `GltfImporter.ImportScene`の結果と`scene_meta.json`を合成し、`StageRenderer`入力
    (`StageMeshCreateInfo[]`/`StageMaterialCreateInfo[]`/`StageObjectCreateInfo[]`)と
    ECSスポーン情報(位置/回転/スケール/mobility)を1回の`Load`呼び出しで返す。
  - Mobilityは`scene_meta.json`の`mobilityOverrides`をノード名(アクタラベル)で引き、
    見つからない場合は既定で`"static"`になる。
  - `Program.cs`はロード結果を元に`StageRenderer.Create`→mobility別に`StageProp`/
    `MovableStageProp`をスポーン→`SunLight`/`AmbientLight`/`FogVolume`をスポーン→
    `World.CollectViews`で1回だけビューリストを収集し、以後は毎フレームそのリストを
    再利用する(`CollectViews`を毎フレーム呼び直さない)。
  - `dotnet run --project UserGameProject`を実際に起動し、プロセスが`Responding=True`のまま
    10秒以上生存しCPU時間が蓄積すること(=シーンロード・レンダラー初期化・メインループが
    クラッシュせず実際に動作していること)を確認した。
  - (ADR-0027追記)`TryImportDirectionalLight`の独自`-Vector3.UnitZ`変換を削除し
    `ImportedLightDirection.WorldForward`(`+Vector3.UnitZ`、`WorldTransform`が既にエンジン空間の
    ため)へ統一した。`scene_meta.json`の`PlayerStart`(`export_lvl_firstperson.py`が既に
    UE→glTF変換を適用した値であることをスクリプトのコメントで確認済み)にも
    `GltfAxisConversion.ConvertWorldTransform`と同じ経路を適用し、位置Z成分と純Y軸回転である
    Yaw角の両方を変換する(Yaw角は共役変換の結果、符号が反転することを導出・実測確認した)。
    `dotnet run --project UserGameProject`を再実行し15秒以上クラッシュしないこと、
    スクリーンショット(BitBlt直接キャプチャ)で空・床・チェッカーグリッド壁が破綻なく
    描画されることを確認した(変更前との比較用スクリーンショットは無いため、鏡像になっていない
    か等の完全な視覚的確証はユーザー自身の実機確認が必要)。
  - (ADR-0029追記)`Program.cs`のスポーンコードを`World.Create<T>()`ファサード代入から
    `world.CreateEntity(archetype)` + `world.GetRef<T>(entity) = value`(F-L3-007)へ書き換えた。
    `StageProp`/`MovableStageProp`という宣言済みアーキタイプ型は無くなり、`EntityArchetype`を
    `world.CreateArchetype<...>()`で都度組み立てる。`SunLight`/`AmbientLight`/`FogVolume`という
    シングルトン用のアーキタイプ型宣言も同様に無くなった。挙動は等価(実機描画確認は
    F-L9-004のADR-0029追記を参照)。
- Conformance: Matches
- Known deviations: UserGameProjectの見た目が変更前と完全に一致するかは未確認(上記参照)。
- Next action: ユーザーによる実機での見た目確認。

## F-L9-004 — ECS入力抽象化とゲームプレイSystems群

- Layer: L9 — Game / Application
- Parent capability: UE5.8 FirstPersonステージ移植
- Status: Implemented
- Verification: TestVerified、RuntimeVerified
- Intended specification: Docs/Decisions/ADR-0018-Textured-Stage-And-Ecs-Systems.md、
  Docs/Decisions/ADR-0028-Ecs-Gameplay-Ruf-Boundary.md(RUF境界配置)
- Implementation evidence:
  - UserGameProject/Components.cs(`InputState`/`CameraRig`/`VerticalMotion`/`SceneSpawnRequest`)
  - UserGameProject/Systems/Systems.cs(ADR-0033で5ファイルから1ファイルへ集約。記述順=実行順。
    起動時のスポーンを担う`SceneSpawnSystem`と、Archetype形状の宣言専用の`PlayerSpawn`/
    `MovableStagePropSpawn`もここに含まれる)
  - UserGameProject/Systems/CameraSystem.cs
  - UserGameProject/Systems/MovingPropSystem.cs
  - UserGameProject/Systems/RenderSyncSystem.cs
- Tests:
  - tests/UserGameProject.Tests/PlayerControlSystemTests.cs
- Reverse-engineered behavior:
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
    `dotnet run --project UserGameProject`を実行し、W前進キー(`keybd_event`で模擬入力)で
    プレイヤーが移動した後のスクリーンショットで空・タイル張りの床・PBRシェーディングされた
    影付きプロップが破綻なく描画されることを実機確認した(検証手順の詳細はM0009参照。
    マウスルック自体は別途発見した`Win32Window`の未修正の既知の問題によりこの確認では
    無効化して検証、詳細はM0009「未解決事項」参照)。
- Conformance: Matches
- Known deviations: 一人称でのマウス/キーボード操作感そのものは自動検証していない
  (ADR-0016から引き継いだ制約)。
- Next action: なし。

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
  Docs/EditorSpec.md §3(データモデル)
- Removal rationale: Scene Authoringが[[ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth]]/
  [[ADR-0031-Scene-Definition-3D-Preview]]でC#正本方式(`[SceneDefinition]`+`GameSystemGenerator`)へ
  移った後、このJSON方式実装には**生きた利用者が1つも残らなかった**(参照は自分自身のテストのみ)。
  [[ADR-0034-Project-Consolidation]]で`src/CSharpGameEngine.Scene`と
  `tests/CSharpGameEngine.Scene.Tests`をディレクトリごと削除した。
- Superseded by: F-L3-xxx系のScene Authoring(C#正本)。VSIXが`*.scene`を読むパーサーは
  `tools/CSharpGameEngine.VSExtension/SceneFile.cs`として**VSIX内に存続**しており(F-L8-003)、
  エディタ機能そのものは失われていない。
- Implementation evidence(削除済み。履歴はgitの`f17fbd5`以前を参照):
  - ~~src/CSharpGameEngine.Scene/{Transform,SceneNode,SceneGraph,SceneNodeTree,SceneJson}.cs~~
  - UserGameProject/DefaultScene.scene(存続、VSIXが読む)
  - UserGameProject/Assets/DefaultScene/Box.glb(存続)
- Tests: 8件のうち7件は削除対象そのもののテストなのでカバレッジ損失なし。残る1件
  (出荷される`UserGameProject/DefaultScene.scene`を実際に読む唯一のテスト)は削除に先立って移植した:
  - tests/CSharpGameEngine.VSExtension.Tests/DefaultSceneFileTests.cs
- Reverse-engineered behavior(削除時点の記録として保持):
  - EditorSpec §3のJSONスキーマ(`schemaVersion`/`nodes[].{id,name,parentId,transform,
    meshAssetPath,components}`)と完全一致する往復シリアライズを、`StableId`/`Vector3`/
    `Quaternion`用の`JsonConverter`を書かず素の文字列/`float[]`との明示的な相互変換で実装。
  - 型名は仕様書の`Scene`ではなく`SceneGraph`(名前空間`CSharpGameEngine.Scene`の末尾
    セグメントと同名になることを避けるため)。
  - `SceneNodeTree.Build`は「到達済みノード数 != 全ノード数」を循環参照/解決不能参照の検出条件に
    使う - 各ノードの親は高々1つなので、真の循環を構成するノード群はルートとして拾われず
    構造的に到達不能になるという性質を利用している。
  - `UserGameProject/DefaultScene.scene.json`はRoot+`Cube_A`/`Cube_B`(いずれも
    `Assets/DefaultScene/Box.glb`を参照、X軸上で左右に配置)の最小3ノードシーン。
    `assets/TestFixtures/Gltf/Box.glb`のコピーを新規アセットとして追加。
- Conformance: N/A(削除済み)
- Known deviations: `UserGameProject.csproj`への`CSharpGameEngine.Scene`への`ProjectReference`は
  ついに追加されなかった。ランタイム統合はEditorSpec Phase 4のスコープだったが、その前に
  Scene AuthoringがC#正本方式へ移ったため、このJSON実装は一度も本番経路に載らずに終わった。
- Next action: 無し。EditorSpec Phase 2/Phase 4のランタイム統合は、C#正本方式
  (`[SceneDefinition]` / `CreateRuntime(ref World)`)の上で設計する。

## F-L8-003 — シーンダブルクリックプレビューとヒエラルキー選択連動

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: シーンダブルクリックプレビューとヒエラルキー選択連動
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0020-Scene-Preview-And-Hierarchy-Selection-Sync.md
- Implementation evidence:
  - tools/CSharpGameEngine.VSExtension/SceneFile.cs
  - tools/CSharpGameEngine.VSExtension/SceneFileEditorFactory.cs
  - tools/CSharpGameEngine.VSExtension/SceneWindowPane.cs
  - tools/CSharpGameEngine.VSExtension/SceneWindowControl.xaml(.cs)
  - tools/CSharpGameEngine.VSExtension/SceneHwndHost.cs
  - tools/CSharpGameEngine.VSExtension/SceneRenderer.cs
  - tools/CSharpGameEngine.VSExtension/SceneEditorHub.cs
  - tools/CSharpGameEngine.VSExtension/HierarchyToolWindowPane.cs
  - tools/CSharpGameEngine.VSExtension/HierarchyToolWindowControl.xaml(.cs)
  - tools/CSharpGameEngine.VSExtension/ShowHierarchyToolWindowCommand.cs
  - tools/CSharpGameEngine.VSExtension/CSharpGameEngineViewerPackage.cs
  - tools/CSharpGameEngine.VSExtension/OpenViewerCommandPackage.vsct
- Tests:
  - tests/CSharpGameEngine.VSExtension.Tests/SceneFileTests.cs
- Reverse-engineered behavior:
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
- Conformance: Matches(ユーザーの実機確認により、`.scene`ダブルクリック起動・ヒエラルキー
  ツリー表示・選択ハイライト(オレンジ系タイント)がすべて実際に動作することを確認済み)。
- Known deviations: **ADR-0021で判明・修正済み**: `.scene.json`という複合拡張子は
  `ProvideEditorExtension`で単一の拡張子として認識されず、ダブルクリックしても何も開かない
  不具合だった(実機検証で確認)。拡張子を単一の`.scene`に変更して解決した(スキーマ自体は
  無変更)。
- Next action: VS実機でシーンタブの体感速度が改善したかユーザーに確認してもらう(F-L8-002の
  Next actionと同じ、ADR-0010追記参照)。

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
    ウィンドウ初回生成を含む)を`TryAutoOpenUserGameProjectAsync`からインライン`await`で
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
- Status: Implemented
- Verification: RuntimeVerified(ユーザーの実機F5デバッグで、球形/シリンダー/ディスク等を実際に
  クリックしてシーンに追加描画され、ヒエラルキーにもノードが増え、選択ハイライトも機能することを
  スクリーンショットで確認済み)
- Intended specification: Docs/Decisions/ADR-0021-Editor-Layout-And-Shape-Palette.md
- Implementation evidence:
  - tools/CSharpGameEngine.Primitive.Cli/Program.cs
  - tools/CSharpGameEngine.VSExtension/ShapePaletteToolWindowPane.cs
  - tools/CSharpGameEngine.VSExtension/ShapePaletteControl.xaml(.cs)
  - tools/CSharpGameEngine.VSExtension/ShowShapePaletteToolWindowCommand.cs
  - tools/CSharpGameEngine.VSExtension/PrimitiveSpawner.cs
  - tools/CSharpGameEngine.VSExtension/SceneNodeAdder.cs(ADR-0022で`PrimitiveSpawner`から抽出)
  - tools/CSharpGameEngine.VSExtension/ExternalBridgeProcess.cs(`RunProducingFile`メソッド)
  - tools/CSharpGameEngine.VSExtension/SceneFile.cs(`Save`/`AddNode`/`FindFirstRootNodeId`/
    `ContainsNodeNamed`メソッド)
  - tools/CSharpGameEngine.VSExtension/SceneEditorHub.cs(`ReloadRequested`イベント)
- Tests: なし(VSIX側のクリックハンドラ自体は専用テストが無い - `SceneFile`の`Save`往復は
  F-L2-001/F-L8-003のテストでカバー)。
- Reverse-engineered behavior:
  - 8ボタン(ボックス・球形・シリンダー・カプセル・コーン・トーラス・矩形・ディスク)のみ
    `Click`ハンドラを持ち、残り10ボタンは`IsEnabled="False"`のプレースホルダー
    (対話的ギズモ操作が前提でEditorSpec §6のスコープ外)。
  - `PrimitiveSpawner.SpawnAsync`: `UserGameProject/Assets/Primitives/<Name>.glb`が無ければ
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
- Conformance: Matches(ユーザーの実機確認により、シェイプクリック→シーンへの追加描画→
  ヒエラルキーへのノード追加→選択ハイライトの一連の流れが実際に動作することを確認済み)。
- Known deviations: None(実機確認済み)。
- Next action: なし。

## F-L8-007 — ヒエラルキーへのドラッグ&ドロップ配置(`SceneNodeAdder`)

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: ヒエラルキーへのドラッグ&ドロップ配置(EditorSpec Phase 2前半)
- Status: Implemented
- Verification: TestVerified(ビルドのみ。ソリューションエクスプローラーからの実ドラッグ操作は
  実機未確認)
- Intended specification: Docs/Decisions/ADR-0022-Hierarchy-Drag-Drop-Asset-Placement.md
- Implementation evidence:
  - tools/CSharpGameEngine.VSExtension/SceneNodeAdder.cs
  - tools/CSharpGameEngine.VSExtension/HierarchyToolWindowControl.xaml(.cs)
  - tools/CSharpGameEngine.VSExtension/PrimitiveSpawner.cs(`SceneNodeAdder`利用に更新)
- Tests: なし(WPFドラッグ&ドロップイベントハンドラ自体は専用テストが無い - ノード追加の
  中核ロジックは`SceneNodeAdder`経由でF-L8-003/F-L8-006のテストと同じ`SceneFile`往復テストで
  間接的にカバーされる)。
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
- Conformance: Matches(ビルド・型チェックのみ、実際のドラッグ&ドロップ操作は未確認)。
- Known deviations: ソリューションエクスプローラーからの実ドラッグで`DataFormats.FileDrop`が
  実際に得られるかは実機未確認。得られない場合はVS固有形式(`CF_VSSTGPROJECTITEMS`)への
  切り替えが必要になる可能性がある。
- Next action: VS実験用インスタンスで実際にファイルをドラッグしてヒエラルキーにドロップし、
  ノードが追加されることを実機確認する。

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
    決定性・repoRoot/ブリッジ違いでの別名)
- Reverse-engineered behavior:
  - 1リクエスト=1接続(パイプの多重化なし)。`BridgeServerHost`は接続ごとに独立した`Task`へ
    ディスパッチし、`SceneHwndHost`の並列メッシュロードにそのまま対応できる。
  - `ResidentBridgeClient`は`--serve`常駐サーバーへの接続を最初に試み、失敗時のみ`Mutex`
    (`Global\...`)経由で新規起動する。パイプ名は`ComputePipeName`が`repoRoot`+ブリッジ
    フォルダ名からSHA256で決定的に生成する(`GetHashCode()`はプロセスごとにランダム化される
    ため使用不可)。
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
  - 消費側: samples/AosSoaGeneratedExpansion/(独立、参照なし)、UserGameProject.csproj
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
- Next action: なし(`UserGameProject`実利用はM0006で追加済み)。

## F-L9-001(重複ID、旧台帳の既知の不整合) — Particle Entityベンチマーク(UserGameProject、削除済み)

- Layer: L9 — Game / Application
- Parent capability: Generated Entity Storage
- Status: Removed
- Verification: TestVerified、RuntimeVerified(Releaseビルドで実測)
- Intended specification: Docs/Decisions/ADR-0024-Generated-Entity-AoS-SoA.md、
  Docs/Milestones/M0006-Generated-Entity-Benchmark.md
- Implementation evidence:
  - UserGameProject/Entities/Particle.cs(ユーザーが書く唯一の宣言、両フラグ指定)
  - UserGameProject/Entities/ParticleComponents.cs、ParticleMotion.cs、ParticleSystems.cs
  - UserGameProject/Entities/BenchmarkHarness.cs、ParticleStorageBenchmark.cs
  - UserGameProject/Program.cs(`benchmark`サブコマンド分岐)
- Tests:
  - tests/UserGameProject.Tests/ParticleStorageTests.cs
  - tests/UserGameProject.Tests/BenchmarkHarnessTests.cs
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
  (`UserGameProject`ゲームループ、L327行)とID重複した既存の台帳不整合であり、ID自体は
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

## F-L3-008 — 構造変更(Component追加・削除、Archetype間Entity移動)

- Layer: L3 — ECS / Job Runtime
- Parent capability: SoA一本化ECSへの全面移行(`[GameSystem]`/`[Update]`)
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0029-Dynamic-Archetype-Soa-Ecs.md、
  Docs/Specifications/Ecs-Soa-Systems.md
- Implementation evidence:
  - src/CSharpGameEngine.Entities/World.cs(`AddComponentDefault`/`AddComponentValue`/
    `RemoveComponent`/`GetArchetypeWithAdded`/`GetArchetypeWithRemoved`/`MoveEntity`/
    `CopySharedColumns`、すべて`internal`)
  - src/CSharpGameEngine.Entities/Archetype.cs(`TryGetAddEdge`/`SetAddEdge`/
    `TryGetRemoveEdge`/`SetRemoveEdge`)
- Tests:
  - tests/CSharpGameEngine.Entities.Tests/StructuralChangeSpec.cs
- Reverse-engineered behavior:
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
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L3-009 — `EntityQuery` / `QueryMatches`(複数Archetype横断・差分スキャン)

- Layer: L3 — ECS / Job Runtime
- Parent capability: SoA一本化ECSへの全面移行(`[GameSystem]`/`[Update]`)
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0029-Dynamic-Archetype-Soa-Ecs.md、
  Docs/Specifications/Ecs-Soa-Systems.md
- Implementation evidence:
  - src/CSharpGameEngine.Entities/EntityQuery.cs
  - src/CSharpGameEngine.Entities/QueryMatches.cs
- Tests:
  - tests/CSharpGameEngine.Entities.Tests/EntityQuerySpec.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorEndToEndTests.cs
    (`Move_ProcessesEntitiesAcrossMultipleArchetypes_UsingSoaSpans`ほか、生成されたExecutor経由の
    実行時検証)
- Reverse-engineered behavior:
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
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L3-010 — `EntityCommandBuffer`(構造変更の遅延適用)

- Layer: L3 — ECS / Job Runtime
- Parent capability: SoA一本化ECSへの全面移行(`[GameSystem]`/`[Update]`)
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0029-Dynamic-Archetype-Soa-Ecs.md、
  Docs/Specifications/Ecs-Soa-Systems.md
- Implementation evidence:
  - src/CSharpGameEngine.Entities/EntityCommandBuffer.cs
  - src/CSharpGameEngine.Entities/ComponentValueStore.cs
  - src/CSharpGameEngine.Entities/Entity.cs(`Temporary`/`IsTemporary`/`TemporaryIndex`)
- Tests:
  - tests/CSharpGameEngine.Entities.Tests/EntityCommandBufferSpec.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorEndToEndTests.cs
    (`Commands_StructuralChangesApplyOnlyAfterPlaybackAtEndOfGeneratedLoop`)
- Reverse-engineered behavior:
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
- Conformance: Matches
- Known deviations: None
- Next action: なし。

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
    連番だったが、UserGameProject移行で3つのContext型(`StageRenderer`/`Win32Window`/
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

## F-L3-011 — Scene Authoring基盤型(`Parent`/`SceneNodeTag`/`SceneNodeSymbol`系)

- Layer: L3 — ECS / Job Runtime
- Parent capability: Scene Authoring(C#コードをSceneの正本にする、Phase A)
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth.md
- Implementation evidence:
  - src/CSharpGameEngine.Entities/SceneDefinitionAttribute.cs
  - src/CSharpGameEngine.Entities/SceneNodeId.cs
  - src/CSharpGameEngine.Entities/SceneNodeSymbol.cs
  - src/CSharpGameEngine.Entities/SceneBuilder.cs
  - src/CSharpGameEngine.Entities/SceneNodeBuilder.cs
  - src/CSharpGameEngine.Entities/Parent.cs
  - src/CSharpGameEngine.Entities/SceneNodeTag.cs
- Tests:
  - tests/CSharpGameEngine.Entities.Tests/SceneAuthoringSpec.cs
- Reverse-engineered behavior:
  - `Parent`はUnity DOTSの`Unity.Transforms.Parent`と同じ形の汎用Component(`Entity Value`のみ)。
    このECS自体はHierarchy概念を持たないため、親子関係は`SceneDefinitionGenerator`が`Define`の
    入れ子構造から自動導出してのみ付与する(ユーザーが直接`Set`することは想定しない)。
  - `SceneNodeTag`は0フィールドのタグComponentで、`World.CreateArchetype`に0型引数版が無いため、
    `Set`呼び出しを1つも持たないノード(見本の`Root`自身がこのケース)を成立させるためだけに存在する。
  - `SceneBuilder`/`SceneNodeBuilder`の`Create`/`Set`メソッド本体はほぼ空実装(`Set`は何もしない)。
    `SceneDefinitionGenerator`は`Define`メソッドの構文木を静的解析するだけで、これらのメソッド本体は
    通常のRuntime Buildでは一切実行されない(将来のEditor Preview用途向けの余地として存在するのみ)。
  - `SceneNodeId`は`ulong`1個のラッパーで、`SceneDefinitionAnalyzer`がノードを表すpartial
    プロパティの完全修飾シンボル名からFNV-1a 64bitハッシュとして決定論的に導出する。Rename後は
    再計算されるだけで値が変わること自体は無害という設計(Phase Aの判断、ADR-0030参照)。
- Conformance: Matches
- Known deviations: None
- Next action: Phase B(VSIX側Roslyn WorkspaceによるHierarchy Docking Window編集)でSceneNodeIdの
  Rename間安定化が必要になった場合、別途hidden annotationまたは`.scene.meta`方式を検討する。

## F-L7-012 — `SceneDefinitionGenerator`(Analyzer/Model/SceneNodeSymbolEmitter/SceneRuntimeEmitter)

- Layer: L7 — Static Compiler / Bake
- Parent capability: Scene Authoring(C#コードをSceneの正本にする、Phase A)
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/SceneDefinitionGenerator.cs
  - src/CSharpGameEngine.CodeAnalysis/SceneDefinitionAnalyzer.cs
  - src/CSharpGameEngine.CodeAnalysis/SceneDefinitionModel.cs
  - src/CSharpGameEngine.CodeAnalysis/SceneNodeSymbolEmitter.cs
  - src/CSharpGameEngine.CodeAnalysis/SceneRuntimeEmitter.cs
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDefinitionGeneratorTestHelper.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDefinitionGeneratorEmissionTests.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDefinitionGeneratorEndToEndTests.cs
- Reverse-engineered behavior:
  - `[SceneDefinition]`クラスの`Define`メソッドは実行しない。構文木を歩いて`Create`(親子関係)/
    `Set`(Component)呼び出しのネストを`SceneNodeModel`木へ変換し、そこから直接
    `World.CreateArchetype`/`CreateEntity`/`GetRef`呼び出しコードを生成する(Reflection・
    動的解釈は一切行わない)。
  - `Create`/`Set`呼び出しの「レシーバー名」は意味解析ではなく、現在のスコープ名(1つ外側の
    Createラムダの引数名、最上位ではDefineのSceneBuilderパラメータ名)とのテキスト一致だけで
    検証する。これにより、あるノードのラムダ本体からクロージャ経由で外側ノードのBuilderを
    誤って参照する(構文上は子に見えて実際は別ノードの子になる)ケースも正しく検出する。
  - 非Rootノードには`Parent`を、`Set`呼び出しが0個のノードには`SceneNodeTag`を、それぞれ
    ユーザーコードに書かせず自動的にArchetypeの型引数へ追加する。`Set`の引数式
    (`new Camera { FieldOfView = 60f }`等)は構文木からテキストとしてそのまま生成コードへ
    貼り付け、元ファイルのusingディレクティブも生成ファイル先頭へ複製することで、
    `Vector3.One`のような非完全修飾参照も解決できるようにしている(意味解析による完全修飾化は
    行わない、Phase Aの簡略化方針)。
  - `.Collect()`は使わない(生成物はクラスごとに独立しており、`GameSystemGenerator`の
    `GeneratedGameLoop`のようなsolution横断の結合出力が不要なため)。
  - 実際に`samples/SceneAuthoringDemo`で`dotnet run`を実行し、生成された`CreateRuntime`が
    実`World`上にRoot(SceneNodeTagのみ)→MainCamera/Player(いずれも`Parent`付き)という
    Entity階層と、Define本体に書いたとおりのComponent値を構築することを確認した。
- Conformance: Matches
- Known deviations: 見本(シーン構築見本.cs)の単一`LocalTransform`ではなく、ユーザー確定により
  既存の`Position`/`Rotation`/`Scale`の3コンポーネント構成を採用している(意図的な差分、
  ADR-0030参照)。
- Next action: Phase B(VSIX Hierarchy Docking WindowによるAdd/Delete/Rename/Reparent、
  Roslyn Workspace編集)は別タスク。

## F-L7-013 — `SceneDefinitionGenerator`診断(SCENE001〜009)

- Layer: L7 — Static Compiler / Bake
- Parent capability: Scene Authoring(C#コードをSceneの正本にする、Phase A)
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/SceneDefinitionDiagnostics.cs
  - src/CSharpGameEngine.CodeAnalysis/AnalyzerReleases.Unshipped.md
- Tests:
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDefinitionGeneratorDiagnosticsTests.cs
    (SCENE001〜009すべてに1件ずつのテストケース+正常系1件)
- Reverse-engineered behavior:
  - `SCENE001`〜`009`(カテゴリ`CSharpGameEngine.Entities.CodeGen`、すべて`Error`)。
    見本(シーン構築見本.cs)が仕様コメントとして提示したSCENE001〜006・008・009にほぼ対応するが、
    見本のSCENE007「循環検出」は、`Create`呼び出しが構文的なネストでしか親子関係を表現できない
    本方式では真の循環がSCENE005(同一ノードの複数回生成)に構造的に包含されるため独立の診断
    としては実装せず、見本のSCENE010「削除時の被参照チェック」はIDE操作時のガードでPhase B以降の
    対象のため実装していない。Phase A固有の`SCENE009`(8型引数上限超過)は見本には無い追加診断。
  - 診断が1件でもあるクラスは、`SceneNodeSymbolEmitter`/`SceneRuntimeEmitter`がそのクラス分の
    生成を完全にスキップする(`GameSystemGenerator`と同じ診断ゲート規約)。
- Conformance: Matches
- Known deviations: 見本の10診断からSCENE007/010を除外し、Phase A固有のSCENE009を追加
  (ADR-0030に理由を記録済み)。
- Next action: なし(Phase Aスコープでは完結)。

## F-L9-006 — `samples/SceneAuthoringDemo`

- Layer: L9 — Game / Application
- Parent capability: Scene Authoring(C#コードをSceneの正本にする、Phase A)
- Status: Implemented
- Verification: RuntimeVerified
- Intended specification: Docs/Decisions/ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth.md
- Implementation evidence:
  - samples/SceneAuthoringDemo/SceneAuthoringDemo.csproj
  - samples/SceneAuthoringDemo/Components.cs
  - samples/SceneAuthoringDemo/MainScene.cs
  - samples/SceneAuthoringDemo/DemoScenario.cs
  - samples/SceneAuthoringDemo/Program.cs
- Tests: なし専用(`DemoScenario.Run()`自身が"OK"/"FAIL:<理由>"という決定的な文字列を返す
  自己検証シナリオで、`dotnet run`で実行して確認する。`samples/SoaSystemsDemo`と同じ「サンプルは
  dotnet runで動かす、xUnitプロジェクトは持たない」規約に合わせた)。
- Reverse-engineered behavior:
  - ユーザーが貼り付けた見本(シーン構築見本.cs)のRoot/MainCamera/Player構成を、
    `LocalTransform`ではなく`Position`/`Rotation`/`Scale`の3分割`Set`呼び出しへ書き換えた上で
    実際の`SceneDefinitionGenerator`で動くコードとして再現している。
  - `DemoScenario.Run()`は`MainScene.CreateRuntime(world)`を実行し、Rootが`SceneNodeTag`のみを
    持ち`Parent`を持たないこと、MainCamera/Playerがそれぞれ`Parent.Value == Root`を持つこと、
    Define本体に書いた各Component値が実際にWorldへ反映されていることを、`EntityQuery`で
    該当Entityを検索した上で検証する(`CreateRuntime`はRootのEntityしか返さないため)。
    `dotnet run --project samples/SceneAuthoringDemo`で実行し"OK"を確認済み。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L7-014 — `CSharpGameEngine.SceneAuthoring.Cli`(実行ベースの汎用Worldブリッジ)

- Layer: L7 — Static Compiler / Bake
- Parent capability: `[SceneDefinition]`クラスのコンテキストメニュー3Dプレビュー
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0031-Scene-Definition-3D-Preview.md
- Implementation evidence:
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
- Conformance: Matches
- Known deviations: None
- Next action: Collectible `AssemblyLoadContext.Unload()`の常駐ブリッジサーバー環境での繰り返し
  安定性の専用ストレステストは未実施(ADR-0031の残課題)。

## F-L8-009 — Scene Definition 3Dプレビュー(コンテキストメニューコマンド、ギズモフォールバック)

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: `[SceneDefinition]`クラスのコンテキストメニュー3Dプレビュー
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0031-Scene-Definition-3D-Preview.md
- Implementation evidence:
  - tools/CSharpGameEngine.VSExtension/PreviewSceneDefinitionCommand.cs
  - tools/CSharpGameEngine.VSExtension/SceneDefinitionTextDetector.cs
  - tools/CSharpGameEngine.VSExtension/ProjectOutputAssemblyResolver.cs
  - tools/CSharpGameEngine.VSExtension/ExternalScenePreviewLoader.cs
  - tools/CSharpGameEngine.VSExtension/ScenePreviewSceneFileAdapter.cs
  - tools/CSharpGameEngine.VSExtension/GizmoMeshFactory.cs
  - tools/CSharpGameEngine.VSExtension/ScenePreviewRequest.cs、ScenePreviewBridgeDocument.cs
  - tools/CSharpGameEngine.VSExtension/SceneHwndHost.cs(ギズモフォールバック拡張、F-L8-003参照)
  - tools/CSharpGameEngine.VSExtension/OpenViewerCommandPackage.vsct
  - tools/CSharpGameEngine.VSExtension/CSharpGameEngineViewerPackage.cs
- Tests:
  - tests/CSharpGameEngine.VSExtension.Tests/GizmoMeshFactoryTests.cs
  - tests/CSharpGameEngine.VSExtension.Tests/ScenePreviewSceneFileAdapterTests.cs
  - tests/CSharpGameEngine.VSExtension.Tests/ProjectOutputAssemblyResolverTests.cs
  - tests/CSharpGameEngine.VSExtension.Tests/ExternalScenePreviewLoaderTests.cs
    (実ブリッジサブプロセスを起動する、VS非依存の統合テスト)
- Reverse-engineered behavior:
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
    プロパティから実ビルド出力を解決する(TFM/RID非依存)。`UserGameProject`のような
    `net10.0-windows10.0.19041.0`+`win-x64`という非標準TFMでも、フォルダ名を決め打ちする
    `BridgeExecutableLocator`方式とは異なりそのまま解決できることを、実際に
    `UserGameProject/Scene1.cs`向けのブリッジ実行(F-L9-007参照)で確認した。
  - `GizmoMeshFactory`は`CSharpGameEngine.Geometry.ProceduralMeshFactory.CreateBox`を経由せず、
    同じ頂点/法線構成をプレーンな`System.Numerics.Vector3`で独立に再実装している
    (`ProceduralMeshFactory`はRUF空間抽象化を経由し、net10.0専用でVSIXから参照できないため)。
  - `.vsct`のビルドは`IDM_VS_CTXT_CODEWIN`/`DynamicVisibility`とも初回で正常にコンパイルされた。
- Conformance: Matches
- Known deviations: None
- Next action: 実機VS Experimental Instanceでのコンテキストメニュー表示/非表示切替・3D描画の
  目視確認・既存`.scene`プレビューへの無影響確認(ユーザー側、F-L8-006〜008と同じ役割分担)。

## F-L9-007 — `UserGameProject/Scene1.cs`

- Layer: L9 — Game / Application
- Parent capability: `[SceneDefinition]`クラスのコンテキストメニュー3Dプレビュー
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0031-Scene-Definition-3D-Preview.md
- Implementation evidence:
  - UserGameProject/Scene1.cs
- Tests: なし専用(ブリッジCLIを`dotnet run`で直接実行し応答JSONを確認する運用、F-L7-014の
  検証と同じ位置づけ)。実機VSでのコンテキストメニュー起動確認はユーザー側。
- Reverse-engineered behavior:
  - `Root`→`MainCamera`(Position/Rotation/Scale/CameraRig)/`Player`(Position/Rotation/Scale/
    PlayerTag/Health)/`Lighting`(Positionのみ、Sun/Ambientのグルーピングアンカー)、
    `Lighting`→`Sun`(Position/DirectionalLightData)/`Ambient`(Position/AmbientLightData)という
    3階層6ノード構成。`UserGameProject/Components.cs`の既存型のみを使い、新規Component型は
    追加していない。`Camera`(D3D12)ではなく`CameraRig`を採用したのは、`Camera`のフィールド
    (ピッチクランプ範囲・軌道半径等)が実行時に更新され続けるライブ状態向けであり、静的な
    初期値としての意味を持たないため。
  - `Program.cs`の実際のゲームループには一切組み込まれていない(`samples/SceneAuthoringDemo/
    MainScene.cs`と同じ、単独のプレビュー確認用フィクスチャという位置づけ)。
  - `dotnet build UserGameProject`で生成される`Generated/.../Scene1.SceneNodes.g.cs`/
    `Scene1.SceneRuntime.g.cs`を目視確認し、6ノード全ての階層・Component型引数・GetRef代入が
    Define本体のとおりであることを確認した。ブリッジCLIを実際のビルド済み(TFM/RID付き)dllに
    対して実行し、応答JSONが同じ6ノードの階層・値と完全一致することも確認した(F-L7-014参照)。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

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
  - `World`/`State`は`partial struct`で、`Runtime.cs`が持つのは`EntityArchetypeIndices`だけ。
    Archetypeごとのリスト・`Create`/`MoveArchetype`/`Detach`/`PlaybackMoveArchetypes`は
    GameSystemGeneratorが同じ`partial struct World`へ生成する(ADR-0033決定事項1)。
  - このプロジェクトは`<ProjectReference>`されない**ソース正本**。partial型がアセンブリ境界を
    またげないため、消費側は`<Compile Include Link>`で取り込む。単体`dotnet build`は
    「System 0件でも空の`Archetype` enumと空の`partial struct World`が生成されコンパイルできる」
    ことのスモークテストとして機能する。
  - M0012の`src/CSharpGameEngine.StaticArchetype/Runtime.cs`を、グローバル名前空間から
    `CSharpGameEngine.Entities`名前空間へ移して統合したもの(旧プロジェクトは削除済み)。
  - `Bootstrap`(M0015)は生成されたエントリポイントが作る唯一のシードEntityのComponent。
    `Parent`/`SceneNodeTag`と同じ「エンジンが持つComponent」で、`Done`フラグを持つのは閉集合方式に
    `Destroy`が無くArchetypeループが毎フレーム回り続けるため(ADR-0035決定事項3)。
  - `SystemOrderAttribute`(M0016)は「属性を一切使わない構造的推論」(ADR-0033決定事項2)からの
    唯一の例外。`ISystem`実装struct自身に直接(そのSystem1つだけの順序)、または`file`スコープの
    マーカークラスに(ファイル既定値)、どちらにも付けられる(ADR-0036)。
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L7-015 — `GameSystemGenerator`(Analyzer/Model/IndexAssigner/Types・World・Snapshot・Loop・EntryPoint各Emitter)

- Layer: L7 — Static Compiler / Bake
- Parent capability: 静的(閉集合)Archetype ECS
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0033-Static-Archetype-Ecs-Migration.md、
  Docs/Decisions/ADR-0035-Generated-Entry-Point-And-Engine-Objects-As-Components.md、
  Docs/Decisions/ADR-0036-System-Order-Attribute.md、
  Docs/Specifications/Ecs-Soa-Systems.md
- Implementation evidence:
  - src/CSharpGameEngine.CodeAnalysis/GameSystemGenerator.cs(ISystem+[SceneDefinition]の統合エントリポイント)
  - src/CSharpGameEngine.CodeAnalysis/GameSystemAnalyzer.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemModel.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemIndexAssigner.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemTypesEmitter.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemWorldEmitter.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemSnapshotEmitter.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemLoopEmitter.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemEntryPointEmitter.cs(M0015)
  - src/CSharpGameEngine.CodeAnalysis/GameSystemOrderAnalyzer.cs(M0016)
  - src/CSharpGameEngine.CodeAnalysis/GameSystemExecutionOrder.cs(M0016)
  - src/CSharpGameEngine.CodeAnalysis/SceneRuntimeEmitter.cs(Createファクトリ呼び出しへ書き換え)
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
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDefinitionGenerator*Tests.cs(統合後のScene生成)
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
  - `[SceneDefinition]`のノード形状も同じArchetype空間へ入れるため、SceneDefinitionGeneratorは
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
