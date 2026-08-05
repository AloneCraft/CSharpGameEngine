# M0007: RUFセマンティック座標系の完成

## 目的
`Docs/RUF-Semantic-Coordinate-Specification-Prompt.md`が要求するRUF(Right/Up/Forward)意味座標系を、
「土台(コア契約・型)のみ孤立して存在」という状態から、仕様書自身が定める完了条件を満たす状態まで
完成させる。設計判断は[[ADR-0025-RUF-Semantic-Coordinates]]、仕様は[[Spatial-RUF]]を参照。

## 実装したこと

### Unity/Unreal/Blenderの具体的Convention(新規)
- `src/CSharpGameEngine.Spatial.Unity/UnityCoordinateConvention.cs`(R=+X,U=+Y,F=+Z,LeftHanded)
- `src/CSharpGameEngine.Spatial.Unreal/UnrealCoordinateConvention.cs`(R=+Y,U=+Z,F=+X,LeftHanded)
- `src/CSharpGameEngine.Spatial.Blender/BlenderWorldConvention.cs`(R=+X,U=+Z,F=+Y,RightHanded)

いずれも固定の成分並べ替えのみ(heap割当・分岐なし)。

### コンパイル時Preset選択(新規)
- `src/CSharpGameEngine.Spatial.Abstractions/SpatialPresetSelection.props`: `$(SpatialPreset)`に
  応じて対応するConventionプロジェクトへの`ProjectReference`と`DefineConstants`
  (`SPATIAL_PRESET_*`)を設定し、未指定/未知の値をBuild Errorにする。
- `samples/SpatialPresetDemo/`(新規、`CSharpGameEngine.slnx`には未登録 — 理由は
  [[ADR-0025-RUF-Semantic-Coordinates]]参照): `ProjectSpatial.cs`が仕様書の「型ベース呼び出し」
  節が要求するFacadeパターンを実例として示す。

### テスト(新規、`tests/CSharpGameEngine.Spatial.Tests/`、58件)
`BasisTests`/`RoundTripTests`/`HandednessTests`/`TransformConventionTests`/`RufQuaternionTests`/
`CameraBasisTests`/`LightBasisTests`/`CoordinateBasisValidatorTests`/`UnitConversionTests`/
`ImportAxisConversionTests`/`DeterminismTests`の11ファイル。仕様書「テスト」節の全カテゴリ
(Basis/Round Trip/Handedness/Transform/Camera/Light/Import/Determinism Tests)に対応。

### ドキュメント
- `Docs/Specifications/Spatial-RUF.md`(新規、既存ソースコードのコメントが既に参照していた
  未作成ファイルの参照切れを解消)
- `Docs/Decisions/ADR-0025-RUF-Semantic-Coordinates.md`(新規)
- `Docs/Architecture.md`(現在のマイルストーンの範囲に追記)
- `Docs/Implementation-Status.md`(F-L1-001〜004、F-L2-003の5エントリ、既存コードへの遡及含む)
- `Docs/Conformance.md`(RE-L1-001〜004、RE-L2-003)

### `CSharpGameEngine.slnx`への登録
`/src/`に4プロジェクト(`Spatial.Abstractions`/`Spatial.Unity`/`Spatial.Unreal`/`Spatial.Blender`)、
`/tests/`に`Spatial.Tests`、`/Docs/Specifications/`に`Spatial-RUF.md`、`/Docs/Decisions/`に
`ADR-0025-...md`、`/Docs/Milestones/`に本ファイルを追加。

## 意図的に除外したこと
- 既存`CSharpGameEngine.Graphics.D3D12/Camera.cs`・`ProceduralMeshFactory.cs`・
  `GltfImporter`/`FbxImporter`とのRUF統合(仕様書自身の対象外リストと「次のマイルストーンへ
  進まないでください」に基づく)。
- `LocalTransform`/`WorldTransform`ECSコンポーネント型(既存`CSharpGameEngine.Scene.Transform`との
  重複整理が必要)。
- `BlenderGltfExportConvention`/`BlenderFbxExportConvention`への分割(Blender用Import/Export
  パイプライン自体が未着手)。
