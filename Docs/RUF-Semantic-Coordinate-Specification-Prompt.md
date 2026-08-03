# C# Game Engine — RUF Semantic Coordinate Specification Prompt

C# Game Engineに、座標系を意味論的なRUF（Right / Up / Forward）で統一する仕様と、その最小実装基盤を追加してください。

この作業の目的は、ゲームコードからUnity、Unreal Engine、Blenderなどの物理XYZ配置や行列規約の差を隠し、すべてのゲームコードをRUFという共通の意味座標で記述できるようにすることです。

最初にリポジトリ全体と、次の文書を確認してください。

- `AGENTS.md`
- `CLAUDE.md`
- `.github/copilot-instructions.md`
- `.github/skills/CSharpGameEngine-Documentation/SKILL.md`
- `Docs/Architecture.md`
- `Docs/Implementation-Status.md`
- `Docs/Conformance.md`
- `Directory.Build.props`
- `Directory.Packages.props`
- 既存のSpatial、Math、Transform、Rendering、Import関連コード
- 既存のADRとMilestone文書

既存設計を無視して全面的に変更しないでください。
既存のビルドとテストを壊さないでください。

## 製品名

ユーザー向け表示名: `C# Game Engine`

内部識別名: `CSharpGameEngine`

ファイル名、プロジェクト名、Assembly名、Root Namespaceには`#`を使用しないでください。

## 用語

RUFは次の意味座標を表します。

- R: Right
- U: Up
- F: Forward

RUFは物理的なメモリ配置やXYZ成分名ではありません。
RUFはゲーム上の意味を表す絶対的なセマンティック座標です。

すべてのプロジェクトで、次の意味は不変です。

- Rightは右方向
- Upは上方向
- Forwardは前方向
- BackwardはForwardの反対
- LeftはRightの反対
- DownはUpの反対

ゲームコードは原則としてXYZの意味を直接仮定せず、RUFを使用します。

## 最重要設計原則

次の層を分離してください。

1. Semantic Coordinate — ゲーム上の意味としてのRight、Up、Forward。
2. Physical XYZ Mapping — RUFが実際のX、Y、Zのどの成分へ対応するか。
3. Coordinate Handedness — Left-handedまたはRight-handed。
4. Transform Convention — 行ベクトル／列ベクトル、行列の積順、親子Transform合成。
5. Storage Layout — Vector型やMatrix型のメモリ上の配置。
6. External Tool Convention — Unity、Unreal Engine、Blender、FBX、glTFなど外部ツールの座標規約。

これらを一つの巨大な`CoordinateSystem`型へ混在させないでください。
特に、座標軸のマッピングと行列の掛け順を同じ概念として扱わないでください。

## RUFの不変仕様

```csharp
RufVector.Right
RufVector.Left
RufVector.Up
RufVector.Down
RufVector.Forward
RufVector.Backward
```

ゲームコードから見た意味は、プロジェクトプリセットにかかわらず変化してはいけません。

```csharp
RufVector direction = RufVector.Forward;
```

これはUnity、Unreal、Blenderのどのプリセットでも「ゲーム上の前方向」を表します。

## 初期プリセット

### Unity互換

```text
R = +X
U = +Y
F = +Z
Handedness = Left-handed
```

### Unreal互換

```text
R = +Y
U = +Z
F = +X
Handedness = Left-handed
```

### Blender交換用

```text
R = +X
U = +Z
F = +Y
Handedness = Right-handed
```

Blenderについては次を分離してください。

- Blenderワールド座標
- Export設定のForward／Up
- Meshのローカル前方向
- Cameraのローカル視線方向
- Lightのローカル方向

Blender Cameraがローカル`-Z`を見ることと、ゲーム上のForwardを同じ概念として扱わないでください。

## プロジェクト構成

既存構成へ合わせつつ、概ね次を検討してください。

```text
src/
  CSharpGameEngine.Spatial.Abstractions/
  CSharpGameEngine.Spatial.Unity/
  CSharpGameEngine.Spatial.Unreal/
  CSharpGameEngine.Spatial.Blender/

tests/
  CSharpGameEngine.Spatial.Tests/
```

用途のない抽象プロジェクトや空プロジェクトを増やさないでください。

## TFM

