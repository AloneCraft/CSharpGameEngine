# ADR-0042: Scene Editorギズモ軸はゲーム本体の実行時行列規約へ一致させる

## ステータス

承認済み(2026-08-16)

## 背景

[[ADR-0025-RUF-Semantic-Coordinates]]は`UnityCoordinateConvention.Handedness`を
`LeftHanded`と宣言する。一方、ゲーム本体の`Camera.BuildViewMatrix`/`BuildProjection`と
Scene Designerの`SceneViewCamera.GetView`/`GetProjection`は、どちらも
`Matrix4x4.CreateLookAt`/`CreatePerspectiveFieldOfView`を同じ引数で使う既存規約を持つ。
この組合せでは、既定視点からワールド`+X`を投影するとNDC Xは負、すなわち画面左へ写る。

M0020 Phase 1では、この左右を「左手系宣言との不整合」と誤診してScene Designerだけの行列を
変更し、ゲーム本体との見た目が左右反転したため差し戻した。Phase 3のMoveギズモはワールド軸を
直接可視化するので、同じ判断を曖昧なまま残せない。

参照箇所を全検索した結果、`CoordinateHandedness`は宣言だけの表示ラベルではない。
`Spatial<TConvention>.RightFromUpForward`が左右手系に応じてcross productの符号を選ぶため、
RUF意味座標APIの挙動には実際に影響する。しかしCameraのView/Projection構築、Scene Renderer、
Scene Designerの行列構築はこの値を参照しない。したがって「RUF Conventionの意味上の左右手系」と
「既存Camera行列による画面投影」は現在別の契約であり、片方のラベルだけを変えることも、
Scene Designerだけの行列を変えることも正しい修正ではない。

## 決定事項

### ギズモはスクリーン方向へ再マップせず、実際のワールド軸を表示する

World空間のMoveギズモは`+X=(1,0,0)`、`+Y=(0,1,0)`、`+Z=(0,0,1)`をそのまま使う。
Local空間では対象のワールド回転をこれらへ適用する。画面上で分かりやすい方向へ軸を反転したり、
`GetFlatRight`を「画面右」へ読み替えたりしない。

この帰結として、既定カメラで`+X`ハンドルは画面左へ写る。ハンドルを正方向へドラッグすると
C# SceneのPositionは実際の正方向へ増える。色はX=赤、Y=緑、Z=青を維持し、画面方向ではなく
ワールド軸名を識別させる。ゲーム本体とScene Designerの見た目が一致することを最優先の制約とする。

### ADR-0025の`LeftHanded`宣言は変更しない

この値は`Spatial<TConvention>`の意味演算に使われており、単なる誤表示ではない。Camera行列へ未接続な
ことを本ADRとADR-0025の追記で明示する。将来、エンジン全体のCamera/Importer/既存Scene資産を
同時移行する別ADRなしに変更しない。

### VSIX内に純粋なTransform数学境界を置く

Phase 0で保留していた`TransformMath.TryCalculateLocal`相当の共有方式は、
`SceneTransformMath`としてVSIX内の純粋層へ実装する。VSIXはnet472、Runtime.Windowsはnet10.0専用で
直接参照できない。ランタイムComponent型を共有ソースとしてVSIXへ持ち込まず、同じrow-vector式
(`local * parentWorld`、`world * inverse(parentWorld)`、TRS分解)をテストで相互固定する。

## 代替案として検討したもの

- **Scene Designerだけ`+X`を画面右へ向ける**: ゲーム内の実軸とギズモ軸が逆になり、編集値も
  画面表示も一致しないため不採用。
- **Cameraを左手系行列へ一括変更する**: ゲームの可視結果、深度、Importer軸変換、既存Sceneへ波及する。
  M0020 Phase 3の範囲を超え、Phase 1でScene Designer単独変更が実際に失敗したため不採用。
- **`UnityCoordinateConvention.Handedness`を`RightHanded`へ改名する**: RUF意味演算の実挙動を変え、
  Unity presetのGolden Value契約を壊すため不採用。
- **Runtime.Windowsの`Transform`/`TransformMath`をVSIXへ共有コンパイルする**: VSIXが実行時Component型の
  第二定義を所有することになり、ライブRoslyn解析境界を曖昧にするため不採用。

## 帰結

- `TransformGizmoTests.WorldPositiveXHandle_ProjectsToScreenLeftUsingTheRuntimeCameraConvention`が
  Moveギズモの実軸と投影方向を直接固定する。
- `SceneViewCameraTests.GetFlatRight_ProjectsToScreenLeft_BecauseTheEngineUsesRightHandedMatrices`と
  `ViewAndProjection_MatchTheRuntimeGameConstruction`が、Scene Designerとゲーム本体の行列一致を固定する。
- `CoordinateHandedness`を変えず、Camera行列へ自動接続もしない。両契約の統合は将来の明示的ADR案件。
- Phase 4以降のRotate/Scale/Propertyギズモも同じワールド軸と投影規約を使う。

## 検証結果

```text
rg -n "CoordinateHandedness" src tests tools Docs
  → 実装上の分岐はSpatial<TConvention>.RightFromUpForward。Camera/Renderer/SceneViewCameraからの参照0件。

dotnet test tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj \
  -c Debug --filter "FullyQualifiedName~SceneViewCameraTests|FullyQualifiedName~TransformGizmoTests"
  → 成功(実際の最終全体テスト件数はM0020 Phase 3検証節を参照)

dotnet build CSharpGameEngine.slnx -c Debug
  → 成功、警告0/エラー0
```

## 関連機能

- F-L8-011 — シーンエディタ インタラクション基盤
- [[ADR-0025-RUF-Semantic-Coordinates]]
- [[ADR-0026-Camera-Geometry-Ruf-Integration]]
- [[ADR-0041-Scene-Editor-Interaction-Architecture]]
- [[M0020-Scene-Editor-Interaction]]
