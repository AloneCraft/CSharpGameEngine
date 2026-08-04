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
F-L3系2件・F-L7系3件を追加しています。

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
- Intended specification: Docs/Decisions/ADR-0021-Editor-Layout-And-Shape-Palette.md
- Implementation evidence:
  - src/CSharpGameEngine.Geometry/ProceduralMeshData.cs
  - src/CSharpGameEngine.Geometry/ProceduralMeshFactory.cs
- Tests:
  - tests/CSharpGameEngine.Geometry.Tests/ProceduralMeshFactoryTests.cs
- Reverse-engineered behavior:
  - `src/CSharpGameEngine.Graphics.D3D12/ProceduralMeshFactory.cs`(`CreatePlane`/`CreateBox`のみ)
    から移設。移設前の時点で`Graphics.D3D12`本体のどこからも参照されておらず(唯一の呼び出し元は
    `StageGpuIntegrationTests.cs`)、`Graphics.D3D12`への`ProjectReference`は追加せず
    `tests/CSharpGameEngine.Graphics.D3D12.Tests.csproj`から直接参照する「テスト専用エッジ」
    とした(同csprojの`Import.Gltf`直接参照と同じ前例パターン)。
  - `CreateSphere`は極を単一頂点にしてファンする方式(素朴なUV球のように極の列を複製して
    quad化する方式ではない)で実装しており、これにより退化三角形が構造的に発生しない。
    `CreateCylinder`/`CreateCone`/`CreateDisk`は共有`AddRing`/`AddQuadBand`ヘルパーで
    リング状の頂点列とバンドの三角形分割を統一的に処理する。`CreateCapsule`は半球リング生成
    (`AddHemisphereRings`)+円柱側面+半球リングという3段構成。
  - 全8種(Box/Plane含む)についてretry`tests/CSharpGameEngine.Geometry.Tests`で頂点/インデックス
    範囲・法線単位長・退化三角形の不在(三角形面積>1e-6の実測値チェック)を検証、30件全て初回
    実行で成功。
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

## F-L3-005 — `AosEntityStore<TRow>` / `EntityAllocator`

- Layer: L3 — ECS / Job Runtime
- Parent capability: Generated Entity Storage(AoS）
- Status: Implemented
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
- Next action: なし。

## F-L3-006 — `World.TryGetArchetypeChunk<T>` / `ArchetypeChunk`

- Layer: L3 — ECS / Job Runtime
- Parent capability: Generated Entity Storage(SoA）
- Status: Implemented
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
- Next action: 固定容量ページングの具体的な要件が確定した時点で再検討する。

## F-L7-006 — `CSharpGameEngine.Generation.Abstractions`

- Layer: L7 — Static Compiler / Bake
- Parent capability: Generated Entity Storage
- Status: Implemented
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
- Conformance: Matches
- Known deviations: None
- Next action: なし。

## F-L7-007 — `GenerateEntityGenerator`

- Layer: L7 — Static Compiler / Bake
- Parent capability: Generated Entity Storage
- Status: Implemented
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
- Conformance: Matches
- Known deviations: None(見本との差異はF-L3-006/本Milestoneに記載の設計上の意図的な違いのみ)
- Next action: `[GenerateEntity]`の`UserGameProject`実利用は、`Scene1.cs`との名前衝突リスクが
  解消された時点(またはユーザーが許容した時点)で追加を検討する。

## F-L7-008 — `GenerateEntityGenerator`診断(ENTGEN001〜015)

- Layer: L7 — Static Compiler / Bake
- Parent capability: Generated Entity Storage
- Status: Implemented
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
- Next action: なし。
