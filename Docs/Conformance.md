# Conformance

このファイルは`.github/skills/CSharpGameEngine-Documentation/SKILL.md`が定義するConformanceモデル
(意図した仕様 ↕ 実装 ↕ リバースエンジニアリングした観測挙動 ↕ テスト/実行検証)に従う。

このファイルは**ADR-0024(Generated Entity Source Generator)以降のADR由来のエントリのみ**を含む。
既存機能(ADR-0001〜ADR-0023相当)への遡及的なBackfillは行っていない
(`Docs/Implementation-Status.md`がADR-0014から遡及なしで開始した前例に倣う)。ADR-0025
(RUFセマンティック座標系)分、ADR-0026/ADR-0027/ADR-0028(Renderer/Import/ECS内部へのRUF全面統合、
M0008)分をこのファイルの末尾に追加した。ADR-0029(動的Archetype SoA ECSへの全面移行、M0009)で
`CSharpGameEngine.Ecs`/`CSharpGameEngine.Generation.Abstractions`関連の既存エントリを
`ObsoleteSpecification`へ更新し、末尾にRE-L3-007〜010・RE-L7-009〜011・RE-L9-005を追加した。
ADR-0030(C#コードをSceneの唯一の正本とするScene Authoring Phase A、M0010)で末尾に
RE-L3-011・RE-L7-012〜013・RE-L9-006を追加した。ADR-0031(`[SceneDefinition]`クラスの
コンテキストメニュー3Dプレビュー、M0011)で末尾にRE-L7-014・RE-L8-009・RE-L9-007を追加した。

---

### RE-L3-005 — `AosEntityStore<TRow>` / `EntityAllocator`(削除済み、ADR-0029/M0009)

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Ecs/AosEntityStore.cs
  - src/CSharpGameEngine.Ecs/EntityAllocator.cs
  - tests/CSharpGameEngine.Ecs.Tests/AosEntityStoreSpec.cs、EntityAllocatorSpec.cs
- Observed behavior:
  - `Entity`は行配列の直接Indexではなく、常にLocation Table経由で行番号へ解決される。
  - 削除はswap-backで行い、移動したEntityのLocationだけが更新される。
  - Id再利用時にGenerationが増分され、古いハンドルは`IsAlive`が`false`を返し、
    `GetRowRef`/`Destroy`が`InvalidOperationException`を投げる。
- Intended behavior:
  - Docs/Specifications/Generated-Entity-Storage.md「AoSの正本」「Entity・Generation・
    Locationの分離」節と同一。
- Result: ObsoleteSpecification(ADR-0029/M0009でSoA一本化に伴い削除。後継はF-L3-007の
  `CSharpGameEngine.Entities.Entity`/`World`、RE-L3-007参照)

### RE-L3-006 — `World.TryGetArchetypeChunk<T>` / `ArchetypeChunk`(削除済み、ADR-0029/M0009)

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Ecs/World.cs、ArchetypeChunk.cs、ArchetypeStorage.cs
  - tests/CSharpGameEngine.Ecs.Tests/ArchetypeChunkSpec.cs
- Observed behavior:
  - 該当アーキタイプのEntityが1件も無ければ`false`(`Count=0`、空の`Entities`)。
  - 1つの`ArchetypeStorage`全体がそのまま1つの`ArchetypeChunk`として返る
    (固定容量ページングは行われない)。
  - 正規化後の構成が同じ異なる`IEcsArchetype<T>`型は同一のChunkを共有する
    (`ArchetypeSignature`のType[]シーケンス一致による、既存`CollectViews<T>`と同じ挙動)。
- Intended behavior:
  - Docs/Specifications/Generated-Entity-Storage.md「SoAの正本」節と同一。見本
    (`AosSoaGeneratedExpansion.SingleFile.cs`)の`ChunkCapacity`ベースの物理的な複数Chunk
    生成は、ADR-0024で単一Chunk方式へ意図的に置き換えている。
- Result: ObsoleteSpecification(単一Chunk方式自体はADR-0024で意図した設計だったが、
  Chunk/Query機構全体がADR-0029/M0009でSoA一本化に伴い削除。後継はF-L3-007〜009、
  RE-L3-007〜009参照)

### RE-L7-006 — `CSharpGameEngine.Generation.Abstractions`(削除済み、ADR-0029/M0009)

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Generation.Abstractions/*.cs
  - src/CSharpGameEngine.Generation.Abstractions/CSharpGameEngine.Generation.Abstractions.csproj
- Observed behavior:
  - net10.0、`PackageReference`なし、`Microsoft.CodeAnalysis.*`にも`CSharpGameEngine.Ecs`にも
    依存しない。`EntityStorageKind`(enum)と`GenerateEntityAttribute`のみを公開する。
- Intended behavior:
  - ADR-0024「`CSharpGameEngine.Generation.Abstractions`を新設し、`EntityGenerationSettings`は
    同梱しない」節と同一。
- Result: ObsoleteSpecification(ADR-0029/M0009で削除。`[GameSystem]`方式ではComponent型は
  独立したstructで十分で、専用の属性契約アセンブリは不要になった)

### RE-L7-007 — `GenerateEntityGenerator`: コンパイル時Backend選択とFacade統一(削除済み、ADR-0029/M0009)

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.CodeAnalysis/GenerateEntityGenerator.cs、GenerateEntityAnalyzer.cs、
    GenerateEntityCommonEmitter.cs、GenerateEntityAosEmitter.cs、GenerateEntitySoaEmitter.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GenerateEntityGeneratorEmissionTests.cs、
    GenerateEntityGeneratorConstSwitchTests.cs、GenerateEntityAosRuntimeTests.cs、
    GenerateEntitySoaRuntimeTests.cs、GenerateEntityConformanceTests.cs
- Observed behavior:
  - 属性引数の`EntityStorageKind`は`const`フィールド参照であってもRoslynが解決済みの値を
    渡す。選択されなかったBackendの`AddSource`は一度も呼ばれず、生成ソースに
    `EntityStorageKind`のRuntime比較・`switch`は一切現れない
    (`GenerateEntityGeneratorConstSwitchTests`でテキスト検索により確認)。
  - `{Type}`/`{Type}CreateInfo`の公開メンバー形状はAoS/SoA間でreflection比較により完全一致
    (`PublicCharacterApi_IsIdenticalBetweenAosAndSoa`)。
  - 実際にコンパイル・`Emit`・`Assembly.Load`・リフレクション実行して、Create/get・set/
    refアクセサ/ユーザー定義`Move`・`ApplyDamage`・`IsDead`/コピーハンドルの同一性/Destroy/
    stale handle拒否/swap-back/ChunkViewバッチループ/2生成型間のアーキタイプ共有が
    両Backendで機能することを確認した。
- Intended behavior:
  - ADR-0024「公開Facadeを両Backendで統一する」「物理ストレージだけをコンパイル時に
    切り替える」「本番では選択Backendだけを生成する」「Runtime分岐を残さない」節と同一。
- Result: ObsoleteSpecification(ADR-0029/M0009で削除。後継はF-L7-009、RE-L7-009参照)

### RE-L7-007 — `GenerateEntityGenerator`: `[Flags]`による1宣言からの両バックエンド生成(削除済み、ADR-0029/M0009)

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Generation.Abstractions/EntityStorageKind.cs
  - src/CSharpGameEngine.CodeAnalysis/GenerateEntityEmitterSupport.cs、GenerateEntityGenerator.cs、
    GenerateEntitySoaEmitter.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GenerateEntityCombinedKindTests.cs
- Observed behavior:
  - 両フラグ指定時、1つの宣言から`{Type}.Common.g.cs`/`{Type}.Aos.g.cs`/`{Type}.Soa.g.cs`の
    3ファイルが出力される。Commonは1回だけで、Facadeは`{Type}AosStore`に束縛される。
  - Store/ChunkViewは`{Type}Aos*`/`{Type}Soa*`へ改名され衝突しない。SoA側の`Create`は
    Facadeを持たないため`Entity`を返す。
  - 単一指定時の生成物は`[Flags]`化前と完全に同一(既存79テストが無改変で通過)。
  - 生成コードに`EntityStorageKind`のRuntime比較も`switch`も現れない。
- Intended behavior:
  - ADR-0024「`EntityStorageKind`を`[Flags]`とし、両指定は比較・計測モードとする」節、
    およびDocs/Specifications/Generated-Entity-Storage.md「両指定(比較・計測モード)」節と同一。
- Result: ObsoleteSpecification(ADR-0024当時の設計だった`[Flags]`による2バックエンド選択自体が
  ADR-0029/M0009でSoA一本化に伴い削除された)

### RE-L9-001 — Particle Entityベンチマークの実測挙動(削除済み、ADR-0029/M0009)

- Confidence: High
- Evidence:
  - UserGameProject/Entities/*.cs
  - tests/UserGameProject.Tests/ParticleStorageTests.cs、BenchmarkHarnessTests.cs
  - Docs/Milestones/M0006-Generated-Entity-Benchmark.md(実測値)
- Observed behavior:
  - 1宣言から生成された両バックエンドが、10フレーム分のバッチ更新後も合計値まで完全に一致する。
  - 実測(500,000件・51試行・中央値、Release): 部分コンポーネント走査でSoAが1.59〜1.76倍速く、
    全コンポーネント走査では差が消え、CreateはAoSが1.69倍速く、ランダム単体アクセスは同等。
- Intended behavior:
  - ADR-0024「Hot loopにReflection/Dictionary検索/Location lookup/boxing/interface dispatchなし」
    「SoAはSpanまたは配列列を直接処理」「AoSはData row配列を直接処理」というコード構造上の保証。
    観測された性能特性はこの構造から予期されるとおりのもの。
- Result: ObsoleteSpecification(実測値自体はM0006に記録として残るが、AoS/SoA両バックエンドの
  選択肢自体がSoA一本化(ADR-0029/M0009)で無くなったため、再実行可能な比較ベンチマークは
  失われた。ユーザー確認済みの許容されたトレードオフ、ADR-0029参照)

### RE-L7-008 — `GenerateEntityGenerator`診断(ENTGEN001〜015、削除済み、ADR-0029/M0009)

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.CodeAnalysis/GenerateEntityDiagnostics.cs、
    GenerateEntityAnalyzer.cs、AnalyzerReleases.Unshipped.md
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GenerateEntityGeneratorDiagnosticsTests.cs
- Observed behavior:
  - `ENTGEN001`〜`015`(15件)がそれぞれ対象コードで報告され、`ValidCharacterSource_
    ProducesZeroDiagnostics`(正しいCharacter宣言)ではいずれも報告されないことを確認した。
  - 診断が1件でもある型は、その型のソース生成が完全にスキップされる。
- Intended behavior:
  - Docs/Specifications/Generated-Entity-Storage.md「Diagnostic一覧」節と同一。
- Result: ObsoleteSpecification(ADR-0029/M0009で削除。後継はF-L7-011のSYSGEN001〜020、
  RE-L7-011参照)

### RE-L1-001 — RUF/XYZセマンティック座標コア型と`ICoordinateConvention`契約

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Core/Spatial/RufVector.cs、XyzVector.cs、ICoordinateConvention.cs、
    CoordinateBasisValidator.cs、UnitConversion.cs、Spatial.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/BasisTests.cs、CoordinateBasisValidatorTests.cs、
    UnitConversionTests.cs、ImportAxisConversionTests.cs、DeterminismTests.cs
- Observed behavior:
  - `RufVector`/`XyzVector`はblittable・unmanagedで、`XyzVector`→`Vector3`は`explicit`演算子のみ。
  - `CoordinateBasisValidator`は非有限値・非単位長・非直交をそれぞれ検出するが、重複軸専用の
    チェックは直交性チェックが先に走るため到達不能(重複軸同士の内積は必ず1になるため)。
  - `UnitConversion`のスカラー変換は`ICoordinateConvention`の軸変換と可換(順序を入れ替えても
    結果が変わらない)ことを実測確認した。
- Intended behavior:
  - Docs/Specifications/Spatial-RUF.md「RUFは意味座標であり、XYZは物理配置である」
    「Performance原則」「未確定事項」節と同一。
- Result: MatchesWithDocumentedExtension(重複軸チェックの到達不能性は意図した設計ではなく
  既存コードの副次的な挙動だが、エラーにはなるため完了条件自体は満たしている。
  ADR-0025・Spatial-RUF.mdの両方に明記済み)

### RE-L1-002 — `RufQuaternion`/`ITransformConvention`/`StandardTransformConvention`

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Core/Spatial/RufQuaternion.cs、ITransformConvention.cs、
    StandardTransformConvention.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/RufQuaternionTests.cs、TransformConventionTests.cs
- Observed behavior:
  - `StandardTransformConvention.Compose`はScale→Rotation→Translationの順(row-vector規約)。
    `Combine`は`local*parentWorld`(child-then-parent)。`TransformNormal`は逆転置行列を使う
    独立実装で、非一様スケール下でも法線が変換後の面に垂直なまま保たれる。
  - `RufQuaternion.operator *`は`parent*child`で「childを先、parentを後」に適用する合成。
    `LookRotation`はForward/Up平行時に決定的フォールバックへ切り替わりNaNを生成しない。
- Intended behavior:
  - Docs/Specifications/Spatial-RUF.md「Transform／Matrix Conventionとの分離」
    「Quaternion仕様」節と同一。
- Result: Matches

### RE-L1-003 — Unity/Unreal/Blenderの具体的Convention実装

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Core/Spatial/Conventions/UnityCoordinateConvention.cs、
    src/CSharpGameEngine.Core/Spatial/Conventions/UnrealCoordinateConvention.cs、
    src/CSharpGameEngine.Core/Spatial/Conventions/BlenderWorldConvention.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/BasisTests.cs、RoundTripTests.cs、HandednessTests.cs
  - samples/SpatialPresetDemoの実行結果(Docs/Decisions/ADR-0025-RUF-Semantic-Coordinates.md参照)
- Observed behavior:
  - 3実装とも成分の固定並べ替えのみ。Preset別に`samples/SpatialPresetDemo`を実行し、Forward軸の
    物理XYZ値が仕様書のGolden Values(Unity=(0,0,1)、Unreal=(1,0,0)、Blender=(0,1,0))と
    完全一致することを実測した。
  - LeftHandedなUnity/Unrealは`Cross(Right,Up)==Forward`、RightHandedなBlenderは
    `Cross(Right,Up)==Backward`になることを確認した。
- Intended behavior:
  - Docs/RUF-Semantic-Coordinate-Specification-Prompt.md「Golden Values」節と同一。
- Result: Matches

### RE-L1-004 — コンパイル時Preset選択(`SpatialPresetSelection.props`) 【ADR-0034で削除済み】

- Status: **Removed**(ADR-0034 / M0014)。この機構の利用者は`samples/SpatialPresetDemo`だけで、
  しかもそれは`CSharpGameEngine.slnx`へ意図的に未登録だったため、通常ビルドで一度も実行されて
  いなかった。下記の検証記録は削除時点の証跡としてそのまま保持する。
  Convention型自体は`CSharpGameEngine.Core`内に名前空間ごと残っており、実行時選択は引き続き可能
  (RE-L1-002 / RE-L1-003 は有効なまま)。
- Confidence: High
- Evidence(削除済み。履歴はgitの`f17fbd5`以前を参照):
  - ~~src/CSharpGameEngine.Spatial.Abstractions/SpatialPresetSelection.props~~
  - ~~samples/SpatialPresetDemo/ProjectSpatial.cs、Program.cs~~
  - Docs/Decisions/ADR-0025-RUF-Semantic-Coordinates.md「検証結果」節の実行コマンドと結果(存続)
- Observed behavior:
  - `SpatialPreset`未指定・未知の値のいずれも`ValidateSpatialPresetSelection`ターゲットで
    Build Errorになることを実際のコマンド実行で確認した。
  - `samples/SpatialPresetDemo`を`CSharpGameEngine.slnx`へ登録すると、`SpatialPreset`未指定で
    ソリューション全体の`dotnet build`が失敗することを確認したため、意図的に未登録とした。
- Intended behavior:
  - Docs/RUF-Semantic-Coordinate-Specification-Prompt.md「コンパイル時プリセット選択」の
    保証事項(0件選択・複数選択をBuild Errorにする)と同一。
- Result: MatchesWithDocumentedExtension(`samples/SpatialPresetDemo`をslnx未登録にした判断は
  意図した設計上の差異であり、ADR-0025に明記済み)

### RE-L2-003 — `CameraBasis`/`DirectionalLightBasis`/`SpotLightBasis`

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Core/Spatial/CameraBasis.cs、DirectionalLightBasis.cs、
    SpotLightBasis.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/CameraBasisTests.cs、LightBasisTests.cs
- Observed behavior:
  - `CameraBasis`のコンストラクタは`Forward`のみ正規化し`Up`はそのまま保持する。
  - `DirectionalLightBasis`/`SpotLightBasis`の`DirectionToLight`は常に`-Forward`。
  - 既存`CSharpGameEngine.Graphics.D3D12.Camera`とは未接続(このマイルストーンの対象外)。
- Intended behavior:
  - Docs/RUF-Semantic-Coordinate-Specification-Prompt.md「Object Basis」節と同一。
- Result: Matches

### RE-L0-001 — `Camera`のRUF内部実装統合

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Windows/Graphics/Camera.cs
  - tests/CSharpGameEngine.Windows.Tests/Graphics/CameraTests.cs(既存60件、シャドウテスト2件含む)
- Observed behavior:
  - `GetFlatRight()`は`Spatial<UnityCoordinateConvention>.RightFromUpForward`経由になり、
    旧`Cross(Vector3.UnitY,flatForward)`式とビット単位で一致することを実測確認した。
  - `GetForward`/`GetFlatForward`は`RufQuaternion`合成(Pitch軸は`RufVector.Left`)になり、
    旧手書き三角関数式とYaw/Pitch広範囲で`1e-4`許容誤差内に一致することを実測確認した。
  - `GetView`/`GetProjection`自体は無改修(D3Dビュー/クリップ空間規約はRUF未契約のまま)。
- Intended behavior:
  - Docs/Decisions/ADR-0026-Camera-Geometry-Ruf-Integration.md「Camera.cs」節と同一。
- Result: Matches

### RE-L2-002 — `ProceduralMeshFactory`のRUF内部実装統合

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Core/Geometry/ProceduralMeshFactory.cs
  - tests/CSharpGameEngine.Core.Tests/Geometry/ProceduralMeshFactoryTests.cs(既存30件、無改修)
- Observed behavior:
  - 全8プリミティブの頂点/法線計算が`RufVector`ベースになり、出力直前の
    `Spatial<UnityCoordinateConvention>.ToXyz`(恒等写像)により既存30件のテスト
    (法線の厳密一致チェック含む)が無改修のまま成功することを実測確認した。
- Intended behavior:
  - Docs/Decisions/ADR-0026-Camera-Geometry-Ruf-Integration.md「ProceduralMeshFactory.cs」節と同一。
- Result: Matches

### RE-L0-006 — `Camera.BuildViewMatrix`共有ヘルパー

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Windows/Graphics/Camera.cs、ModelRenderer.cs
- Observed behavior:
  - `Matrix4x4.CreateLookAt`への薄い委譲。`Camera.GetView`と`ModelRenderer`独自のバウンディング
    スフィア自動フィットeye計算の両方から呼ばれ、以前は2箇所独立していたLookAt構築を統一した。
- Intended behavior:
  - Docs/Decisions/ADR-0026-Camera-Geometry-Ruf-Integration.md「ModelRenderer.cs」節と同一。
- Result: Matches

### RE-L1-005 — `GltfCoordinateConvention` / RE-L1-006 — `FbxCoordinateConvention`

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Core/Spatial/Conventions/GltfCoordinateConvention.cs、
    src/CSharpGameEngine.Core/Spatial/Conventions/FbxCoordinateConvention.cs
  - tests/CSharpGameEngine.Core.Tests/Spatial/BasisTests.cs、RoundTripTests.cs、HandednessTests.cs
- Observed behavior:
  - 両方ともR=+X,U=+Y,F=-Z,RightHanded(数値的に同一定義だが独立した型)。Golden Value・
    RoundTrip・Handedness(`Cross(Right,Up)==Backward`)をいずれも実測確認した。
- Intended behavior:
  - Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.md「Convention新設」節と同一。
- Result: Matches

### RE-L0-005 — `GltfImporter.ImportScene`の軸変換

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Import/Gltf/GltfAxisConversion.cs、GltfImporter.cs、
    ImportedLightDirection.cs
  - tests/CSharpGameEngine.Import.Tests/Gltf/GltfImporterSceneTests.cs(既存18件+Z非ゼロ新規2件)
- Observed behavior:
  - Translation/Rotation(ミラー行列共役変換)/メッシュローカル頂点/Tangent/Windingが変換され、
    `ImportScene`の公開API出力は全てエンジン(`UnityCoordinateConvention`)空間になる。
    既存fixtureのZ成分がたまたま0のため検知できなかった既存2テストとは別に、Z非ゼロの
    新規fixtureで変換の実効性を実測確認した。
- Intended behavior:
  - Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.md「GltfImporter.ImportScene」節と同一。
- Result: Matches

### RE-L9-003 — `SceneLoader`の軸変換・重複解消

- Confidence: High
- Evidence:
  - UserGameProject/SceneLoader.cs
  - tests/UserGameProject.Tests/SceneLoaderTests.cs(期待値更新3箇所)
- Observed behavior:
  - PlayerStart(`scene_meta.json`、既にUE→glTF変換済みの値)にも`GltfAxisConversion.
    ConvertWorldTransform`と同じ経路を適用し、位置Z成分とYaw角(純Y軸回転、共役変換により
    符号反転)の両方が変換されることを実測確認した。ライト方向の独自`-Vector3.UnitZ`実装は
    `ImportedLightDirection.WorldForward`呼び出しに置き換えた。
  - `dotnet run --project UserGameProject`を実行し15秒以上クラッシュしないこと、
    スクリーンショットで空・床・グリッド壁が破綻なく描画されることを確認した。
- Intended behavior:
  - Docs/Decisions/ADR-0027-Gltf-Fbx-Import-Axis-Conversion.md「SceneLoader.cs」節と同一。
- Result: MatchesWithDocumentedExtension(変更前との比較スクリーンショットが無いため、鏡像に
  なっていないか等の完全な視覚的確証はユーザー自身の実機確認が必要と明記済み)

### RE-L9-004 — `PlayerControlSystem`/`CameraSystem`のRUF境界配置

- Confidence: High
- Evidence:
  - UserGameProject/Systems/PlayerControlSystem.cs、CameraSystem.cs
  - tests/UserGameProject.Tests/PlayerControlSystemTests.cs(既存、無改修)
- Observed behavior:
  - flatForward/flatRight/身体の向きQuaternion/EyeHeight加算が`RufVector`/`RufQuaternion`経由に
    なった。ECSコンポーネント(バラfloatフィールド)は無改修。`UnityCoordinateConvention`が
    恒等写像のため既存テストが無改修のまま成功することを実測確認した。
- Intended behavior:
  - Docs/Decisions/ADR-0028-Ecs-Gameplay-Ruf-Boundary.md「決定事項」節と同一。
- Result: Matches

### RE-L3-007 — `CSharpGameEngine.Entities`: 動的Archetype生成とノーリフレクション保証

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Entities/ComponentType.cs、World.cs、Archetype.cs
  - tests/CSharpGameEngine.Entities.Tests/ComponentTypeRegistrySpec.cs、ArchetypeSpec.cs、
    WorldSpec.cs
- Observed behavior:
  - Archetypeは`World.GetOrCreateArchetype`が実行時のComponent集合から動的生成し、
    append-onlyのリストで管理される。`ComponentType<T>.Id`の静的初期化時に登録される
    ファクトリデリゲート配列により、動的生成であっても`MakeGenericType`等のReflectionを
    一度も呼ばないことをコードレビューおよびテストで確認した。
- Intended behavior:
  - 依頼文の「Archetypeは実行時のComponent集合によって決まる」「ReflectionやType
    Dictionaryによる検索をホットループに入れない」、Docs/Specifications/Ecs-Soa-Systems.md
    「動的Archetype生成」節と同一。
- Result: Matches

### RE-L3-008 — 構造変更の安全性(コールドパス限定のDictionary、Span無効化対策)

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Entities/World.cs、Archetype.cs
  - tests/CSharpGameEngine.Entities.Tests/StructuralChangeSpec.cs
- Observed behavior:
  - Component追加・削除の即時APIは`internal`であり、外部(`[Update]`メソッド)からは
    `EntityCommandBuffer`経由でしか到達できない。Add/Removeエッジキャッシュの
    `Dictionary<int, Archetype>`は`Playback`時(コールドパス)にのみ参照される。
- Intended behavior:
  - 依頼文の「構造変更はCommand Buffer経由で安全な地点にまとめて適用する」
  - Docs/Specifications/Ecs-Soa-Systems.md「構造変更」節と同一。
- Result: Matches

### RE-L3-009 — `EntityQuery`のQueryキャッシュ差分更新

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Entities/EntityQuery.cs
  - samples/SoaSystemsDemo/DemoScenario.cs(実行時シナリオ)
- Observed behavior:
  - `samples/SoaSystemsDemo`のシナリオで、既に1回`Update`済みのQueryが、フレーム間に新規生成
    されたArchetypeをその後のフレームで正しく検出することを実測した(`dotnet run`で"OK"を確認)。
    新Archetypeが生まれていないフレームは`int`比較のみで完了する構造であることをコードで確認した。
- Intended behavior:
  - 依頼文の「新しいArchetypeが生成されたときだけQuery Cacheを更新する」、
    Docs/Specifications/Ecs-Soa-Systems.md「EntityQuery」節と同一。
- Result: Matches

### RE-L3-010 — `EntityCommandBuffer`のPlaybackタイミング

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Entities/EntityCommandBuffer.cs
  - src/CSharpGameEngine.CodeAnalysis/GameSystemLoopEmitter.cs(`world.Commands.Playback(world)`
    をループ末尾に生成)
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorEndToEndTests.cs
    (`Commands_StructuralChangesApplyOnlyAfterPlaybackAtEndOfGeneratedLoop`)
- Observed behavior:
  - 行ループ中に`commands.Add<T>`を積んでも、`GeneratedGameLoop.Update`が最後に
    `world.Commands.Playback(world)`を呼ぶまでArchetypeは変わらないことを実測確認した。
- Intended behavior:
  - 依頼文の「構造変更はCommand Buffer経由で安全な地点にまとめて適用する」と同一。
- Result: Matches

### RE-L7-009 — `GameSystemGenerator`: 引数からのQuery/Read-Write推論

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.CodeAnalysis/GameSystemAnalyzer.cs、GameSystemExecutorEmitter.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorEmissionTests.cs、
    GameSystemGeneratorEndToEndTests.cs
- Observed behavior:
  - `ref X`→Write、`in X`→Read、フィールド0のタグ型はQuery条件のみで列を持たない、という
    推論規則が実際に生成されるコードとテキストレベル・実行時レベルの両方で一致することを
    確認した。生成された`Execute`メソッドの行ループ内に`typeof(`/`Has<`/`Dictionary`等が
    一切現れないことをテストでassertしている。
- Intended behavior:
  - 依頼文の「処理メソッドの引数からQuery条件とRead/Writeアクセスを推論する」
    「ホットループ内ではComponent有無の判定を行わない」、
    Docs/Specifications/Ecs-Soa-Systems.md「推論規則」節と同一。
- Result: Matches

### RE-L7-010 — `GameSystemScheduler`: Write→Read依存順序と循環時のフォールバック

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.CodeAnalysis/GameSystemScheduler.cs、GameSystemLoopEmitter.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorEmissionTests.cs、
    GameSystemGeneratorDiagnosticsTests.cs
- Observed behavior:
  - Write側メソッドが宣言順・クラス名Ordinal順では後であっても、Read側より先に実行される
    ことをテストで確認した(依存関係が宣言順を上書きする実例)。循環依存を持つ2メソッド対で
    SYSGEN019が報告されつつ、`GeneratedGameLoop.g.cs`自体はbaseline順で出力され続けることも
    確認した。
- Intended behavior:
  - 依頼文の「各処理メソッドを独立した実行単位として扱う」、ユーザー回答「実行順序の決定のみ
    (単一スレッド)」、Docs/Specifications/Ecs-Soa-Systems.md「実行順序」節と同一。
- Result: Matches

### RE-L7-011 — `GameSystemGenerator`診断(SYSGEN001〜020)

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.CodeAnalysis/GameSystemDiagnostics.cs、AnalyzerReleases.Unshipped.md
  - tests/CSharpGameEngine.CodeAnalysis.Tests/GameSystemGeneratorDiagnosticsTests.cs
- Observed behavior:
  - `SYSGEN001`〜`020`の20件それぞれが対象コードで報告され、正しい`[GameSystem]`クラスでは
    いずれも報告されないことを確認した。診断が1件でもあるクラスは、そのクラス分のExecutor
    生成が完全にスキップされる。
- Intended behavior:
  - Docs/Specifications/Ecs-Soa-Systems.md「診断一覧」節と同一。
- Result: Matches

### RE-L9-005 — UserGameProjectの`GameSystemGenerator`移行後の実機描画・移動

- Confidence: High
- Evidence:
  - UserGameProject/Components.cs、Systems/*.cs、Program.cs
  - tests/UserGameProject.Tests/PlayerControlSystemTests.cs(新API向けに書き換え)
- Observed behavior:
  - `dotnet run --project UserGameProject`を実行し、シミュレートしたキー入力(W前進)で
    プレイヤーが実際に移動した後のスクリーンショットで、空・タイル張りの床・PBRシェーディング
    された影付きプロップが破綻なく描画されることを確認した。マウスルック自体は、リモート
    スクリーンショット取得ツール(`SetForegroundWindow`)がフォーカス喪失後の再取得時に
    `Win32Window`の`ClipCursor`/`GetCursorPos`へ大きな一回限りのデルタを与えてしまう
    (このセッションで新たに発見した、ECS移行とは無関係な既存の入力バグ)ため、この確認では
    `SetMouseCaptured(true)`を一時的にコメントアウトして検証し、確認後に元へ戻した。
  - (2026-08-05追記)上記のバグ自体を`Win32Window.cs`で修正した(F-L0-004参照)。修正後、
    同一の`SetForegroundWindow`連打シナリオを再現し、以前は毎回Pitchが極端な角度まで飛んで
    いたのに対し、修正後は2回連続で水平な視点のまま安定することをスクリーンショットで確認した。
- Intended behavior:
  - Docs/Decisions/ADR-0029-Dynamic-Archetype-Soa-Ecs.md「UserGameProjectの移行」節と同一。
- Result: Matches(マウスルックの自動検証も含め、既知の乖離は無い)

### RE-L3-011 — Scene Authoring基盤型(`Parent`/`SceneNodeTag`/`SceneNodeId`)

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.Entities/Parent.cs、SceneNodeTag.cs、SceneNodeId.cs
  - tests/CSharpGameEngine.Entities.Tests/SceneAuthoringSpec.cs
- Observed behavior:
  - `Parent`/`SceneNodeTag`は他Componentと同じ`World.CreateArchetype<T1..T8>`+`GetRef<T>`経路で
    問題なく扱えることを単体テストで確認した。`SceneNodeId`/`SceneNodeSymbol`は値ベースの
    `record struct`として期待どおり等価性比較できる。
- Intended behavior:
  - Docs/Decisions/ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth.md「決定事項4・8」と同一。
- Result: Matches

### RE-L7-012 — `SceneDefinitionGenerator`: Createの構文的レシーバー検証とRuntime生成

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.CodeAnalysis/SceneDefinitionAnalyzer.cs、SceneRuntimeEmitter.cs
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDefinitionGeneratorEndToEndTests.cs、
    SceneDefinitionGeneratorEmissionTests.cs
- Observed behavior:
  - `CompileAndLoad`で実際にコンパイル・実行し、`CreateRuntime`が構築した`World`上でRoot→
    MainCamera/Playerの`Parent`関係とComponent値が、`Define`本体に書いたとおりであることを
    確認した。生成コードのテキストが2回の独立実行で完全に一致すること(決定性)も確認した。
- Intended behavior:
  - 依頼文の「Reflection・ラムダ式の解析・SceneBuilderの動的解釈を行わない」、
    Docs/Decisions/ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth.md「決定事項5・7」と同一。
- Result: Matches

### RE-L7-013 — `SceneDefinitionGenerator`診断(SCENE001〜009)と診断ゲート

- Confidence: High
- Evidence:
  - src/CSharpGameEngine.CodeAnalysis/SceneDefinitionDiagnostics.cs、AnalyzerReleases.Unshipped.md
  - tests/CSharpGameEngine.CodeAnalysis.Tests/SceneDefinitionGeneratorDiagnosticsTests.cs
- Observed behavior:
  - `SCENE001`〜`009`の9件それぞれが対象コードで報告され、正しい`[SceneDefinition]`クラスでは
    いずれも報告されないことを確認した。診断が1件でもあるクラスは、そのクラス分の
    SceneNodeSymbol実装/CreateRuntime生成が完全にスキップされる。
- Intended behavior:
  - Docs/Decisions/ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth.md「決定事項6」と同一。
- Result: Matches

### RE-L9-006 — `samples/SceneAuthoringDemo`の実行時Entity階層構築

- Confidence: High
- Evidence:
  - samples/SceneAuthoringDemo/MainScene.cs、DemoScenario.cs
  - samples/SceneAuthoringDemo/Generated/.../SceneAuthoringDemo.MainScene.SceneRuntime.g.cs
    (`EmitCompilerGeneratedFiles`により実ファイルとして確認可能)
- Observed behavior:
  - `dotnet run --project samples/SceneAuthoringDemo`を実行し"OK"を確認した。生成された
    `SceneRuntime.g.cs`を直接読み、Rootが`SceneNodeTag`のみのArchetypeで構築され、MainCamera/
    Playerがそれぞれ`Position`/`Rotation`/`Scale`/固有Component+`Parent`のArchetypeで構築され、
    `Parent`の初期化式が`Value = Root`(親のローカル変数をそのまま参照)になっていることを
    目視でも確認した。
- Intended behavior:
  - `samples/SoaSystemsDemo/シーン構築見本.cs`のRoot/MainCamera/Player構成
    (`LocalTransform`ではなく`Position`/`Rotation`/`Scale`の3分割Setに置き換えた点を除く)と同一。
- Result: Matches

### RE-L7-014 — `CSharpGameEngine.SceneAuthoring.Cli`: 実行ベースの汎用World走査

- Confidence: High
- Evidence:
  - tools/CSharpGameEngine.SceneAuthoring.Cli/SceneRuntimeInvoker.cs、WorldInspector.cs
  - tests/CSharpGameEngine.SceneAuthoring.Cli.Tests/SceneRuntimeInvokerTests.cs、
    WorldInspectorTests.cs
- Observed behavior:
  - `samples/SceneAuthoringDemo`の実ビルド出力に対し、`SceneRuntimeInvoker.Invoke`で
    `CreateRuntime`を実際にReflection実行し、`WorldInspector.Inspect`で得たノード値
    (Root/MainCamera/Player、`MainCamera.Position == [0,2,-10]`等)が`MainScene.cs`のソースと
    完全一致することをテストと`dotnet run`による手動実行の両方で確認した。
    `UserGameProject/Scene1.cs`(`net10.0-windows10.0.19041.0`+`win-x64`という非標準TFM)に
    対しても同様に実行し、6ノード全ての階層・値が一致することを確認した。
- Intended behavior:
  - Docs/Decisions/ADR-0031-Scene-Definition-3D-Preview.md「決定事項3」と同一
    (テキスト解析ではなく実行して値を得る、という設計判断どおり)。
- Result: Matches

### RE-L8-009 — Scene Definition 3Dプレビュー: 既存シーンプレビュー基盤の再利用とギズモフォールバック

- Confidence: High
- Evidence:
  - tools/CSharpGameEngine.VSExtension/PreviewSceneDefinitionCommand.cs、SceneHwndHost.cs、
    GizmoMeshFactory.cs
  - tests/CSharpGameEngine.VSExtension.Tests/GizmoMeshFactoryTests.cs、
    ExternalScenePreviewLoaderTests.cs
- Observed behavior:
  - `ExternalScenePreviewLoaderTests`が実際にブリッジサブプロセスを起動し、
    `samples/SceneAuthoringDemo`向けの応答が正しいことを(VSを起動せずに)確認した。
    `GizmoMeshFactoryTests`で生成されるプレースホルダー立方体が24頂点/36インデックス・
    単位法線・非退化三角形であることを確認した。`.vsct`のビルドで`IDM_VS_CTXT_CODEWIN`/
    `DynamicVisibility`とも初回でエラー無く解決されることを確認した。
- Intended behavior:
  - Docs/Decisions/ADR-0031-Scene-Definition-3D-Preview.md「決定事項1・4・5」と同一。
- Result: Matches(実機VS Experimental Instanceでの目視確認はユーザー側の確認待ち)

### RE-L9-007 — `UserGameProject/Scene1.cs`の実行時Entity階層構築

- Confidence: High
- Evidence:
  - UserGameProject/Scene1.cs
  - UserGameProject/Generated/.../UserGameProject.Scene1.SceneRuntime.g.cs
    (`EmitCompilerGeneratedFiles`により実ファイルとして確認可能)
- Observed behavior:
  - 生成された`Scene1.SceneRuntime.g.cs`を直接読み、Root→MainCamera/Player/Lighting、
    Lighting→Sun/Ambientという3階層6ノードの`CreateArchetype`型引数・`GetRef`代入・`Parent`の
    自動付与がDefine本体のとおりであることを確認した。ブリッジCLIを実際のビルド済みdllに対して
    実行し、応答JSONが同じ階層・値と完全一致することも確認した(RE-L7-014参照)。
- Intended behavior:
  - `UserGameProject/Components.cs`の既存型構成、Docs/Decisions/ADR-0031-Scene-Definition-3D-Preview.md
    と同一。
- Result: Matches

## 2026-08 Runtime Component/System/Resource expansion addendum

- `UserGameProject` now exercises reusable ECS data for shared mesh/material handles, world-transform
  hierarchy, camera, directional/point/spot lights, motion, input, and 3D audio sources/listener.
- `StageRenderer` now has a real directional shadow pass and an HDR offscreen target. Imported
  emissive factors feed the same HDR path; the fullscreen post-process performs a thresholded
  spatial 3x3 bloom blur followed by ACES tone mapping.
- Evidence: `dotnet test tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj -c Debug
  --filter FullyQualifiedName~StageGpuIntegrationTests` (4/4), the post-process shader compile test,
  and the UserGame/Core test suites listed in `Docs/Implementation-Status.md`.
- The old `UserEcsProject` audio sample remains intentionally unchanged and still has its historical
  single-entity/2D limitation; the new `UserGameProject/AudioRuntime` is the practical 3D path.

## 2026-08 StageRenderer FrameCB frame-slot sizing fix

- Mismatch → now Matches: `585abf6` added a `ShadowViewProjection` field to `FrameCB`, growing it to
  560 bytes, but left the double-buffered per-frame-slot stride at the unrelated
  `ConstantAlignment=512`. `SetDirectionalLight`'s per-frame write (`UserEcsProject/Systems/RuntimeSystem.cs`)
  therefore corrupted/overran the adjacent frame slot every other frame — the "ぐちゃぐちゃな表示"
  (scrambled display) `585abf6`'s own commit message self-reported. Independently, `LevelLoad` called
  `Renderer.SetCamera` only once at load instead of every frame, so one of the two frame slots stayed
  pinned to the constructor-time default camera instead of the scene camera.
- Fix: `StageRenderer.FrameConstantsStride` is now derived from `FrameConstantsSize` (rounded up to
  the 256-byte root CBV alignment) instead of reusing `ConstantAlignment`, so future `FrameCB` growth
  keeps the slot width in sync automatically; `LevelLoad` now calls `SetCamera` every frame like the
  sibling light systems; the shadow pass got its own `VSShadowMain` entry point instead of reusing
  `VSMain` (removing an incidental out-of-bounds constant read for `FrameIndex=1`, latent but not
  visible).
- Evidence: 2 new tests in `StageGpuIntegrationTests.cs`
  (`Stage_Warp_BothFrameInFlightSlots_RenderIdenticalImagesForAnUnchangingScene`,
  `Stage_Warp_BlockerCastsAVisibleShadowOnTheGroundPlane`, 6/6 total), full-solution build (0
  warnings/errors), and a live `UserEcsProject.exe` launch captured via `PrintWindow` across multiple
  frames confirming a single stable scene with no per-frame alternation.

## 2026-08 `[StartupScene]` multiple-scene Bootstrap.Done collision fix (ADR-0040 Phase 6)

- Mismatch → now Matches: discovered while implementing `PrimitiveMesh`/`RenderServer` (F-L4-002).
  `SceneStartupSystemEmitter.EmitHiddenSystem` generated every `{ClassName}__StartupSystem` to read
  and write the single shared `Bootstrap.Done` field. With more than one `[StartupScene]` class, the
  first one to run in a frame set `Done=true`, so every subsequently-chained `__StartupSystem` for
  that same frame saw `Done` already true and skipped its own `InstantiateScene` call forever - only
  the first-declared `[StartupScene]` (`MainScene`, `Order=0`) ever actually instantiated. A
  standalone repro (two showcase primitives scaled to 6x and floated above the room) rendered nothing
  until fixed, and rendered clearly once fixed - see the screenshots from that session.
- Fix: each generated `__StartupSystem` now guards its one-time `InstantiateScene` call with a
  private instance field (`Done`) instead of the shared ECS `Bootstrap.Done` field, relying on the
  same "System struct instances are created once per `Run()` and persist across all frames" guarantee
  `LevelLoad`'s own accumulator overload (F-L4-002) depends on.
- Evidence: new regression test
  `SceneDslGeneratorEndToEndTests.MultipleStartupScenes_BothInstantiateIndependently_ThroughTheGeneratedRunFrameChain`
  drives the actual generated `RunFrame` chain with two `[StartupScene]` classes and asserts both
  produce an entity (previously only one did); full `CSharpGameEngine.CodeAnalysis.Tests` suite
  (100/100); live `UserEcsProject.exe` re-verified with both a Box and a Sphere primitive visible.

## 2026-08 シーンエディタ設計文書整備(ADR-0040改稿・ADR-0041/M0019/M0020新規・EditorSpec削除・文字化け修復)

- Mismatch → now Matches: `Docs/Decisions/ADR-0040-...md`(初版41行)とVSIX/Runtime/テストの
  計12コードファイルは、日本語コメント・エラーメッセージ文字列が全て literal `?` に化けた状態で
  コミットされていた(`394ff04`のblob時点で破損。`git show 394ff04:<file>`で確認、履歴からの
  復元は不能)。ADR-0040は日本語で全面改稿し(当初「Phase 11で文書化」と先送りしていた正式文書化)、
  12ファイルのコメント・メッセージ文字列はコード理解に基づいて書き直した(ロジック行は無変更)。
- ObsoleteSpecification removed: `Docs/EditorSpec.md`(`.scene` JSON前提の目標仕様書。ギズモ・
  Undo/Redo・複数選択をスコープ外と宣言)は`Docs/シーンエディタの仕様.md`と正面矛盾するため削除した。
  生き残り項目(C#スクリプトアタッチ / Play WYSIWYG / Inspector本体)は
  `Docs/Milestones/M0020-Scene-Editor-Interaction.md`の「意図的に除外したこと」へ移設して保全。
  歴史的ADR(0015/0020/0021/0022/0030)内のEditorSpec参照は当時の記録として温存し、
  living docs(Implementation-Status/Architecture)側の参照には削除注記を付けた。
- 新規文書: `ADR-0041-Scene-Editor-Interaction-Architecture.md`(インタラクション基盤の意思決定、
  全項目Planned)、`Docs/Specifications/Scene-Editor-Design.md`(クラスレベル詳細設計)、
  `Docs/Milestones/M0019-CSharp-Scene-DSL.md`(Phase 1〜9回顧録)、
  `Docs/Milestones/M0020-Scene-Editor-Interaction.md`(Phase 0〜7前方計画)。台帳へF-L8-011
  (Planned)を追加。
- 既知の実装バグの文書化(修正はM0020 Phase 0): `SceneModelRenderObjectBuilder.cs`のTransform型
  ハードコード(`global::UserEcsProject.Transform`)により、実シーン
  (`global::CSharpGameEngine.Runtime.Windows.Transform`)が全ノードIdentityで原点に固まって
  描画される。単体テストは`namespace UserEcsProject`のフィクスチャを自前宣言しているため
  グリーンのまま実バグを検出しない。あわせて階層Transform合成の未実装も記録した。
- Evidence: `grep -rE '\?{4,}'`が対象拡張子で0件、`dotnet build CSharpGameEngine.slnx -c Debug`
  成功・`dotnet test`全件成功(文書整備・コメント修復の前後で不変)。

## 2026-08 M0020 Phase 0: 実バグ2件の修正と設計時の事実誤り3件の訂正

- **Mismatch → now Matches(実装バグ)**: `SceneModelRenderObjectBuilder`がTransform型を
  `global::UserEcsProject.Transform`でハードコードしていたため、実シーンの
  `global::CSharpGameEngine.Runtime.Windows.Transform`が認識されず、全ノードがIdentity・原点に
  重なって描画されていた。**ユニットテストは`namespace UserEcsProject`のフィクスチャを自前宣言して
  ハードコードに合わせていたため、グリーンを維持したまま実バグを隠していた**。新規
  `SceneComponentBindings`表へ置換し、フィクスチャを実FQNへ移した(この2つは同一編集で行う必要が
  ある — 片方だけ直すと別の2テストが落ちる)。退役FQNは意図的に登録しない(両方登録すると
  「直ったかどうか」が判定不能になる)。
- **Mismatch → now Matches(未実装)**: 階層Transform合成が無く、各ノードのローカルTRSをワールドと
  して使っていた。`world = local * parentWorld`はランタイム側の`TransformMath.ToWorld`/
  `TransformWorldResolver`/`TransformWorldResolverTests`が既に固定している規約であり、エディタは
  新しい規約を導入していない(net472/net10.0のTFM壁のため型ではなく式のみを複製)。ただし
  **入れ子`Children`を持つシーンがリポジトリに1つも存在しないため、実シーンでの証跡は無く、
  ユニットテスト4件のみが証跡**である。
- **ObsoleteSpecification removed(設計時の事実誤り3件)**:
  1. ADR-0041 決定事項1 は`src/CSharpGameEngine.Windows/Graphics/Camera.cs`を「純
     `System.Numerics`・依存なし」と記述していたが誤り。実際は`[SceneFactory]`
     (`CSharpGameEngine.Authoring`)と`Spatial.Abstractions`/`Spatial.Unity`経由のRUF(いずれも
     net10.0)に依存する。net472移植では属性を落とし、方向計算を`CameraTests`が固定する閉形式へ
     インライン展開する必要がある(`MathF`もnet472に存在しない)。
  2. 同決定事項の「初期フレーミングは`CreateBoundsFit`相当を用い現行の見た目を維持する」は
     自己矛盾だった(`CreateBoundsFit`はAABB半対角・near 0.1・far D+4R、現行は真の境界球半径*3.0・
     near 0.05・far R*20)。FOV導出形`Radius/sin(FovY/2)*1.1`を採用しRadiusは真の境界球半径、
     near/farは現行値を維持する形で確定した(FovY=45°で2.874*R、旧3.0*Rと4%差)。この主張は
     `ComputeDistance_AtFortyFiveDegrees_StaysWithinFivePercentOfTheLegacyThreeRadiusFraming`が固定する。
  3. ADR-0041 決定事項2 は`WM_MOUSEWHEEL`を`RenderSurfaceWindow.WndProc`で受ける計画だったが、
     ホイールメッセージは**フォーカスを持つHWND宛**であり、`RenderSurfaceFocusPolicy`により
     Win32フォーカスを取らないサーフェスへは構造的に届かない。ホイールはWPFの
     `RenderHostGrid.PreviewMouseWheel`から取る。併せて座標単位の不一致(WndProc=物理ピクセル /
     WPF=DIP)を記録し、物理ピクセルを正本として`TransformToDevice`で変換すると定めた。
- **新たに見つけた実装バグ(テストが検出)**: `SceneCameraFraming`の退化半径で`far`(0.02)が
  `near`(0.05)を下回り`Matrix4x4.CreatePerspectiveFieldOfView`が`ArgumentOutOfRangeException`を
  投げていた。`far`に下限ガードを追加。実経路では`ComputeBounds`が半径を1fで床打ちするため到達
  しないが、退化入力テストがその境界を踏んだ。
- **受け入れ済み退行の清算**: ADR-0040 Phase 9で書き込み経路を失った`.scene`系
  (`SceneFile.cs`/`SceneNodeAdder.cs`)、シェイプパレット一式、ヒエラルキーのドラッグ&ドロップを、
  Phase 10を待たず全面削除した(台帳F-L8-006/007をRemoved、F-L8-002/003/010を更新)。
  プリミティブ生成ブリッジ(`tools/CSharpGameEngine.Primitive.Cli`)は描画側が使うため存続。
- **注記**: `SceneRenderer`の空オブジェクトフレーム許容は観測可能なバグの修正ではなく、
  Phase 3(グリッド/ギズモ)の前提整備である。`Count==0`はPhase 0の本番経路では到達不能。
- Evidence: `dotnet build CSharpGameEngine.slnx -c Debug` 成功・警告0/エラー0、
  `dotnet test` 622件成功・失敗0(610 − 9 + 21)。削除の取りこぼしgrepは0件。
  実機VS Experimental Instanceでの目視確認(10項目)は未実施。

## 2026-08 M0020 Phase 1: 実機検証で判明した3件の不具合(設計時の事実誤り1件・実装バグ2件)

Phase 1(ナビゲーション)実装完了後、実機のExperimental Instanceで動作確認したところ、Tab/Backspace/
Deleteの非回帰(最優先項目)は問題なかったが、以下3件が仕様と異なる挙動を示した。

- **ObsoleteSpecification removed(設計時の事実誤り)**: ADR-0041 決定事項2は「`WM_MOUSEWHEEL`は
  フォーカスを持つHWND宛にpostされ、Win32フォーカスを取らない`RenderSurfaceWindow`には構造的に
  届かない」という理解のもと、ホイールをWPF側`RenderHostGrid.PreviewMouseWheel`で受ける設計に
  していた(M0020 Phase 0時点)。**実機検証の結果、この`PreviewMouseWheel`はビューポート上で
  一度も発火しなかった**(同時にRMB Look/Fly・MMB Pan・Alt+LMB Orbit・WASDQEはすべて仕様どおり
  動作していたため、フォーカス自体の問題ではない)。VS自体がカーソル直下のHWNDへホイールを
  リダイレクトしていると見られ、他のマウスメッセージと同様にヒットテストで`RenderSurfaceWindow`の
  WndProcへ直接届く。`WM_MOUSEWHEEL`をWndProcで直接受ける実装へ戻し(`RenderSurfaceWindow.
  WheelInput`イベント新設)、機能しないWPF側`PreviewMouseWheel`購読は削除した。
- **Mismatch → now Matches(実装バグ、Look/Fly)**: `SceneViewState.TickFly`のPitch(上下視点)が
  既定(`InvertLookY=false`)で仕様と逆方向に動いていた。design doc §3の`Pitch += Δy * 感度 *
  (InvertLookY ? -1 : 1)`に対し、実装は共有関数`SceneViewCamera.ApplyLookDelta`内部で
  `Pitch -= Δy * ...`という逆符号の式になっていた。Orbit(Alt+LMB、`ApplyLookDelta`を共有)は
  実機で正しい方向だったため共有関数自体は変更せず、Fly/Look側の呼び出しでのみ`DeltaY`を反転して
  渡すよう修正した。
- **Mismatch → now Matches(実装バグ、Pan)**: `SceneViewState.ApplyPan`の左右(X)移動が既定で
  仕様と逆方向に動いていた(上下Yは正しかった)。カメラ右ベクトル方向の項の符号を反転して修正した。
- Evidence: `dotnet build CSharpGameEngine.slnx -c Debug` 成功・警告0/エラー0、`dotnet test`
  688件成功・失敗0(686 + `DecodeWheelDelta`用2件)。3件とも実機のExperimental Instanceでの
  目視確認により発見された(ユニットテストでは検出不能な種類の不具合 — ホイール未発火は実HWND間の
  メッセージルーティングの問題、符号反転2件は「値は変化するが向きが逆」という閉形式の数式だけでは
  仕様適合を保証できないクラスのバグ)。

## 2026-08 M0020: ヒエラルキーが常に空白だった不具合(internalプロパティへのWPFバインドの暗黙的失敗)

ヒエラルキーパネルが、`IScene`を実装した`.cs`ファイル(`FirstPersonStageScene.cs`)を開いても
ツリーを一切表示せず、完全な空白のままだった。同じ`SceneModel`を読むシーンデザイナー側は3D
プレビューを正常に描画できていたため、シーン解析・モデル構築は成功していた。

- **Mismatch → now Matches(実装バグ)**: `SceneNodeViewModel`の`DisplayName`・`ComponentSummary`・
  `Children`が`internal`で宣言されていた。**WPFのデータバインディングはpublicプロパティしか
  解決できず、`internal`へのバインドは例外も警告も出さず黙って失敗する。**
  `HierarchyToolWindowControl.xaml`のバインドは全5件で、成否がアクセシビリティと完全に一致して
  いた — `IsExpanded`/`IsSelected`は`public`だったため機能し(展開・選択の同期は動いていた)、
  残る3件は解決されなかった。結果、`HierarchicalDataTemplate`の2つの`TextBlock`が両方空文字に
  なって行の中身が消え、`ItemsSource={Binding Children}`も失敗して子ノードが1件も生成されず
  (展開用の三角も出ず)、パネル全体が空白に見えていた。3プロパティを`public`にして修正した。
  `Path`はXAMLからバインドせずコードからのみ使うため`internal`で据え置いた(型自体が`internal`
  であることは問題にならない — 匿名型がまさに「`internal`な型の`public`プロパティ」でバインドが
  成立する実例)。
- **なぜユニットテストで検出できなかったか**: `CSharpGameEngine.VSExtension.Tests`には
  `InternalsVisibleTo`が効いているため、テストコードからは`internal`プロパティが普通に読める。
  既存の`SceneNodeViewModelTests`は`DisplayName`/`ComponentSummary`/`Children`を網羅的に検証して
  全件成功しており(実Roslyn解析を使った66兄弟のフラットなシーンでの検証も含む)、「データ層は
  正常」という結論自体は正しかった。壊れていたのはリフレクション経由でアクセシビリティを尊重する
  WPFバインディング層であり、これは`InternalsVisibleTo`を持つ単体テストからは構造的に観測できない。
  再発防止として、バインド対象5件が`BindingFlags.Public|BindingFlags.Instance`で取得できることを
  固定する`PropertiesBoundFromXamlArePublicSoWpfCanResolveThem`を追加した(バグを再導入すると
  実際に失敗することを確認済み)。
- **撤回した誤診**: 調査中、`Populate`内で`ItemsSource`設定直後に**同期的に**読んだ
  `Tree.ActualHeight=0`/`ActualWidth=0`を根拠に、`GameEngineLayout.SetFramePos`の`cy`に0を
  渡していることがペインの高さを0にしていると誤診し、`cy=600`へ変更していた。WPFのレイアウト
  パスは遅延実行されるため、この同期読み出しは「前回のレイアウト時点の値」(ペインが未表示の
  時点の値)を見ていたにすぎない。ドッキング動作を根拠なく変える変更だったため、この修正は
  コミットせず`cy=0`へ戻した。
- Evidence: `dotnet build CSharpGameEngine.slnx -c Debug` 成功・警告0/エラー0、`dotnet test`
  712件成功・失敗0(707 + アクセシビリティ固定5件)。原因特定は実機ログではなく、
  「シーンデザイナーは描画できているのにヒエラルキーだけ空白」「`IScene`未実装時のメッセージ
  (`EmptyMessage`、バインドを使わず`VsBrushes`を直接指定した`TextBlock`)は正しく出る」という
  症状の差分と、XAML内の全バインド5件のアクセシビリティ照合によって行った。

## 2026-08 M0020: シーンデザイナーのコンボボックス未同期とコンテキストメニューの重複表示

上記の修正でヒエラルキーが表示できるようになった後、実機確認で続けて2件見つかった。どちらも
「表示されている状態」と「UIが示している状態」が食い違うという同じ性質の不具合。

- **Mismatch → now Matches(コンボボックスがPinに追従しない)**: コンテキストメニューから
  「シーン デザイナーで開く」で開いたとき、ビューポートは対象シーンを描画するのに
  `SceneSelector`(ComboBox)が空欄のまま、あるいは直前に選んだ別シーンのまま残っていた。
  `SelectedItem`を代入している箇所が`RefreshSceneCatalogAsync`の中の1箇所だけで、
  コンテキストメニュー経由の経路がそこを通らないことが原因。`SetTarget`は
  `SetSession`→`ShowSnapshot`と進むが、`SetSession`は`ReferenceEquals`で同一セッションだと
  早期returnするため(デザイナーが既に開いている場合が該当)カタログ再取得が走らず、
  `ShowSnapshot`はビューポートしか触らない。`Pin`が発火する`PinnedSnapshotChanged`の購読先
  `OnPinnedSnapshotChanged`もセレクタを更新していなかった。「ComboBoxの選択=Pinされている
  シーン」という不変条件を`TrySyncSelectorToPinnedScene`という単一の経路へ集約し、
  `OnPinnedSnapshotChanged`(Pinの全変化に追従)・`SetTarget`(カタログ未取得時のみ
  `RefreshSceneCatalog`へフォールバック)・`RefreshSceneCatalogAsync`の3箇所から呼ぶようにした。
  自分で入れた選択で`OnSceneSelectionChanged`が再発火してPin→選択→Pinと往復しないよう、既存の
  `UpdatingSelector`ガードをヘルパー内で使う。
- **Mismatch → now Matches(「シーン デザイナーで開く」が2つ並ぶ)**: コードエディタの右クリック
  メニューに同名項目が2つ表示され、2つ目は矢印付きでグレーアウトしていた。2つ目は複数Scene用の
  入れ子サブメニュー`OpenSceneDesignerSubmenuCommandId`(0x010A)で、`.vsct`で
  `DynamicVisibility`を宣言していたにもかかわらず**このIDへ`OleMenuCommand`を登録している
  コードが1行も存在しなかった**(リポジトリ全体を検索して0件)。このフラグは「パッケージが
  QueryStatusで可視/不可視を答える」ことを前提とした宣言にすぎないため、答える側がいないVSは
  常に表示し続ける。Scene1件のファイルでは`Direct`モードで直接ボタンが出る上に、子項目0件の
  サブメニューがグレーで居座っていた。サブメニュー自体を廃止し、動的項目
  (`OpenSceneDesignerDynamicItemStart`)をコンテキストメニュー直下へ平らに置くことで、重複が
  構造的に起こり得ないようにした。0x010A・0x1026・0x1025(宣言のみで未使用だった)は退役IDとして
  記録し再利用しない。サブメニューが無くなったことで`SceneDesignerMenuMode.Submenu`が実態と
  合わなくなるため`SceneList`へ改名し、平らに並ぶ項目が単体で読めるよう表示テキストを
  クラス名のみ(「BoxScene」)から「BoxScene をシーン デザイナーで開く」へ変更した
  (`SceneDesignerContextMenuPolicy.ItemText`)。
- **Mismatch → now Matches(開いていないシーンに✓が付く)**: 上記の平坦化後、実機で
  `PrimitiveShowcaseScene.cs`の`PrimitiveShowcaseBoxScene`上で右クリックすると同項目に✓が付いたが、
  シーンデザイナーが実際に表示していたのは別ファイルの`FirstPersonStageScene`だった。✓
  (`OLECMDF_LATCHED`)の判定基準が「右クリックしたキャレット位置のシーン」
  (`SceneDocumentMenuSnapshot.GetTargetSceneFullyQualifiedName`)であり、開いているかどうかと
  無関係だったため。VSのメニューで✓は「今その状態にある」ことを表す慣習なので誤読を招く。
  基準を`SceneEditorSessionService.PinnedSceneKey.ClassFullyQualifiedName`へ変更した。一覧の項目は
  右クリックしたファイル内のシーンだけなので、別ファイルのシーンを開いている場合はどの項目にも
  ✓が付かない(それが正しい挙動)。キャレット位置は項目テキストへ「（カーソル位置）」を付記して
  示す(`SceneDesignerContextMenuPolicy.ItemText`)。`SceneDesignerMenuSelectionPolicy`は
  `IsLatched`(Pin基準)と`IsAtCaret`(キャレット基準)へ明示的に分離し、2つの状態が再び混ざらない
  ようにした。
- **ObsoleteSpecification removed(太字はVSのメニューで表現できない)**: 当初の要望は「キャレット位置を
  太字で示す」だったが、**VSのメニュー項目に任意のFontWeightを与える手段は公式APIに存在しない**。
  `OleMenuCommand`が公開するのは`Checked`/`Enabled`/`Visible`/`Text`のみで、`OLECMDF`に太字を表す
  ビットは無く(`SUPPORTED`/`ENABLED`/`LATCHED`/`NINCHED`/`INVISIBLE`/`DEFHIDEONCTXTMENU`)、
  `.vsct`の`CommandFlag`にもフォント関連の指定は無い。`SceneDesignerMenuSelectionPolicy`の旧コメントも
  同じ結論に達しており、元々✓が太字の代用だった。今回✓を「開いているシーン」へ譲ったため、
  キャレット位置は文字列付記で代替している。**将来「太字にする」提案が出た場合、この制約により
  実装不能である**ことをここに記録する(独自のWPFポップアップでメニューを自作する以外に手段が無く、
  得られるものに対して不相応)。
- **Mismatch → now Matches(1番目を押すと2番目のシーンが開く)**: 平坦化後の実機確認で、
  `PrimitiveShowcaseScene.cs`の**1番目**の項目(`PrimitiveShowcaseBoxScene`)をクリックしたのに
  `PrimitiveShowcaseSphereScene`が開いた。**これは平坦化で入った不具合ではなく、動的項目のinvoke経路に
  以前から存在していた**(サブメニュー時代も同じ構造で、複数Sceneの経路が実機で試されたのが初だった
  ため露出した)。原因は`DynamicSceneMenuItemMatcher.GetItemIndex`がindex 0(先頭)を
  `MatchedCommandId==0`で表しており、**この0が「新しい情報が無い」状態と同じ値**だったこと。
  `OleMenuCommandService.FindCommand`はroot ID(`0x0110`)を辞書の完全一致で解決するため
  **先頭項目のクリックでは`DynamicItemMatch`が呼ばれず**(非rootの`0x0111`以降だけがフォールバックで
  それを通る)、`MatchedCommandId`は直前のクエリが残した値のままになる。VSがメニュー操作中に2番目の
  `QueryStatus`を呼んだ後に1番目をクリックすると、残っていた`0x0111`でindex 1が解決された。
  修正は**`DynamicItemStart`(可変長)そのものを捨て、シーン項目を固有CommandIDを持つスロットとして
  `.vsct`に固定で並べる**こと(`0x0110`〜`0x012F`の32スロット + 溢れ表示`0x0130`)。index は
  コマンド登録時のクロージャで固定され、共有される可変状態から導出しないため、誘導ミスが原理的に
  起こり得ない。`DynamicSceneMenuCommand`/`DynamicSceneMenuItemMatcher`は`DynamicItemStart`契約に
  固有の型なので削除した。
- **先に試して実機で不成立だった案(再試行を防ぐため記録)**: 「rootを実項目から外して常に非表示の
  アンカーにし、シーンを`root+1`以降へ置く」— 表示される全項目が非rootになりクリック時に必ず
  `DynamicItemMatch`を通るため、上限なしで曖昧さも消えるはずだった。**しかし実機では
  シーン項目が1つも表示されなくなった**: VSはroot(先頭)の`QueryStatus`が不可視を返した時点で
  動的リストの列挙を打ち切る。したがって`DynamicItemStart`ではrootを実項目に使わざるを得ず、
  root が `DynamicItemMatch` を通らない以上この経路の曖昧さは解消できない。
- **検討して却下した案(再提案を防ぐため記録)**: 「クエリ末尾で`MatchedCommandId`を0へリセットする」
  (Microsoft の Dynamic Menu サンプル準拠)は、`FindCommand`→`Invoke`の間に`QueryStatus`が
  入らないというVSの内部挙動への依存が残り、破れた場合は症状が「先頭以外を押すと先頭が開く」へ
  **反転するだけ**になるため採らなかった。
- **32件上限とその扱い**: 固定ID方式の代償は表示件数の上限である。**この上限はサブメニューでは
  回避できない** — リーフ1項目につきCommandIDが1つ必要で、入れ子は見た目のグルーピングにすぎない。
  同じリーフIDを複数のサブメニューで使い回せばIDは節約できるが、「今どのチャンクか」を共有可変状態で
  持つことになり今回と同種の曖昧さが戻る。そのため上限は残し、**33件目以降が存在する場合は
  溢れ表示(`0x0130`、押せない情報行)が「他 N 件はシーン デザイナーのコンボボックスから選択して
  ください」と出る**ようにして、「存在するのに表示されない」状態が不可視にならないようにした。
  コンボボックスはプロジェクト内の全シーンを列挙する完全な経路なので機能的な行き止まりにはならない。
- Evidence: `dotnet build CSharpGameEngine.slnx -c Debug` 成功・警告0/エラー0、`dotnet test`
  717件成功・失敗0(712 + `ItemText`2件 + ✓/キャレット分離3件 + スロット可視/溢れ4件 + ID整合2件、
  旧`IsLatched`1件を書き換え、旧DynamicItemStart 4件を削除)。`.vsct`のスロット32個は手書きの
  typoを避けるためスクリプトで生成し、`VSCTCompile`がビルドに含まれるためID定義の不整合はビルド
  失敗として検出される。C#側の定数と`.vsct`のID範囲がずれる事故(スロット数だけ変えて溢れ表示のIDが
  スロットと衝突する等)は`SceneDesignerCommandIdsTests`で固定した。
  なお`MatchedCommandId`の**ライフサイクル**が壊れていた点は重要で、`DynamicSceneMenuItemMatcher`
  自体は純粋関数として旧契約どおり正しく既存4テストも全て通っていた — 共有フィールドの寿命は
  matcher単体のテストでは原理的に捉えられない層にある(ヒエラルキーの`internal`バインドと同じ
  「テストが構造的に見られない層」の事例)。
  `.vsct`は`VSCTCompile`がビルドに含まれるため、存在しないIDSymbolを参照していればビルドが失敗する
  形で検証されている。ComboBox同期・メニュー構造・✓の実描画はいずれもVS SDK/WPFへの依存が深く
  ユニットテスト対象外(Phase 1/2と同じ理由)なので、純粋な表示ポリシー
  (`SceneDesignerContextMenuPolicy`/`SceneDesignerMenuSelectionPolicy`)側をテストで固定している。
  特に「開いているシーンとキャレット位置のシーンが別なら✓と付記は別々の項目に付く」という今回の
  不具合そのものは`OpenSceneAndCaretScene_AreTrackedIndependently`で固定した。
