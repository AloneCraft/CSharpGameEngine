# Spatial RUF — セマンティック座標系仕様

このドキュメントは`Docs/RUF-Semantic-Coordinate-Specification-Prompt.md`が要求するRUF
(Right/Up/Forward)意味座標系の正本である。実装は[[ADR-0025-RUF-Semantic-Coordinates]]
(コア契約・Preset)、[[ADR-0026-Camera-Geometry-Ruf-Integration]]/
[[ADR-0027-Gltf-Fbx-Import-Axis-Conversion]]/[[ADR-0028-Ecs-Gameplay-Ruf-Boundary]]
(Renderer/Import/ECS内部統合)、機能一覧は`Docs/Implementation-Status.md`のF-L1-001〜F-L1-006・
F-L2-003・F-L0-006を参照。

## RUFの目的

ゲームコードからUnity/Unreal/Blenderなど外部ツールの物理XYZ配置・行列規約の差を隠し、すべての
ゲームコードをRUFという共通の意味座標で記述できるようにする。

## RUFは意味座標であり、XYZは物理配置である

- `RufVector`(`R`/`U`/`F`)はゲーム上の意味としての右/上/前方向であり、どのProject Presetが選ばれても
  「ゲーム上の前方向」という意味は変化しない。
- `XyzVector`(`X`/`Y`/`Z`)はエンジンが選んだ物理ストレージ上の配置であり、単体では意味を持たない。
- 両者の変換は`ICoordinateConvention.ToXyz`/`FromXyz`だけが知っており、ゲームコードはこの境界の
  外側で生のXYZ軸を直接仮定してはならない。

## Unity/Unreal/Blenderマッピング(Golden Values)

| Preset | R | U | F | Handedness | 実装 |
|---|---|---|---|---|---|
| Unity | +X | +Y | +Z | LeftHanded | `CSharpGameEngine.Spatial.Unity.UnityCoordinateConvention` |
| Unreal | +Y | +Z | +X | LeftHanded | `CSharpGameEngine.Spatial.Unreal.UnrealCoordinateConvention` |
| Blender(交換用/世界座標) | +X | +Z | +Y | RightHanded | `CSharpGameEngine.Spatial.Blender.BlenderWorldConvention` |

3つとも成分の並べ替えのみ(heap割当なし、分岐なし)。`samples/SpatialPresetDemo`で実測済み
(F-L1-004参照)。

これとは別に、Import境界専用の固定Convention(`SpatialPresetSelection.props`からは選択不可、
プロジェクトが選ぶPresetではない)が2つある:

| Convention | R | U | F | Handedness | 実装 | 用途 |
|---|---|---|---|---|---|---|
| glTF | +X | +Y | -Z | RightHanded | `CSharpGameEngine.Spatial.Gltf.GltfCoordinateConvention` | `CSharpGameEngine.Import.Gltf`内部専用 |
| FBX(AssimpNet既定) | +X | +Y | -Z | RightHanded | `CSharpGameEngine.Spatial.Fbx.FbxCoordinateConvention` | `CSharpGameEngine.Import.Fbx`内部専用 |

両者は数値的に同一の定義だが、2つのImporterを互いに独立させるためあえて別々の型にした
(ADR-0027)。

## Handedness

`CoordinateHandedness`(`LeftHanded`/`RightHanded`)は各Preset自身が外部ツールの文書どおりに宣言する
権威値であり、RUF/XYZマッピングから逆算した値ではない。

検証済みの関係(`Spatial<TConvention>.Cross`、`HandednessTests`で確認):

- LeftHandedなConvention(Unity/Unreal): `Cross(Right, Up) == Forward`
- RightHandedなConvention(Blender): `Cross(Right, Up) == Backward`
- `RightFromUpForward(Up, Forward)`は両Handednessで常に`Right`を返す(符号補正済み)。

## Transform／Matrix Conventionとの分離

RUFが決めるのは意味/物理マッピング/Handedness/外部ツール軸変換のみ。Scale/Rotation/Translationの
合成順、行列の掛け順、Parent/Child合成、Point/Vector/Normal変換は`ITransformConvention`という
別契約が決める(`ICoordinateConvention`とは独立)。

このマイルストーンで実装した唯一の実装は`StandardTransformConvention`:

- 適用順: Scale → Rotation → Translation(`scale * rotation * translation`、row-vector規約)
- Parent/Child合成: `Combine(parentWorld, local) = local * parentWorld`(child-then-parent)
- `TransformPoint`/`TransformDirection`/`TransformNormal`は別関数(`TransformNormal`は逆転置行列を
  使い、非一様スケール下でも法線が面に垂直なまま保たれる)

