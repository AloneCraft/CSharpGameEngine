# ADR-0025: RUFセマンティック座標系の完成

## ステータス
承認済み

## 背景

`Docs/RUF-Semantic-Coordinate-Specification-Prompt.md`により、ゲームコードからUnity/Unreal/Blenderの
物理XYZ配置・行列規約の差を隠す、RUF(Right/Up/Forward)意味座標系の導入依頼があった。

調査の結果、このマイルストーンは**土台(コア契約・型)のみ孤立した状態で実装済み、残りは未着手**と
判明した:

- `src/CSharpGameEngine.Spatial.Abstractions/`にRUFのコア型(`RufVector`/`XyzVector`/
  `RufQuaternion`/`ICoordinateConvention`/`ITransformConvention`/`StandardTransformConvention`/
  `CoordinateHandedness`/`PhysicalAxis`/`CameraBasis`/`DirectionalLightBasis`/`SpotLightBasis`/
  `CoordinateBasisValidator`/`UnitConversion`/`Spatial<TConvention>`/`SpatialConfigurationException`)
  は実装済みで、コード中コメントは仕様に忠実だった(`RufQuaternion.cs`は
  `RufQuaternionTests.Multiply_AppliesChildThenParent`という未実装テストの存在を前提に書かれていた)。
- `src/CSharpGameEngine.Spatial.Unity/`・`.Unreal/`・`.Blender/`はcsprojのみで、具体的な
  `ICoordinateConvention`実装が0件だった。
- `tests/CSharpGameEngine.Spatial.Tests/`はcsprojのみでテストコードが0件だった。
- 上記5プロジェクトは`CSharpGameEngine.slnx`に未登録だった。
- `Docs/Specifications/Spatial-RUF.md`(ソースコードのコメントが既に参照していた)、専用ADR、
  Milestoneが存在しなかった。

本ADRは、この状態から仕様書自身の完了条件を満たすところまでを完成させるための決定を記録する。

## 決定事項

### Unity/Unreal/Blenderの具体的Conventionは単純な成分並べ替えとして実装する
`UnityCoordinateConvention`(R=+X,U=+Y,F=+Z,LeftHanded)、`UnrealCoordinateConvention`
(R=+Y,U=+Z,F=+X,LeftHanded)、`BlenderWorldConvention`(R=+X,U=+Z,F=+Y,RightHanded)はいずれも
仕様書のGolden Valuesと一致する固定の成分並べ替えのみで、heap割当・分岐が一切ない。

### Blenderは`BlenderWorldConvention`1つのみ実装し、Export別Conventionへの分割は見送る
仕様書は「Blenderの差異は曖昧な一種類へまとめず、必要に応じて`BlenderGltfExportConvention`/
`BlenderFbxExportConvention`のように名前を分けてください」としているが、これは「必要に応じて」の
任意事項であり、Blender用のImport/Exportパイプライン自体が本マイルストーンの対象外であるため
(要件が確定していない)、Blenderワールド座標のGolden Valuesと一致する`BlenderWorldConvention`
1つのみを実装し、分割は[[Spatial-RUF]]の「未確定事項」に明記した。

### コンパイル時Preset選択は再利用可能な`.props`ファイルとして実装する
`src/CSharpGameEngine.Spatial.Abstractions/SpatialPresetSelection.props`を新設した。
`$(SpatialPreset)`が`Unity`/`Unreal`/`Blender`のいずれか1つの場合のみ対応するConvention
プロジェクトへの`ProjectReference`を追加し、対応する`DefineConstants`
(`SPATIAL_PRESET_UNITY`等)を設定する。カスタムMSBuildタスクは使わず、`<Target
BeforeTargets="BeforeBuild;CoreCompile">`内の`<Error>`条件式だけで「0件選択」「未知の値」の
両方をBuild Errorにする — `SpatialPreset`は単一の文字列プロパティなので、「複数選択」は
実質的に「未知の値」(例: `Unity;Unreal`)として同じ経路で弾かれる。

### `ProjectSpatial`ファサードの実例として`samples/SpatialPresetDemo`を新設する
仕様書の「型ベース呼び出し」節が要求する「選択済みConvention用Facade」の実例が無いと、
`SpatialPresetSelection.props`が実際に使われる形を証明できない。`samples/AosSoaGeneratedExpansion/`
と同じ「実行可能な設計サンプル」の前例に倣い、`samples/SpatialPresetDemo/`を追加した。
`ProjectSpatial.cs`は`#if SPATIAL_PRESET_UNITY / SPATIAL_PRESET_UNREAL / SPATIAL_PRESET_BLENDER`
(コンパイル時のみ分岐)で`Spatial<TConvention>`を1つに固定して転送する。

