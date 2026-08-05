# ADR-0017: UE5.8レベルエクスポートパイプライン

## ステータス
承認済み(**「エンジンのワールド空間はglTF空間と同一」という宣言は[[ADR-0027-Gltf-Fbx-Import-Axis-Conversion]]により上書き済み** — 2026-08-05追記。M0008でエンジンのワールド空間は`UnityCoordinateConvention`(左手系)と再宣言され、`GltfImporter`が内部でglTF空間からの軸変換を行うようになった。本ADRの決定事項自体(UnrealEditor-Cmd.exe+Pythonでのレベル全体1GLBエクスポート等)は無効化されていない)

## 背景

`G:\UE\5.8\WFC\Content\FirstPerson\Lvl_FirstPerson.umap`(UE5.8 FirstPersonテンプレート系マップ)を
解析し、`UserGameProject`に同等のステージをECS駆動で再現する依頼があった(Docs/20260803-2.txt)。
調査の結果、このレベルはWorld Partition / One File Per Actor形式で、実体は
`Content/__ExternalActors__/FirstPerson/Lvl_FirstPerson/`配下の63個の`.uasset`に分散しており、
StaticMeshActor 55体(SM_Cube×20・SM_QuarterCylinder×12・SM_Cylinder×9・SM_Ramp×12・
SM_SkySphere×1・Floor×1)・DirectionalLight×1・SkyLight・SkyAtmosphere・ExponentialHeightFog・
VolumetricCloud・PlayerStartで構成されることが判明した(実際にUEエディタを起動し
`get_all_level_actors()`で数え上げて確定させた値。バイナリ`.uasset`内の文字列頻度からの
概算では「Cube20/QuarterCylinder20/Ramp12」という誤った内訳を得ていたため、実測に置き換えた)。

マテリアルは`MI_FirstPersonColorway`(`M_PrototypeGrid`のインスタンス)で、チェッカーグリッド
柄になることを想定していたが、実際にベイクした結果は**大半が単色ソリッドカラーへの
フラットベイク**だった(後述)。

`.uasset`をC#で直接パースする(CUE4Parse方式)アプローチは今回不採用とした。理由:
プロシージャルマテリアルの見た目はUEのマテリアルグラフを実際に評価しないと再現できず、
UE本体を使わないパーサではテクスチャベイクができない。一方、UE 5.8エディタが
`C:\Program Files\Epic Games\UE_5.8`に既にインストール済みで、`GLTFExporter`プラグイン
(`Engine\Plugins\Enterprise\GLTFExporter`、コンパイル済みDLL同梱・ビルド不要)が
使えることを確認した。

## 決定事項

### 方式: UnrealEditor-Cmd.exe + Pythonでのレベル全体1GLBエクスポート

`tools/UnrealExport/export_lvl_firstperson.py`を新規作成し、以下のコマンドで実行する
(`WFC.uproject`にはPython/GLTFExporterプラグインが未登録なのでコマンドラインで有効化。
マテリアルベイクはGPUレンダリングを伴うため`-nullrhi`は使わない):
```
UnrealEditor-Cmd.exe "G:\UE\5.8\WFC\WFC.uproject" -ExecutePythonScript="...\export_lvl_firstperson.py" -EnablePlugins=PythonScriptPlugin,GLTFExporter -stdout -unattended -NoSplash
```

**設計の核心**: `GLTFExporter.h`/`GLTFLevelExporter.h`/`GLTFLightConverters.cpp`のソースを直接
確認した結果、`UGLTFExporter::ExportToGLTF(Object=UWorld, FilePath, Options, SelectedActors=空集合)`
で**レベル全体を1つのGLBとしてエクスポート**すると、UE側が以下を全て自動処理することが
分かった:
- UE(cm・Z-up・左手系)→glTF(m・Y-up・右手系)の座標変換(ノード変換行列レベルで実施)
- StaticMeshComponentごとのマテリアルオーバーライド解決+ベイク(アセット単体エクスポートでは
  拾えないインスタンス差分も反映される)
