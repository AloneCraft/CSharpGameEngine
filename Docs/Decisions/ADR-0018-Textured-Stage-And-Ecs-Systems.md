# ADR-0018: テクスチャ付きPBRステージとECSシステム群

## ステータス
承認済み

## 背景

ADR-0017でエクスポートしたUEレベルをUserGameProjectで再現するには、ADR-0016の`StageRenderer`
(単色Lambertian・ハードコード光源・`MeshCreateInfo`1メッシュ=1マテリアル=1オブジェクト固定)
では要件を満たせない。要求されたのは: (1)静止/可動オブジェクトの区別、(2)ライト、
(3)カラーテクスチャ、(4)ノーマルマップ、(5)ラフネス、(6)ECSとして実装した入力抽象化を
プレイヤーに結びつけた操作、(7)可能な限りの部分をECSで実装、の7点。

`CSharpGameEngine.Ecs`にはQuery/System基盤が無く(ADR-0015時点で「要件確定時に再検討」と
された段階のまま)、`World`にはエンティティ列挙API自体が無い
(`CreateEntityCore`/`GetComponentRef`/`IsAlive`/`Destroy`のみ)。55個のステージプロップを
`Program.cs`のローカル変数として個別に持つADR-0016の方式は、この規模ではもう成立しない。

## 決定事項

### `StageRenderer`をin-place進化(新規クラス複製はしない)

ADR-0004は「複製して後で統合」の前例(`ModelRenderer`と`TriangleRenderer`を意図的に別クラス
にした)を作ったが、今回はこれを踏襲しない。理由: `StageRenderer`の実際のコンシューマは
`UserGameProject/Program.cs`と`StageGpuIntegrationTests`のみで、両方ともステージ用途。
旧コントラクト(`MeshCreateInfo`: Position+Normal+Indices+BaseColorFactor、1:1のmesh=material=object)
は、`ModelRenderer`が既に確立していた「テクスチャ無しなら1×1フォールバックテクスチャで
代替する」パターンで完全に包含できるため、3つ目の類似レンダラーを複製する利益が無いと
判断した。旧`Create(IReadOnlyList<MeshCreateInfo>)`はアダプタとして残し、
`StageGpuIntegrationTests`は無改修のまま通ることを確認した。

新設計はメッシュ・マテリアル・オブジェクトを分離する(`StageMeshCreateInfo`/
`StageMaterialCreateInfo`/`StageObjectCreateInfo`、`src/CSharpGameEngine.Graphics.D3D12/StageRenderInputs.cs`):
- 頂点layoutにTangent(float4)・UVを追加(Position/Normal/Tangent/UVの4要素、48バイトstride)。
- マテリアル毎に3テクスチャ(BaseColor/Normal/MetallicRoughness、`ModelRenderer`の
  SRV+静的サンプラパターンを踏襲)をシェーダー可視CBV_SRV_UAVヒープにまとめ、
  オブジェクトの描画時に`materialIndex*3`のオフセットでディスクリプタテーブルを切り替える。
  テクスチャ未指定のスロットは1×1フォールバック(BaseColor/MR=白、Normal=(128,128,255)=
  タンジェント空間の(0,0,1))で埋め、`ModelRenderer.DecodeOrFallback`と同じ「常に本物のSRVを
  バインドする」方針を踏襲。
- `FrameCB`(b0)にカメラ位置・ディレクショナルライト方向/色・アンビエント色・フォグパラメータを
  追加、`ObjectCB`(b1)にラフネス/メタリックファクターを追加。各`Set*`メソッド
  (`SetCamera`/`SetDirectionalLight`/`SetAmbientLight`/`SetFog`)は定数バッファ内の
  自分の担当バイト範囲だけを書くため、呼び出し順序に依存しない。
- **sRGBは今回導入しない**: 既存の`ModelRenderer`もバックバッファも一貫して素の
  `R8G8B8A8_UNORM`でガンマ補正を一切行っていないため、テクスチャだけsRGBビューにすると
  逆に不整合になる。既存の「ガンマ非対応」という前提を変えないことを優先した
  (本格的なガンマ補正パイプラインは別途の課題とする)。

### シェーディングモデル: GGX Cook-Torrance

Blinn-Phongではなく**GGX NDF + Smith height-correlated可視性項 + Schlick Fresnel**
(`assets/Shaders/Stage.hlsl`)を採用した。理由: glTFのmetallic-roughnessセマンティクスを
そのまま(単位変換無しに)消費できる。F0は`lerp(0.04, albedo, metallic)`。実装量は
既存のLambert1行に対して約25行の追加で収まった。