### `SpatialPresetDemo`は`CSharpGameEngine.slnx`に登録しない(意図的な逸脱)
実装当初は`Docs/Architecture.md`の「slnx登録ルール」に従い`/samples/`フォルダへ登録したが、
`dotnet build CSharpGameEngine.slnx`を実行すると`SpatialPreset`未指定により
`SpatialPresetSelection.props`のBuild Errorで**ソリューション全体のビルドが失敗する**ことが
実際のコマンド実行で判明した。これは仕様書自身が要求する「既存のビルドとテストを壊さないでください」
に反する。`SpatialPresetDemo`にだけ既定Presetを与えると、今度は「Preset未指定がBuild Errorになる」
という仕様書の保証事項自体を証明できなくなる(既定値が常に補われてしまうため)。
両立できないため、`SpatialPresetDemo`は`CSharpGameEngine.slnx`に登録しない
`samples/AosSoaGeneratedExpansion/`とは異なり明示的な`-p:SpatialPreset=`指定を必須とする
スタンドアロンサンプルとして扱う判断とした。`Docs/Architecture.md`の登録ルールは元々
`src/`/`tests/`/`tools/`配下のみを対象としており(`samples/`は対象外)、この判断はルールに反しない。

## 代替案として検討したもの

- **`SpatialPresetDemo`に既定Preset(例: `Unity`)を設定してslnxに残す案**: ソリューション全体の
  ビルドは通るが、「0件選択がBuild Errorになる」ことを`dotnet build
  samples/SpatialPresetDemo/SpatialPresetDemo.csproj`(既定値なし)単体では証明できなくなる。
  仕様書の完了条件「Conventionの未選択／複数選択を検出できる」を弱めることになるため不採用。
- **`SpatialPresetSelection.props`自体を緩め、未選択時に既定Presetへフォールバックする案**:
  仕様書が明示的に「0件選択をBuild Errorにする」ことを保証事項としているため不採用。
- **Blenderを`BlenderGltfExportConvention`/`BlenderFbxExportConvention`に最初から分割する案**:
  Blender用Import/Exportパイプライン自体が未着手で、両者の実際の差分要件(Export設定の
  Forward/Up軸等)が確定していないため、確定していない仕様を推測で決めることになり不採用
  (仕様書「未確定事項は推測で確定せず、仕様書へ明示してください」に従う)。

## 帰結

- `CoordinateBasisValidator`の「Right/Up/Forwardの重複」チェック(`if (right == up || up ==
  forward || forward == right)`)は、直交性チェックが先に走るため実際には到達不能なコードだと
  判明した。重複した2軸(いずれも単位長)の内積は必ず1になるため、`ValidateOrthogonal`が先に
  例外を投げる。エラー自体は発生するため仕様書の要件(「Right、Up、Forwardの重複」を明確な
  エラーにする)は満たしているが、実際に投げられるメッセージは「重複」ではなく「非直交」になる。
  既存コード(`CoordinateBasisValidator.cs`)は無改修のまま、この挙動を
  `CoordinateBasisValidatorTests.DuplicateAxes_ThrowsViaOrthogonalityCheck`とこのADR・
  [[Spatial-RUF]]に記録するにとどめた(skill.mdの「Do not rewrite intended specifications to
  match accidental bugs. Instead record a conformance deviation.」に従う)。
- 既存`CSharpGameEngine.Graphics.D3D12/Camera.cs`・`GltfImporter`/`FbxImporter`とのRUF統合は
  今回実施していない(仕様書自身の対象外リストと「次のマイルストーンへ進まないでください」に
  基づく意図的な対象外)。

## 検証結果(実際に実行して確認)

```
dotnet build CSharpGameEngine.slnx -c Debug     → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Release   → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx               → 397件成功、失敗0(既存339 + Spatial.Tests新規58)
```

`SpatialPresetSelection.props`のPreset選択(`samples/SpatialPresetDemo`、slnx外の直接ビルド):

```
dotnet build samples/SpatialPresetDemo/SpatialPresetDemo.csproj -p:SpatialPreset=Unity
  → 成功。Unity/Abstractionsのみ参照。
dotnet build samples/SpatialPresetDemo/SpatialPresetDemo.csproj -p:SpatialPreset=Unreal
  → 成功。Unreal/Abstractionsのみ参照。
dotnet build samples/SpatialPresetDemo/SpatialPresetDemo.csproj -p:SpatialPreset=Blender
  → 成功。Blender/Abstractionsのみ参照。
dotnet build samples/SpatialPresetDemo/SpatialPresetDemo.csproj
  → 失敗(意図通り)。error: SpatialPreset is not set.
dotnet build samples/SpatialPresetDemo/SpatialPresetDemo.csproj -p:SpatialPreset=Bogus
  → 失敗(意図通り)。error: Unrecognized SpatialPreset 'Bogus'.
```

Preset別の実行結果(Golden Valuesと実測が一致することを確認):