通常の共有Spatial契約は`net10.0`を基本としてください。

Windows専用機能を必要としない限り`net10.0-windows`にしないでください。
Visual Studio Extensibilityのアウトプロセス拡張と共有する場合も、両側が`net10.0`なら同一の共有ライブラリとして利用できる構成にしてください。

古い.NET Framework互換をSpatial Runtime全体へ持ち込まないでください。

## 契約API

```csharp
public interface ICoordinateConvention
{
    static abstract CoordinateHandedness Handedness { get; }

    static abstract XyzVector ToXyz(in RufVector value);

    static abstract RufVector FromXyz(in XyzVector value);

    static abstract XyzVector RightXyz { get; }

    static abstract XyzVector UpXyz { get; }

    static abstract XyzVector ForwardXyz { get; }
}
```

要件:

- RUFからXYZへ変換できる
- XYZからRUFへ逆変換できる
- Right、Up、Forwardの物理XYZ方向を取得できる
- Handednessを明示できる
- Runtime Reflectionを必要としない
- Hot pathで毎回分岐しない

## RUF型

```csharp
public readonly struct RufVector
{
    public float R { get; }
    public float U { get; }
    public float F { get; }
}
```

または既存Vector型を包む薄いblittable wrapperでも構いません。

要件:

- unmanagedまたはblittable
- Heap Allocationなし
- `Right`、`Up`、`Forward`を明示
- RUFとXYZの暗黙変換を乱用しない
- Debuggerで確認しやすい
- Equalityと文字列表現の規約を明確化

境界の原則:

```text
Public API / Authoring / Import / Export
→ RUF型を優先

ECS Chunk / SIMD / Renderer内部
→ 必要に応じて生のblittable Vectorを利用
```

## コンパイル時プリセット選択

```xml
<PropertyGroup>
  <SpatialPreset>Unity</SpatialPreset>
</PropertyGroup>
```

```xml
<ItemGroup Condition="'$(SpatialPreset)' == 'Unity'">
  <ProjectReference Include="..\CSharpGameEngine.Spatial.Unity\CSharpGameEngine.Spatial.Unity.csproj" />
</ItemGroup>

<ItemGroup Condition="'$(SpatialPreset)' == 'Unreal'">
  <ProjectReference Include="..\CSharpGameEngine.Spatial.Unreal\CSharpGameEngine.Spatial.Unreal.csproj" />
</ItemGroup>

<ItemGroup Condition="'$(SpatialPreset)' == 'Blender'">
  <ProjectReference Include="..\CSharpGameEngine.Spatial.Blender\CSharpGameEngine.Spatial.Blender.csproj" />
</ItemGroup>
```

保証事項:

- Conventionが一つだけ選ばれる
- 0件選択をBuild Errorにする
- 複数選択をBuild Errorにする
- Hot pathでRuntime分岐しない
- ビルド結果が決定的
- 各Presetを個別にテストできる

## 型ベース呼び出し

```csharp
public static class Spatial<TConvention>
    where TConvention : struct, ICoordinateConvention
{
    public static XyzVector ToXyz(in RufVector value)
        => TConvention.ToXyz(value);

    public static RufVector FromXyz(in XyzVector value)
        => TConvention.FromXyz(value);
}
```

アプリケーションコード全体へ毎回`TConvention`を書かせず、選択済みConvention用Facadeを用意してください。

```csharp
RufVector direction = ProjectSpatial.Forward;
XyzVector native = ProjectSpatial.ToXyz(direction);
```

Source Generatorを使ってFacadeを生成しても構いませんが、RUF仕様自体をSGへ依存させないでください。

## Transformとの分離

RUFが決めるもの:

- Right / Up / Forwardの意味
- RUFとXYZの対応
- Handedness
- 外部ツールからの軸変換

Transform Conventionが決めるもの:

- Scale / Rotation / Translationの合成
- 行ベクトルまたは列ベクトル
- Matrix multiplication order
- Parent / Child合成順
- Point / Vector / Normal変換

