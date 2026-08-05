# M0008: Renderer/Import/ECS内部へのRUF全面統合

## 目的
ADR-0025で完成したRUF(Right/Up/Forward)意味座標系のコア契約・Unity/Unreal/Blender Conventionを、
既存のRendering/Import/ECSゲームプレイ層の**内部実装**まで一貫して適用する。ユーザーが「次の実装」
として明示的に選択した、仕様書自身の推奨(Renderer内部は生Vectorのままでよい)より広いフルスコープ。
設計判断は[[ADR-0026-Camera-Geometry-Ruf-Integration]]・
[[ADR-0027-Gltf-Fbx-Import-Axis-Conversion]]・[[ADR-0028-Ecs-Gameplay-Ruf-Boundary]]の3つに分割して
記録した。

## 実装したこと

### Camera/Geometry(ADR-0026)
- `src/CSharpGameEngine.Graphics.D3D12/Camera.cs`: `GetFlatRight`を`Spatial<UnityCoordinateConvention>.
  RightFromUpForward`へ、`GetForward`/`GetFlatForward`を`RufQuaternion`合成へ書き換え。
  `Camera.BuildViewMatrix`共有ヘルパーを新設し`ModelRenderer.cs`の重複LookAt構築を解消。
- `src/CSharpGameEngine.Geometry/ProceduralMeshFactory.cs`: 全8プリミティブの頂点/法線計算を
  `RufVector`で再構築。

### glTF/FBXインポート境界の軸変換(ADR-0027、最高リスク)
- `src/CSharpGameEngine.Spatial.Gltf/`(新規)・`src/CSharpGameEngine.Spatial.Fbx/`(新規):
  glTF/AssimpNetそれぞれの既定出力規約(右手系・Y-up・Forward=-Z)を表すConvention。
- `src/CSharpGameEngine.Import.Gltf/GltfAxisConversion.cs`(新規): Translation/Rotation(ミラー行列
  共役変換)/メッシュローカル頂点/Tangent/Windingの変換。
- `src/CSharpGameEngine.Import.Fbx/FbxAxisConversion.cs`(新規): Positions/Normals/Windingの変換。
- `UserGameProject/SceneLoader.cs`: PlayerStart(scene_meta.jsonがglTF空間の値を書き出している)にも
  同じ変換を適用、`TryImportDirectionalLight`の`-Vector3.UnitZ`重複実装を`ImportedLightDirection.
  WorldForward`へ集約。
- **ADR-0017の「エンジンワールド空間=glTF空間(右手系)」宣言を上書き**し、エンジンのワールド空間は
  `UnityCoordinateConvention`(左手系)であると再宣言した。

### ECSゲームプレイ層(ADR-0028)
- `UserGameProject/Systems/PlayerControlSystem.cs`/`CameraSystem.cs`: flatForward/flatRight/
  facing Quaternion/EyeHeight加算をRUF経由に書き換え。ECSコンポーネントのフィールドレイアウトは
  無改修。

## 意図的に除外したこと
- `CSharpGameEngine.Scene/Transform.cs`(ADR-0025が既に別マイルストーンへ切り出し済み)。
- Renderer内部のSpatialPreset可変化(Unity決め打ちのまま)。
- Rendering境界そのもの(View/Projection規約・NDC・Winding既定値・Tangent handedness)の正式な
  RUF契約化。
- `GltfImporter.Import(string)`(単一プリミティブのプレビュー専用API)・`ModelGpuIntegrationTests.cs`
  が使う経路 — 実機確認済みのゲームプレイコンテンツ(UE FirstPersonステージ)は`ImportScene`
  経由のみを使うため対象外。

## 変更したFeature ID
- `F-L0-001`(既存ID更新) — `Camera`
- `F-L2-002`(既存ID更新) — `CSharpGameEngine.Geometry`
- `F-L0-006`(新規) — `Camera.BuildViewMatrix`共有ヘルパー
- `F-L1-005`(新規) — `GltfCoordinateConvention`
- `F-L1-006`(新規) — `FbxCoordinateConvention`
- `F-L0-005`(既存ID更新) — `GltfImporter.ImportScene`
- `F-L9-003`(既存ID更新) — `SceneLoader`
- `F-L9-004`(既存ID更新) — ECS入力抽象化とゲームプレイSystems群

