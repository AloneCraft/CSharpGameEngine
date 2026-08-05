# ADR-0027: glTF/FBXインポート境界の軸変換

## ステータス
承認済み(ADR-0017の該当宣言を上書きする)

## 背景

[[ADR-0026-Camera-Geometry-Ruf-Integration]]でCamera/Geometryの内部実装をRUF化する過程で、
既存の承認済みADRとの実際の矛盾が判明した:

- `Docs/Decisions/ADR-0017-UE-Level-Export-Pipeline.md`(57-60行目)は「エンジンのワールド空間は
  glTF空間(**右手系**・Y-up・メートル)と同一」と明示的に宣言済みで、これに基づき
  `GltfImporter.ImportScene`は一切の軸変換を行っていなかった(意図的な設計、バグではない)。
- 一方、Camera.cs/ProceduralMeshFactory.cs/PlayerControlSystem.csは一貫して「**左手系**」の関係
  (`Cross(Right,Up)==Forward`)を前提にしており、これは`UnityCoordinateConvention`
  (ADR-0025)と数値的に完全一致する。

ユーザーは**Unity互換(左手系)を正とし、ADR-0017を上書きする**ことを選択した。これにより
Camera/Geometry/ゲームプレイ側は無改修で済む代わりに、glTF/FBXインポート境界に**初めて実際の
軸変換**を入れる必要が生じた — これがこのマイルストーンで最もリスクの高い変更である
(F-L9-003で実機確認済みのUE FirstPersonステージの位置・法線・ライト方向の数値が実際に変わる)。

## 決定事項

### `GltfCoordinateConvention`/`FbxCoordinateConvention`を新設する
glTFファイル自体の規約(右手系・Y-up・カメラ/`KHR_lights_punctual`ライトは既定でローカル-Z方向)を
表す`GltfCoordinateConvention`(`src/CSharpGameEngine.Spatial.Gltf/`、新規プロジェクト)、
AssimpNetの既定出力規約(AssimpNet公式XMLドキュメント確認: 「右手系、+Xが右、+Zが視点側(＝Forward
は-Z)、+Yが上」)を表す`FbxCoordinateConvention`(`src/CSharpGameEngine.Spatial.Fbx/`、新規
プロジェクト)を追加した。両者は数値的に同一の定義(R=+X,U=+Y,F=-Z,RightHanded)だが、あえて
別々の型として実装した — `CSharpGameEngine.Import.Fbx`が`CSharpGameEngine.Import.Gltf`に依存する
ことなく独立を保つため、また将来どちらかの規約だけが変わる可能性に備えるため。いずれも既存
Unity/Unreal/Blender Conventionと同じパターン(`Spatial.Abstractions`のみ参照、`Spatial.Tests`に
Golden Value/RoundTrip/Handednessテストを追加)に従う。

### `GltfImporter.ImportScene`は公開APIを出る時点でエンジン(Unity互換)空間にする
新設`GltfAxisConversion`(`CSharpGameEngine.Import.Gltf`、public — 理由は後述)が変換を担う:
- **Translation**: `Spatial<GltfCoordinateConvention>.FromXyz`→`Spatial<UnityCoordinateConvention>.
  ToXyz`(実質Z成分の符号反転)。
- **Rotation**: 素朴な成分反転ではなく、ミラー行列`C=diag(1,1,-1)`による共役変換`R'=C·R·C`→
  `Quaternion.CreateFromRotationMatrix(R')`で四元数を再構築する(回転を含むノードで平行移動だけ
  反転させると基底が破綻するため必須)。
- **Scale**: 大きさは軸反転で変わらないためそのまま。
- 変換後を`StandardTransformConvention.Compose`で再合成し`ImportedSceneNode.WorldTransform`として
  格納する。
- **メッシュローカル頂点データ**(`ImportPrimitiveGeometry`内の`Positions`/`Normals`)も同様に
  変換し、三角形の頂点順序(`indices`)を`FlipTriangleWinding`で入れ替える(単一軸ミラーはカイラリティ
  を反転させるため)。TANGENTアクセサが存在する場合は`w`(bitangent符号)も反転が必要
  (`cross(mirror(a),mirror(b)) = -mirror(cross(a,b))`という単一軸ミラーの代数的性質から導出、
  ADR本文に検算過程を残す)。TANGENTが存在せず`ComputeTangents`で合成する場合は、既に変換済みの
  位置/法線/UV/indicesを渡すことで、追加の符号補正なしに正しいHandednessが自然に導出されることを
  確認した(スワップ不変性: 三角形の第2/第3頂点を入れ替えてもtangent/bitangentの計算結果は
  数式上変化しないことを代数的に確認済み)。