```csharp
public interface ITransformConvention
{
    static abstract Matrix4x4 Compose(
        in RufVector position,
        in RufQuaternion rotation,
        in RufVector scale);

    static abstract Matrix4x4 Combine(
        in Matrix4x4 parentWorld,
        in Matrix4x4 local);

    static abstract RufVector TransformPoint(
        in Matrix4x4 transform,
        in RufVector point);

    static abstract RufVector TransformDirection(
        in Matrix4x4 transform,
        in RufVector direction);

    static abstract RufVector TransformNormal(
        in Matrix4x4 transform,
        in RufVector normal);
}
```

Transformの意味上の標準適用順:

```text
Scale
→ Rotation
→ Translation
```

行列コード上の積順はConventionへ閉じ込めてください。

## Quaternion仕様

最低限、次を文書化してください。

- Positive rotationの向き
- Handednessとの関係
- Axis-angle変換
- Euler角の適用順
- Quaternion multiplicationの意味
- Parent / Child回転の合成順
- Forward方向を向くQuaternionの生成
- LookRotationでUpとForwardが平行な場合の挙動

Euler角をRuntime内部の正とせず、Authoring／Inspector／Import／Export用表現として扱ってください。

## Cross Product仕様

`Vector3.Cross`をエンジン全体へ無制限に露出しないでください。

```csharp
RufVector Cross(in RufVector left, in RufVector right);
```

または意味を限定したAPIを用意してください。

```csharp
RufVector RightFromUpForward(
    in RufVector up,
    in RufVector forward);
```

各PresetでRight、Up、Forwardの外積関係とHandednessをテストしてください。

## Object Basis

座標系とObject Basisを分離してください。

### Mesh Forward

Meshの正面または進行方向。Import時にRUFへ正規化してください。

### Camera Forward

Cameraが見る方向です。

```text
Camera.Forward = 視線方向
```

### Directional Light Forward

光線が進む方向です。

```text
Light.Forward = 光の進行方向
DirectionToLight = -Light.Forward
```

### Spot Light Forward

Spot coneが伸びる方向です。

### Point Light

通常はForwardを持ちません。必要ならAuthoring用Orientationを別に持たせてください。

## Coordinate Spaces

最低限次の空間を明示してください。

```text
Asset Space
Object Local Space
Parent Space
World Space
View Space
Clip Space
NDC
Screen Space
Texture Space
Semantic RUF Space
Physical XYZ Space
```

悪い例:

```csharp
Vector3 Convert(Vector3 value);
```

良い例:

```csharp
RufVector AssetToSemantic(in XyzVector value);
RufVector LocalToWorldDirection(...);
ClipPosition WorldToClip(...);
ScreenPosition ClipToScreen(...);
```

## Units

```text
Distance: meter
Angle API: radians
Authoring UI: degrees表示可能
Time: seconds
Scale: dimensionless
```

軸変換と単位変換を別段階にしてください。

```text
External axis conversion
→ Unit conversion
→ Semantic RUF normalization
```

## Import / Export

Import:

```text
External Source Coordinates
→ External Tool Convention
→ Semantic RUF
→ Engine Storage XYZ
```

Export:

```text
Engine Semantic RUF
→ Target Tool Convention
→ Target XYZ
```

Import後のRuntimeデータへUnity、Unreal、Blender座標を混在させないでください。

必要に応じて次のmetadataを保存してください。

```text
Source Tool
Source Up Axis
Source Forward Axis
Source Handedness
Source Unit Scale
Import Conversion Version
```

## Rendering境界

最低限次を明記してください。

- View space forward direction
- Projection matrix convention
- Depth range
- Near / Far mapping
- NDC Y direction
- Screen origin
- Texture UV origin
- Front face winding
- Normal transformation
- Tangent basis handedness

RUFがForwardを定義していても、D3D12のClip／NDC規約まで自動的に決まるわけではありません。

## Public APIの命名

推奨:

```csharp
CameraForward
LightTravelDirection
DirectionToLight
SurfaceNormal
ViewDirection
MovementForward
MeshAuthoringForward
```

避けるもの:

```csharp
Direction
Axis
Vector
ForwardVector
```

## パフォーマンス要件

- RUF／XYZ変換でHeap Allocationしない
- Conversionはinline可能な小さな処理
- Preset判定をHot pathで行わない
- Reflectionなし
- DictionaryからConventionを検索しない
- Virtual dispatchをHot pathで使わない
- Boxingなし
- ECS Chunk処理へ不要なWrapper変換を強制しない
- SIMDを妨げない
- Import境界では明確性を優先してよい
- Runtime内部では変換回数を最小化