- `samples/SpatialPresetDemo`の`CSharpGameEngine.slnx`への登録([[ADR-0025-RUF-Semantic-Coordinates]]
  参照。登録するとPreset未指定でソリューション全体のビルドが失敗するため)。
- Analyzer/Source Generator/VS Preset UI(仕様書が明示的に対象外としている)。

## 変更したFeature ID
- `F-L1-001`(新規) — RUF/XYZセマンティック座標コア型と`ICoordinateConvention`契約
- `F-L1-002`(新規) — `RufQuaternion`/`ITransformConvention`/`StandardTransformConvention`
- `F-L1-003`(新規) — Unity/Unreal/Blenderの具体的Convention実装
- `F-L1-004`(新規) — コンパイル時Preset選択と`ProjectSpatial`ファサード例
- `F-L2-003`(新規) — `CameraBasis`/`DirectionalLightBasis`/`SpotLightBasis`

## 実行したコマンドと結果
```
dotnet build CSharpGameEngine.slnx -c Debug     → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Release   → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx               → 397件成功、失敗0(既存339 + Spatial.Tests新規58)

dotnet build samples/SpatialPresetDemo/SpatialPresetDemo.csproj -p:SpatialPreset=Unity   → 成功
dotnet build samples/SpatialPresetDemo/SpatialPresetDemo.csproj -p:SpatialPreset=Unreal  → 成功
dotnet build samples/SpatialPresetDemo/SpatialPresetDemo.csproj -p:SpatialPreset=Blender → 成功
dotnet build samples/SpatialPresetDemo/SpatialPresetDemo.csproj                          → 失敗(意図通り)
dotnet build samples/SpatialPresetDemo/SpatialPresetDemo.csproj -p:SpatialPreset=Bogus    → 失敗(意図通り)

dotnet run --project samples/SpatialPresetDemo -p:SpatialPreset=Unity
  → Forward(XYZ)=Xyz(0,0,1)
dotnet run --project samples/SpatialPresetDemo -p:SpatialPreset=Unreal
  → Forward(XYZ)=Xyz(1,0,0)
dotnet run --project samples/SpatialPresetDemo -p:SpatialPreset=Blender
  → Forward(XYZ)=Xyz(0,1,0)
```

## リバースエンジニアリングした挙動
- `CoordinateBasisValidator`の重複軸チェックは、直交性チェックが先に走るため到達不能である
  (重複した単位軸同士の内積は必ず1になるため)。エラー自体は発生するが、メッセージは
  「重複」ではなく「非直交」になる。既存コードは無改修、[[ADR-0025-RUF-Semantic-Coordinates]]と
  [[Spatial-RUF]]の両方に記録した。
- `Spatial<TConvention>.Cross(Right,Up)`は、LeftHandedなUnity/Unrealで`Forward`、RightHandedな
  Blenderで`Backward`になることを3 Presetすべてで実測確認した(`Spatial.cs`の既存コメントが
  主張していた関係と一致)。
- `SpatialPresetSelection.props`を`CSharpGameEngine.slnx`に登録されたプロジェクトから
  参照すると、`SpatialPreset`未指定時に**ソリューション全体の`dotnet build`が失敗する**ことを
  実際に確認した(これが`SpatialPresetDemo`をslnx未登録にした直接の理由)。

## 意図と観測の一致
Conformance: Matches。[[ADR-0025-RUF-Semantic-Coordinates]]に記載した意図した設計と、実際の
ビルド・テスト・実行結果の間に、上記1件(重複軸チェックの到達不能性)を除いて既知の乖離は無い。
この1件も「エラーにする」という完了条件自体は満たしており、メッセージ文言のみの差異として
記録済み。

## 残課題
- 既存Rendering/Import層とのRUF統合(次のマイルストーン候補)。
- `LocalTransform`/`WorldTransform`型の実装。
- Blenderの`GltfExport`/`FbxExport`Convention分割。
- `CoordinateBasisValidator`の重複軸チェックの到達不能性(修正は今回のスコープ外、記録のみ)。