```
dotnet run --project samples/SpatialPresetDemo -p:SpatialPreset=Unity
  SpatialPreset = Unity / Forward(RUF)=Ruf(0,0,1) / Forward(XYZ)=Xyz(0,0,1) / round-tripped=Ruf(0,0,1)
dotnet run --project samples/SpatialPresetDemo -p:SpatialPreset=Unreal
  SpatialPreset = Unreal / Forward(RUF)=Ruf(0,0,1) / Forward(XYZ)=Xyz(1,0,0) / round-tripped=Ruf(0,0,1)
dotnet run --project samples/SpatialPresetDemo -p:SpatialPreset=Blender
  SpatialPreset = Blender / Forward(RUF)=Ruf(0,0,1) / Forward(XYZ)=Xyz(0,1,0) / round-tripped=Ruf(0,0,1)
```

いずれもXYZ側の値が仕様書のGolden Values(Unity Forward=(0,0,+1)、Unreal Forward=(+1,0,0)、
Blender Forward=(0,+1,0))と完全に一致し、RUF→XYZ→RUFの往復も無劣化だった。

`CSharpGameEngine.Spatial.Tests`(58件、`BasisTests`/`RoundTripTests`/`HandednessTests`/
`TransformConventionTests`/`RufQuaternionTests`/`CameraBasisTests`/`LightBasisTests`/
`CoordinateBasisValidatorTests`/`UnitConversionTests`/`ImportAxisConversionTests`/
`DeterminismTests`)はすべて`dotnet test CSharpGameEngine.slnx`に含まれる形で成功している。
なお仕様書の`dotnet test -p:SpatialPreset=Unity`のようなPreset別テスト実行は、
`CSharpGameEngine.Spatial.Tests`自体が3 Preset全てを1回のテスト実行内で検証する設計
(各ConventionをGeneric型引数として直接参照)のため該当せず、Preset選択の実証は上記の
`samples/SpatialPresetDemo`ビルド/実行コマンドで行った。

## 未解決事項

- 既存Rendering/Import層とのRUF統合([[Spatial-RUF]]「未確定事項」参照)。
- `LocalTransform`/`WorldTransform`型の実装(既存`CSharpGameEngine.Scene.Transform`との整理が必要)。
- Blenderの`GltfExport`/`FbxExport`Convention分割。
- `CoordinateBasisValidator`の重複軸チェックが到達不能である点(既存コードは無改修のまま記録のみ)。

## 関連機能
- F-L1-001 — RUF/XYZセマンティック座標コア型と`ICoordinateConvention`契約
- F-L1-002 — `RufQuaternion`/`ITransformConvention`/`StandardTransformConvention`
- F-L1-003 — Unity/Unreal/Blenderの具体的Convention実装
- F-L1-004 — コンパイル時Preset選択(`SpatialPresetSelection.props`)と`ProjectSpatial`ファサード例
- F-L2-003 — `CameraBasis`/`DirectionalLightBasis`/`SpotLightBasis`

## ADR-0034による一部Superseded(6分割構成とSpatialPreset機構)

[[ADR-0034-Project-Consolidation]]により、本ADRの次の2点は**Superseded**である。
RUF意味座標系の設計・型・変換規則・Golden Values はすべて無変更で有効である。

### 1. 6プロジェクト分割 → `CSharpGameEngine.Core`へ統合

`CSharpGameEngine.Spatial.Abstractions` / `.Spatial.Unity` / `.Spatial.Unreal` /
`.Spatial.Blender` / `.Spatial.Gltf` / `.Spatial.Fbx` の6アセンブリは
`CSharpGameEngine.Core`1つへ統合された。**名前空間は6つとも無変更**であり、
本ADRのGolden Values表が完全修飾型名で書かれていても無改修で正しいままである。

統合の根拠は実測値である: Spatial系5プロジェクトは**合計132行**(平均26行、大半がXMLドキュメント
コメント)しかなく、`Spatial.Fbx`と`Spatial.Gltf`は数学的に同一で、ファイル内コメントにも
「`Import.Fbx`が`Import.Gltf`を参照しなくて済むように分けただけ」と書かれていた。

### 2. `SpatialPresetSelection.props`(コンパイル時Preset選択機構)の廃止

`SpatialPresetSelection.props`と、その唯一の使用例だった`samples/SpatialPresetDemo`を削除した。

この機構の利用者は`samples/SpatialPresetDemo`だけで、しかも`.slnx`に未登録だったため
**通常ビルドで一度も実行されていなかった**。エンジン本体は
[[ADR-0026-Camera-Geometry-Ruf-Integration]]で`UnityCoordinateConvention`に固定済みであり、
`Spatial.Unreal`/`Spatial.Blender`の本番利用はゼロだった。

**失われたのは「コンパイル時に1つだけリンクする」機構だけ**である。Convention型そのものは
`CSharpGameEngine.Core`内に名前空間ごと残っているため、実行時に選択して使うことは引き続きできる。
本ADRの検証結果に転記済みの実測値も証跡として残っている。
