# Implementation Status (Feature Ledger)

このファイルは`.github/skills/CSharpGameEngine-Documentation/SKILL.md`が定義するFeature ledgerです。
`F-L<layer>-<number>`形式の安定IDで機能を追跡します。既存(ADR-0001〜ADR-0013)の機能への遡及的な
Backfillは行っておらず、ADR-0014(CSharpGameEngine.CodeAnalysis基盤)で導入した2エントリを起点として
開始し、ADR-0015(アーキタイプ型ECS静的ランタイム)で5エントリ、ADR-0016(ECS駆動ステージと
ThirdPersonキャラクターコントローラー)で7エントリを追加しています。

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

## F-L0-001 — `Camera`(オービットカメラ)

- Layer: L0 — Platform / Native Backends
- Parent capability: ECS駆動ステージ
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0016-Ecs-Driven-Stage-And-ThirdPerson-Controller.md
- Implementation evidence:
  - src/CSharpGameEngine.Graphics.D3D12/Camera.cs
- Tests:
  - tests/CSharpGameEngine.Graphics.D3D12.Tests/CameraTests.cs
- Reverse-engineered behavior:
  - Yaw/Pitch/Distance/Targetからview/projection行列を計算するUE5風スプリングアームカメラ。
    `System.Numerics`のみに依存しTerraFXを含まないため、GPU無しで単体テストできる。
  - `ApplyMouseDelta`はPitchを`[MinPitchRadians, MaxPitchRadians]`にクランプする。
- Conformance: Matches
- Known deviations: `GetFlatRight()`の符号・yaw基準方向は実機での前後左右の直感一致を
  確認できていない(ADR-0016参照)。
- Next action: 実機確認で符号が逆であれば反転する。

## F-L0-002 — `StageRenderer`(複数オブジェクト・毎フレームカメラ更新対応)

- Layer: L0 — Platform / Native Backends
- Parent capability: ECS駆動ステージ
- Status: Implemented
- Verification: TestVerified
- Intended specification: Docs/Decisions/ADR-0016-Ecs-Driven-Stage-And-ThirdPerson-Controller.md
- Implementation evidence:
  - src/CSharpGameEngine.Graphics.D3D12/StageRenderer.cs
  - assets/Shaders/Stage.hlsl
- Tests:
  - tests/CSharpGameEngine.Graphics.D3D12.Tests/StageGpuIntegrationTests.cs
- Reverse-engineered behavior:
  - `ModelRenderer`とは別クラス(ADR-0004の複製方針)。FrameCB(b0)/ObjectCB(b1)の2種の
    root CBVを`FrameCount`分ダブルバッファし、`SetCamera`/`SetObjectTransform`は呼び出し時点の
    `_frameIndex`スロットのみを書き込む。テクスチャ/SRVは使わない単色Lambertianシェーダー。
  - WARPアダプタでの実描画テストにより、地面+ボックスが両方描画されること、Resize後も
    壊れないことを確認済み。
- Conformance: Matches
- Known deviations: None
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