この結果、**`GltfImporter`の公開APIから出た時点ですべてEngine(Unity互換)空間になる**ため、
`SceneLoader.cs`(PlayerStartを除く、後述)・`StageRenderer.cs`・`ModelRenderer.cs`は無改修で済む。

`GltfAxisConversion`は当初`internal`としたが、`UserGameProject.SceneLoader`がPlayerStart変換
(後述)のために同じ変換ロジックを必要としたため`public`にした。

古い`GltfImporter.Import(string)`(単一プリミティブのプレビュー専用API、VSIXのアセットプレビュー
ブリッジ`tools/CSharpGameEngine.Import.Gltf.Cli`が使う)は対象外とした — 自身のXMLコメントが
「minimal vertical slice」と明記する通りプレビュー専用であり、実機確認済みのゲームプレイ
コンテンツ(UE FirstPersonステージ)には使われない。

### `SceneLoader.cs`のPlayerStartにも同じ変換を適用する
`tools/UnrealExport/export_lvl_firstperson.py`を確認した結果、`scene_meta.json`の
`playerStart.translation`/`yawRadians`は**UE生値ではなく、Pythonが既にUE→glTF変換を適用した後の
値**であることが判明した(`ue_to_gltf_translation`のコメント: "UE centimeters, left-handed Z-up ->
glTF meters, right-handed Y-up"、Epic公式`FGLTFCoreUtilities`の規約に一致)。つまりPlayerStartは
`.glb`ファイルを経由しないが、そこに書かれている数値は他のノードと同じglTF空間の値であるため、
`GltfAxisConversion.ConvertWorldTransform`と同じ変換をSceneLoader側で明示的に適用する必要がある。

Yaw角についても、純粋なY軸回転はミラーの不動軸(Y)まわりの回転であるため、共役変換
`R'=C·R·C`を適用すると**角度の符号が反転する**ことを導出した(pure Y-rotation行列の非対角成分
(X-Z結合項)がミラーによって符号反転し、これは`sinθ→-sinθ`、すなわち`θ→-θ`と等価)。
ハンドコードした符号反転ではなく、Yaw角をQuaternionへ変換→`ConvertWorldTransform`と同じ経路を
通す→結果のQuaternionからYaw角を再抽出、という実装にして、Node変換と全く同じコードパスを
再利用することで、将来の変換ロジック変更にも自動的に追従するようにした。

### `SceneLoader.TryImportDirectionalLight`の重複実装を`ImportedLightDirection`へ集約
既存の`Vector3.TransformNormal(-Vector3.UnitZ, worldTransform)`という独自再実装(`ImportedLight`の
XMLコメントが定める契約の重複)を削除し、新設`ImportedLightDirection.WorldForward`
(`CSharpGameEngine.Import.Gltf`、`+Vector3.UnitZ`を適用 — `WorldTransform`が既にEngine空間に
変換済みのため)を呼ぶだけにした。`ImportedLight.cs`のXMLコメントもこの新しい契約に更新した。

### FBXは`FbxCoordinateConvention`でPositions/Normals/Windingのみ変換する
`FbxImporter.Import`はシーングラフを持たない(単一メッシュのみ、glTFの`Import(string)`と同格の
プレビュー専用スコープ)ため、Node WorldTransformの共役変換は不要。`FbxAxisConversion`
(`CSharpGameEngine.Import.Fbx`、internal — 現時点でImport.Fbx内部の1呼び出し元しか無いため)が
`Positions`/`Normals`の変換と`Indices`のWinding反転のみを行う。

Assimpの`PostProcessSteps.MakeLeftHanded`フラグに頼る案(仕様書ドラフト段階での第一候補)は、
AssimpNet公式XMLドキュメントで「既定出力は右手系」という規約自体を確認できたため採用を見送り、
既にglTF側で確立・実測検証済みの明示的な`ICoordinateConvention`パターンを再利用する方が、
未検証の外部ライブラリの挙動に依存するより安全と判断した。

## 代替案として検討したもの
- **Assimpの`ConvertToLeftHanded`系フラグに委譲する案**: AssimpNetの公式ドキュメントで
  Assimpの既定出力規約(右手系・+Z視点側)を直接確認できたため、この規約を明示的な
  `FbxCoordinateConvention`として実装する方が既存glTFパターンとの一貫性が高く、フラグの
  組み合わせ(`MakeLeftHanded`+`FlipWindingOrder`+`FlipUVs`)を実機で試行錯誤する必要もない
  ため不採用。
- **`GltfCoordinateConvention`をFBXでも直接再利用する案**: 数値的には同一の定義だが、
  `CSharpGameEngine.Import.Fbx`が`CSharpGameEngine.Import.Gltf`(または`Spatial.Gltf`という
  "Gltf"を冠した型)に依存する形は、2つのインポーターの独立性を損ない将来の分岐可能性
  (Assimpが正規化しない形式が出てきた場合等)にも対応しづらいため不採用。

## 帰結
- ADR-0017の「エンジンのワールド空間はglTF空間(右手系・Y-up・メートル)と同一と宣言する」は
  **本ADRにより上書き**された。エンジンのワールド空間は`UnityCoordinateConvention`
  (左手系・Y-up・メートル)であり、glTF空間からの変換は`GltfImporter`内部で完結する。
  ADR-0017本文は書き換えず、本ADRへの参照を追記するにとどめた(既存決定が後から覆されたことを
  記録する既存の流儀)。
- 既存`GltfImporterSceneTests.cs`の2ケース(ネスト変換合成・ライト方向)はfixtureのZ成分が
  たまたま0のため、軸変換を入れても**数値が変わらない**(検知力が無いテストになる)ことが
  判明した。Z成分が非ゼロの新規fixtureを2件追加し、変換の実効性を実測で証明した。
- `tests/UserGameProject.Tests/SceneLoaderTests.cs`の`Load_CombinesGlbNodesWithSceneMetaMobility
  AmbientFogAndPlayerStart`は、Cubeノードの`Translation(2,0,3)`(Z非ゼロ)を使っていたため
  `(2,0,-3)`への期待値更新が必要だった。PlayerStartの位置・Yaw角の期待値も同様に更新した
  (位置Zが反転、Yawが符号反転)。

## 検証結果(実際に実行して確認)
```
dotnet test tests/CSharpGameEngine.Spatial.Tests/...csproj      → 68件成功(既存63 + Gltf/Fbx新規5×2)
dotnet test tests/CSharpGameEngine.Import.Gltf.Tests/...csproj  → 20件成功(既存18 + 新規Z非ゼロ2件)
dotnet test tests/CSharpGameEngine.Import.Fbx.Tests/...csproj   → 10件成功(既存7 + 新規変換関数単体3件)
dotnet test tests/UserGameProject.Tests/...csproj               → 41件成功(既存、期待値更新3箇所)
dotnet test tests/CSharpGameEngine.Graphics.D3D12.Tests/...csproj → 60件成功(WARP実描画、既存)
dotnet build CSharpGameEngine.slnx -c Debug/Release              → 成功、警告0/エラー0
dotnet run --project UserGameProject                              → 15秒以上クラッシュせず動作、
  スクリーンショットで空・床・チェッカーグリッド壁が破綻なく描画されることを確認
  (ただし変更前との比較用スクリーンショットが無いため、ミラーしていないか等の完全な視覚的確証は
  ユーザー自身の実機確認が必要、Known deviations参照)
```

## 未解決事項
- `dotnet run --project UserGameProject`のスクリーンショットは「クラッシュしない・破綻していない」
  ことは確認したが、変更前の見た目と直接比較していないため、ワールドが実際に正しい向きで
  (鏡像になっていない状態で)描画されているかはユーザー自身の実機確認が必要。
- `Matrix4x4.Decompose`が失敗する退化した行列(元シーンに奇数回反射が焼き込まれている場合の
  負スケール等)は変換せずそのまま返すフォールバックのみで、明示的な対応はしていない。
- FBXインポータの軸変換は、既存`Box.fbx`フィクスチャが左右対称のため、ファイルベースの
  結合テストでは軸反転を検知できない(変換関数自体の単体テストで代替した)。非対称な`.fbx`
  フィクスチャの追加は将来の課題。

## 関連機能
- F-L1-005 — `GltfCoordinateConvention`(新規)
- F-L1-006 — `FbxCoordinateConvention`(新規)
- F-L0-005 — `GltfImporter.ImportScene`(既存ID更新、軸変換追加)
- F-L9-003 — `SceneLoader`(既存ID更新、PlayerStart変換・重複解消)