- DirectionalLightの`KHR_lights_punctual`変換(`export_lights=True`で有効化。方向は
  ライトノードの変換行列に、`Intensity`/`LightColor`は単位変換なしでそのまま
  `Light.Intensity`/`Light.Color`に格納されることをソースで確認済み)

このため、Pythonスクリプトが自前でやる座標変換・マテリアル解決・光源変換は不要になった。
Python側で個別対応が必要なのは、GLTFExporterが構造的に触れない4つだけ:
**SkyLight(アンビエント近似)・ExponentialHeightFog・PlayerStart(非表示アクタなので唯一
手動変換が要る箇所)・Mobilityタグ(glTFに概念がない)**。これらは補助ファイル
`scene_meta.json`に出力する。

エンジンのワールド空間は本マイルストーンでは**glTF空間(右手系・Y-up・メートル)と同一**と
宣言する。`src/CSharpGameEngine.Spatial.*`(RUF座標系抽象、別スペック
`Docs/RUF-Semantic-Coordinate-Specification-Prompt.md`)は今回スコープ外(既存の空
スキャフォールドのまま変更しない)。

### `unreal.GLTFExporter.export_to_gltf`の実際のPython呼び出し形状(実行して確定)

C++シグネチャは`bool ExportToGLTF(UObject*, FString, UGLTFExportOptions*, TSet<AActor*>&, FGLTFExportMessages&)`
だが、`OutMessages`が非const参照の出力引数のため、Pythonバインディング生成器は
これを呼び出し引数から除外し、**戻り値(本来の`bool`)を`OutMessages`自体に置き換える**。
5引数で呼ぶと`takes at most 4 arguments`、`bool, messages = ...`とアンパックすると
`cannot unpack non-iterable GLTFExportMessages object`になることを実際に実行して確認し、
最終的に`messages = unreal.GLTFExporter.export_to_gltf(world, path, options, set())`
(戻り値は`GLTFExportMessages`単体、成功判定は`messages.errors`の有無で行う)に落ち着いた。
UE公式Pythonリファレンスにもこの挙動の明記は無く、実行時エラーメッセージから逆算して確定させた。

### 出力ファイル(コミット対象、`UserGameProject/Content/`とは別)

```
UserGameProject/Assets/Stage/Lvl_FirstPerson/
  Lvl_FirstPerson.glb   (55メッシュノード+1ライトノード、埋め込みベイク済みテクスチャ)
  scene_meta.json        (ambient/fog/playerStart/mobilityOverrides)
```

`scene_meta.json`の`mobilityOverrides`は既定で全アクタ`"static"`、実証用に`SM_Cube16`・
`SM_Cube17`の2体だけPythonスクリプト内で`"movable"`に上書きする。

### Content生コピーとGit方針

