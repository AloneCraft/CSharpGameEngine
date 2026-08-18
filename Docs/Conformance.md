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

## 2026-08 M0020 Phase 2 実機検証: A/Dの左右・複数選択の不可視・プリエンプト不発(誤診と差し戻しの記録つき)

Phase 2の実機確認10項目のうち#5/#8/#9で問題が報告され、3件すべてを修正した。**うち#8では調査の
過程で誤った根本原因に飛びつき、実装してから差し戻している。**同じ誤診を繰り返さないため経緯を残す。

- **Mismatch → now Matches(RMBナビ中のA/Dが左右逆、実機検証#8)**: `SceneViewState.TickFly`の
  A/Dが`D→+FlatRight`だったため画面上で左右逆に動いていた。`D→-FlatRight`へ訂正した。
  理由は**描画が右手系の行列を使うため`SceneViewCamera.GetFlatRight`が名前に反して画面の「左」へ
  写る**こと。M0020 Phase 1で`ApplyPan`のXに入れた符号反転もこれと同一の理由であり、
  対症療法ではなくエンジンの規約に対する正しい変換だった。
- **ObsoleteSpecification removed(誤診: 「左右反転はバグ」と判断して行列を左手系へ変えたが差し戻した)**:
  基底が左手系(`GetFlatForward=(sinY,0,cosY)`、ADR-0025のUnity規約=LeftHanded)なのに行列が右手系
  (`Matrix4x4.CreateLookAt`は`xaxis=Cross(Up,normalize(Eye-Target))=-GetFlatRight`を作る)という
  食い違いを見つけ、「これが唯一の根本原因で、直せばA/DもPanも反転が不要になる」と結論して
  View/Projection/直交投影を左手系で明示構築した。修正前に失敗するテストまで書いて反転の存在を
  証明し(`右方向がNDC X=-0.244065`)、テストは全て通った。**しかしランタイム側を確認していなかった。**
  実機で見た目が変わったとの指摘を受けて確認したところ、**ゲーム本体も同じ右手系**
  (`src/CSharpGameEngine.Windows/Graphics/Camera.cs`の`BuildViewMatrix`/`BuildProjection`、
  `StageRenderer`、`ModelRenderer`)であり、修正前のVSIXの描画はゲームと一致していた。
  つまり左手系化はシーンデザイナーだけをゲームと左右反転させる改悪だったため差し戻した。
  **判断基準は「シーンデザイナーはゲームの見た目を再現するものなので、ゲームとの一致が正しさ」**。
  再発防止として、反転の存在を証明していたテストを反転の**維持**を固定するテストへ書き換えた
  (`GetFlatRight_ProjectsToScreenLeft_BecauseTheEngineUsesRightHandedMatrices`、
  `ViewAndProjection_MatchTheRuntimeGameConstruction`)。`GetView`にも「ここを左手系へ直す誘惑への警告」
  として経緯をコメントで残した。
  **教訓: 描画規約の「正しさ」は単体の整合性では決まらない。参照先(この場合ゲーム本体)を先に確認する。**
- **残る本質的な矛盾(別ADR案件)**: 「ADR-0025がUnity=左手系と宣言しているのに行列は右手系」という
  エンジン全体の不整合は実在する。ただしゲームの描画にも等しく効いているためシーンエディタ単独では
  決着できない(直すならインポータの軸変換や既存シーンの見た目にも波及する)。**Phase 3のMoveギズモは
  +Xハンドルが画面左を指すことになるため影響を受ける**ので、その時点で別ADRとして扱う。
- **Mismatch → now Matches(複数選択に見た目が無かった、実機検証#5)**: データ層は正しく、
  `SceneSelection.NodeIds`のトグルは実装済みだった。壊れていたのは表示側で、
  `SceneHwndHost.SetSelection`が`PrimaryNodeId`1件だけから`SelectedNodePath`を決めて
  `Renderer.SetSelectedNode(単一)`へ渡し、`NodeIds`はFキーの境界球にしか使っていなかった
  (「ハイライトは単一のまま」と意図的に保留するコメントが残っていた)。シェーダは
  `shaded + Selected * SelectionTint`と加算しているだけなので、`Selected`へ
  primary=1.0/副選択=0.5/非選択=0.0を入れれば**シェーダ無改造で濃淡が付く**。判断は
  `SceneSelectionTint.GetWeight`へ切り出してテストで固定した(GPU側の定数バッファ更新はテスト不能)。
  再解析でRendererを作り直す経路にも複数選択を渡すよう直した(片方だけだと副選択のハイライトが
  再ビルドで消える)。
- **Mismatch → now Matches(プリエンプトが不発、実機検証#9)**: 「LMB押下中にRMBを押しても
  ナビゲーションに切り替わらず視点が回らない」。原因は`RenderSurfaceWindow.SetPointerCapture`が
  無条件に`SetCapture`を呼んでいたこと。**Win32の`SetCapture`はキャプチャを失う側へ
  `WM_CAPTURECHANGED`を送るため、自分がキャプチャ中の再取得ではそれが自分自身へ届く**。
  `PendingSelect`で既にキャプチャしている状態からRMBで`Flying`へ遷移すると`Capture=Begin`で
  再取得が走り、届いた`WM_CAPTURECHANGED`が`CaptureLost`へ翻訳され、状態機械が「奪われた」と
  解釈して`Idle`へ戻っていた。通常のIdle→RMB経路では事前キャプチャが無くこのメッセージが出ない
  ため、RMBナビ単体は動いていた。自分が保持済みなら再取得しないガードを入れ、加えて翻訳器へ
  「キャプチャを取得する側のHWND」を渡して自己再取得なら`CaptureLost`を発生させないようにした
  (LParamがゼロ=完全解放は従来どおり`CaptureLost`。MMBでのプリエンプトも同じ経路なので同時に直る)。
- **意図的に残した制約**: ヒエラルキーは primary のみ表示のまま。WPFの`TreeView`は単一選択で、
  `SceneEditorHub.PublishSelectedNodePath`も単一`NodePath`契約のため、複数選択表示には`TreeView`の
  置き換えが必要。**ビューポートは複数選択を表示するがヒエラルキーは primary のみ**という非対称を
  M0020の残課題へ明記した。当時、矩形選択(LMBドラッグでの範囲選択)は仕様に無く
  (`シーンエディタの仕様.md`はCtrl+LMBのみ)、`Scene-Editor-Design.md`§154の
  「LMB Down→PendingSelect→Up確定」の変更を伴うため、仕様追記から始める別タスクとした。
  この別タスクは2026-08-16の後続Task Cで解消済み(本書末尾の同名節参照)。
- **(2026-08 解消)** 以下の「未解決の仕様矛盾」は、ユーザーの決定によりRMB Look(X/Y)を両方
  既定で反転する形で解消した。詳細は本節末尾の追記を参照。
- ~~**未解決の仕様矛盾(要判断)**: `シーンエディタの仕様.md`§4.1は「マウス上→上を見る」と書いているが、
  実装は「マウス上→下を見る」(オブジェクトが画面上方向へ動く)になっている。これはM0020 Phase 1で
  `Scene-Editor-Design.md`§3の式`Pitch += Δy * 感度`に合わせた結果で、**2つの文書が縦方向の既定で
  矛盾している**。実機検証ではユーザーが現在の挙動を「OK」と判断したため変更していない。
  `InvertLookY`設定が既に存在するので、既定値をどちらにするかの判断が残っている。~~
- Evidence: `dotnet build CSharpGameEngine.slnx -c Debug` 成功・警告0/エラー0、`dotnet test`
  728件成功・失敗0(717 + 描画規約2件 + 濃淡5件 + キャプチャ3件、うち左手系化を証明していた3件は
  右手系維持を固定する2件へ書き換え)。実機での確認(A/Dの向き・複数選択の濃淡・プリエンプト・
  ゲームと同じ見た目に戻っていること)はユーザー側。

### 追記(2026-08): RMB Look(X/Y)の既定を反転側へ一時変更 → 実機確認後に反転しない側へ確定

A/D・複数選択・キャプチャの3件の実機確認が取れた後、ユーザーから「RMBドラッグの左右・上下どちらの
視点角度移動も反転してほしい、それを既定値にしたい」との指示があり、`SceneEditorSettings`の
未保存時の既定値(`ReadBoolean`のフォールバック)を`false`→`true`に変更した。`InvertLookX`/
`InvertLookY`は既に`SceneViewState.TickFly`→`SceneViewCamera.ApplyLookDelta`まで配線済みだった
ため、既定値の変更だけで両軸の反転が有効になった(配線自体の変更は無し)。いずれオプション画面
から選ばせる前提で、既定値を`SceneEditorSettings`に`DefaultInvertLookX`/`DefaultInvertLookY`
という名前付き定数として定義した(現時点ではオプションUIは無く、この定数が唯一の設定箇所)。

その後ユーザーが実機で試した結果、**反転しない側(`false`)を最終的な既定として維持する**ことに
決め、この定数を手作業で`false`へ戻した(詳細と、それを私が誤ってバグと判断した経緯は本節末尾の
追記を参照)。上記の「未解決の仕様矛盾」(縦方向の既定)は、既定値をどちらにするかという判断が
明示的に下された時点で解消済みとみなす(挙動は元の`false`のままだが、意図的な選択の結果である
ことが明確になった)。

Evidence: `dotnet build CSharpGameEngine.slnx -c Debug` 成功・警告0/エラー0、`dotnet test`
737件成功・失敗0。既定値の変更→差し戻しに伴い、`ReadInvertLookX/Y_NoStoredValue_...`は
`false`を期待するテストへ、`WriteThenReadInvertLookX/Y_RoundTrips`は非既定値`true`の往復
テストへ、それぞれ最終的に戻した。

### 追記(2026-08): ソリューションエクスプローラーの右クリックにも「シーン デザイナーで開く」を追加

ユーザー要望「ソリューションエクスプローラの右クリックもシーンメニュー入れて」。従来はコード
エディタの右クリック(`IDM_VS_CTXT_CODEWIN`)にしか出ていなかった。

- 単に既存の`SceneDesignerContextMenuGroup`(34個のボタン: Direct/32スロット/Overflow)を
  `IDM_VS_CTXT_ITEMNODE`(ソリューションエクスプローラーの単一項目右クリック)にも
  `<CommandPlacement>`で配置するだけでは動かないことが判明した:
  `OpenSceneDesignerCommand.RefreshPresentation`は`ActiveDocumentTracker.CurrentFilePath`
  (=アクティブなコードエディタ文書)を起点にしており、ソリューションエクスプローラーで
  右クリックしたファイルはそれと一致しない(今開いていないファイルを右クリックするのがこの機能の
  本来の使い方)。加えて、各シーン名を列挙するには`SceneWorkspaceService`がそのファイルの
  解析済みスナップショットを持っている必要があるが、同サービスは**アクティブ文書1つ分**しか
  継続解析しておらず、`BeforeQueryStatus`はRoslyn待機禁止という既存ルールのためその場での解析も
  できない。
- ユーザーには「クリック時に解決する軽量案」と「事前解析してコードエディタと完全に同じ体験に
  する案(フル一致)」を提示し、**フル一致**を選択された。そのため`SceneWorkspaceService`に、
  アクティブ文書とは完全に独立した第2の解析スロット(`SolutionExplorerDocumentPath`/
  `SolutionExplorerMenuSnapshot`/専用の`SceneAnalysisGenerationGate`/専用のデバウンスタイマー)を
  追加した。`SetSolutionExplorerSelection`は`CurrentModel`/`AllModels`やそのイベントには一切
  触れない — これらはHierarchy専用であり、Hierarchy/Scene Designerはこれまで何度も壊れてきた
  箇所([[project_vsix_joinabletask_deadlock]]等)のため、既存のアクティブ文書側のコードは
  1行も変更せず新スロットを完全並行させることでリグレッションリスクを避けた。
- `ActiveDocumentTracker`はこれまで`OnSelectionChanged`(`IVsSelectionEvents`)を実装しておらず
  no-opだった。ここへ、階層選択(ソリューションエクスプローラー等)が変わるたびに選択ファイルパスを
  解決し`SceneWorkspaceService.SetSolutionExplorerSelection`へ転送する処理を実装した(単一選択のみ
  対応、`.cs`以外はnullを渡す)。
- 「今回の右クリックがソリューションエクスプローラー起点かコードエディタ起点か」は、ファイルパスの
  一致だけでは判定できない(コードエディタで別ファイルを編集中でも、SE側が以前選んでいたファイルの
  残骸が残りうるため)。`IVsMonitorSelection.GetCurrentElementValue(SEID_WindowFrame)`で取得した
  フォーカス中フレームの`VSFPROPID_GuidPersistenceSlot`が`ToolWindowGuids80.SolutionExplorer`
  (`GameEngineLayout.cs`等で既に実績のある比較方法)と一致するかで判定する
  (`ActiveDocumentTracker.TryGetSolutionExplorerInvocationFilePath`)。
- ソリューションエクスプローラー起点の呼び出しはキャレット位置の概念が無いため常に位置0を渡す。
  `SceneDocumentMenuSnapshot.GetTargetState`は位置0がどの型宣言にも属さない前提で自然に
  `OutsideType`へ落ち、ファイル内の全シーンを一覧表示する(既存ロジックの無改造での再利用)。
  ✓(開いているシーン)は従来通り`Session.PinnedSceneKey`基準のまま動作するが、
  「（カーソル位置）」表記は付かない(キャレットが存在しないため)。
- 開いた後にフォーカスを元へ戻す処理(`RestoreEditorFocus`)は、SE起点では対象の`IVsTextView`が
  そもそも存在しないため自然にスキップされ、フォーカスはソリューションエクスプローラーに残る
  (「フォーカスを奪わない」方針と一致、[[project_editor_spec_roadmap]])。
- **誤診の記録**: `SceneEditorSettings.DefaultInvertLookX`/`DefaultInvertLookY`が`false`
  (未コミットの作業ツリー)になっているのを見て「前段の追記で混入したバグ」と誤判定し、`true`へ
  上書きした。実際には**ユーザーが実機確認の結果を踏まえて手作業で`false`へ戻した意図的な値**
  だった([[feedback_live_file_editing]]が警告する「並行編集を誤ってバグと判断する」ケース)。
  ユーザーの指摘で`false`へ差し戻し、合わせてテスト(`ReadInvertLookX_NoStoredValue_ReturnsTheConfiguredDefault`
  等)と上の「RMB Look(X/Y)の既定を反転側へ変更」の記述も実態(最終的には反転しない側を維持)に
  合わせて訂正した。**変更前に必ず`git diff`で作業ツリーの現状を確認してから「バグ」と判断すべき
  だった。**
- ソリューションエクスプローラー右クリックの実機確認では、Scene実装クラスを2つ持つファイル
  (`PrimitiveShowcaseScene.cs`)で常に汎用的な単一項目しか出ない不具合も見つかった。原因は
  `SetSolutionExplorerSelection`がアクティブ文書側の`ScheduleAnalysis`と同じ250msデバウンスを
  流用していたため: ソリューションエクスプローラーの右クリックは「選択変更→即座に右クリックで
  メニュー表示」であり、`BeforeQueryStatus`が呼ばれる時点ではほぼ確実にデバウンス中の解析が
  終わっていなかった。デバウンスはタイピングの連打吸収が目的でありSE選択には無関係だったため、
  `SetSolutionExplorerSelection`から`System.Threading.Timer`を取り除き、選択変更を検知した
  直後に解析を開始するよう変更した(連打耐性は既存の`SceneAnalysisGenerationGate`+
  `CancellationTokenSource`のキャンセル機構がそのまま担保する)。
- **実機不具合(2026-08)**: 別ファイル(`FirstPersonStageScene.cs`)を右クリックしたのに、
  以前右クリックした`PrimitiveShowcaseScene.cs`のシーン一覧が表示され続けるバグが見つかった。
  原因は`ActiveDocumentTracker.OnSelectionChanged`が`IVsMultiItemSelect`を「非nullなら複数選択」と
  誤判定していたこと: `IVsMultiItemSelect`は複数選択に対応する階層(ソリューションエクスプローラー
  等)では**単一選択時にも非nullで渡されることがある**というVS SDKの既知の仕様があり、非null判定で
  弾いてしまうと`SolutionExplorerFilePath`が新しいファイルへ更新されないまま、直前に成功した
  単一選択の値に取り残される。`PrimitiveShowcaseScene.cs`単体のテストで正常に見えたのは、その時
  たまたま条件に該当しなかっただけで、バグ自体は実装当初から存在していた。参照のnullチェックでは
  なく`IVsMultiItemSelect.GetSelectionInfo`の実際の選択件数(`>1`)で判定するよう修正した。
- Evidence: `dotnet build CSharpGameEngine.slnx -c Debug` 成功・警告0/エラー0、
  `dotnet test CSharpGameEngine.slnx -c Debug` **784件成功・失敗0**(直前のピクセル完全一致
  ドラッグ追加分を含む)。`ActiveDocumentTracker`は実COM型に直結しユニットテスト不能なため、
  今回の修正も実機でのF5確認(`FirstPersonStageScene.cs`右クリックで正しい項目が出ること、
  別ファイルへの切り替えに追従すること)が必要。`SceneWorkspaceService`/
  `ActiveDocumentTracker`は実COM型(`VisualStudioWorkspace`/`IVsMonitorSelection`)に直結しており
  このテストプロジェクトはそれらをモックしていないため、今回追加した新スロット/選択判定ロジックの
  単体テストは無し(既存の設計判断を踏襲)。実機でのF5確認(アクティブでない`.cs`ファイルを
  ソリューションエクスプローラーで右クリックしてシーン名一覧が正しく出ること、複数シーンを持つ
  ファイルからの選択、フォルダ/非`.cs`項目では出ないこと)はユーザー側。

### 追記(2026-08): SE経路修正の最終記録 — `VSFPROPID_GuidPersistenceSlot`は常に失敗していた

上記`IVsMultiItemSelect`修正の後も、実機で新しい壊れ方が見つかった:
`FirstPersonStageScene.cs`自身がアクティブタブのときにその項目を右クリックすると正しいが、
**`PrimitiveShowcaseScene.cs`がアクティブタブの状態で`FirstPersonStageScene.cs`のファイル
ノードを右クリックすると、間違って(アクティブタブ側の)`PrimitiveShowcaseScene`のシーン一覧が
出た**。「右クリックしたノード自身のシーンが出るべき」という当然の期待に反する。

- 診断ログ(`System.IO.File.AppendAllText`、`ActivityLog`はクリーンシャットダウン時にしか
  flushされないため不使用)を`TryGetSolutionExplorerInvocationFilePath`に仕込んで実機再現した
  ところ、**このセッションで記録された全呼び出しで一度の例外もなく**
  `Frame.GetProperty((int)__VSFPROPID.VSFPROPID_GuidPersistenceSlot, out object GuidObj)`が
  `Hr=0x80020003`(`DISP_E_MEMBERNOTFOUND`)を返していた。つまりこの関数は**一度も`true`を
  返したことがなく**、`OpenSceneDesignerCommand.RefreshPresentation`はSE経路を一度も通らず
  常にコードエディタのアクティブ文書経路へフォールバックしていた。直前の追記で「実機確認済み」と
  書いた複数シーンファイルのテストも、実際にはSE経路ではなく**たまたまアクティブタブ自身を
  右クリックしていたためコードエディタ経路の結果が偶然正しく見えていただけ**だったと判明した。
- `VSFPROPID_GuidPersistenceSlot`によるフレーム識別は、少なくともこの環境の
  `IVsMonitorSelection.GetCurrentElementValue(SEID_WindowFrame)`が返すフレームに対しては
  機能しないAPIと判断し、別方式に置き換えた: `IVsUIShell.FindToolWindow(0, ref
  SolutionExplorerGuid, out Frame)`(`ToolWindowGuids80.SolutionExplorer`、組み込み・独自を
  問わずGUIDでツールウィンドウの`IVsWindowFrame`を取得できる標準API)でソリューション
  エクスプローラー自身のフレームを一度だけ取得してキャッシュし、`SEID_WindowFrame`で得た
  「現在アクティブなフレーム」との`ReferenceEquals`(in-process拡張機能では同一COMオブジェクトに
  対して同一RCWインスタンスが解決されるため確実に機能する)で判定する
  (`ActiveDocumentTracker.TryGetSolutionExplorerFrame`/`TryGetSolutionExplorerInvocationFilePath`)。
  プロパティ照会を経由しないため、今回失敗した経路とは無関係な、より単純で壊れにくい方式になった。
- 置き換え後も同じ診断ログを残したまま実機で再現してもらったところ、`FirstPersonStageScene.cs`
  ノードを`PrimitiveShowcaseScene.cs`アクティブタブ中に右クリックした際のログに
  `TryGetSolutionExplorerInvocationFilePath: MATCH,
  SolutionExplorerFilePath=...\firstpersonstagescene.cs`および`RefreshPresentation:
  SE経路を採用 FilePath=...\firstpersonstagescene.cs`が記録され、SE経路が正しく選択され
  意図通りのファイルへ解決されることを確認できた。確認後、診断ログ(`DiagLog`呼び出しおよび
  メソッド本体、`ActiveDocumentTracker.cs`/`OpenSceneDesignerCommand.cs`の両方)は削除した。
- Evidence: `dotnet build CSharpGameEngine.slnx -c Debug` 成功・警告0/エラー0、
  `dotnet test CSharpGameEngine.slnx -c Debug` **784件成功・失敗0**(診断コード削除のみで
  ロジック自体は無変更のためテスト件数・内容に変化なし)。`IVsUIShell.FindToolWindow`/
  `ReferenceEquals`ベースの判定も実COM型に直結するためユニットテスト不能であり、今回の確認は
  実機診断ログのみによる(上記の通り確定済み)。

### 追記(2026-08): 縦長ウィンドウのFOV補正と Tools>Options ページの新設

M0020の残作業のうち「設計判断が要らないもの」をまとめて実装した(判断が必要な件は別途1件ずつ確認)。

**1. アスペクト比 < 1(縦長ウィンドウ)のFOV補正**

- 残課題として記録されていた既知の逸脱を解消した。`SceneCameraFraming.ComputeDistance`は
  境界球が収まる接距離を**縦FOVだけ**から出していたため、水平FOV
  (`2*atan(tan(FovY/2)*aspect)`)が縦より狭くなる縦長ウィンドウで境界球が左右にはみ出していた。
- 新設した`ComputeEffectiveFovRadians(FovYRadians,AspectRatio)`が
  `min(FovY, 2*atan(tan(FovY/2)*aspect))`(縦と水平の狭い方)を返し、距離計算はこれを使う。
  **投影行列には従来どおり縦FOVを渡す**(実効FOVは距離決定にのみ使う)。
  aspect≥1では実効FOV=FovYなので、横長・正方形の距離はPhase 0以来1ビットも変わらない。
- `ComputeDistance`/`CreateView`/`SceneViewState.ResetToBounds`/`FocusOnBounds`に`AspectRatio`
  引数を追加した。呼び出し元の`SceneHwndHost`は既に`AspectRatio`プロパティを単一の供給源として
  持っていたため新しい配管は不要。Fキーのフォーカスも同じ問題を持っていたので併せて直った。
- **既存テストの盲点を実測で確認してから直した(赤→緑)**:
  `FrameBounds_KeepsTheWholeBoundingSphereInsideTheVerticalFieldOfView`は`±UnitX`もループして
  いるのに**NDCのYだけ**を検証しており、この破綻を素通りさせていた。縦横両方+複数アスペクト
  (4 / 16:9 / 1 / 0.75 / 0.5 / 0.25)を回す
  `FrameBounds_KeepsTheWholeBoundingSphereInsideBothFieldsOfView`へ拡張したところ、
  **aspect 0.75 という緩い縦長でも`NDC x=1.011734`と溢れて失敗**した(極端な縦長だけの問題では
  なかった)。同時に`ComputeDistance`が距離を一切変えていないこと(縦長・正方形とも14.37219)も
  確認できた。修正後は35件すべて成功。
- `ComputeDistance_WideAspect_MatchesTheSquareCaseExactly`でaspect≥1の完全一致を固定し、
  既存の`ComputeDistance`/`CreateView`テストには`AspectRatio:1f`を明示して「既存の期待値が
  変わっていない」ことを読み取れるようにした。

**2. Tools>Options の「C# Game Engine / Scene Editor」ページ(このVSIX初のオプションページ)**

- ADR-0006が将来の方向性として挙げ、ユーザーからも要望のあったオプション画面を追加した。
  `DialogPage`/`ProvideOptionPage`はこのリポジトリに1件も存在しなかったため新規の仕組みになる。
- **公開するのは7項目のうち3項目だけ**: `InvertLookX`/`InvertLookY`/`NavigationSpeedMultiplier`。
  `ActiveTool`/`TransformSpace`/`ProjectionMode`は「今どのツールを選んでいるか」という一時的な
  現在状態でありツールバー側の責務、`NavigationPreset`はenumが`Default`/`Custom`の2値しか無く
  プリセット値の充填がM0020スコープ外(ADR-0041「器のみ用意する」)で、**今公開すると何も
  起きないダミーUIになる**ため除外した。
- **設計上の必須要件だった上書き問題**: 7項目は`SceneViewState.CreateFromSettings`が
  `SceneHwndHost.BuildWindowCore`で1回だけ読み、さらに`DestroyWindowCore`が`SaveToSettings`で
  7項目すべてを書き戻す。そのためScene Viewを開いたままオプション画面で変更すると、ビュー破棄時に
  メモリ上の古い`ViewState`で**上書き消滅する**。`SceneEditorSettings.SettingsChanged`(staticイベント、
  `SceneEditorHub`の方式に倣う)を`SceneHwndHost`が購読して生きた`ViewState`を更新することで、
  即時反映と上書き解消を同時に達成した。`WriteNavigationSettings`は3項目を書いてから
  **イベントを1回だけ**発火する(1回の[OK]で購読側を3回叩かないため)。
- WPFで描くため`UIElementDialogPage`を使った。素の`DialogPage`はWinFormsの`PropertyGrid`描画に
  なり、`UseWPF=true`/`UseWindowsForms=false`のこのプロジェクトの方針と既存のWPF+XAML
  ツールウィンドウ群から外れる。`DialogPage`標準の永続化(`ApplicationPrivateSettings`)は
  `CSharpGameEngine\SceneEditor`とは別の場所なので、`LoadSettingsFromStorage`/
  `SaveSettingsToStorage`を上書きして`SceneEditorSettings`へ委譲し二重永続化を避けた。
- 速度倍率のクランプ範囲は`SceneViewState.MinNavigationSpeedMultiplier`/`Max...`(0.05〜20)を
  `internal`化して再利用した。オプション画面だけ別範囲を許すと、設定した値がRMB+Wheel操作の
  直後に勝手に変わることになるため。値の二重定義は作っていない。
- **ユニットテストできなかったもの(正直な記録)**: `SceneEditorOptionsPage`(`UIElementDialogPage`)と
  `SceneEditorOptionsControl.xaml`は実VSホストを要するためテスト不能
  (テストプロジェクトは`Package`/`DialogPage`系の型を読み込めない)。そのため検証・クランプ・
  書式は純粋クラス`SceneEditorOptionsModel`へ切り出し、そこを自動証跡にした。ページ本体は
  コントロールとモデルの詰め替えだけを行う薄い殻にしてある。実機での「Tools>Optionsに実際に
  ページが出て、値を変えると開いているScene Viewへ反映される」確認はユーザー側。

- Evidence: `dotnet build CSharpGameEngine.slnx -c Debug` 成功・**警告0/エラー0**、
  `dotnet test CSharpGameEngine.slnx -c Debug` **760件成功・失敗0**(737 + FOV補正6件 +
  オプション画面17件)。VSExtension.Testsは245→268件。
  なお全体テストの1回目で`TriangleGpuIntegrationTests.Triangle_Warp_DebugLayer_Verify_RendersAndPasses`が
  `CreateDXGIFactory2`の`HRESULT=0x887A0001`で1件失敗したが、**単体で再実行すると成功**し、
  全体を再実行しても成功した。変更範囲は`tools/CSharpGameEngine.VSExtension/`とテスト・ドキュメント
  のみで`src/CSharpGameEngine.Windows/`に一切触れていない(`git status`で確認)ため、
  Scene Designerが生きたD3D12デバイスを持つExpハイブ2つと並行実行したことによるGPU競合の
  一時的な失敗と判断した(この種の並行実行時の揺れは記録に残す)。

### 追記(2026-08): オプション画面のコントロール寿命バグ修正、Orbit反転設定、ピクセル完全一致ドラッグ

**1. 実機バグ修正: 「チェックしても反転しない」**

上記オプション画面の実機確認で、チェックを入れてOKを押しても反転しないと報告された。
`SceneEditorSettings`に一時診断(`IsStoreAvailable`/`SettingsChangedSubscriberCount`)、
`SceneEditorOptionsPage`/`SceneHwndHost`に`File.AppendAllText`即時追記のプレーンテキストログを
仕込んで実機ログを取得したところ、書き込み・読み戻し・購読(`LiveViewSubscribers=1`)はすべて
正常で、コントロールも生存していた。原因はコントロール寿命だった: `SceneEditorOptionsPage`が
`OnClosed`で`Control`を`null`にしていたため、`OnApply`が`Child`経由で**既定値の新しいコントロール
を作り直して読む**経路が存在し、他のページを見てから戻ると未チェック状態で保存されていた。

- コントロールを一度作ったら使い回すよう修正(`OnClosed`でのnull化を撤去)。
- ユーザー編集を`SceneEditorOptionsControl.ValueEdited`イベントで**即座にモデルへ取り込む**方式へ
  変更し、`OnApply`がコントロールの状態に依存しなくても正しい値を書けるようにした(コントロール
  再生成が起きても取りこぼさない、二重の保険)。
- 併せて、`VsBrushes.WindowTextKey`をUserControlへ指定していたことでTextBlockだけが淡色になり
  見出しが読みにくかった表示不具合も修正(CheckBoxは自前のForegroundを持つため濃いままで、
  TextBlockとの濃淡差が生じていた)。ホストのダイアログ既定色を継承させるよう撤去した。
- 原因確認後、診断コード(`IsStoreAvailable`/`SettingsChangedSubscriberCount`/`DiagLog`一式)は
  すべて削除した。

**2. Orbit(Alt+LMB)の反転設定**

「Orbitもチェックボックスで設定できるようにして」。これまで`SceneViewState.ApplyOrbit`は
`InvertX:false,InvertY:false`を固定で渡しており反転設定が存在しなかった。既存の
`InvertLookX`/`InvertLookY`とまったく同じ配線パターンで`InvertOrbitX`/`InvertOrbitY`を追加し、
オプション画面にチェックボックスを2つ追加した(既定`false`、挙動不変)。永続項目は7→9項目、
オプション画面が公開する項目は3→5項目になった。

**3. RMB Look / Alt+LMB Orbit のピクセル完全一致ドラッグ(ベストエフォート)**

「RMBのクリックした部分をレイキャストで固定してドラッグしたときにそのピクセルとマウスカーソルの
先が完全一致するようにしてほしい。反転の時はむりだけど。それはOrbitも同様に」。

調査の結果、LookとOrbitで数学的な性質が大きく異なることが判明した:

- **Look(RMB)**: カメラ位置(Eye)は回転中固定なので、クリック時の「方向」だけを覚えておけば
  純粋なベクトル代数で**厳密な閉形式**が導ける。`SceneViewCamera.DecomposeDirection`が
  ワールド方向を`FlatRight`/`Up`/`Forward`基底へ射影した係数(Yaw/Pitchの値によらず不変)を求め、
  `SolveLookOrientation`がそれを目標方向へ一致させるYaw/Pitchを`atan2`/`acos`だけで厳密に解く。
  `CreateLookAt`/`CreatePerspectiveFieldOfView`の左右手系規約には一切踏み込まない設計にした
  (このセッションで一度、その規約を誤って「修正」してゲーム本体と不整合を起こした前例があるため、
  既存の`GetForward`/`GetFlatRight`/`GetUp`/`ScreenPointToRay`をブラックボックスとしてのみ使う)。
- **Orbit(Alt+LMB)**: カメラ位置(Eye)自体が球面上を動くため、固定した世界座標の点をスクリーン
  上の目標位置へ投影する連立方程式が非線形になり、**閉形式が存在しない**。減衰付きNewton法
  (Levenberg–Marquardt、`TrySolveOrbitYawPitch`)で常にベストエフォートの解を返す設計にした
  ——**この解には解の枝が折り返す(fold)構造的な限界があり、クリック位置とドラッグ量によっては
  一時的に完全一致を維持できない区間が生じる**(バグではなく2自由度・注視点固定という束縛条件が
  持つ性質。テスト設計時にも実際に踏んだ: アンカーが`FocusPosition`にほぼ一致する退化に近い設定で
  Pitchが可動域の端(-80°)まで暴走する現象を実際に観測し、テスト側のアンカー選定を直して解決した
  — 実装のバグではなく数値的な感度の問題だったことをその場で切り分けた)。折り返し区間では
  「その時点で最も近づける位置」に留まり、抜ければ自動的に完全一致へ復帰する(ユーザー承認済みの
  仕様、反転時に完全一致を諦めるのと同じ精神)。
- 反転設定(`InvertLookX/Y`・`InvertOrbitX/Y`)がONの間は、ピクセル完全一致を使わず既存の
  `delta*sensitivity`経路のまま(反転しながらの完全一致は定義上不可能なため — ユーザーの
  明示的な了承事項)。
- `ScenePicker.TryPick`は交差判定で距離を内部計算していたのに呼び出し元へ返していなかった
  ため、`out float HitDistance`付きの多重定義を追加してOrbitのアンカー捕捉に使えるようにした
  (既存の3引数版は5引数版への転送に変更、判定ロジック自体は無改造)。
- `SceneInputStateMachine`は無改造(Vector3/世界座標を持ち込まない)。アンカー捕捉・保持は
  すべて`SceneHwndHost`に閉じた。

- **ユニットテストできなかったもの(正直な記録)**: `SceneHwndHost`の配線(RMB/Alt+LMB Down時の
  アンカー捕捉、Moveイベントでのピクセル完全一致呼び出し)はVS SDK型に深く依存しユニットテスト
  不能(既存の他機能と同じ理由)。数学の正しさは`SceneViewCamera`側の純粋関数テストで
  端から端まで固定したが、「実機でカーソルとオブジェクトが完全に一致して見える」ことと
  「Orbitの折り返しが実運用でどの程度の頻度で起こるか」はユーザー側のF5確認が必要。
- Evidence: `dotnet build CSharpGameEngine.slnx -c Debug` 成功・**警告0/エラー0**、
  `dotnet test CSharpGameEngine.slnx -c Debug` **784件成功・失敗0**。VSExtension.Testsは
  268→292件(Orbit反転14件+ピクセル完全一致10件)。

## 2026-08 M0020: F5起動時にslnxが二重に読み込まれる(最終原因: このセッション自身が汚したExpハイブの永続化状態)

実機報告: `UserEcsProject.slnx`をF5起動すると、一度開いてシーンの画像が出た**後にもう一度読み込み
直される**(「0/1 のプロジェクトの読み込みを解除しています」というVSの標準ダイアログが出る)。
拡張機能のコード(`TryAutoOpenUserEcsProjectAsync`)を疑って2回修正したが再現が続いたため、
`ActivityLog`ではなく`System.IO.File.AppendAllText`で即時反映するプレーンテキストへ診断ログを
仕込み、`IVsSolutionEvents`でソリューション/プロジェクトの開閉イベントの実時系列を記録した
(`HierarchyToolWindowControl`の調査時と同じ、原因特定後に削除する前提の一時コード)。

### 調査の経過(誤診を含めて記録する)

1. **誤診その1**: 最初のログがF5を誤って2回押した1回分だったため「単発F5では起きない、二重押下の
   誤操作」と結論したが、これは**そのテストに限った話**で、通常の単発F5では不具合が消えていない
   ことが後で判明した。原因: 1回のログだけで一般化してしまった。
2. PIDを記録してから単発F5で再現させたところ、**単一プロセス・単一の`TryAutoOpen`呼び出し**でも、
   オープン完了の**19〜25秒後**に、拡張機能のコードが一切再関与しないまま自動でクローズ→
   再オープンが起きることをログで直接証明した(`TryAutoOpenUserEcsProjectAsync`・
   `TryApplyInitialLayoutAsync`/`GameEngineLayout.ApplyAsync`のどちらも、
   その時間帯には一切動いていないことをログで確認済み)。
3. ステータスバーに「パッケージを復元しています」→「ソリューションを解除しています」という
   直接の因果順序が観測され、NuGet自動復元が引き金という仮説を立てたが、`obj`/`bin`を全消去して
   クリーンな状態から復元・再ビルドしても同じ間隔(19〜25秒)で再現し、しかも次のテストでは
   「パッケージを復元しています」の表示すら出ないまま再読み込みだけが起きたため**この仮説も否定**。
4. 拡張機能を一切含まない最小のソリューション(空のクラスライブラリ1つ)を同じExpインスタンスで
   開いても再現せず、続けて`CSharpGameEngine.CodeAnalysis`(独自ソースジェネレータ)への参照だけを
   追加しても再現せず、さらに`UserEcsProject.csproj`と同じ依存関係(Windows/Core/Import)・
   同じプロパティ(`EmitCompilerGeneratedFiles`等)を丸ごと複製しても再現しなかった。
   これで「VS 2026自体の一般的な癖」「ソースジェネレータ」「プロジェクト構造・依存関係」の
   いずれも否定された。
5. **最終的な原因**: `.vs/UserEcsProject.slnx/`配下(`DesignTimeBuild/.dtbcache.v2`が調査開始時点で
   5日前の日付のまま残っていたことから発覚)の永続化キャッシュ、特に`.suo`/`DocumentLayout.json`
   (直前に開いていたドキュメント・ツールウィンドウの状態を復元する仕組み)が、このセッション中に
   拡張機能自身(ヒエラルキー・シーンデザイナー・選択・`GameEngineLayout`関連)を何十回も
   ビルドし直してきたことで、実際のコードと食い違う古い状態を指すようになっていた。VSが起動時に
   この古い状態を復元しようとして不整合を検知し、それを解消するためソリューションを再読み込みして
   いたと考えられる。`.vs/UserEcsProject.slnx/`を削除して実機の`UserEcsProject.slnx`をF5で開いた
   ところ、再読み込みは発生しなくなった。
- **重要な性質**: これは通常のエンドユーザーが遭遇する類の不具合ではない。**同じセッションで
  この拡張機能自身を繰り返しビルド・デプロイしながら、同じExpハイブでテストを重ねる**という
  拡張機能開発特有の使い方によって、`.suo`等の永続化状態が実際のコードから乖離したことが原因。
  今後の開発セッションでも、拡張機開発中のツールウィンドウ/選択状態に関わるコードを大きく
  変更した際に同様の「原因不明の再読み込み」が起きたら、まず`.vs/<対象slnx>/`の削除を試すこと。
- **教訓**: 2回のコード修正がどちらも効かなかった時点で「同じコードパスへの推測をこれ以上重ねない」
  判断に切り替え、ログで実時系列を記録したのが正しかった。さらに、NuGet復元という次の仮説も
  クリーンビルドという実験で反証し、最小構成からの積み上げ切り分け(空→ジェネレータのみ→
  依存関係丸ごと)で「コードやプロジェクト構造の問題ではない」ことを完全に消去してから、
  最後に残った`.vs`永続化キャッシュへたどり着いた。**憶測で修正を書く前に、切り分け実験で
  候補を1つずつ排除する**手順が、袋小路(F5二重押下という誤った一般化、NuGet復元という反証された
  仮説)を経ながらも最終的に正しい原因へ到達させた。
- **後始末**: 診断コード(`IVsSolutionEvents`実装・`DiagLog`呼び出し)は削除済み。
  途中で追加した`StartupAutoOpenPolicy.CommandLineAlreadySpecifiesASolution`とUIスレッド取得後の
  再確認は、実害のない正しい防御的コードとしてそのまま残している(今回の不具合の直接の修正では
  なかったが、コマンドラインでslnxパスが渡るケースに対する妥当な保険であり除去する理由がない)。
  切り分けに使った一時テストプロジェクト(スクラッチ領域)は削除済み。
- Evidence: `dotnet build CSharpGameEngine.slnx -c Debug` 成功・警告0/エラー0、`dotnet test`
  736件成功・失敗0(診断コード除去後も無改変で成功)。原因の特定は憶測ではなく、実機の
  `IVsSolutionEvents`診断ログによる実時系列の記録と、複数段階の切り分け実験(最小ソリューション→
  ジェネレータ追加→依存関係複製→`.vs`キャッシュ削除)によって行った。

### 追記(2026-08): 再発 → 「DTBキャッシュ陳腐化」説は反証され、未解決に戻った

上記の`.vs/UserEcsProject.slnx/`削除で解消したはずだったが、別セッションで**再発**した
(「またソリューションの解除が発生した。根本解決して」)。発生タイミングは前回と同じく
「F5起動直後、何も操作していない段階」。

**誤って「確定」と書いてしまった仮説とその反証**: 再発直後、`.dtbcache.v2`(16:01:52作成)が
このチャットの`dotnet build`によるCore/Import/Windows再ビルド(16:18:19-23、キャッシュ取得より
後)より古いという1点のタイムスタンプ一致だけを根拠に、「コマンドラインbuildが参照先を
Expハイブのキャッシュより新しくすること」を機序として**確定と書いた**。これは誤りだった。
`DesignTimeBuild/`だけを削除して再度F5させたところ、VSは新しい(参照DLLより後の、陳腐化していない)
`.dtbcache.v2`を19:39:17に生成したにもかかわらず、**その直後のF5でも再読み込みが再発した**。
陳腐化していないキャッシュでも起きた以上、「DTBキャッシュの陳腐化」は少なくとも唯一の原因ではない。
**1回の相関だけで「確定」と書いたのが失敗**であり、[[feedback_vsix_deploy_automation]]が既に
記録している教訓(「反証実験なしに仮説を確定扱いしない」)を今回も守れていなかった。

**今回実際に行った対処**: 前回本当に効いたのは`DesignTimeBuild/`だけでなく
`.vs/UserEcsProject.slnx/`**フォルダ全体**(`v18/.suo`・`DocumentLayout.json`・
`DocumentLayout.backup.json`・`FileContentIndex/`を含む)の削除だったと確認し、
`DesignTimeBuild/`と`v18/`を削除した(`FileContentIndex/`はExpハイブのdevenv.exeプロセスが
実行中でロックしており削除できなかったが、両方の調査で原因候補に挙がったことは無いため対象外とした)。
ユーザーの元々の仮説(前回セッション末の「永続化が悪影響を？」)、すなわち`v18/`配下の
ドキュメント/ツールウィンドウ復元状態が実際の原因(または少なくとも`DesignTimeBuild/`単体の
削除では不十分な理由)である可能性が、今回の反証で再び有力になった。

- **現状**: 根本原因(なぜ`v18/`の削除が必要なのか、正確な機序)はまだ**未確定**。
  「`.vs/UserEcsProject.slnx/`全体を削除すると直る」という経験則のみが2回とも再現している。
  次に再発した場合は、`DesignTimeBuild/`のみ・`v18/`のみを個別に削除する切り分け実験を行い、
  どちらが実際に効くかを1回の相関ではなく反復可能な形で確認すること(前回の
  `IVsSolutionEvents`診断ログ+切り分けラダーの手法が再利用できる)。
- Evidence: `dotnet build`/`dotnet test`成功・警告0/エラー0・737件成功(コード変更は(C)のSE
  デバウンス修正のみで、この節の対処はキャッシュ削除という運用操作)。実機での次回F5確認
  (二重読み込みが起きないこと)はユーザー側。

### 追記(2026-08): 三度目の再発 — **保留中の未解決問題として記録する**

上記の「`.vs/UserEcsProject.slnx/`全体を削除すると直る」という経験則も、その後の別セッションで
**再度破られた**: `DesignTimeBuild/`と`v18/`の両方を削除した直後のF5でも、ユーザーから
「そしてソリューション介助(解除)がまたおきた」と報告があった。

これで根本原因についての仮説(DTBキャッシュ陳腐化・`v18/`永続化状態)は両方とも実質的に反証された
形になり、**現時点で信頼できる恒久対策は無い**。この問題は一旦保留とし、以下を記録するに留める:

- 発生条件・機序ともに未確定(誤診の経緯は上記2つの追記を参照)。
- `.vs/<slnx>/`削除という運用は、少なくとも一貫して再現を防げる対策ではないと判明した
  (2回は効いたように見えたが3回目で破れた — 効いたように見えた2回も、実際には無関係な別要因
  ―たとえばVSやOSの再起動、あるいは単なるタイミング―による偶然だった可能性がある)。
- 次にこの問題へ再着手する場合は、前回・前々回のような「キャッシュ削除で対症療法する」方針を
  一旦離れ、`IVsSolutionEvents`診断ログ(このセッションで最初に確立した手法)を**もう一度**
  最初から仕込み、`.vs`キャッシュの状態を変えずに複数回のF5を比較するなど、より厳密な再現実験から
  やり直す必要がある。
- 実害の重さ(F5後に毎回ではないが時々ソリューションが自動で再読み込みされる、程度)を踏まえ、
  他の作業を優先し、この問題への再着手はユーザーの判断を待つ。

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

## 2026-08-16 M0020 Phase 3: Moveギズモ、編集トランザクション、live C#書き戻し

- **Matches(軸/Camera規約)**: `CoordinateHandedness`の参照を全検索した結果、実装分岐は
  `Spatial<TConvention>.RightFromUpForward`に存在する一方、ゲーム本体/Scene Designerの
  View/Projection行列は参照していなかった。既知点の投影テストでは、両Cameraが同じ
  `Matrix4x4.CreateLookAt`/`CreatePerspectiveFieldOfView`を使い、既定視点のワールド`+X`は
  NDC X<0(画面左)になった。Moveギズモは実ワールド軸を反転せず表示する。
  `UnityCoordinateConvention.Handedness=LeftHanded`はRUF意味契約として使われているため変更していない。
  判断と将来の変更制約は[[ADR-0042-Scene-Editor-Gizmo-Axis-Handedness]]へ記録した。
- **Matches(Moveギズモ純粋層)**: 軸3+平面3、Perspective/Orthographicのスクリーン一定スケール、
  円柱+円錐+クアッドoverlay、通常/hover/active/disabled色、解析形状hit、軸/平面拘束を実装した。
  入力優先順位はNavigation > Gizmo > Selectionで、WはIdle時だけMoveへ切り替わり、Esc/capture lossは
  previewを破棄する。式を保持するPositionに対応するハンドルは淡色かつ非ヒットになる。
- **Matches(編集トランザクション/競合)**: MouseDownで開始snapshotと選択rootの元world/parent worldを
  捕捉し、ドラッグ中はRendererのworld定数だけを更新する。同一Sceneの再解析は最新1件を保留する。
  MouseUpでは最新CompilationからModel/Root/SourceTextを同じ世代で取得し直し、安定Node IDへ
  `SetTransform(Position)`を適用する。解決後にlive textが変化したときだけ1回再試行し、対象消滅、
  C#解析エラー、2回目の競合は自動上書きせず元previewへ戻し、ツール内とVSステータスバーへ通知する。
- **テストで検出して修正した最小差分不具合**: 当初の`SetTransform`はTRS全10メンバーを比較していた。
  Move後の行列分解ではQuaternion正規化やScaleの表現差まで「変更」と判定しうるため、Positionだけを
  動かしたドラッグがRotation/Scaleリテラルを巻き込む可能性があった。`SceneTransformMemberMask`を追加し、
  Move製品経路をPosition限定にした。`SetTransform_PositionMask_DoesNotRewriteRotationOrScale`が
  非正規化`RotationW=2f`と`ScaleX=3f`を保持することを固定する。
- **Matches(Undo契約のコード構造)**: `SceneTextBufferWriter`はRoslynのTextChangeを後方から適用し、
  1コミット全体を1つの`ITextUndoTransaction`で完了する。ディスクへ直接書かず、閉じていたDocumentも
  backgroundでVisualStudioWorkspaceのlive bufferとして開く。Undo後は既存WorkspaceChanged解析が
  Scene表示を復元する。
- **Unknown(実機Observed)**: `SceneHwndHost`/D3D12 overlay/RDT/managed `ITextBuffer`/VS Undoは
  実COM・HWND・GPUを要求し、既存方針どおりモック化していない。Experimental Instanceでのドラッグ、
  Ctrl+Z、1 Undo単位、ステータス通知、Tab/Backspace/Delete非回帰は**未実施 — ユーザー側で実施**。
  手順は[[M0020-Scene-Editor-Interaction]] Phase 3の10項目表に記録した。
- **Known deviation(後続Phaseへ明示的に残す)**: MouseUp後の通常解析ではPhase 6まで
  `SceneRenderer`全再構築が残り、毎フレーム`WaitForGpuIdle`もPhase 7まで残る。これは
  ADR-0041の段階戦略どおりで、Phase 3のpreviewだけが先行して増分定数更新を使う。
  **この歴史的制約はPhase 7で解消済み**。
- Evidence:
  - 対象54件(`SceneTransformMathTests`/`TransformGizmoTests`/`EditTransactionControllerTests`/
    `SceneSourceEditorTests`/`SceneInputStateMachineTests`/`SceneTextChangePlannerTests`)成功・失敗0。
  - `dotnet build CSharpGameEngine.slnx -c Debug`成功、警告0/エラー0。
  - `dotnet test CSharpGameEngine.slnx -c Debug`は全8テストプロジェクト808件成功・失敗0
    (Entities 40 / Core 122 / Bridge.Hosting 8 / Import 30 / Windows 87 / CodeAnalysis 101 /
    SceneModel 104 / VSExtension 316)。

## 2026-08-16 実装台帳69件の全件監査(タスクB)

- **監査母数**: `Docs/Implementation-Status.md`のFeature見出し69件を1件ずつ確認した。
  Status 69件、Verification 69件を再集計し、監査後の内訳はImplemented 33件、Removed 35件、
  PartiallyImplemented 1件。
- **旧L9アプリケーション参照63行**: 17 Featureエントリと導入/移行要約2行に分布していた。
  5 Feature(F-L8-001/F-L7-005/F-L8-005/F-L4-001/F-L4-002)は現行`UserEcsProject`または現行ツールへ
  証跡を付け替え、5 Feature(F-L3-014/F-L9-001手書きループ/F-L9-002/F-L9-003/F-L9-004)は
  実装消滅を確認してRemovedへ変更、7 Feature(F-L7-003/F-L2-001/F-L7-007/F-L9-001重複ID/
  F-L7-010/F-L8-009/F-L9-007)は既にRemovedだったため削除履歴として書き直した。監査後、旧プロジェクトの
  固有名リテラルは0件。
- **残る69件の横断監査**: 開始時はImplemented/PartiallyImplementedエントリのImplementation evidence/
  Testsに、存在しないパス候補56件(17 Feature)があった。旧動的ECS 3件(F-L3-008〜010)、旧`.scene`
  プレビュー(F-L8-003)、旧SceneAuthoringサンプル(F-L9-006)、旧STARTUP診断(F-L7-018)をRemovedへ変更。
  現行Scene DSL/StartupScene/GameSystem 6件(F-L3-011/F-L3-013/F-L7-012/F-L7-013/F-L7-015/F-L7-017)と
  Scene Designer(F-L8-010)は現存ファイル/テストへ証跡を更新した。
- **監査後のパス整合**: evidence/testパス候補372件のうち、現行Featureの欠落は0件。
  Removed Featureの欠落108件は削除前ファイルを指す意図的な履歴証跡として維持した。また現行Feature本文に
  残る欠落パス4件(`SpatialPresetDemo`、旧Geometry配置2件、旧StaticArchetype配置1件)は、いずれも
  「移設前」「削除済み」と明記された来歴説明であり、現行Implementation evidenceではない。
- **Conformance判断**: 削除済み実装をImplementedと扱う不一致は解消した。旧機能と現行後継の機能集合が
  同一でない箇所(キャラクター操作、汎用構造変更、複数音源/距離減衰など)は、後継へ同一視せずRemovedの
  Known deviationsとして保持した。
- **Evidence**: `dotnet build CSharpGameEngine.slnx -c Debug`は警告0/エラー0。
  `dotnet test CSharpGameEngine.slnx -c Debug --no-build`は全8テストプロジェクト808件成功・失敗0
  (Entities 40 / Core 122 / Bridge.Hosting 8 / Import 30 / Windows 87 / CodeAnalysis 101 /
  SceneModel 104 / VSExtension 316)。

## 2026-08-16 M0020後続Task C: 空間ドラッグ矩形選択

- **Matches(入力状態機械)**: `PendingSelect`のLMB Up確定を維持したまま、Down時にヒットが無く
  開始点から物理ピクセル4px以上移動した場合だけ`MarqueeSelecting`へ進む。オブジェクト開始は
  大きく移動しても切り替えず、marquee開始後はLMB Up/capture loss/Escまで操作を固定する。
- **Matches(矩形pick)**: `ScenePicker.PickMarquee`は各render objectのメッシュ頂点を
  `World * View * Projection`でスクリーンへ投影し、正規化済み選択矩形と境界を含めて交差判定する。
  描画順を保ち、同一NodePathは1件へまとめ、カメラ後方/depth外だけの対象は除外する。
- **Matches(選択発行)**: 通常ドラッグは矩形内NodeIdsで置換し、Ctrlドラッグは既存選択へ加算する。
  どちらも`SceneSelectionOrigin.Viewport`で発行する。最後の新規ヒットをprimaryとし、Viewportは全件を
  ハイライトする。WPF TreeView/`SceneEditorHub`は単一NodePath契約のためHierarchy表示はprimaryだけ。
  この非対称性は意図的であり、複数選択情報はsession内に保持される。
- **Matches(overlay再利用)**: `MarqueeOverlay`は矩形を半透明triangle 2枚+line 4辺へ変換し、
  Phase 3の深度無効overlay PSO/固定upload bufferへMoveギズモと合成する。専用PSO/GPU resourceは
  追加していない。
- **Unknown(実機Observed)**: HWND capture、D3D12での矩形表示、WPF選択反映、コードエディタの
  Tab/Backspace/Delete非回帰は実COM/HWND/GPUを要するため未実施。M0020のTask C目視表4項目を
  ユーザー側確認として残した。
- Evidence:
  - Task C対象50件(`SceneInputStateMachineTests`/`ScenePickerTests`/
    `MarqueeSelectionPolicyTests`/`BuildMarqueeOverlay`)成功・失敗0。
  - `dotnet build CSharpGameEngine.slnx -c Debug --no-restore`成功、警告0/エラー0。
  - `dotnet test CSharpGameEngine.slnx -c Debug --no-build --no-restore`は全8テストプロジェクト
    824件成功・失敗0(Entities 40 / Core 122 / Bridge.Hosting 8 / Import 30 / Windows 87 /
    CodeAnalysis 101 / SceneModel 104 / VSExtension 332)。

## 2026-08-16 M0020 Phase 4: Rotate/Scale、増分スナップ、World/Local

- **Matches(Phase 3合成モデルの拡張)**: `TransformGizmo`をpartial拡張し、Moveと同じ
  `MoveGizmoFrame`、解析hit、`GizmoOverlayPrimitive`、深度無効line/triangle PSO、固定upload bufferを
  Rotate/Scaleにも使う。Rotateは軸リング3+viewリング、Scaleは軸3+軸端立方体3+中心均一立方体を持ち、
  通常/hover/active/disabled色も既存経路で解決する。別の描画方式やcommit経路は追加していない。
- **Matches(拘束・修飾・座標空間)**: Rotateはリング平面上の開始/現在方向から符号付き角度を求め、
  Scaleは軸距離比またはview平面上の中心距離比を0.01〜100へ制限する。CtrlはMove=1.0単位、
  Rotate=15°、Scale=0.1単位(最小0.1)へ丸める。Worldは単位軸、Localはprimary回転から得た基底を使い、
  上部ComboBoxの値を既存`SceneEditorSettings`へ永続化する。
- **Matches(入力優先順位とトランザクション)**: Idle時のW/E/RだけがMove/Rotate/Scaleを選ぶ。RMB Fly中の
  W/EはNavigationへ渡す。複数選択ではprimary位置を共有Pivot、primary回転をLocal基底とし、選択済み
  祖先を持たないrootとその子孫へ同じworld deltaをpreviewする。MouseUpでは既存batch/1 Undo経路を使い、
  RotateはPosition+Rotation、ScaleはPosition+Rotation+Scaleだけを書き戻す。
- **Matches(編集可能境界)**: Pivot回転や非等方world scaleをTRS分解すると複数メンバーが変化しうる。
  このためRotateはPosition+Rotation、Scaleは全TRSがliteral/defaultで編集可能な場合だけ有効にし、
  式を保持する必要メンバーを暗黙に上書きしない。純粋テストでdisabled判定を固定した。
- **MatchesWithDocumentedExtension(テストで検出した既存Roslyn不具合)**: 複数の既存Transform assignmentを
  1操作で置換すると、最初の`SeparatedSyntaxList.Replace`後も辞書が元listのsyntax nodeを保持していたため、
  2項目目の置換で`ArgumentOutOfRangeException(nodeInList)`になった。名前→indexを保持し、置換直前に
  現在のlistからnodeを取り直す方式へ修正した。Rotation/Scale maskが対象外メンバーを保持するテストと、
  ScaleX/Y/Zの複数既存メンバーを連続置換するテストが回帰を固定する。
- **Unknown(実機Observed)**: HWND入力/capture、D3D12リング/立方体、WPF ComboBox、live buffer、VS Undoは
  実COM/HWND/GPUを要するため未実施。M0020 Phase 4の目視表8項目を**未実施 — ユーザー側で実施**として
  残した。依存方向・所有権・永続化形式・公開契約の新しい判断はなく、新規ADRは不要。
- Evidence:
  - Task D対象39件(`TransformGizmoTests`/`EditTransactionControllerTests`/
    `SceneSourceEditorTests`/`SceneTransformMathTests`)成功・失敗0。
  - `dotnet build CSharpGameEngine.slnx -c Debug`成功、警告0/エラー0。
  - `dotnet test CSharpGameEngine.slnx -c Debug --no-build --no-restore`は全8テストプロジェクト
    838件成功・失敗0(Entities 40 / Core 122 / Bridge.Hosting 8 / Import 30 / Windows 87 /
    CodeAnalysis 101 / SceneModel 104 / VSExtension 346)。

## 2026-08-16 M0020 Phase 5: Duplicate、Follow View、Vertex/Surface Snap

- **Matches(Duplicate入力・preview)**: IdleのAlt+LMBはTransform handle hitをOrbitより先に判定する。
  hit時は選択rootとrender descendantを予測NodePathの一時renderer objectとして追加し、元objectを
  動かさず整数handle経由で複製側だけをpreviewする。非hit時は従来のOrbitを開始する。
  Scene Rootは親Childrenがないため開始拒否する。
- **Matches(Duplicate source/Undo/Selection)**: DuplicateNodeOperationは対象object creationのtriviaを
  含めてcloneし、NameをX (n)形式で一意化し、最終local Transformをclone内へ適用して兄弟末尾へ挿入する。
  複数選択も1 operation列・1 live text Undoで確定し、再解析で予測pathが全て解決した後だけ複製rootへ
  Selectionを移す。Cancel/失敗では一時GPU objectを破棄し、元選択・元Transform・ソースを維持する。
- **Matches(Follow View)**: drag開始時のFocusPosition/FocusDistance/Yaw/Pitchに対するtotal deltaで
  Moveは視点とFocusを平行移動し、Rotateは共有Pivot周りで視点/forwardを回す。Scaleは追従しない。
  Shift再押下は現在のtotal deltaへ追いつき、Esc/capture loss/commit失敗は開始cameraへ戻す。
- **Matches(Vertex/Surface Snap)**: Move中のVは物理8px内で最近の投影頂点へPositionだけを合わせる。
  Nは最近三角形交点へPositionを合わせ、primaryのworld回転後ローカル+Yを幾何法線へ整列する。
  selection rootと全子孫を両queryから除外し、成功優先順位はN→V→Ctrl。SurfaceSnap法線整列は
  Phase 5製品経路ではtrue固定で、無効化設定UIは将来拡張として残した。
- **MatchesWithDocumentedExtension(テストで検出したbatch順序不具合)**: 一般source batchの
  node位置降順をそのままduplicateへ使うと、同じ親の複数選択が選択順と逆にChildren末尾へ追加され、
  事前予測したNodePathと実体が入れ替わった。duplicateのsort keyを親Children位置にし、
  親collection間は後方順、同一親内は要求順へ変更した。これによりRoslyn span安全性とpath予測を両立する。
- **Unknown(実機Observed)**: HWND modifier/capture、D3D12一時object、WPF duplicate Selection、
  live buffer、Visual Studio 1 Undo、Follow Viewの操作感は実COM/HWND/GPUを要するため未実施。
  M0020 Phase 5の目視表8項目をユーザー側確認として残した。新しい依存方向・所有権・公開永続契約の
  判断はなく、新規ADRは不要。
- Evidence:
  - Phase 5で16件追加。VSExtension.Tests 362件成功・失敗0。
  - dotnet build CSharpGameEngine.slnx -c Debug --no-restore成功、警告0/エラー0。
  - dotnet test CSharpGameEngine.slnx -c Debug --no-build --no-restoreは全8テストプロジェクト
    854件成功・失敗0(Entities 40 / Core 122 / Bridge.Hosting 8 / Import 30 / Windows 87 /
    CodeAnalysis 101 / SceneModel 104 / VSExtension 362)。

## 2026-08-16 M0020 Phase 6: Compound/FrustumとCamera/Light PropertyGizmo

- **MatchesWithDocumentedExtension(ライト語彙)**: ADR-0041で未決定だったライトComponent所在を
  ADR-0043で現行L9型の完全修飾名Binding登録と決定した。VSIXは文字列照合だけを行い、
  UserEcsProjectへのバイナリ参照を追加しない。Directional/Point/Spotを登録し、RectLightは未登録。
- **Matches(Compound/Camera)**: active TransformGizmoとPropertyGizmoをCompoundGizmo.Composeで
  同一overlayへ合成する。Perspective CameraはFOV/Near/Far、Orthographic Cameraは
  OrthographicSize/Near/Farを構成し、解析的handle hitからdrag previewへ接続した。
- **MatchesWithDocumentedExtension(Light Property)**: DirectionalはDirection、PointはRange、
  SpotはDirection/InnerCone/OuterCone/Rangeを構成する。SourceRadius/SourceSizeは現行L9型に
  Memberが存在せず、RectLightは型・描画契約も存在しないため明示的対象外。
- **Matches(modifier)**: PropertyはCtrl単位snapを許可し、Shift/Altを無効化する。Alt押下中は
  Property hitを除外し、Transform handleならPhase 5 Duplicate、それ以外ならOrbitへ戻す。
- **Matches(source/transaction)**: Property operation列はEditTransactionControllerのdirect経路で
  snapshot保留・Esc/capture loss cancel・live text 1 Undoを再利用する。SetComponentMemberは型に
  宣言済みだがinitializerで省略されたMemberを追記する。Add/RemoveComponentは
  ComponentsListLocation/SceneComponent.Locationだけを変更し、同型重複・未宣言Member・不正式を拒否する。
- **Unknown(実機Observed)**: Frustum/Light overlay、実pointer hit/capture、D3D12描画、
  live buffer、Visual Studio Undo、既存Alt Duplicateの非回帰は実COM/HWND/GPUを要するため未実施。
  M0020 Phase 6の目視表8項目をユーザー側確認として残した。
- Evidence:
  - Phase 6で19件追加。VSExtension.Tests 381件成功・失敗0。
  - dotnet build CSharpGameEngine.slnx -c Debug --no-restore成功、警告0/エラー0。
  - dotnet test CSharpGameEngine.slnx -c Debug --no-build --no-restoreは全8テストプロジェクト
    873件成功・失敗0(Entities 40 / Core 122 / Bridge.Hosting 8 / Import 30 / Windows 87 /
    CodeAnalysis 101 / SceneModel 104 / VSExtension 381)。

## 2026-08-16 M0020 Phase 7: SceneDiff増分更新、frames-in-flight、競合/Esc仕上げ

- **Matches(SceneDiff初消費)**: `SceneIncrementalUpdatePolicy`はTransform/非mesh
  `ComponentChanged`/`MemberChanged`をIncremental、mesh・Node構造・Component追加削除を
  Rebuildへ分類する。同一Sceneの直前表示世代だけを増分対象とし、世代が飛んだ場合はlatest-only
  rebuildへ戻す。`SceneRenderIncrementalUpdater`は既存meshを再利用し、親から子へ
  `world = local * parentWorld`を全件再計算してboundsも更新する。
- **Matches(frames-in-flight)**: `SceneRenderer`は2 frame slotごとにcommand allocator、
  object定数、overlay定数/頂点領域、fence値を所有する。`RenderFrame`末尾の
  `WaitForGpuIdle`を除去し、back-buffer slot再利用時だけそのslotのfenceを待つ。
  resize、一時object resource破棄、disposeは資源寿命のため全体待機を維持する。
- **Matches(競合差分)**: Transform/Property operationへdrag開始値を保持し、
  `SceneEditConflictMerger`がPosition/数値Propertyの加算差分、RotationのQuaternion差分、
  Scaleの倍率差分をcommit時点の最新値へ合成する。対象/Component消失、外部変更後の非editable式、
  非有限結果では自動上書きせず失敗通知へ戻す。
- **Matches(Esc終端)**: `SceneNavState`全7状態を列挙し、EscでIdle、capture解放、
  Fly key消去、選択/commit非発火を固定した。MouseUp後のCommittingはEscでcancelせず、
  source writeの完了/失敗を終端として扱う。
- **Unknown(実機Observed)**: Renderer再利用、実GPU queue/fence、連続drag時のフレーム挙動、
  live buffer/Visual Studio Undo、同一Member外部編集の製品配線は実COM/HWND/GPUを要するため未実施。
  M0020 Phase 7の目視表6項目をユーザー側確認として残した。
- Evidence:
  - Phase 7で8件追加。VSExtension.Tests 389件成功・失敗0。
  - dotnet build CSharpGameEngine.slnx -c Debug --no-restore成功、警告0/エラー0。
  - dotnet test CSharpGameEngine.slnx -c Debug --no-build --no-restoreは全8テストプロジェクト
    881件成功・失敗0(Entities 40 / Core 122 / Bridge.Hosting 8 / Import 30 / Windows 87 /
    CodeAnalysis 101 / SceneModel 104 / VSExtension 389)。


## 2026-08-16 M0020 follow-up: ヒエラルキー選択からソースをマーク／移動

- **Matches(選択規則)**: Hierarchyは通常クリックで置換、Ctrl+クリックで追加/解除、Shift+クリックで深さ優先表示順の範囲選択を行う。選択集合・primary・アンカーは`HierarchyNodeSelection`で保持し、再構築後は`NodePath`で復元する。単一`NodePath` Hub通知は互換経路として維持する。
- **Matches(非フォーカス・マーク寿命)**: Hierarchy起点だけが、既に開いている対象C# bufferへ`SceneNode.Location.Span`全体を背景マークする。親子重複は併合し、複数の独立範囲は併記する。通常クリックは文書を開かず、フォーカスもキャレットも変更しない。選択解除、Scene切替、解析失敗、再解析でマーカーを消去または再計算する。
- **Matches(明示移動)**: ダブルクリックまたはEnterだけがprimaryの`Span.Start`へ移動を要求する。RDTで既に開いた文書を確認できない場合、または再解析でNode/TextViewを得られない場合は何もしない。
- **Matches(実機Observed)**: VS Experimental Instanceで、連続Ctrl/Shift複数選択、複数マーク、親子Span、Enter/double-click移動、および未表示文書を開かないことを確認した。
- Evidence: `HierarchySourceSyncTests`を含む`dotnet test tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj --no-restore`はDebug/Releaseとも425件成功・失敗0。


## 2026-08-16 M0020 follow-up: 未表示ソースからの Scene Designer 起動

- **Matches**: ソリューションエクスプローラーで右クリックした`.cs`のSceneは、対象文書を開かずに即時解析・Pinする。SceneSelectorはPinの`ProjectId`を優先してカタログを取得するため、アクティブなコード文書が無い状態でも3D表示を維持する。
- **Matches**: カタログ取得失敗はSelectorを無効化する補助UIの失敗であり、Pin済みSnapshotや`SceneHwndHost`を破棄しない。Scene自体の解析失敗・消失だけが空状態へ遷移する。
- Evidence: `SceneCatalogTargetPolicyTests`を追加。`dotnet test tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj --no-restore`はDebug/Releaseとも399件成功・失敗0。

## 2026-08-16 M0020 follow-up: VS テーマ Hierarchy、Component icon、標準 source marker

- **Matches(code review: Hierarchy presentation)**: 公開 VS TreeView テーマを基礎に、Hierarchy 内の
  keyboard focus は active selection、他ウィンドウへの移動後は inactive selection となる。複数選択行を
  すべて表示し、primary のみ left accent を持つ。`ExpandRight` / `ExpandDown` の展開操作は selection
  event を発行しない。
- **Matches(code review: Component visual contract)**: `SceneComponentIconAttribute` は struct 専用・
  単一指定で、プロジェクト相対 SVG / PNG だけを許可する。root / node / component の既定 moniker、
  project image、将来の lock overlay を `SceneVisualIcon` に集約した。無効 path・欠落 file・参照 metadata は
  既定 component icon へ安全に戻る。SharpVectors の実行時依存は VSIX payload に含める。
- **Matches(code review: glyph authoring)**: 書込み可能なソース component struct の glyph だけがアンカー
  picker を開く。候補は既定 icon とプロジェクト配下の SVG / PNG に限定し、選択/解除は Roslyn 更新を
  現在の text buffer へ 1 Undo で適用する。partial 型の既存属性はその所有宣言を更新し、未指定時は
  クリックした宣言を所有箇所にする。他文書を開かず、コードエディタの focus を移さない。
- **Matches(code review: marker implementation)**: `SceneSourceMarkerService` は Visual Studio 標準
  `SimpleTagger<TextMarkerTag>` を使い、拡張所有 `TrackingTagSpan` だけを追加・除去する。書式は
  `MarkerFormatDefinition/CSharpGameEngine.SceneHierarchySource` の半透明青紫背景で、C# 構文前景色を
  上書きしない。通常 Hierarchy 選択は既に開いた文書のみを非選択 marker で装飾し、double-click / Enter
  だけが primary へ移動する。RDT/buffer/tagger/tag 数の成否は ActivityLog へ記録する。
- **Matches(実機Observed)**: VS Experimental Instanceで active/inactive 見た目、展開ボタン、
  project SVG/PNG の描画・picker・Undo、実 marker の表示/解除、未表示文書非オープン、および
  Enter/double-click の明示移動を確認した。
- Evidence: `ComponentIconAuthoringTests`、`SceneHierarchyIconTests`、
  `SceneSourceMarkerTaggerTests` と既存 `HierarchySourceSyncTests` が純粋層/MEF 契約を固定する。

## 2026-08-16 M0020 follow-up: Hierarchy構造編集とRuntime/User component icon

- **Matches(code review)**: HierarchyのContextMenu・F2/Delete・DragDropはAddChild/Rename/Remove/Reparent/Reorderを選択ルート単位で1 Undoにまとめる。子を含む削除だけ確認し、Root削除・生成ソース・自己/子孫へのReparentを拒否する。
- **Matches(code review)**: RuntimeのTransform/StaticMesh/PrimitiveMesh/CameraおよびUserEcsProjectのScene-addable structは`SceneComponentIconAttribute`で宣言プロジェクトのSVGを参照する。PNGも同じ検証済み相対パスで利用でき、無効/欠落/metadataは既定アイコンへ戻る。
- **Matches(code review)**: HierarchyとC# glyph marginの画像は`SceneVisualIcon`へ集約し、TreeViewのFontSizeからアイコン/インデントを算出するため、Solution Explorerの文字サイズ変更に追従する。
- Evidence: `SceneSourceEditorTests` 22/22、component icon関連テスト20/20、VS extension Debug/Release tests 431/431、Entities Debug/Release tests 42/42。構造編集のExperimental Instance目視は未再確認。

## 2026-08-16 M0030: ギズモTransform編集の受け入れ監査と深度精度(残課題R1)

- **ObsoleteSpecification(依頼仕様 §1〜§7)**: 「シーンビューにギズモを表示し、ドラッグで位置変更し、
  C#ソースへ1 Undoで反映する」という新規実装依頼を受けたが、最終コードを調査した結果、当該機能は
  M0020 Phase 3〜7で実装済みだった(`TransformGizmo`/`TransformGizmoPhase4`/`EditTransactionController`/
  `SceneEditConflictMerger`/`SceneSourceEditor`/`SceneTextBufferWriter`)。依頼仕様の§4 Must 6項目と
  §5 Should 4項目は既存実装で充足しており、再実装は行っていない。対応表は
  [[M0030-SceneView-Gizmo-Transform-Editing]]の「監査結果」節。
- **Mismatch → 解消(依頼仕様 §6 データモデル)**: 依頼仕様は`HierarchyEditKind.TranslateNode`と
  `SceneTransformEdit`の新設を求めていたが、`HierarchyEditKind`は構造編集専用であり、Transform値編集は
  `EditTransactionController`→`SceneTransformChange`→`SetTransformOperation`の別経路が正である。
  提案どおり実装すると「値の書き戻し」が2系統になりADR-0041と衝突するため、**仕様側を現行実装へ
  合わせる**判断を行い、その理由をMilestone文書へ記録した。
- **Matches(code review + test)**: 依頼仕様§8の失敗時ハンドリング5項目(対象ノード消失/解析失敗/
  バッファ不在/例外のActivityLog記録/ドラッグイベント重畳)は既存実装で満たされていた。ただし
  失敗経路を固定するテストが存在しなかったため、`SceneEditConflictMergerTests`(新規6件)、
  `EditTransactionControllerTests`(+2件)、`SceneInputStateMachineTests`(+1件)を追加した。
- **Mismatch → 解消(M0020残課題R1: 深度精度)**: near 0.05固定と`far = max(FocusDistance*20,
  BoundsRadius*20)`の組み合わせで、実ステージ規模のnear:far比が20000:1を超えていた。
  `SceneCameraFraming.ComputeNearPlane`で`Near = max(0.05, Far/10000)`のfar連動式へ変更し、
  `SceneViewState`側の`NearPlane`二重定義を削除した。正射投影は深度が線形なので比率調整の対象外。
  深度バッファは`D32_Float`。実ステージ規模でnearは0.8〜1.0となり、M0020が挙げた推奨レンジに入る。
- **Unknown(実機Observed)**: Z-fightingの消失、および near を上げたことで手前が不自然にクリップ
  されないことのExperimental Instance目視は**未実施**。M0020 Phase 0〜7の目視も未実施のまま引き継ぐ。
- Evidence: `SceneEditConflictMergerTests` 6/6、`SceneCameraFramingTests`/`SceneViewStateTests` 50/50、
  VS extension tests 448/448(M0020完了時431から+17)、ソリューション全体 942/942。
  変異テストで各追加テストが本体の破壊を検出することを確認済み(詳細はMilestone文書)。

## 2026-08-17 M0031: Inspector 数値編集UI

- **Matches(依頼仕様との適合)**: M0020が将来送りにした「数値編集UI」を実装した。既存の
  `SetComponentMemberOperation`/`SceneEditConflictMerger`/`SceneEditorSessionService.CommitAsync`
  経路をそのまま再利用し、新しい編集パイプラインは作っていない。
- **Matches(code review + test)**: `InspectorEditorKind`判定はCLR型(`SpecialType`/`TypeKind.Enum`)
  から機械的に決まり、読み取り専用種別(`ConstantExpression`/`ObjectCreation`/`Collection`/
  `StaticMemberRef`/`FactoryCall`/`Unsupported`)は`SceneValue.IsEditable`で先に弾かれる。
  `SceneInspectorModelTests`で全種別を固定した。
- **Mismatch → 解消(実装中に発見)**: 数値パースを一度`int`へ丸めてから対象型へ振り分ける素朴な
  実装は、`short`/`byte`等の狭い型で範囲外の値がオーバーフローしたまま素通りし、生成ソースが
  コンパイルエラーになる欠陥を持っていた。各型の`TryParse`へ直接投げる実装へ変更し、
  変異テストで検出できることを確認した。
- **Mismatch → 解消(実装中に発見)**: `CheckBox.IsChecked`/`ComboBox.SelectedItem`を`TwoWay`
  バインドし`Checked`/`SelectionChanged`をcommitの起点にする素朴な実装は、初期のデータバインド
  代入自体がそれらのイベントを発火させるため「確定→再構築→再バインド→再確定」の無限ループになる
  欠陥を持っていた。`OneWay`バインド+`Click`/`DropDownClosed`(ユーザー操作でしか発火しない
  イベント)へ変更した。コミット前に設計変更したため、欠陥のあるコードは履歴に残っていない。
- **Matches(code review)**: `SetComponentMemberOperation.OriginalValueExpression`は常に`null`で
  渡す。渡すと`SceneEditConflictMerger`が差分リベースを行い、絶対値指定のタイプ入力が
  相対ジェスチャとして扱われてしまうため。
- **Unknown(実機Observed)**: ノード選択→行表示、Enter/フォーカス喪失での確定とUndo、
  Enum/CheckBoxの単発commit、編集中の再解析での入力保持のExperimental Instance目視は**未実施**。
- Evidence: `SceneInspectorModelTests` 19/19、`InspectorMemberViewModelTests` 11/11、
  `InitialLayoutToolWindowPlanTests`更新済み、VS extension tests 474/474(M0030完了時448から+26)、
  ソリューション全体 968/968。

## 2026-08-17 M0030追記: 実機フィードバック4件

- **Mismatch → 解消(実機Observed起因)**: `SceneEditorSettings.DefaultActiveTool`が`Select`のため、
  選択しただけではギズモが出ずWキーが必要という体験がユーザーには分かりにくいと判明した。
  既定を`Move`へ変更した(永続設定がある場合はそちらが優先されるため既存ユーザーには無影響)。
- **Mismatch → 解消(実機Observed、原因特定)**: `SceneHwndHost.CompleteEditCommit`はコミット
  成功直後に`UpdateMoveGizmo()`を呼ぶが、この関数がpivot算出に使う`CurrentRenderObjects`は
  非同期Roslyn再解析が終わるまで更新されないため、ドラッグ確定直後にギズモが一瞬旧位置へ戻り、
  再解析完了後に正しい位置へ「追いつく」視覚的ズレがあった。`EditTransactionController`に
  `CurrentPreviews`アクセサを追加し、コミット成功時に`CurrentRenderObjects`該当ノードを最終
  Transformへ即時パッチしてから`UpdateMoveGizmo()`を呼ぶよう変更した。`UpdateMoveGizmo()`自体は
  無変更。
- **Unknown → 対応(実機Observed、フォーカス非奪取は未検証)**: `SceneSourceMarkerService.Update()`
  はHierarchy選択時にマークするだけでスクロールしていなかった。`IVsTextManager.EnumViews`
  (対象文書をアクティブ化せず既存ビューを列挙)+`ITextView.DisplayTextLineContainingBufferPosition`
  (caretを動かさない)でマーク行をエディタ最上部へスクロールするようにした。再解析のたびに
  同一選択で再スクロールしないようLastScrolledPathsで直近選択と比較している。この経路の
  正確なC# out/ref判定はildasmで`Microsoft.VisualStudio.Interop.dll`を逆アセンブルして実シグネチャ
  を確認した(`Microsoft.VisualStudio.TextManager.Interop.dll`は型フォワーダのみで検証に使えない)。
  フォーカス非奪取が実機で保たれるかは未検証。
- **Unknown → 対症療法(実機Observed、根本原因未特定)**: ソース反映後にコードエディタのcaret/
  スクロール位置が変わるという報告に対し、`SceneTextBufferWriter.TryApply`は既に最小差分適用の
  ため明確な単一原因を特定できなかった。`SceneWorkspaceService.ApplyTextAsync`で編集前後に
  対象バッファの全ビューのcaret/ビューポート先頭行を`ITrackingPoint`でsave/restoreする対症療法を
  追加した。実機での効果検証は未実施。
- Evidence: `EditTransactionControllerTests`(+1件、`CurrentPreviews`)、`SceneEditorSettingsTests`
  (2件更新)。②③④はHWND/D3D12/VS COMサービスに深く依存するためユニットテスト不可、実機確認が
  必須。ソリューション全体969/969(既存968+1)。

## 2026-08-17 M0032: シーンビュー選択でもC#ソースをマーク・スクロール

- **Mismatch → 解消(実機Observed)**: Hierarchyでノードを選択するとC#ソースがマーク・スクロール
  される一方、シーンビュー(ビューポート)でオブジェクトをクリック選択したときはHierarchyツリーの
  ハイライトだけが追従しソース側は無反応という非対称があった。原因は
  `CSharpGameEngineViewerPackage.OnSelectionChangedForHierarchy`が`Origin==Hierarchy`以外
  (実質Viewportのみ)では`SourceMarkerService.Update(Model:null,Paths:[])`でマークを明示的に消す
  だけの実装だったこと(M0020設計時点で「ソース装飾はHierarchy起点に限る」という意図的な制限
  だった)。Viewport起点でも選択中の`NodeIds`を`NodePath`へ解決し`SourceMarkerService.Update`へ
  渡すよう書き換え、Hierarchyと同じマーク・スクロール(フォーカス非奪取の既存経路を再利用)を
  適用した。
- Evidence: `SourceMarkerService`本体は無変更(既存の`EnumViews`スクロール経路・重複スクロール
  防止をそのまま再利用)。呼び出し元はライブVSサービスに依存するPackage内グルーコードで姉妹
  メソッド`OnHierarchySelectionChanged`同様ユニットテスト対象外のため新規テストは追加していない。
  ソリューション全体969/969(既存969、ロジック変更のみでテスト数増減なし)。実機での動作・
  フォーカス非奪取確認は未実施。

## 2026-08-17 M0033: ギズモをMove+Rotate+Scale同時表示化、Local/Globalアイコン追加

- **Mismatch → 解消(実機Observed起因のUX変更)**: Move/Rotate/Scaleが`SceneActiveTool`により
  排他的にどれか1系統だけ表示・操作可能で、W/E/Rキーで切り替える方式だった。ユーザーから
  Blender/Unity寄りの「3系統を常に同時表示・操作できるように」という要望を受け、
  `TransformGizmo.TryHitTransform`(Move/Rotate/Scale/新設Local-Globalトグルアイコンをレイ距離
  最近傍でまとめてヒットテスト)を新設し、`SceneHwndHost`の該当箇所(`TryHitTransformGizmoAt`/
  `UpdateMoveGizmo`のdisabled-handle計算/`RefreshOverlay`)をすべて系統横断へ書き換えた。
- **Mismatch → 解消(実機Observed起因、真のバグ)**: 旧`RotateRingRadius=0.9`と
  `ScaleAxisLength=0.9`が完全一致しており、Scale軸キューブとRotateリングが座標として重なって
  いた。加えてRotateリングはMove平面ハンドルと同一平面上にあり対角最大到達距離(≈0.68)より
  内側だと平面ハンドルの当たり判定を貫通する。半径帯をRotate(0.78)→Scale(1.0)→Move円錐
  (1.15〜1.35)の順に重ならないよう再配置した。
- **Mismatch → 解消(設計上の潜在バグ、3系統同時ヒット化で表面化)**: `BeginTransformGizmoDrag`は
  ヒットした`GizmoHandleId`ではなくambientな`State.ActiveTool`でMove/Rotate/ScaleGizmoDragのどれを
  開始するか決めていたため、3系統同時ヒットテストにより両者が食い違い得る状態になっていた。
  新設`GetHandleFamily(GizmoHandleId)`でHandle自身から系統を逆引きするよう修正した。
- **Unknown → 対応(実機Observed)**: Local/Global切替はWPF ComboBoxのみでギズモ上には無かった。
  ピボットは既にScaleUniformのキューブが占有しているため、カメラRight/Up方向へ固定オフセットした
  位置に新設`TransformSpaceToggleGizmo`(Local=立方体ワイヤーフレーム、World=リンググリフ)を配置。
  クリックはドラッグを生成せず即座に`SetTransformSpace`をトグルし、既存の
  `Machine.CancelCurrent()`経路(`SceneInputStateMachine`側の変更ゼロ)でIdleへ戻る。旧ComboBoxは
  二重UIになるため削除した。
- W/E/Rキーと`SceneViewState.ActiveTool`永続状態(3系統が常時全部有効になり不要)を削除した。
  `SceneActiveTool`列挙自体は「ハンドル系統識別子」として存続する。
- Evidence: `TransformGizmoTests.cs`に7件追加(系統横断最近傍解決・Move平面ハンドルの生存確認・
  `TransformSpaceToggleGizmo`のヒット/オーバーレイ)。`SceneEditorSettingsTests`/
  `SceneViewStateTests`のActiveTool関連テストを削除・置換。ソリューション全体974/974
  (969からActiveTool関連2件削除+9件追加)。半径帯が実機で視覚的に重ならないか・アイコンの
  位置/クリック操作感・ドラッグ系統解決の実際の配線はHWND/D3D12依存のためユニットテスト不可、
  実機確認が必須。

## 2026-08-17 M0034: Shift+Ctrlのサーフェススナップを中心の平面四角ハンドル化

- **Unknown → 対応(実機Observed起因、Unity準拠への操作性変更)**: サーフェススナップ自体
  (位置+面法線への回転整列)は既にM0020 Phase 5で実装済みだったが、起動が`N`キーホールドで
  Unityと異なっていた。Shift+Ctrl押下中にギズモ中心へ現れる専用ハンドル
  `GizmoHandleId.SurfaceSnapPlane`(平面四角、輪郭はfill色のRGB反転)をドラッグする方式へ変更。
  数学(`ScenePicker.TrySurfaceSnapQuery`/`EditTransactionController.PreviewSurfaceSnap`)は
  無変更。頂点スナップ(`V`キー)はUnity標準に合わせそのまま残した。
- **Mismatch → 解消(設計上の簡素化)**: `SceneTransformBinding.CanEditTransform`へ
  `SurfaceSnapPlane`のHandle優先分岐(Toolに依らず`CanEditRotation`と同じ要件)を追加したことで、
  ドラッグ開始時に同じ条件を別途チェックしていた`CanSurfaceSnapSelection`フィールドが完全に
  重複になったため削除した。
- **Unknown → 対応(実機Observed)**: マウス静止中でも1フレーム以内に武装表示(四角の出現/消滅)を
  反映する必要があった。既存のFly Fast/Precisionポーリング(`GetKeyState`、Renderingティック)と
  同じ方式で新規`SceneHwndHost.UpdateSurfaceSnapArmed`を追加し、新規のWndProc/WPFキー配線は
  一切追加していない(ネイティブサーフェスは意図的にキーボードフォーカスを持たない設計のため
  その経路は行き止まりと確認済み)。
- W/E/Rキー撤廃(M0033)で確立した「ドラッグ系統はambientな状態ではなくヒットしたHandleが決める」
  方針を踏襲し、`SurfaceSnapPlane`ドラッグ中は`MoveGizmoDrag.TryBegin`の評価自体を`||`短絡で
  回避する専用分岐を追加。一度ドラッグが始まれば修飾キーを離してもmouse-upまでSurfaceSnap
  ドラッグのまま継続し、ドラッグ中はFollowView(Shift)を明示的に抑止する(カメラが動くと
  ヒット面が動くフィードバックループになるため)。
- Evidence: `SurfaceSnapGizmoTests.cs`(新規7件)、`SceneTransformMathTests.cs`に2件追加。
  ソリューション全体983/983(既存974+9)。掴みやすさ・武装の反応速度・吸着の操作感は
  HWND/D3D12/`GetKeyState`依存のためユニットテスト不可、実機確認が必須。

## 2026-08-17 M0035: ギズモの視認性改善(ScaleUniformのhover、Move矢印の間隔、Rotateリングの面積)

- **Mismatch → 解消(実機Observed、真のバグ)**: `BuildScaleOverlay`の`AddCube(ScaleUniform,...)`が
  基準色へ既に`Vector4.One`(純白)を渡しており、hoverロジック`Lerp(Base,Vector4.One,0.55f)`が
  `Lerp(白,白,x)=白`となり**hoverしても常に白のまま**だった。基準色を濃いグレーへ変更するだけで
  既存のhoverロジックがそのまま機能するようにした。あわせてhover/Active時だけ白→黒の二重
  ワイヤーフレーム輪郭(`AddOutlineCube`)を重ねるようにし、どんな背景色でも視認できるようにした。
- **Mismatch → 解消(実機Observed)**: `TransformGizmo.AxisStart=0.12f`が`ScaleUniformHalfExtent
  =0.11f`(対角では≈0.19まで届く)とほぼ同じ距離で、Move矢印がキューブへめり込んで見え誤選択の
  原因になっていた。M0033の半径帯再設計は外側だけを対象にしておりピボット直近のこの重なりを
  見落としていた。`AxisStart`を`0.26f`へ変更しキューブの対角到達距離より確実に外側にした。
- **Unknown → 対応(実機Observed)**: Rotateリング(`AddRing`)が細い線ループのみで実装上面積が
  無かった。内外半径を持つ平帯(annulus)の`Triangle`プリミティブを追加し、半幅は既存の当たり
  判定許容量`RotateRingThickness=0.07`をそのまま流用した(見えている太さと掴める太さを一致させる)。
  立体的なトーラス形状はunlitパイプラインと頂点予算(`MaxOverlayVertices=4096`)の制約で採用せず、
  平帯を選択した。
- Evidence: `TransformGizmoTests.cs`に新規5件追加、既存の
  `BuildRotateOverlay_GeneratesThreeAxisRingsAndViewRing`をTriangle帯追加に合わせて更新。
  ソリューション全体988/988(既存983+5)。実際の見え方(明暗の区別・輪郭の視認性・間隔感・
  リングの面積感)はHWND/D3D12依存のためユニットテスト不可、実機確認が必須。

## 2026-08-17 M0036: シーンビューでCtrl+Z/YのUndo/Redoを高速化

- **Mismatch → 設計を維持した上で高速化(実機Observed)**: 「シーンビューにフォーカスがあると
  Ctrl+Zが効かない」「ソースでのUndoは反映が遅い」という報告に対し、ADR-0040が明記する
  「C#ソースの`ITextUndoTransaction`だけが唯一の編集履歴」という前提はユーザーと確認の上で
  維持した。フォーカス問題は`SceneDesignerToolWindowPane`が`IOleCommandTarget`を持たず描画
  サーフェスも意図的に`Focusable=false`(ADR-0041)なため、Ctrl+Zがシェルコマンドとして解決
  されても行き先が無く無反応だったことが原因。既存のWASDQE配線が使う`RenderHostGrid`(実際に
  WPFキーボードフォーカスを持てる)へ`PreviewKeyDown`ハンドラを新設し、Ctrl+Z/Y/Ctrl+Shift+Zを
  横取りして対象.csファイルの`ITextUndoHistory`を直接呼ぶ(シェルコマンド解決やペインへの
  `IOleCommandTarget`実装には頼らない)。
- **Unknown → 高速化(実機Observed)**: 反映速度そのもの(Roslyn再解析の250msデバウンス+解析時間)
  は変えられないが、M0030で実装済みの即時パッチ手法(`ApplyFinalTransformsToRenderObjects`、
  無変更で再利用)をUndo/Redoにも転用した。`BeginTransformGizmoDrag`でコミット前のRoot Worldを
  退避し、成功したTransformコミットごとに`RecentTransformCommits`スタックへbefore/after/子孫/
  WorldDeltaを記録。実際のテキストUndo/Redoが成功したときだけ、記録をpopして3Dビューを
  Roslyn再解析を待たず即座に巻き戻す/やり直す。**どこにも書き込みを行わない表示専用の先読み
  キャッシュ**で、外れても実害は無い(その場合は従来どおりの速度で反映される)。対象はMove/
  Rotate/Scale/SurfaceSnapのTransformコミットのみ、Property編集・複製・別Scene切替時はキャッシュ
  を丸ごと破棄する。Hierarchy編集はこのキャッシュを経由しない別経路のため、直後のUndoだけが
  この保護の外側にあり理論上は一瞬古い状態を見せた後Roslyn再解析で自己修復する(データが壊れる
  ことはない、既知の限定的なギャップとして明記)。
- Evidence: `SceneTextBufferWriter.TryUndo`/`TryRedo`はVS SDKの`ITextUndoHistory`への薄い
  ラッパーで、この既存クラスの`TryApply`同様ユニットテスト対象外(このテストプロジェクトに
  `ITextBuffer`系のモック基盤が無い)。`SceneHwndHost.TryFastUndo`/`TryFastRedo`もHWND依存の
  前例を踏襲しユニットテストなし。ソリューション全体988/988(既存988、テスト数増減なし)。
  フォーカス問題の解消・体感速度・Hierarchy編集を挟んだ場合の自己修復挙動は実機確認が必須。

## 2026-08-17 M0037: Ctrl+Zの真の修正とUE5風ドラッグHUD

- **Mismatch → シェルコマンドルーティングでの正攻法修正(実機Observed)**: M0036の
  `PreviewKeyDown`横取りは実機で効かないと報告された。真因はWPFの入力系にすら到達しないこと
  だった — VSシェルのキーバインド解決がCtrl+Zを`Edit.Undo`(`guidVSStd97`/`cmdidUndo`)コマンドと
  してGlobalスコープで消費し、キーストロークがWPFのルーテッドイベントになる前に処理してしまう
  (WASDQE/Escape/Fが動くのはVSキーマップに割り当てが無く素通しされるためで、この非対称性が
  決め手)。`WindowPane`の`IOleCommandTarget`はexplicit+sealedでoverride不可だが、内部でペインの
  `IMenuCommandService`へ委譲するため、`SceneDesignerToolWindowPane.OnToolWindowCreated`で
  `VSStd97CmdID.Undo/Redo/MultiLevelUndo/MultiLevelRedo`を`OleMenuCommandService`へ登録し、
  `BeforeQueryStatus`でビューポートのキーボードフォーカス+ピン留めシーン有無を判定して
  `Supported`を切り替える方式へ置換した(コードエディタ等、VS内の他の場所のCtrl+Zには
  一切影響しない)。M0036の`PreviewKeyDown`配線は削除。
- **Unknown → RDT未登録ファイルでの無言失敗を解消(自己発見・実機未報告)**: 調査中に
  `SceneWorkspaceService.TryUndo/TryRedo`が`ApplyTextAsync`と同じ`IsDocumentOpen`→
  `OpenDocument(activate:false)`手順を踏んでいないことを発見した。`FindAndLockDocument`は
  RDTに無いモニカに対しても成功HRESULT+null`DocDataPointer`を返すため、`ErrorHandler.Failed`
  では捕まらず無言で失敗する(.csタブを閉じた後のCtrl+Zがこれに該当)。`EnsureDocumentOpen`を
  追加して解消。
- **Mismatch → Move矢印を立方体からさらに離す(実機Observed)**: `AxisStart 0.26→0.40`、
  `ShaftEnd(円錐開始) 1.15→1.38`、`AxisLength 1.35→1.60`、視点リング`RotateViewRingRadius
  1.5→1.85`(半径帯の内外順序は維持)。
  
- **Unknown → UE5風ドラッグ中HUDの追加(実機Observed)**: Move/Rotate/Scaleのドラッグ中に
  UE5風の数値・角度フィードバックが欲しいという要望に対し、`SceneRenderer`のD3D12パイプラインには
  テクスチャ/サンプラ/文字描画能力が一切無く(単一CBVルートシグネチャのみ)、ネイティブHWND子は
  `WS_EX_NOREDIRECTIONBITMAP`でWPFテキストの重畳描画も不可能(既存の`EmptyStateText`/
  `SceneHwndHost`相互排他パターンで確認済み)という制約の中、`GizmoDragHud.cs`を新設し数字を
  手書きベクターフォント(7segスタイルの線分ストローク、`0-9.+-°xcm`をカバー)としてオーバーレイの
  Line/Triangleプリミティブへ直接焼き込む方式で実装した(ユーザー確認済み・推奨案)。
  既存の`MarqueeOverlay.ToWorld`と同じ手法(near面へ逆投影し0.01だけ押し出す)を
  `SceneViewCamera.ScreenPointToRay`の新オーバーロード(事前計算した逆行列を受け取る)で再利用し、
  Perspective/Orthographic問わずスクリーン上で一定ピクセルサイズのHUDを実現。Move中は開始点の
  黄色マーカーと現在位置までの黄色破線+移動量数値、Rotate中は開始角の黄色マーカー・半透明の扇+
  輪郭線・マウス方向の黄色破線・角度数値、Scale中はハンドルに応じて1軸なら1値/Uniformなら3値の
  倍率数値を表示する(面(2軸)スケールハンドルは未実装のため今回はスコープ外・ユーザー確認済み)。
  `RotateGizmoDrag.TryUpdate`の`Radians`は`Atan2`由来で(-π,π]に折り返るため、`GizmoDragHud.
  AccumulateAngle`で連続的な累積角度を保持し扇と数値readoutの破綻を防ぐ。`SceneRenderer.
  SetOverlay`は頂点上限(`MaxOverlayVertices=4096`)超過で例外を投げるため、`RefreshOverlay`で
  HUD追加前に合計頂点数を数え、超過するならHUDのみ落としギズモ本体を守る防御を追加した。
- Evidence: `SceneDesignerUndoCommandPolicy.ShouldHandle`・`GizmoDragHud`のベクターフォント/
  `AccumulateAngle`/Move・Rotate・ScaleのHUDビルダーは全て純粋staticとしてユニットテスト対象
  (新規`SceneDesignerUndoCommandPolicyTests.cs`4件、`GizmoDragHudTests.cs`28件)。
  `OleMenuCommandService`登録・RDT操作・HWNDオーバーレイ合成はVS SDK/HWND依存の前例を踏襲し
  ユニットテスト対象外。ソリューション全体1020/1020(既存988+新規32件、リグレッションなし)。
  Ctrl+Z/Yが実際に効くか、コードエディタの通常のCtrl+Zに回帰が無いか、HUDの実際の見た目
  (線幅・文字サイズの可読性・扇の色/濃度・破線の間隔)は実機確認が必須。

## 2026-08-18 M0038: 編集レイテンシの根本解消(予測モデル層)

- **Mismatch → 待ち時間そのものを消す(実機Observed)**: 「まだ編集の速度が遅い」という報告に対し、
  M0030の即時パッチ・M0036の先読みUndo・M0037が**いずれも見た目の巻き戻りを隠していただけ**で
  待ち時間を短縮していなかったことを確認した。実測した主因は①コミット完了まで次の操作が
  ブロックされること(`EditTransactionController`が`Committing`のまま)、②1回のギズモ操作で
  プロジェクト全体のコンパイル取得が2回発生すること、③250msの固定デバウンス。glTF再読込は
  `MeshLoader`のプロセス寿命キャッシュで既に回避済みで原因ではなかった。
- **Unknown → 予測モデル層の新設(ユーザー確認済み)**: ユーザーからは「編集用データ構造を正本にし、
  保存時にソースへ反映する」案が出たが、**今回は土台のみ**(メモリ上のモデルへ即時適用して画面を
  即更新する層)を作り実機で体感を測ってから保存方式へ進むか判断する方針で合意した。
  **ADR-0040決定事項1(C#テキスト単一正本)は維持**しており、この時点でSupersedeは発生していない。
  `SceneModelEditApplier`はM0030/M0036と同じ「どこにも書き込まない表示専用の先読み」idiomを
  モデル層まで引き上げたもので、ソース書き戻しも再解析も従来どおり走る。予測が当たれば再解析時の
  diffが実質no-opになり、**再解析がそのまま予測の自動検証になる**。扱えない操作(構造編集・非リテラル式)は
  丸ごと諦めて従来経路へ落とすため、全操作種別への対応は不要。
- **Unknown → 書き戻しの直列化とUndo抑止(自己発見)**: 次の操作をブロックしなくした結果、
  ①書き戻しを並行させると後続コミットが古いソースを基準にリベースして直前の編集を打ち消しうること、
  ②書き戻しが landする前のCtrl+Zでテキストundoと先読みundoが食い違いソースと表示がずれることの
  2つの危険が新たに生じた。①は`PendingCommitWrite`のチェーンで直列化、②は
  `SceneDesignerUndoCommandPolicy.CanExecute`で飛行中の実行を抑止して塞いだ。②では`Supported`を
  落とさない — 落とすとCtrl+Zがシェルへ戻り別ドキュメントのUndoが走るため、`Enabled`だけを落とす。
- **却下**: `ResolveSceneEditContextAsync`のコンパイル再取得省略(計画では任意項目)は**実装しない**と
  判断した。ここが返す`Text`は`SceneTextBufferApplyGuard`がライブバッファと全文比較する基準であり、
  キャッシュすると不一致→リトライ増加で**かえって遅くなる**。UIは予測で既に更新済みなので体感の
  利得も無い。250msデバウンスも同じ理由で変更していない。
- Evidence: `SceneModelEditApplier`と`SceneDesignerUndoCommandPolicy`は純粋staticで、新規17件を
  追加(ソリューション全体1037/1037、既存1020、リグレッションなし)。最重要は契約テスト
  `ModelApply_MatchesSourceApplyThenReanalyze` — モデル層とソース層で編集セマンティクスが
  二重定義になるため、両者の一致を`SceneDiffEngine.Compare`で固定した(componentの
  `EmitExpressionText`だけは解析層のemit規則の再実装になるため`ComponentChanged`のみ許容)。
  `PredictedDiff_StaysOnTheIncrementalRenderPath`は、予測がD3D12デバイス全再生成へ落ちないことを守る。
  配線・書き戻しの直列化はVS SDK/HWND依存のためユニットテスト対象外。**体感速度は実機でしか測れない。**

## 2026-08-18 M0039: 予測コミット失敗時にUndo履歴が分岐する不具合の修正

- **Mismatch → 出荷直後に実機で回帰(実機Observed)**: M0038出荷直後、「Undoが直前の1回しか
  できない」と報告された。M0038の予測配信が2つの構造欠陥を持ち込んでいたことが判明した。
  ①`TryPublishPrediction`が予測snapshotの`DocumentVersion`を実解析と同じ共有カウンタから
  払い出しており、`SceneEditConflictMerger.Merge`のfast path判定(「ドラッグ開始時から実際の
  ソースが変化したか」)を機能不全にし、1回でも予測が発行された後はほぼ必ずrebase経路に
  落ちるようになっていた。②ローカル完了(予測配信)とソース書き戻し成功の対応関係が保証されて
  おらず、直列書き戻しチェーンの途中で1件でも失敗すると、それより後ろの書き込みが「実際には
  起きていない編集」の上に積み上がった。`RecentTransformCommits`(先読みUndoスタック)は無条件で
  pushされるため、実際に`ITextUndoHistory`へ積まれるトランザクション数より多い件数を持つ状態に
  なり、実Undoが尽きた後は`Applied=false`となって`TryFastUndo`にすら到達しなくなっていた
  (数式でも検証: `Merged = Current(前段の失敗地点まで) + (Final − Original(予測基準))`で
  失敗した編集の差分がそのまま失われる)。
- **Unknown → 版数の意味を分離し、失敗を検知即中断・強制同期する方式で修正(実機Observed)**:
  `DocumentVersion`(実解析のみが進める楽観的並行性制御の版数)と`AnalysisGeneration`
  (レンダラの増分更新判定用、予測でも進めてよい)を分離し、予測は前者を据え置くよう修正した。
  併せて`SceneDesignerToolWindowControl.PendingCommitWrite`を`Task<bool>`(健全性フラグ)化し、
  前段が不健全なら自分の書き込みを試みず失敗として連鎖伝播するようにした。チェーンで最初に
  不健全化した箇所だけ、新設`SceneEditorSessionService.RefreshPinnedSnapshotFromSourceAsync`で
  Roslynへ直接再解決した真実へpin中snapshotを強制上書きし、新設
  `SceneHwndHost.ResyncAfterCommitFailure`で先読みUndo/Redoスタックを丸ごと破棄する — 既存の
  「Property編集・複製・別Scene切替時は先読みUndoキャッシュを丸ごと破棄する」という確立済みの
  idiomを再利用しただけで、新しい概念は持ち込んでいない。
- **既知の残存する軽微なケース(Unknown)**: 不健全化したチェーンの後ろに予測非対応の編集
  (Inspectorの複雑な式、構造編集など)がたまたま連なっていた場合、そのローカルキャンセル処理が
  自身のドラッグ開始時点の古い基準値を、直前に強制同期した真実の上へ一時的に上書きしうる。
  表示のみの一時的なズレで、次のRoslyn再解析(既存の250msデバウンス経由)で自己修復する
  (M0036で既に文書化されている「Hierarchy編集を挟んだ直後のUndoの自己修復」と同種の限界として
  許容する)。
- Evidence: `SceneEditorSessionService`は`SceneWorkspaceService`(VS Workspace/RDT/Editor Adapters
  依存)を必須とするため実インスタンスでのユニットテストは既存方針どおり対象外。
  `SceneDesignerToolWindowControl`のチェーン中断伝播もUIコントロール内のprivate配線で対象外。
  ソリューション全体1037/1037(既存1037、テスト数増減なし、リグレッションなし)。編集回数分だけ
  正しくUndoを遡れるか、連続編集後も画面とソースが一致するかは実機確認が必須。

## 2026-08-19 M0040: ソースマーカーが再解析着地のたびに無関係な位置へ再スクロールする不具合の修正

- **Mismatch → M0020/M0032時点からの既存不具合と判明(実機Observed)**: 「選択直後はソースが
  正しくマークされるが、編集を確定すると無関係な位置(1053行のファイルで行1055付近、ほぼ末尾)へ
  再スクロールしてしまう。選択・編集するたびに毎回」と報告された(100%決定論的に再現)。
  `CSharpGameEngineViewerPackage.OnSceneModelChangedForSourceMarkers`(編集確定後のRoslyn再解析が
  着地するたびに毎回発火)だけが、Hierarchyパネル専用の`SceneEditorHub.HierarchySelection`
  (Hierarchy起点のクリックでしか更新されない静的フィールド)を参照していた。Viewport起点で
  選択・編集してもこのフィールドは一切更新されないため、実機検証セッション中どこかで最後に
  Hierarchyパネルをクリックしたときの(今の選択とは無関係な)古い位置で再解析のたびにマーカーが
  上書きされていた——「毎回」(再解析は編集のたびに必ず走る)と「無関係な位置」(セッション中
  最後にHierarchyでクリックしたノードの位置)の両方を過不足なく説明する。M0038/M0039とは無関係の
  既存不具合(Hierarchy起点とViewport起点の再マークが異なる「今の選択」の参照元を使っていた
  非対称性)。
- **Unknown → 統一選択サービスから都度解決する方式へ修正(実機Observed)**:
  `OnSceneModelChangedForSourceMarkers`を、`SceneEditorSessionService.Selection.
  CurrentSelection`(Hierarchy/Viewport/Inspectorを問わない統一選択サービス)から今の選択を
  都度解決するよう修正した。解決ロジック(`NodeIds`→pin中snapshotの`Projection`→`NodePath`)は
  `OnSelectionChangedForHierarchy`が既に持っていたのと同じ形だったため、`SceneSelectionPathResolver`
  (新設、VS SDK非依存の純粋関数、`HierarchySourceSync.cs`)へ切り出して両方の呼び出し元から
  使うようにし、将来また非対称にならないようにした。
- Evidence: `SceneSelectionPathResolver`は`SceneSelection`/`SceneProjection`/`EditorNodeId`/
  `NodePath`というVS SDK非依存の型だけで完結する純粋関数のため、新規
  `SceneSelectionPathResolverTests.cs`(6件)でユニットテストした(選択なし・Projectionなし・
  SceneKey不一致は空リスト、解決可能なNodeIdだけ変換、複数選択で入力順を保持)。ソリューション
  全体1043/1043(既存1037+新規6、リグレッションなし)。`CSharpGameEngineViewerPackage`内の
  実際のイベント配線はVS SDK依存のため既存方針どおりユニットテスト対象外。マーク位置が編集確定後も
  動かないままであることは実機確認が必須。

## 2026-08-19 M0041: ギズモ編集確定直後の無関係スクロールをM0040とは別原因として特定・修正

- **Mismatch → M0040修正後も実機で再現、消去法で真因を特定(実機Observed)**: M0040適用後も
  「編集を確定すると無関係な位置(ファイル末尾付近)へスクロールする」と再報告された。ユーザーへの
  確認で、誤ったスクロールは①確定直後・コード値が更新されるより前に起きる、②このテスト中
  Hierarchyパネルは一度もクリックしていない、の2点が判明し、M0040が修正した経路
  (`HierarchySelection`由来)は今回の再現に無関係と確定した。`SceneSourceMarkerService.Update`の
  スクロールゲート(`SelectionChanged=BufferChanged||!LastScrolledPaths.SequenceEqual(Paths)`)を
  再確認したところ、Move/Rotate/Scaleのドラッグは選択を一切変えないため`NodePath`(値型、
  `IEquatable<NodePath>`)比較は不変で、`SourceMarkerService`は単純なTransform編集で理論上
  一切スクロールしないはずだと判明。消去法で、ビューのスクロール位置を実際に触るコードは
  `SceneWorkspaceService.ApplyTextAsync`内の`CaptureViewPositions`/`RestoreViewPositions`
  (M0036由来、ビューポート先頭行の保存・復元)だけだと特定した。
- **Unknown → M0038の非同期化とM0036機能の設計前提の衝突(実機Observed)**: この機能は
  「書き戻しがコミット直後に同期的に近いタイミングで起こる」という前提で設計されていたが、
  M0038で書き戻しを非同期化・背後直列化したことで、捕獲(`CaptureViewPositions`)が実際に
  呼ばれるのは「コミットした瞬間」ではなく「書き戻しチェーンが自分の番に到達した瞬間」まで
  遅れるようになった。この間にスクロール位置が選択直後の正しい位置からズレていた場合、その
  ズレた状態をそのまま捕獲し書き戻し後に復元してしまう——`SourceMarkerService`が選択不変時に
  介入しないため、この経路だけが実際に見えるスクロール位置を編集のたびに書き換える唯一の経路
  として残っていた。捕獲時点でなぜズレるのかという一次原因の完全特定までは静的解析で断定
  できなかったが、この機構を経由しない限り位置が動く経路自体が存在しないため修正対象として
  採用した。
- **Unknown → Top保存・復元を撤去しSourceMarkerServiceへ一本化(実機Observed)**: `ApplyTextAsync`
  からビューポート先頭行(Top)の保存・復元を撤去し、caret位置の保存・復元だけを残した。
  スクロール位置の決定権を`SourceMarkerService.Update`の「選択が実際に変わったときだけ
  スクロールする」ロジックへ完全に一本化した(M0032/M0040で既に意図されていた設計)。
- Evidence: `CaptureViewPositions`/`RestoreViewPositions`はVS SDK依存のため既存方針どおり
  ユニットテスト対象外。純粋ロジックの変更を伴わないため新規テストなし。ソリューション全体
  1043/1043(既存1043、テスト数増減なし、リグレッションなし)。編集確定後もスクロール位置が
  動かないままであることは実機確認が必須。

## 2026-08-19 M0042: メッシュ無しノード/SkySphereをピッキング対象から除外

- **Mismatch → M0041の症状が実は選択対象そのものの誤りだったと判明(実機Observed)**: M0041の
  実機検証で「移動するとソースの先頭(Root宣言位置)が表示される」と再報告された。調査の結果、
  `SceneModelRenderObjectBuilder.Build`は`StaticMesh`/`PrimitiveMesh`を持たないノード(Root
  自身や整理用の空グループノード)にも無条件で`SceneRenderObject`を作り、
  `GizmoMeshFactory.Create()`が生成する0.3ユニットの小さな立方体で代替描画していた——これは
  今回に限らず、この一連の実機検証セッション全体でスクリーンショットに写り続けていた
  「ギズモの中の小さなキューブ」そのものだった。このプレースホルダーは`ScenePicker.TryPick`/
  `PickMarquee`の走査対象に無条件で含まれており、Transform無しならワールド原点付近に描画される
  ためステージの実オブジェクトへ紛れ込み誤選択・誤ドラッグしやすい。誤って選択されたノードの
  `Location.Span`はソース中のその宣言位置(Rootなら`Root=>new(){...}`の開始、ファイル先頭付近)
  を指すため、ソースマーカー(M0040/M0041で修正済み)は「正しく」その位置へスクロールして
  いた——マーカー機構自体は無罪で、選択されているノードそのものが意図しないものだった。
- **Mismatch → 以前保留していたSkySphereの問題も同種の穴と確認(ユーザー確認済み)**: M0038計画時に
  保留していた「何もない空間をクリックしても選択解除されない(背景のSM_SkySphereに当たっていた)」
  問題も、「本来ユーザーが直接操作すべきでないオブジェクトが、通常のシーンコンテンツと同列に
  ピッキング対象へ混ざっている」という同じ種類の穴だと確認し、まとめて修正する方針でユーザーへ
  確認・合意した。
- **Unknown → SceneRenderObjectへIsPickableフラグを新設して両方を解消**: 既存の`IncludeInBounds`
  (自動フレーミング用の除外フラグ)と全く同じパターンで`IsPickable{ get; init;}=true`を新設。
  `HasDeclaredMesh(AssetPathまたはPrimitiveMeshEnumMemberNameの非null)&&!IsSkyDomeAsset`を
  算出条件とし、メッシュ未宣言ノードとSky domeの両方を除外する。`IncludeInBounds`がたどる
  複製箇所と全く同じ4箇所(`SceneModelRenderObjectBuilder.cs`生成、`SceneIncrementalUpdate.cs`
  増分更新、`SceneHwndHost.cs`の複製プレビュー・コミット直後パッチ)で複製し、
  `ScenePicker.cs`の`TryPick`/`PickMarquee`/`TryVertexSnapQuery`/`TrySurfaceSnapQuery`全てで
  除外した。Hierarchyパネル経由の選択は`ScenePicker`を経由しないため無影響(メッシュ無し
  ノードも引き続きHierarchyからは選択・編集できる、意図的)。
- Evidence: `ScenePicker`/`SceneModelRenderObjectBuilder`はGPU/HWND非依存の純粋関数のため新規
  5件でユニットテストした(`ScenePickerTests.cs`に`TryPick`/`PickMarquee`/`TryVertexSnapQuery`/
  `TrySurfaceSnapQuery`それぞれの非ピッキング対象除外4件、
  `SceneModelRenderObjectBuilderTests.cs`にメッシュ未宣言ノードの除外1件、既存の
  `Build_SkySphereStaticMesh_IsExcludedFromAutomaticFraming`も拡張)。ソリューション全体
  1048/1048(既存1043+新規5、リグレッションなし)。実際の3Dビューでのクリック・ドラッグ体験は
  実機確認が必須。

## 2026-08-19 M0043: 再解析ではソースマーカーを絶対にスクロールさせない(原因特定から要件保証へ方針転換)

- **Mismatch → 3回の修正でも症状が残り、方針を転換(実機Observed)**: M0042で選択自体は正しく
  なった(実機で`選択: SM_Cylinder6(26)`、マーク範囲も正しく498-515行)にもかかわらず、
  「ギズモでドラッグしてY座標を変えるとマーク部位がスクロールで見えなくなる」症状が残った。
  飛び先はM0042前が「末尾行」、M0042後が「1行目」。M0040/M0041/M0042はいずれも実在するバグの
  修正だったが**この症状の決定打ではなく**、飛び先が毎回変わることからも複数要因が絡んでいた。
  今回も「なぜ飛び先が1行目なのか」を静的解析だけで断定できなかったため、**原因を1つに特定して
  潰す方針をやめ、ユーザーの要件(編集してもスクロール位置を維持する)をコードの構造として
  保証する方針へ切り替えた**。
- **確認済みの構造的欠陥**: 修正前の`SceneSourceMarkerService.Update`は
  `bool SelectionChanged=BufferChanged||!this.LastScrolledPaths.SequenceEqual(Paths);`で
  「選択が変わったか」を推測していた。コメントの意図は正しかったが実装が保証していない——
  `BufferChanged=!ReferenceEquals(MarkedBuffer,Buffer)`は`MarkedBuffer==null`のときも必ず
  trueになり、`Clear()`が`MarkedBuffer=null`にする。そして`Clear()`は編集サイクル中の過渡状態
  (`Model?.Root is null||Model.HasErrors`=書き戻し直後の一瞬Roslynが解析エラーを見る、
  文書未オープン、`OnSelectionChangedForHierarchy`のpin不一致経路が呼ぶ`Update(null,[])`)で
  普通に走る。つまり**過渡状態を一度でも経由すると次の正常な再解析が選択変更と誤認してスクロール
  する**構造で、飛び先はそのときの`Ranges[0].Span.Start`次第——実機で飛び先が毎回違った事実とも
  整合する。
- **Unknown → スクロール可否を呼び出し側の明示に変更**: `SceneSourceMarkerScrollBehavior`
  (`ScrollToSelection`/`PreserveScroll`)を新設し、`Update`の引数として受け取る形にした。判定は
  `SceneSourceMarkerScrollPolicy.ShouldScroll(Behavior,MarkerRangeCount)`(新設、純粋static、
  既存の`SceneDesignerUndoCommandPolicy`/`MarqueeSelectionPolicy`/`SceneSelectionPathResolver`と
  同じ流儀)へ委譲。`LastScrolledPaths`フィールドは削除し、`BufferChanged`相当の判定はタグ再構築
  にのみ残してスクロール判定から完全に切り離した。呼び出し側は
  `OnHierarchySelectionChanged`/`OnSelectionChangedForHierarchy`(選択変更)=`ScrollToSelection`、
  **`OnSceneModelChangedForSourceMarkers`(再解析の着地)=`PreserveScroll`**、pin不一致の
  マーカー消去=`PreserveScroll`。編集→書き戻し→再解析の経路は`PreserveScroll`しか通らないため、
  過渡状態が何であってもスクロールが起きえない。
- **受け入れる挙動変更**: 「Hierarchyで選択した状態で対象文書を後から開く」ケースは、従来
  `BufferChanged=true`で自動スクロールしていたが、今後はマークされるだけでスクロールしない。
  ファイルを開いた瞬間にビューが飛ばなくなるので望ましい方向であり、意図的な「その場所へ飛ぶ」
  操作はHierarchyのダブルクリック(`Navigate`、caretを明示的に動かす別経路)が引き続き担う。
  選択変更経路は`SceneSelectionService.Select`の`SameSemanticSelection`が意味的に同一な選択を
  既に弾いているため、`LastScrolledPaths`の重複抑制を失っても同じ選択で繰り返しスクロールしない。
- Evidence: 新規`SceneSourceMarkerScrollPolicyTests.cs`6件。中心は
  `ShouldScroll_PreserveScroll_IsAlwaysFalse`——マーク範囲の有無にかかわらず再解析経路からは
  スクロールが起きないことを固定する。`SceneSourceMarkerService`本体・
  `CSharpGameEngineViewerPackage`の配線はVS SDK依存のため既存方針どおりユニットテスト対象外。
  ソリューション全体1054/1054(既存1048+新規6、リグレッションなし)。編集時にスクロール位置が
  維持されることは実機確認が必須。

## 2026-08-19 M0044: 書き戻しを全文置換から最小差分へ(実測で原因を確定)

- **Mismatch → 実測でユーザーの見立てが正しかったと確定(実機Observed)**: M0043でスクロール位置は
  直ったが「一瞬一番上までスクロールしてから、その場所に戻る」ちらつきが残り、ユーザーから
  「全体書き換えだからか」「部分書き換えは無理か」「描画を抑制できないか」と問われた。
  M0040〜M0043で4回推測を外しているため、今回は**一時的な計測テストで先に事実を取った**。
  結果、`SceneTextChangePlanner.Plan(SourceText,SourceText)`は1リテラルの変更でも
  **span=(0,18531)=ファイル全体を覆う1個のTextChange**を返していた(離れた2箇所の変更でも同じ)。
  `SourceText.GetTextChanges`は比較対象が`WithChanges`由来でない無関係な`SourceText`のとき
  共通接頭辞・接尾辞の切り詰めを行わない。`CommitAsync`が毎回
  `SourceText.From(Edited.Root.ToFullString(),…)`でまっさらな`SourceText`を作っていたため常に
  この経路に落ちていた。**バッファ全体が削除・再挿入されるのでビューのレイアウト基準点ごと
  作り直され、表示が一度最上部へ飛んでいた**——ユーザーの見立てどおりだった。
- **Unknown → 構文ツリー差分へ切り替え(Q2の回答)**: `Edited.Root`は`Context.Root`へ
  `ReplaceNode`した結果でgreen nodeを共有しているため、`SyntaxTree.GetChanges`なら実際に変わった
  領域だけを取り出せる。`SceneTextChangePlanner`へ`Plan(SyntaxTree,SyntaxTree)`を追加し
  `CommitAsync`をこちらへ切り替えた(実測: 単一プロパティ編集で10文字、離れた2箇所で独立2変更・
  計11文字)。`SourceText`版は血縁を作れない`ComponentIconGlyphMargin`のために残し、粗さを
  XMLコメントで明記した。副次効果として`ToFullString()`による全文文字列化が消え、M0038の遅さ
  調査で挙げた項目の1つが解消した。
- **Unknown → ITextEditのバッチ適用(Q3の回答)**: VSには描画を止める公開APIが無いが、等価な仕組みが
  `ITextBuffer.CreateEdit()`によるバッチ適用。以前は変更ごとに`Buffer.Replace`を個別に呼び、
  1回ごとに独立したバッファ変更・`TextChanged`イベント・ビューのレイアウトが走っていた。
  全変更を1つの`ITextEdit`へまとめて`Apply`する形に変え、これらを1回にした。併せて
  `Edit.Replace`の戻り値と`Edit.Canceled`を確認するようにした——以前のループは適用失敗を一切
  検知していなかった。
- Evidence: `SceneTextChangePlannerTests.cs`へ実測値を固定するテストを追加(計8件)。
  `Plan_SourceTextOverload_RewritesTheWholeFileEvenForOneLiteral`はSourceText版の粗さを
  「span開始0・長さ=ファイル全長」として明示的に固定し、この特性が将来変わったら気付けるように
  した。最重要は`Plan_SyntaxTreeOverload_AppliedChangesReproduceTheEditedTreeExactly`——差分の
  出所を変える以上、適用結果が編集後ツリーの全文と完全一致することがソースを壊さないための契約。
  `SceneTextBufferWriter.TryApply`本体(`ITextBuffer`/`ITextUndoHistory`/`ITextEdit`)はVS SDK依存の
  ため既存方針どおりユニットテスト対象外。ソリューション全体1058/1058(既存1054+新規4、
  リグレッションなし)。ちらつきが消えたことは実機確認が必須。