## Quaternion仕様(`RufQuaternion`)

- R/U/Fの3成分を、Preset非依存の内部的な右手系三つ組として扱う(どのConventionのHandednessにも
  依存しない)。
- `FromAxisAngle`: 指定軸周りの正回転は右手の法則に従う。
- `operator *`: `parent * child`は「childを先に、parentを後に」適用する合成
  (`RotateVector(parent*child,v) == RotateVector(parent, RotateVector(child,v))`、
  `RufQuaternionTests.Multiply_AppliesChildThenParent`で検証)。
- `LookRotation(forward, up)`: 戻り値の局所Forwardが`forward`に一致するQuaternionを構築する。
  `forward`と`up`が平行/ほぼ平行、または`up`がゼロ/非有限の場合は決定的なフォールバック
  (`RufVector.Up`、`forward`自身が`Up`と平行なら`RufVector.Right`)を使い、NaNにはならないが
  フォールバック境界をまたぐ際の連続性は保証しない。`forward`がゼロ長・非有限の場合は
  `SpatialConfigurationException`を投げる。

## Camera Forward / Mesh Forward / Light Forward

- **Camera Forward**(`CameraBasis.Forward`/`Camera.GetForward()`): カメラの視線方向そのもの。
  View行列内部のForward軸とは独立した概念として扱う(Transform Conventionにより符号/軸が
  変わり得るため)。`Camera.cs`の内部実装はRUF経由になった(ADR-0026)が、`GetView`/
  `GetProjection`自体(D3Dのビュー/クリップ空間規約)は依然RUF未契約(下記「Rendering境界」参照)。
- **Mesh Forward**: Meshの正面/進行方向を表す専用型(`MeshAuthoringForward`相当)は未実装のまま
  (「未確定事項」参照)だが、Import時の頂点座標自体はRUF経由でエンジン空間へ正規化されるように
  なった(`GltfAxisConversion`/`FbxAxisConversion`、ADR-0027)。
- **Directional Light Forward**(`DirectionalLightBasis.Forward`): 光が実際に進む方向(photonの進行
  方向)。
- **Spot Light Forward**(`SpotLightBasis.Forward`): Spot coneが伸びる方向。
- **Point Light**: Forwardを持たない(このマイルストーンで型は追加していない)。

## DirectionToLight

`DirectionalLightBasis.DirectionToLight`/`SpotLightBasis.DirectionToLight`は共に`-Forward`
(表面から光源へ向かう方向)。`LightBasisTests`で検証済み。

## Object Basis

座標系(Convention)とObject Basis(個々のCamera/Light/Meshが持つForward)は独立した概念。本
マイルストーンでは`CameraBasis`/`DirectionalLightBasis`/`SpotLightBasis`のみ実装(F-L2-003)。
`MeshAuthoringForward`相当の型は、Mesh Importerの実装自体が対象外のため未実装(「未確定事項」参照)。

## Coordinate Spaces

このマイルストーンで実装した契約が扱うのは`Semantic RUF Space`(`RufVector`)と
`Physical XYZ Space`(`XyzVector`)の2つのみ。Asset/Object Local/Parent/World/View/Clip/NDC/
Screen/Texture Spaceは、既存Rendering/Scene層(`CSharpGameEngine.Scene.Transform`、
`CSharpGameEngine.Graphics.D3D12`)がRUFへ未統合のため、本仕様書では定義しない
(「未確定事項」参照)。

## Units

Runtime単位は距離=メートル、角度API=ラジアン、時間=秒、スケール=無次元。`UnitConversion`は
Authoring/Import境界専用のヘルパー(メートル/センチメートル、ラジアン/度)であり、Runtime内部の
正とはしない。軸変換(`ICoordinateConvention`)と単位変換(`UnitConversion`)は独立した処理段階であり、
順序を入れ替えても結果が変わらないこと(線形写像同士の可換性)を`ImportAxisConversionTests`で
確認済み: `External axis conversion → Unit conversion → Semantic RUF normalization`。

## Import／Export境界

```text
Import:  External Source Coordinates → External Tool Convention → Semantic RUF → Engine Storage XYZ
Export:  Engine Semantic RUF → Target Tool Convention → Target XYZ
```

