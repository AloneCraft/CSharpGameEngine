# Implementation Status (Feature Ledger)

このファイルは`.github/skills/CSharpGameEngine-Documentation/SKILL.md`が定義するFeature ledgerです。
`F-L<layer>-<number>`形式の安定IDで機能を追跡します。既存(ADR-0001〜ADR-0013)の機能への遡及的な
Backfillは行っておらず、ADR-0014(CSharpGameEngine.CodeAnalysis基盤)で導入した2エントリを起点として
開始し、ADR-0015(アーキタイプ型ECS静的ランタイム)で5エントリ、ADR-0016(ECS駆動ステージと
ThirdPersonキャラクターコントローラー)で7エントリ、ADR-0017/ADR-0018(UE5.8 FirstPersonステージ
移植、M0004)で既存2エントリを更新し5エントリを追加、ADR-0019(VSIXのglTFアウトオブプロセス
ブリッジ統一)で1エントリ、ADR-0020(シーンダブルクリックプレビューとヒエラルキー選択連動)で
3エントリを追加しています。

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

## F-L3-001 — Entity / World基盤

- Layer: L3 — ECS / Job Runtime
- Parent capability: アーキタイプ型ECS静的ランタイム
- Status: Implemented
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
- Next action: 動的Add/Remove(構造変更)を実装する場合、このswap-back機構をアーキタイプ間移動にも
  拡張する必要がある。

## F-L3-002 — Archetypeストレージ共有

- Layer: L3 — ECS / Job Runtime
- Parent capability: アーキタイプ型ECS静的ランタイム
- Status: Implemented
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
- Next action: なし。

## F-L3-003 — `IEcsArchetype<TSelf>`ランタイム契約

- Layer: L3 — ECS / Job Runtime
- Parent capability: アーキタイプ型ECS静的ランタイム
- Status: Implemented
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
- Next action: なし。

## F-L7-003 — `EcsArchetypeGenerator`

- Layer: L7 — Static Compiler / Bake
- Parent capability: アーキタイプ型ECS静的ランタイム
- Status: Implemented
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
- Next action: 動的Add/Remove(構造変更)またはQuery/System機能の具体的な要件が確定した時点で
  再検討する。

## F-L7-004 — `EcsArchetypeGenerator`診断(ECSGEN001〜009)

- Layer: L7 — Static Compiler / Bake
- Parent capability: アーキタイプ型ECS静的ランタイム
- Status: Implemented
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
- Next action: なし。

## F-L0-001 — `Camera`(オービットカメラ+一人称カメラ)

- Layer: L0 — Platform / Native Backends
- Parent capability: ECS駆動ステージ
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0016-Ecs-Driven-Stage-And-ThirdPerson-Controller.md、
  Docs/Decisions/ADR-0018-Textured-Stage-And-Ecs-Systems.md(一人称モード追加)
- Implementation evidence:
  - src/CSharpGameEngine.Graphics.D3D12/Camera.cs
- Tests:
  - tests/CSharpGameEngine.Graphics.D3D12.Tests/CameraTests.cs
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
- Conformance: Matches
- Known deviations: `GetFlatRight()`の符号・yaw基準方向は実機での前後左右の直感一致を
  確認できていない(ADR-0016参照)。
- Next action: 実機確認で符号が逆であれば反転する。

## F-L0-002 — `StageRenderer`(複数オブジェクト・テクスチャ付きPBR・データ駆動ライティング)

- Layer: L0 — Platform / Native Backends
- Parent capability: ECS駆動ステージ
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0016-Ecs-Driven-Stage-And-ThirdPerson-Controller.md、
  Docs/Decisions/ADR-0018-Textured-Stage-And-Ecs-Systems.md(テクスチャ・PBR・ライティング拡張)
- Implementation evidence:
  - src/CSharpGameEngine.Graphics.D3D12/StageRenderer.cs
  - src/CSharpGameEngine.Graphics.D3D12/StageRenderInputs.cs
  - assets/Shaders/Stage.hlsl
- Tests:
  - tests/CSharpGameEngine.Graphics.D3D12.Tests/StageGpuIntegrationTests.cs
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
  - src/CSharpGameEngine.Graphics.D3D12/ProceduralMeshFactory.cs
- Tests:
  - tests/CSharpGameEngine.Graphics.D3D12.Tests/ProceduralMeshFactoryTests.cs
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
  - src/CSharpGameEngine.Platform.Windows/Win32Window.cs
- Tests: なし(Win32メッセージハンドリング自体の単体テストは無く、実機確認に委ねている。
  ADR-0016参照)。
- Reverse-engineered behavior:
  - `IsKeyDown`はポーリング方式(`WM_KEYDOWN`/`UP`で`bool[256]`を更新)。マウスは
    カーソルセンタリング方式(`GetCursorPos`/`SetCursorPos`/`ClipCursor`/`ShowCursor`)。
  - `WM_KILLFOCUS`でキー状態全クリア+マウスキャプチャ解除、`WM_SETFOCUS`で再キャプチャする。
- Conformance: Matches
- Known deviations: None
- Next action: マウス/キーボードの対話的操作感そのものはユーザー自身の実機確認が必要
  (この環境からは入力を注入できないため)。

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
  Docs/Decisions/ADR-0018-Textured-Stage-And-Ecs-Systems.md
- Implementation evidence:
  - src/CSharpGameEngine.Import.Gltf/GltfImporter.cs(`ImportScene`メソッド)
  - src/CSharpGameEngine.Import.Gltf/ImportedScene.cs
  - src/CSharpGameEngine.Import.Gltf/ImportedSceneNode.cs
  - src/CSharpGameEngine.Import.Gltf/ImportedMeshData.cs
  - src/CSharpGameEngine.Import.Gltf/ImportedPbrMaterial.cs
  - src/CSharpGameEngine.Import.Gltf/ImportedLight.cs
  - src/CSharpGameEngine.Import.Gltf/GltfSceneImportResult.cs