## 実行したコマンドと結果
```
dotnet build CSharpGameEngine.slnx -c Debug     → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Release   → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx               → 414件成功、失敗0(M0007時点397 + 本マイルストーン新規17)

内訳(新規17件):
  CSharpGameEngine.Graphics.D3D12.Tests  58→60 (Camera新旧比較シャドウテスト2件)
  CSharpGameEngine.Spatial.Tests         58→68 (Gltf/Fbx Convention Golden Value/RoundTrip/Handedness 5件×2)
  CSharpGameEngine.Import.Gltf.Tests     18→20 (Z非ゼロ軸変換検証2件)
  CSharpGameEngine.Import.Fbx.Tests       7→10 (変換関数単体テスト3件)

dotnet run --project UserGameProject
  → 15秒以上クラッシュせず動作。スクリーンショット(BitBlt直接キャプチャ)で
    空(オレンジ)・床(グレー)・チェッカーグリッド壁が破綻なく描画されることを確認。
    変更前との比較用スクリーンショットは無いため、ミラーしていないか等の完全な視覚的確証は
    ユーザー自身の実機確認が必要(残課題参照)。
```

## リバースエンジニアリングした挙動
- Camera.cs/ProceduralMeshFactory.cs/PlayerControlSystem.csは、これまで一度もRUF文脈で
  意識されないまま、独立に「Y=Up、Z=Forward、X=Right、左手系」という同一の規約を前提にしていた
  ことが判明した。これは`UnityCoordinateConvention`(ADR-0025)と数値的に完全一致する。
- `Camera.GetFlatRight()`のXMLコメントが「実機検証待ちの暫定値」としていた符号は、RUFの
  `RightFromUpForward`(Handedness反映済み、テスト済み)と数式として完全に一致することを確認した
  — 実機フィーリングの検証自体は引き続き未解決だが、少なくとも式としての内部無矛盾性は
  証明された。
- ADR-0017「エンジン空間=glTF空間(右手系)」という宣言は、Camera/Geometry側の「左手系」前提と
  一度も突き合わせて検証されたことがなく、実際に矛盾していた。今回ユーザーの選択により
  左手系側を正としたため、ADR-0017は本マイルストーンで上書きされた。
- `scene_meta.json`のPlayerStartは、Pythonエクスポートスクリプト自身が既にUE→glTF変換を
  適用した後の値であり(コード上のコメントで確認)、他のノードと同じくglTF→Engine変換が
  必要だった。純粋なY軸回転(Yaw)はミラーの不動軸まわりの回転であるため、共役変換の結果
  角度の符号が反転することを導出し、Node変換と同じコードパスを再利用する実装で実証した。

## 意図と観測の一致
Conformance: Matches。[[ADR-0026-Camera-Geometry-Ruf-Integration]]・
[[ADR-0027-Gltf-Fbx-Import-Axis-Conversion]]・[[ADR-0028-Ecs-Gameplay-Ruf-Boundary]]に記載した
意図した設計と、実際のビルド・テスト・実行結果の間に既知の乖離は無い。ただしUserGameProjectの
実機での見た目の妥当性(鏡像になっていないか等)はスクリーンショットの目視のみでの確認であり、
変更前との比較が無い点は未解決事項として記録する。

## 残課題
- `dotnet run --project UserGameProject`の見た目が変更前と完全に一致するかは、ユーザー自身の
  実機確認が必要(F-L9-003の従来の確認方法と同じだが、今回は比較対象のスクリーンショットが無い)。
- `CSharpGameEngine.Scene/Transform.cs`のRUF化(次のマイルストーン候補)。
- Rendering境界(View/Projection規約・NDC・Winding既定値・Tangent handedness)の正式なRUF契約化。
- FBXインポータの軸変換は既存`Box.fbx`フィクスチャが左右対称のため、ファイルベースの結合テストでは
  検知できない(変換関数自体の単体テストで代替、非対称フィクスチャの追加は将来の課題)。
- `Matrix4x4.Decompose`が失敗する退化行列への対応(現状は無変換フォールバックのみ)。