`G:\UE\5.8\WFC\Content\`(140MB、241ファイル)を`UserGameProject\Content\`へrobocopyで
全量コピーした(実行時には読まない参照用ミラー)。`.gitignore`に`UserGameProject/Content/`を
追加し、リポジトリには変換済みGLB/JSONのみコミットする(生の`.uasset`をGit履歴に含めない)。

## 代替案として検討したもの

- **CUE4Parseなど純C#での`.uasset`直接パース**: プロシージャルマテリアルのベイクにUE本体の
  マテリアルグラフ評価が必須なため不採用。
- **3つのユニークメッシュ(SM_Cube/SM_QuarterCylinder/SM_Ramp)を個別にアセットエクスポート**:
  StaticMeshアセット単体をエクスポートすると、そのメッシュの**デフォルトマテリアルスロット**が
  使われ、レベル上のアクタが実際に使っているコンポーネント側マテリアルオーバーライドを
  拾えない。UWorldをオブジェクトとして渡す方式に切り替えることでこの問題自体を回避した。
- **アクタ単位で個別に手動座標変換して`scene.json`に書き出す**: GLTFExporterが既に
  UE→glTFの変換をノード変換行列として正しく行うため、二重に手計算する必要がなく、
  変換ミスのリスクも高い。PlayerStart(エクスポート対象外の非表示アクタ)のみ、
  この手動変換が例外的に必要になる。

## 帰結

- 同一メッシュでもインスタンスごとにベイク結果が異なりうる(`M_PrototypeGrid`が
  ワールド座標依存の場合)ため、GLB内のユニークメッシュ数は3種ではなく実際には26種になった
  (`LogicalMeshes=26`、55ノードの一部が同一メッシュを共有、一部は個別ベイク)。
  `CSharpGameEngine.Import.Gltf.ImportScene`(ADR-0018参照)はメッシュ・マテリアル共有を
  前提にしない設計にした。
- `MI_FirstPersonColorway`は実際には**大半がフラットカラーへのベイク**(`SM_Ramp5`で確認:
  BaseColor=(1, 0.332, 0, 1)のオレンジ単色、Normal/MetallicRoughnessともテクスチャ無し・
  フラットファクターのみ)であり、当初懸念していた「ワールド座標依存グリッドパターンの
  UVベイク崩れ」は実質的に問題にならなかった(21/23マテリアルは実テクスチャ、
  残り2マテリアルはフラットファクター)。
- `LogGLTFExporter`の警告(`SM_QuarterCylinder`のライトマップUV(channel 0、TEXCOORD_1)が
  54.24%重複)は実際に発生したが、これはセカンダリUV(ライトマップ用)についてであり、
  `ImportScene`が読むプライマリUV(TEXCOORD_0、ベースカラー/ノーマル/MR用)には影響しない。
- WorldPartitionの未ロードセルによりアクタが欠落するリスクを想定していたが、実行結果は
  `staticMeshActors: 55`+`directionalLights: 1`=期待通りのノード数(56)と一致し、
  今回のレベル規模では発生しなかった。

## 検証結果(実際に実行して確認)

- `run_export.cmd`を実際に3回実行した(初回2回はPython側の引数エラーで失敗、上記の
  `export_to_gltf`呼び出し形状を修正して3回目で成功)。UEのログにDataflowモジュールの
  無関係な既存エラー(本作業と無関係、UE起動時に常に出るもの)以外のエラーは無し。
- 生成された`Lvl_FirstPerson.glb`(1.9MB)を`SharpGLTF.Schema2`で直接ロードし、
  `LogicalNodes=63`(トップレベル62、1件のみネスト)・`LogicalMeshes=26`・
  `LogicalMaterials=23`・`LogicalPunctualLights=1`・`LogicalTextures=21`を確認した。
  `SM_Ramp5`ノードがユーザー例示通り実在し、Transform(T=(-18,0,-12), S=(3,2,4))・
  `TANGENT`/`TEXCOORD_0`/`TEXCOORD_1`アクセサ完備を確認した。
- `scene_meta.json`が`ambient`(colorRgb=[1,1,1], intensity=1.0の実測SkyLight値)・
  `fog`(density=0.0436, heightFalloff=0.2の実測ExponentialHeightFog値)・
  `playerStart`(translation=[0, 3.02, -0])・`mobilityOverrides`(55アクタ全件、
  SM_Cube16/17のみmovable)を正しく含むことを確認した。

## 未解決事項

- `EditorLevelLibrary`/`LevelEditorSubsystem`のうちどちらが5.8時点で非推奨かは実装時に
  確認せず両対応にしていない(スクリプトは`LevelEditorSubsystem`+`UnrealEditorSubsystem`を
  使用、実行して動作することのみ確認済み)。
- `ExponentialHeightFogComponent`の色プロパティ(`FogInscatteringColor`は`_DEPRECATED`)は
  実装時に厳密なプロパティ名を特定せず、固定のニュートラルグレーで代替した
  (`scene_meta.json`の`fog.colorRgb`は実測値ではない)。
- World Partitionのセルが今回より大きいレベルで一部未ロードになるケースは未検証
  (`WorldPartitionEditorSubsystem`でのセル強制ロードは未実装)。

## 関連機能
- F-L7-005 — UEレベルエクスポートパイプライン(`tools/UnrealExport`)