- Tests:
  - tests/CSharpGameEngine.Import.Gltf.Tests/GltfImporterSceneTests.cs
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
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L3-004 — `World.CollectViews<T>`

- Layer: L3 — ECS / Job Runtime
- Parent capability: UE5.8 FirstPersonステージ移植
- Status: Implemented
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
- Next action: 複数アーキタイプにまたがるクエリが必要になった時点で、本格的なQuery/System
  基盤を再検討する。

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
  Docs/Decisions/ADR-0018-Textured-Stage-And-Ecs-Systems.md
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
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L9-004 — ECS入力抽象化とゲームプレイSystems群

- Layer: L9 — Game / Application
- Parent capability: UE5.8 FirstPersonステージ移植
- Status: Implemented
- Verification: TestVerified、RuntimeVerified
- Intended specification: Docs/Decisions/ADR-0018-Textured-Stage-And-Ecs-Systems.md
- Implementation evidence:
  - UserGameProject/Components.cs(`InputState`/`CameraRig`/`VerticalMotion`)
  - UserGameProject/Systems/InputSystem.cs
  - UserGameProject/Systems/PlayerControlSystem.cs
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
- Conformance: Matches
- Known deviations: 一人称でのマウス/キーボード操作感そのものは自動検証していない
  (ADR-0016から引き継いだ制約)。
- Next action: なし。

## F-L8-002 — VSIX glTFアウトオブプロセスブリッジ(`ExternalGltfLoader`/`ExternalBridgeProcess`)

- Layer: L8 — Visual Studio Authoring Environment
- Parent capability: シーンダブルクリックプレビューとヒエラルキー選択連動
- Status: Implemented
- Verification: TestVerified、RuntimeVerified
- Intended specification: Docs/Decisions/ADR-0019-VSIX-Gltf-OutOfProcess-Bridge.md
- Implementation evidence:
  - tools/CSharpGameEngine.Import.Gltf.Cli/Program.cs
  - tools/CSharpGameEngine.Import.Gltf.Cli/GltfBridgeDocument.cs
  - tools/CSharpGameEngine.VSExtension/ExternalBridgeProcess.cs
  - tools/CSharpGameEngine.VSExtension/ExternalGltfLoader.cs
  - tools/CSharpGameEngine.VSExtension/BridgeDocument.cs
  - tools/CSharpGameEngine.VSExtension/MeshLoader.cs
- Tests: なし(`CSharpGameEngine.Import.Fbx.Cli`と同様、`Process.Start`薄いラッパーのため専用の
  ユニットテストは無い。ブリッジCLI自体は`dotnet run`実行で実地確認)。
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
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L2-001 — `CSharpGameEngine.Scene`(`SceneNode`/`Transform`/`SceneGraph`/`SceneNodeTree`)

- Layer: L2 — Type Schema / Identity
- Parent capability: シーンダブルクリックプレビューとヒエラルキー選択連動
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0020-Scene-Preview-And-Hierarchy-Selection-Sync.md、
  Docs/EditorSpec.md §3(データモデル)
- Implementation evidence:
  - src/CSharpGameEngine.Scene/Transform.cs
  - src/CSharpGameEngine.Scene/SceneNode.cs
  - src/CSharpGameEngine.Scene/SceneGraph.cs
  - src/CSharpGameEngine.Scene/SceneNodeTree.cs
  - src/CSharpGameEngine.Scene/SceneJson.cs
  - UserGameProject/DefaultScene.scene.json
  - UserGameProject/Assets/DefaultScene/Box.glb
- Tests:
  - tests/CSharpGameEngine.Scene.Tests/SceneGraphJsonTests.cs
  - tests/CSharpGameEngine.Scene.Tests/SceneNodeTreeTests.cs
  - tests/CSharpGameEngine.Scene.Tests/DefaultSceneFileTests.cs
- Reverse-engineered behavior:
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
- Conformance: Matches
- Known deviations: `UserGameProject.csproj`への`CSharpGameEngine.Scene`への`ProjectReference`は
  追加していない(ランタイム統合はEditorSpec Phase 4のスコープ、今回は対象外)。
- Next action: EditorSpec Phase 2(ドラッグ&ドロップ配置・Inspector)またはPhase 4(Play統合)が
  必要になった時点で再検討する。

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
  - `HierarchyToolWindowPane`は`CSharpGameEngineViewerPackage.InitializeAsync`内で
    `ShowToolWindowAsync`により常時表示にした(Unityの常時ドッキングされたヒエラルキーの
    体感に合わせた設計判断 - EditorSpec Phase 1の「パッケージロード時走査」とは異なる)。
- Conformance: Matches(ただし実機でのダブルクリック・ハイライト表示の目視確認は未実施 -
  下記Known deviations参照)。
- Known deviations: `.scene.json`という複合拡張子が`ProvideEditorExtension`で実際にVS側で
  単一の拡張子として認識されるかは、VS実験用インスタンスでの実地確認をまだ行っていない。
  選択ハイライトの見た目(オレンジ系タイントの視認性)も同様に実機未確認。
- Next action: 次回セッションでVS実験用インスタンス([[feedback-vsix-verification]]の
  `NativeWindowHandle`経由スクリーンショット手順)により、ダブルクリック起動・ヒエラルキー
  ツリー表示・選択ハイライトを実機確認する。