### 簡易Exponential Height Fog

UEの本格的なExponentialHeightFog(第2フォグ層・inscatteringキューブマップ等)は再現せず、
`fogFactor = saturate(1 - exp(-density * exp(-heightFalloff * max(worldY, 0)) * viewDistance))`
のみをシェーダーに実装した(基準高度は常にY=0固定、UE側の実際のフォグコンポーネント高さは
無視)。

### `Camera`の一人称モード追加

`Distance == 0`のとき`GetView()`の`CreateLookAt(eye, Target, ...)`が`eye==Target`で退化する
問題を、`Distance > 1e-4f`で分岐し一人称時は`eye + GetForward()`を注視点にすることで解消した。
`CreateFirstPerson`ファクトリでは`Target`フィールドがそのまま目の位置を兼ねる
(オービットカメラで毎フレーム`Target`をプレイヤー位置に更新している既存の慣習と同じ発想)。
ピッチクランプ範囲もオービット用の定数(`MinPitchRadians`/`MaxPitchRadians`、-80°/+40°)とは
別に`MinPitchRadiansFirstPerson`/`MaxPitchRadiansFirstPerson`(±85°)をインスタンスフィールド
(`PitchClampMinRadians`/`PitchClampMaxRadians`)として追加し、既存の
`ApplyMouseDelta_ClampsPitchToConfiguredRange`テストが参照する静的定数の値自体は変更していない。

### `World.CollectViews<T>`(クエリ基盤ではない、完全一致アーキタイプの列挙のみ)

55個のステージプロップを`Program.cs`のローカル変数として個別に持つのは現実的ではないため、
`World`に最小限の列挙APIを追加した:
```csharp
public void CollectViews<T>(List<T> results) where T : struct, IEcsArchetype<T>
```
`T`の正規化済みコンポーネント集合に**完全一致**するアーキタイプのストレージのみを対象にする
(複数アーキタイプにまたがるフィルタ・マッチングは行わない)。存在しないアーキタイプに対しては
ストレージを新規作成する副作用を持たない(`TryGetValue`のみ、`GetOrCreateStorage`は使わない)。
`ArchetypeStorage`に`internal Entity GetEntity(int row)`を追加しただけの、ADR-0015の
既存設計に対する最小限の拡張であり、Query/System基盤(複数アーキタイプの動的フィルタリング、
`in`/`ref`アクセス修飾子ごとの列挙等)の導入はまだ行わない。

### Static/Movable = アーキタイプの選択(フラグコンポーネントではない)

`StageProp`(Position/Rotation/Scale/RenderObjectRef)と`MovableStageProp`
(StageProp+Velocity)を別アーキタイプとして定義し、`SceneLoader`が`scene_meta.json`の
`mobilityOverrides`を見てスポーン時にどちらのアーキタイプで`world.Create`するかを決める。
ADR-0015時点から確立している「移動しないオブジェクトは専用の軽量アーキタイプにする」
(`StaticProp`)という設計をそのまま踏襲し、bool flagコンポーネントは追加しなかった
(可動である可能性のあるオブジェクトだけが`Velocity`列のコストを払う)。

### システム = staticクラス+`Program.cs`内の明示的呼び出し順(スケジューラ無し)

`UserGameProject/Systems/`に`InputSystem`・`PlayerControlSystem`・`CameraSystem`・
`MovingPropSystem`・`RenderSyncSystem`を追加した。いずれも`World`や`IEcsArchetype`の
仕組みには手を入れず、既存の`CharacterMotion`と同じ「pure-ish関数、呼び出し順は
`Program.cs`が決める」というADR-0016の方針をそのまま複数ファイルに分割しただけである。
本格的なスケジューラ(依存関係解決・並列実行)の導入は行わない。

`InputState`は`Player`アーキタイプに直接持たせた(要件6「入力抽象化はプレイヤーに
結び付ける」の文言に忠実)。`InputHub`のような独立したシングルトンアーキタイプも設計時に
検討したが、`PlayerControlSystem`が`player.InputState`を直接読む方が単純でプレイヤーに
紐づくという要件にも合致するため、実装せず`InputState`はPlayerに一本化した。
`InputSystem.Update(Win32Window, ref InputState, ref bool wasJumpKeyDown)`が
唯一`Win32Window`に触れる箇所であり、これより下流の全システムは`InputState`という
データ構造のみを見る(入力バックエンドを差し替えてもこれらのシステムは無改修で済む)。

## 代替案として検討したもの