`ImportAxisConversionTests`が各Preset(Unity/Unreal/Blender)の`FromXyz`(Import方向)を
Golden Valuesで検証している。**実際のファイルImporter(glTF/FBX)からこの契約を呼び出す統合は
ADR-0027で実装済み**: `GltfImporter.ImportScene`/`FbxImporter.Import`は、それぞれ
`GltfCoordinateConvention`/`FbxCoordinateConvention`(Import境界専用、上記参照)を使い、
公開APIを出る時点で全てエンジン(`UnityCoordinateConvention`)空間になっている。
`GltfImporter.Import(string)`(単一プリミティブのプレビュー専用API)は対象外のまま
(「未確定事項」参照)。

## Rendering境界

**未確定**。View space forward方向・Projection行列規約・Depth range・NDC Y方向・Screen原点・
UV原点・Front face winding・Normal変換・Tangent basis handednessは、`Matrix4x4.CreateLookAt`/
`CreatePerspectiveFieldOfView`(D3Dのビュー/クリップ空間規約そのもの)が依然RUF契約の外側にある
ため、本仕様書では定義しない(ADR-0026で意図的にスコープ外とした)。`Camera.cs`のForward/Right
計算自体はRUF経由になったが(ADR-0026)、`GetView`/`GetProjection`に渡す直前で
`Spatial<UnityCoordinateConvention>.ToXyz`により生の`Vector3`へ変換される。

## Public APIの命名

このマイルストーンで追加した公開APIは`CameraBasis.Forward`/`DirectionalLightBasis.Forward`/
`DirectionalLightBasis.DirectionToLight`/`SpotLightBasis.Forward`/`SpotLightBasis.DirectionToLight`
のように意味を持つ名前を使い、`Direction`/`Axis`/`Vector`/`ForwardVector`のような無限定な名前は
避けている。

## Performance原則

- `ICoordinateConvention`実装はすべて`readonly struct`+`static abstract`メンバーで、
  `Spatial<TConvention>`はコンパイル時に単相化される(Runtime分岐・Reflection・Boxing・
  Virtual dispatchなし)。
- `SpatialPresetSelection.props`によるPreset選択はMSBuildのコンパイル時条件分岐であり、
  Dictionaryルックアップも実行時分岐も発生しない(`samples/SpatialPresetDemo`の
  `#if SPATIAL_PRESET_*`はコンパイル時のみ)。
- Import境界(`CoordinateBasisValidator`)はAuthoring/Build時専用で、Hot pathからは呼ばれない。

## 未確定事項

- **`CSharpGameEngine.Scene/Transform.cs`は未統合**: 既存`CSharpGameEngine.Scene.Transform`との
  重複整理・JSON永続化フォーマットへの影響・VSIX(net472)側の独立パーサーとの同期が必要になるため、
  別マイルストーンで再検討する(ADR-0025から継続、ADR-0027時点でも対象外)。
- **Blenderの`BlenderGltfExportConvention`/`BlenderFbxExportConvention`への分割は未実施**:
  `BlenderWorldConvention`(Blenderワールド座標のGolden Valuesと一致)のみ実装した。Blender用の
  Import/Exportパイプライン自体が未着手のため、Export設定のForward/Up・Meshローカル前方向・
  Cameraローカル視線方向・Lightローカル方向を区別する具体的な要件が確定していない。
- **Rendering境界(View/Projection/NDC/UV/Winding既定値/Tangent basis handedness)は未定義**
  (上記「Rendering境界」参照。ADR-0026で意図的にスコープ外とした)。
- **`GltfImporter.Import(string)`(単一プリミティブのプレビュー専用API)は軸変換の対象外**:
  `ImportScene`のみRUF経由の軸変換を実装した(ADR-0027)。プレビュー専用でゲームプレイ
  コンテンツには使われないため意図的に対象外とした。
- **`Matrix4x4.Decompose`が失敗する退化行列(負スケール等)への対応は未実装**:
  `GltfAxisConversion.ConvertWorldTransform`は現状、変換せずそのまま返すフォールバックのみ
  (ADR-0027)。
- **UserGameProjectの実機での見た目の妥当性は完全には確認できていない**: ADR-0027実装後に
  `dotnet run --project UserGameProject`を実行しクラッシュしないこと・スクリーンショットで
  破綻がないことは確認したが、変更前との比較用スクリーンショットが無いため、ワールドが
  鏡像になっていないか等はユーザー自身の実機確認が必要。
- `CoordinateBasisValidator`の「Right/Up/Forwardの重複」チェックは、直交性チェックが先に走るため
  実際には到達不能なコードである(重複した軸同士の内積は必ず1になり直交性チェックが先に
  例外を投げる)。エラー自体は発生するが、メッセージは「重複」ではなく「非直交」になる
  (`CoordinateBasisValidatorTests.DuplicateAxes_ThrowsViaOrthogonalityCheck`で確認済みの
  reverse-engineered挙動)。