## Source Generatorとの関係

将来、SGで次を生成しても構いません。

```text
ProjectSpatial facade
Preset選択コード
Convention ID
Serialization metadata
Inspector表示
Unity / Unreal変換補助
座標系診断
```

ただし、このマイルストーンではSGの使用を必須にしないでください。

## Analyzer候補

将来候補として記録してください。

- Game codeで生のXYZ軸定数を使っている
- `Vector3.UnitX`をRightと仮定している
- `Vector3.UnitY`をUpと仮定している
- `Vector3.UnitZ`をForwardと仮定している
- 生のCross Productを使用している
- Camera ForwardとView Matrix Forwardを混同している
- Light ForwardとDirectionToLightを混同している
- Import境界外で外部Tool座標を使用している

このマイルストーンでAnalyzerを実装する必要はありません。

## テスト

### Basis Tests

各Presetで次を検証してください。

- Right、Up、ForwardのXYZ値
- 各軸が単位長
- 各軸が互いに直交
- Left = -Right
- Down = -Up
- Backward = -Forward

### Round Trip Tests

```text
RUF → XYZ → RUF
XYZ → RUF → XYZ
```

ランダム値を使う場合は固定Seedを使用してください。

### Handedness Tests

BasisのHandednessが宣言値と一致すること。

### Transform Tests

- Identity
- Translation only
- Rotation only
- Uniform Scale
- Non-uniform Scale
- Scale → Rotation → Translation
- Parent / Child合成
- TransformPoint
- TransformDirection
- TransformNormal

### Camera Tests

- Camera Forwardが視線方向
- LookAt／LookRotationが期待方向を向く
- UpとForwardが平行に近い場合の挙動が決定的

### Light Tests

- Directional Light Forwardが光の進行方向
- DirectionToLightが反対方向
- Spot Light Forwardがcone方向

### Import Tests

- Unity XYZからRUF
- Unreal XYZからRUF
- Blender XYZからRUF
- 各変換のRound Trip
- Unit conversionとAxis conversionの分離

### Determinism Tests

- Cultureで結果が変わらない
- BuildごとにBasis定義が変わらない
- Runtime Hash Codeを永続仕様へ使わない

## Golden Values

```text
Unity:
Right   = (+1, 0, 0)
Up      = (0, +1, 0)
Forward = (0, 0, +1)

Unreal:
Right   = (0, +1, 0)
Up      = (0, 0, +1)
Forward = (+1, 0, 0)

Blender exchange:
Right   = (+1, 0, 0)
Up      = (0, 0, +1)
Forward = (0, +1, 0)
```

Blenderの差異は曖昧な一種類へまとめず、必要に応じて名前を分けてください。

```text
BlenderWorldConvention
BlenderGltfExportConvention
BlenderFbxExportConvention
```

## エラー処理

次を明確なエラーにしてください。

- 不正なBasis
- 軸が直交していない
- 軸の長さが0
- Right、Up、Forwardの重複
- Conventionが未選択
- Conventionが複数選択
- Import設定のForwardとUpが同一
- Unit scaleが0以下
- 非有限値
- LookRotationのForwardが0

Runtime Hot pathで過剰な例外検査をせず、Authoring／Import／Build時に可能な限り検証してください。

## ドキュメント

```text
Docs/
├ Architecture.md
├ Implementation-Status.md
├ Reverse-Engineered-Spec.md
├ Conformance.md
├ Specifications/
│  └ Spatial-RUF.md
├ Decisions/
│  └ ADR-XXXX-RUF-Semantic-Coordinates.md
└ Milestones/
   └ MXXX-RUF-Semantic-Coordinates.md
```

`Spatial-RUF.md`には最低限、次を記録してください。

- RUFの目的
- RUFは意味座標であること
- XYZは物理配置であること
- Unity、Unreal、Blenderの対応
- Handedness
- Transform／Matrix Conventionとの分離
- Camera Forward
- Mesh Forward
- Light Forward
- DirectionToLight
- Object Basis
- Coordinate Spaces
- Units
- Import／Export
- Rendering境界
- Public API
- Performance原則
- 未確定事項

ADRには次を記録してください。

