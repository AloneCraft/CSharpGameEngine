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