- **StageRenderer v2を新規クラスとして複製する**: ADR-0004の前例に忠実だが、旧コントラクトが
  新コントラクトに完全に包含されるため複製の利益が無いと判断し不採用。
- **本格的なQuery/System基盤(複数アーキタイプマッチング)の導入**: 今回必要なのは
  「1つの厳密なアーキタイプの全エンティティを毎フレーム列挙する」だけであり、
  フィルタ・複数アーキタイプ結合は不要。過剰実装を避けるため`CollectViews`に留めた。
- **Blinn-Phong+ラフネスの簡易変換**: glTFのmetallic-roughnessをBlinn-Phongの光沢指数に
  変換するアドホックな式が別途必要になり、コード量の節約にもならないためGGXを採用した。
- **`InputHub`シングルトンアーキタイプ経由での入力配信**: 設計時に検討したが、要件6の
  「プレイヤーに結び付ける」という文言、および単純さを優先し不採用(上記参照)。

## 帰結

- 55オブジェクトの一部(`SM_Cube16`/`SM_Cube17`)がMovableアーキタイプでスポーンされ、
  `MovingPropSystem`が経過時間ベースの連続回転(ベースライン位置を覚える必要が無い、
  ドリフトしない設計)で毎フレーム更新する。
- `Camera.MinPitchRadiansFirstPerson`/`MaxPitchRadiansFirstPerson`はオービット用の
  `MinPitchRadians`/`MaxPitchRadians`より広い(±85° vs -80°/+40°)。
- `World`に列挙APIが追加されたことで、将来Query/System基盤を作る際は`CollectViews`の
  スコープ(完全一致のみ)を超える部分だけを新規実装すればよい。

## 検証結果(実際に実行して確認)

- `dotnet test CSharpGameEngine.slnx`: 全184件成功、失敗0件
  (`CSharpGameEngine.Ecs.Tests`22件・`CSharpGameEngine.Import.Gltf.Tests`18件・
  `UserGameProject.Tests`21件・`CSharpGameEngine.Graphics.D3D12.Tests`71件・
  `CSharpGameEngine.Core.Tests`13件・`CSharpGameEngine.Import.Fbx.Tests`7件・
  `CSharpGameEngine.CodeAnalysis.Tests`20件・`CSharpGameEngine.Compiler.Hlsl.Tests`7件・
  `CSharpGameEngine.VSExtension.Tests`(net472)5件)。
- `StageGpuIntegrationTests`にWARPアダプタでの新規テストを3件追加し実行して確認:
  2マテリアル(赤/青の異なるBaseColorFactor)を持つ2オブジェクトが、それぞれ正しい
  ディスクリプタテーブルオフセットのマテリアルで描画されること(赤支配的画素・青支配的画素の
  両方が実際のフレームに存在する)。`SetDirectionalLight`/`SetAmbientLight`の呼び出しが
  実際にレンダリング結果を変える(無光/無アンビエント時はほぼ黒、光を入れると明確に明るくなる、
  RGB合計で200以上の差)ことを実測。既存の`Stage_Warp_DebugLayer_Verify_RendersGroundAndCharacter`
  (旧API経由)も無改修のまま成功を維持。
- `World.CollectViews`にEcs.Testsで5件のテスト(空アーキタイプへの副作用無し、生成順での
  列挙、destroy後のswap-back反映、他アーキタイプとの混同無し、既存リストへの追記であり
  置換ではないこと)を追加し全て成功。
- `UserGameProject.exe`(`dotnet run --project UserGameProject`)を実際に起動し、
  プロセスが`Responding=True`のまま10秒以上生存しCPU時間が蓄積し続けること
  (=メインループが実際に毎フレーム動作していること)を確認した上で終了させた
  (M0004参照)。

## 未解決事項

- ガンマ補正/sRGB非対応は今回引き継いだままの制約(上記参照)。
- 一人称カメラの前後左右の直感一致は、ADR-0016のオービットカメラ同様、実機での
  マウス/キーボード操作による確認はできていない。
- 本格的なQuery/System基盤・動的Add/Remove(構造変更)は未着手のまま。

## 関連機能
- F-L0-001 — `Camera`(一人称モード追加)
- F-L0-002 — `StageRenderer`(テクスチャ・PBR・データ駆動ライティング・フォグに拡張)
- F-L0-005 — `GltfImporter.ImportScene`
- F-L3-004 — `World.CollectViews<T>`
- F-L9-003 — `SceneLoader`とステージスポーン
- F-L9-004 — ECS入力抽象化とゲームプレイSystems群