- RUFをゲームコードの意味座標として採用
- XYZ軸の意味をゲームコードへ露出しない
- Project Presetで物理XYZ対応をコンパイル時選択
- Handednessを明示
- Transform／Matrix規約を別契約にする
- Import境界で外部座標を正規化
- Runtime内で複数外部座標系を混在させない
- Hot pathでRuntime分岐しない
- RUF wrapperを全内部データへ強制しない

## L0〜L9上の位置

```text
L1 — Core / Numerics
- RUF value types
- XYZ value types
- Coordinate convention contracts
- Transform math contracts

L2 — Schema / Spatial
- LocalTransform
- WorldTransform
- Parent
- Camera basis
- Light basis

L5 — Runtime Features
- Camera
- Lighting
- Rendering extraction

L6 — Artifacts
- Imported asset coordinate metadata

L7 — Compiler / Bake
- Import coordinate normalization
- 将来のSG／Analyzer

L8 — Visual Studio Authoring
- Preset選択UI
- Axis表示
- Import設定
- Diagnostics
```

下位レイヤーからVS SDKやRoslynを参照しないでください。

## このマイルストーンで実装しないもの

- 完全なFBX Importer
- 完全なglTF Importer
- Unity Project Importer
- Unreal Project Importer
- Blender Add-on
- Visual Studio Preset UI
- Coordinate Analyzer
- Refactoring Provider
- Source Generator
- GPU Projection Pipeline全体
- Camera Editor
- Gizmo Renderer
- Physics変換
- Network Serialization
- Scene Migration Tool

必要な契約と最小テストだけを実装してください。

## コード品質

- public APIを最小限にする
- Right、Up、Forwardの意味をコメントだけに頼らない
- Runtime Reflectionを使わない
- mutable static stateを使わない
- 安定IDへ`GetHashCode()`を使わない
- Culture依存処理を避ける
- Hot pathで不要なinterface dispatchを使わない
- ConventionをService Locatorから取得しない
- ConventionをGlobal mutable settingにしない
- Release buildでも検証する
- Import conversionとRuntime transformを混在させない
- Point、Direction、Normalを同じ変換関数へ押し込めない
- Camera ForwardとLight Forwardの符号を曖昧にしない

## 実行検証

```text
dotnet restore
dotnet build
dotnet test
```

必要ならPreset別にも実行してください。

```text
dotnet test -p:SpatialPreset=Unity
dotnet test -p:SpatialPreset=Unreal
dotnet test -p:SpatialPreset=Blender
```

すべての警告と失敗を報告してください。

## 完了条件

次をすべて満たした場合のみ完了としてください。

- RUFの意味仕様が文書化されている
- XYZとの違いが明記されている
- Unity、Unreal、Blenderのマッピングが定義されている
- Handednessが定義されている
- RUFからXYZへの変換が実装されている
- XYZからRUFへの逆変換が実装されている
- Round Trip Testが成功する
- Basis Testが成功する
- Transform Conventionと分離されている
- Camera Forwardが視線方向として定義されている
- Light Forwardが光の進行方向として定義されている
- DirectionToLightが反対方向として定義されている
- Import境界が文書化されている
- Runtime Hot pathにPreset分岐がない
- Conventionの未選択／複数選択を検出できる
- Architecture、ADR、Milestone、Implementation Statusが更新されている
- 既存BuildとTestが成功する

## 完了報告

最後に次を報告してください。

1. 作成・変更したファイル
2. 追加したプロジェクト
3. プロジェクト間の依存方向
4. 選択したTFM
5. RUFの公開API
6. Unityマッピング
7. Unrealマッピング
8. Blenderマッピング
9. Handednessの定義
10. Transform Conventionとの境界
11. Camera Forwardの定義
12. Light Forwardの定義
13. DirectionToLightの定義
14. Import／Export境界
15. Unit Test件数
16. Preset別Test結果
17. 実行したコマンド
18. Build結果
19. Test結果
20. 更新したFeature ID
21. Conformance結果
22. 未確定の仕様
23. 次のマイルストーン候補

未確定事項は推測で確定せず、仕様書へ明示してください。
次のマイルストーンへ進まないでください。

---

> RUFはゲーム上の意味、XYZは選択された物理表現であり、ゲームコードは物理XYZマッピングへ依存しない。
