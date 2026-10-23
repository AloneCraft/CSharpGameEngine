# M0105 — 実行時の生成と使い捨てエフェクト
- 日付: 2026-10-22
- 状態: 完了(2026-10-22)

## 目的
ADR-0078 の「クリック地点の目印」(TopDown の `FX_Cursor_Success`)のために、ゲームの実行中に Entity と Scene を作り、描き、寿命で消せるようにする。計画の段階 1 の 2。調査では、次の 3 つが妨げだった。
1. `LevelLoad` が描画物の一覧を初回に固定する。後から作った StaticMesh は描かれない。
2. `FixedTransform.cs` が `new Entity(EntityId)` で Generation=1 固定のため、スロットを再利用した Entity の位置解決が失敗する疑い(`Templates/README.md` に記録)。
3. Scene を実行中に位置つきで作る型付きの呼び出しも、寿命つきの Entity も無い。

## 事実(再現して確かめたこと)
- 疑いは本当だった。`World.IsAlive` は Generation まで比べるので、Generation 2 以上の Entity を Generation 1 の `Entity` で引くと `TryGetComponent` が false を返し、`FixedTransformWorldResolver` は MissingTransform を返した。親も同じ(`Parent.Value.Id` から作り直していた)。
  - 修正前の再現: `RecycledEntityTransformTests` の 2 件(自分の位置、親としての位置)が失敗し、描画まで通した `RenderRosterTests` の 3 件(再利用したスロットの Entity が描かれない)も失敗した。修正後は全件成功。
- 描画物は、レンダラーの作成時に大きさが決まる設計だった(オブジェクトの定数バッファ、マテリアルの SRV ヒープ、メッシュの配列)。`RenderServer` は、デバイスを失ったときの再構築のために、同じ一覧を持っている。
- 生成されたループの後始末が、`sealed` で `IDisposable` でないサービスを `is IDisposable` で調べていて、CS8121 でコンパイルできなかった(`Spawner` を足して初めて現れた、`[EngineService]`(M0103)の実バグ)。

## 決定
### 1. 描画物の増減(`LevelLoad` の固定をやめる)
- レンダラーの作成だけは、これまでどおり、レベルを読んだ時点の Entity から作る(作成に最初の一覧が要る)。それ以降は一覧を固定せず、Entity の増減を毎フレーム差分で描画へ反映する。
- レンダラー(`StageRenderer`、`RenderServer`)に、後から足せる口を作った: `AddObject`・`RemoveObject`(スロットを再利用)、`AddMesh`、`AddMaterial`。余裕は `DynamicObjectHeadroom`(4096 個)と `DynamicMaterialHeadroom`(256 個)。メッシュは別々のバッファなので、描画中のものには触らない。マテリアルのテクスチャの転送は GPU を 1 回待つので、まだ誰も使っていない資産の最初の 1 体は 1 フレーム引っかかる。余裕を使い切った追加は断り(`AddObject` は -1、診断を 1 回)、描かない。
- バインドの System(`StaticMeshBind` など)は `CameraOriginSync` の後、`TransformSync` の前に置く(Presentation は System 単位で回るので、カメラ原点が今のフレームのものになってから、描画物の位置を置く)。
- 追加したオブジェクトの定数は、いまのフレームの枠にだけ書く(他の枠は、描画中のフレームが読むかもしれない)。毎フレーム `SetObjectTransform` で全部の枠が埋まる。
- デバイスを失ったときは、`RenderServer` が持つ一覧から作り直す。外した物は `Removed` の印を付けて番号を残す(呼び出し側が持つ番号が変わらない)。成果物の閉包(メッシュ・マテリアル・オブジェクト一覧の同一性)も更新する(オブジェクト一覧はフレームに 1 回)。`RenderFrameState` の配列は、使う番号に合わせて自動で伸びる。
- ランタイム側は `RenderRoster`(`EngineHost.Roster`)が持つ。Presentation の `StaticMeshBind`・`PrimitiveMeshBind` が、まだ描かれていない Entity の描画物を作り(初めて見る資産は読み込んでレンダラーへ足す)、位置もその場で置く。Entity が壊れるか、メッシュの Component を失ったら、`Renderable` の包みが `TransformSync` の中で自分の描画物を外して自分を壊す(`EmissiveMeshLighting` の包みも同じ)。
- 描かれないもの(文書化した未実装): 後から作った `SkeletalMesh`(M0105 の時点。スキニングの準備はレンダラーの作成時だった。M0107 で描かれるようになった)、すでに描かれている Entity の `StaticMesh.AssetPath` の書き換え。

### 2. `FixedTransform` の Generation 固定: 根本原因を直した
- 原因: 解決器が `int` の Entity 番号をキーにして、Component の取得のたびに `new Entity(Id)`(Generation 1)を作り直していた。親の `Parent.Value.Id` も同じ。
- 直し方: 解決器のキーと delegate を `Entity` 全体にした(`TryGetLocalTransform(Entity, …)`、`TryGetParentEntity(Entity, out Entity)`)。空間索引の生成コード(`SpatialWorldEmitter`)も同じ形にした。
- 症状隠しではない。Generation を 1 に固定する、Generation を見ない取得を足す、のような迂回はしていない。古いハンドルは今までどおり解決できない(`AStaleHandleToARecycledSlotIsNotResolved`)。

### 3. Scene の実行時の生成(生成器)
- 形: `Spawner.Spawn<CursorMarker>(Hit.Point)`。`Spawner` は `[EngineService]` のサービス(System の引数に書くと生成されたループが渡す)、`TScene` は Scene JSON から生成されたクラス。計画と理想コードは `Spawner.Spawn(Scenes.CursorMarker, 位置)` だったが、これは書けない。Scene のクラスは名前空間 `<ゲーム>.Scenes` にあるので、`Scenes.CursorMarker` は型そのものを指す(値の型付き参照 `Assets.Sounds.Jump` と違い、同じ名前の名前空間が先に見つかる)。`World.InstantiateScene<TScene>()` と同じ型引数の形にした。型の誤りはコンパイルで分かる。
- 位置を渡せる Scene だけが `Spawn` に渡せる。根ノードが `Transform` を持つ Scene に、生成器が `ISpawnableScene`(`InstantiateDeferredAt(ref World, 位置)` と回転つき)を実装する。根に Transform が無い Scene は実装されず、`Spawn<T>` に渡すとコンパイルエラーになる。`ISpawnableScene` がコンパイルに無いとき(生成器の単体テストなど)は何も出さない(`GenerationGate.HasSceneSpawnContract`)。`ISceneDefinition` は変えていない(それを読む生成器のテストの構成を変えないため)。
- 生成されるのは、既存の `InstantiateDeferred` と同じ構築で、根の Transform の式だけ `SceneSpawn.At(<書いた Transform>, 位置)` で包んだもの。根の位置が置き換わり、回転と拡縮は Scene に書いた値のまま(回転を渡した版は回転も)。子の位置は根からの相対のまま。
- `Spawner` は呼び出しを溜めるだけで、`SpawnerFlush`(Presentation の最後、`Present` の後)が構造変更のキュー経由で作る。System の実行中に Entity を作る唯一の安全な道なので、Scene が現れるのは呼んだフレームの終わり、描かれるのは次のフレームから。`SpawnerFlush` は `LevelRuntime` を持つ Entity(Level)に付く。ベイクのホストなど、Level の無い実行では溜まったままになる。
- 位置の既定: `Spawn` は「根の位置を置き換える」(UE の `SpawnSystemAtLocation` と同じ絶対位置)。
- 物理の体を持つ Scene を同時に 2 つ生かすと、`PhysicsStableId` が Scene の定義から決まるので同じ値になる。これは未対応(破壊の M0113 で扱う)。
- 生成器の修正: `[EngineService]` が `sealed` で `IDisposable` でないと、ループの後始末のコードが CS8121 でコンパイルできなかった。`object` へのキャストを挟んだ(`ASealedServiceThatIsNotDisposable…`)。

### 4. 寿命つき Entity
- `Lifetime`(Component、残り秒数)と `LifetimeExpire`(FixedSimulation)。固定 tick ごとに 1 tick 分を減らし、0 以下で壊す。Scene の根に付けると Scene 全体(子ごと)を壊す。それ以外の Entity は自分だけ。固定 tick で数えるので、シミュレーションと同じ決定性を持つ。

### 5. 粒子のバースト
- `ParticleBurst`(Scene のノードに書く)、`ParticleBurstRuntime`(自動で付く)、`Particle`、`ParticleBurstEmit`・`ParticleMotion`(FixedSimulation)。最初の固定 tick に、ノードのワールド位置から `Count` 個(上限 256)の粒子の Entity を作る。粒子は自分の `Lifetime` を持ち、飛び、重力で落ち、`StartScale` から `EndScale` へ大きさを変え、消える。
- 乱数は `Seed` から決まる xorshift32 で、固定小数点だけで計算する(同じバーストはどのマシンでも同じ)。方向は「横に傾く量 `Spread`(上 1 あたりの横の距離)」の円盤から一様に取って正規化する。
- スプライトの描画パスはレンダラーに無く、描画契約は変えない(`stage-v11` のまま)。粒子は他の描画物と同じメッシュ(`StaticMesh` の資産か `PrimitiveMesh`)で、光を受け、不透明に描く。ビルボードも透明も無い。色は材質で決まる(粒子ごとの色は未実装)。

### 6. TopDown のクリック地点の目印
- UE の `FX_Cursor_Success`(Niagara)から読めた値(`.artifacts/UnrealExport/logic/TopDown/FX_Cursor_Success.t3d` の高速パラメーターの値を読んだもの):
  - エミッター `SimpleSpriteBurst`、Spawn Burst Instantaneous で Spawn Count 1、Spawn Time 0、Loop Duration 0.7 s、Loop Count 1(1 回だけ)。
  - 粒子: Lifetime 0.7 s、色 (1, 1, 1, 1)、位置のずれ 0、速度と回転の力はすべて 0。スプライトの大きさ 50 は、描画がメッシュなので使われない。
  - 描画はメッシュ `SM_CursorMesh`。材質 `M_Cursor` は BLEND_Masked で、基本色 = パラメーター `Color` × 矢印のテクスチャ、不透明マスクはそのアルファ、粗さ 1。パラメーター `TexCoordY`(既定 0.58)に `Emitter.NormalizedLoopAge` を結び、テクスチャの V をずらす(矢印が帯に沿って流れる)。
- 写した値: `Count` 1、`Lifetime` 0.7(粒子と Scene の根)。`StartScale` 1 から `EndScale` 0.25 へ縮む。UE の `TexCoordY` による矢印の流れは、シェーダーの契約を変えずには写せないので、縮みで代えた。
- 形: エンジンはアルファの切り抜きを描かない。`SM_CursorMesh`(UE5.8 から `run_export_skeletal_mesh.cmd` + `CGE_SK_STATIC=1` で書き出した glb)は 3 本の帯の四角形(21 枚)で、矢印はテクスチャの切り抜きなので、そのまま不透明に描くと帯全体が黒く出る。`tools/UnrealExport/cut_masked_mesh.py` が、帯の四角形を 24×24 に割り、テクスチャのアルファが `alphaCutoff`(0.3333)以上のセルだけを残して、矢印の形を形状にした(色は残したテクセルの平均を材質の係数にし、テクスチャは持たせない。両面)。結果は 6,312 三角形(両面)、色は線形 (0.0023, 0.1283, 0.5353)。
- `ClickToMove` が押した瞬間に `Spawner.Spawn<CursorMarker>(Hit.Point)` を呼ぶ。UE は「押していた時間が短かったときの離した瞬間」に出す。押していた時間を測る入力は M0108 なので、ここでは押した瞬間に出す。(M0108 で、押していた時間を測って短押しの離した瞬間に出すように変えた。)

## 作ったもの
| 層 | 中身 |
|---|---|
| L0 描画 | `StageRenderer` の `AddObject`・`RemoveObject`・`AddMesh`・`AddMaterial`(オブジェクト・マテリアルの余裕、有効フラグ、スロットの再利用)、`StageObjectCreateInfo.Removed` |
| L0 サーバー | `RenderServer` の同名の口と一覧、成果物の閉包の更新(`RefreshObjectRosterArtifact`)、`RenderFrameState` の自動の拡張 |
| L3 ECS | `FixedTransformWorldResolver` を `Entity` キーへ、`ISpawnableScene`・`SceneSpawn`(`src/CSharpGameEngine.Entities/ISpawnableScene.cs`) |
| L5 ランタイム | `RenderRoster`、`StaticMeshBind`・`PrimitiveMeshBind`・`SkeletalMeshBindNotice`、`TransformSync` の外し、`Spawner`・`SpawnerFlush`、`Lifetime`・`LifetimeExpire`、`ParticleBurst` ほか |
| L7 生成器 | `SceneInstantiateEmitter`(`InstantiateDeferredAt`、`ISpawnableScene`)、`GenerationGate`、`GameSystemLoopEmitter`(サービスの後始末) |
| L7 ツール | `tools/UnrealExport/cut_masked_mesh.py` |
| 自動実行 | レポートに `renderObjectCounts`(フレームごとの描画物の数) |
| テンプレート | TopDown の `Scenes/CursorMarker.json`、`Assets/Cursor/SM_CursorMesh.glb`、`Gameplay/ClickToMove.cs` |

## 検証
- 単体と WARP(新規 26 + 4 + 4 件):
  - `RecycledEntityTransformTests`(3): 再利用したスロットの Entity の位置、その親、古いハンドルが解決されないこと。
  - `RenderServerDynamicObjectTests`(4、WARP): 足したオブジェクトが描かれ、外すと消え、スロットを再利用する。メッシュとマテリアルを足し、デバイスを失っても同じ画像に戻る(外した物は外れたまま)。余裕を使い切ると -1 を返して壊れない。スキン付きのメッシュは足せない。
  - `RenderRosterTests`(6、WARP。生成されたループの Presentation をそのまま回す): 生成した Entity が描かれ、壊すと消える。再利用したスロットの Entity が正しい位置に描かれる。生成と破壊を 40 回繰り返しても数と最後の画像が変わらない。資産(テクスチャつきを含む)を実行中に読み込む。読めない資産は診断を 1 回出して何も描かない。`Spawner` で出した Scene が描かれる。D3D12 のデバッグ層のエラー 0。
  - `SpawnerLifetimeParticleTests`(13、GPU なし): `Lifetime` の境界(0、Scene の根)、バーストが 1 回だけ出ること、位置・上昇・縮み・終わり、同じ `Seed` で同じ・違う `Seed` で違う、`Count` の上限、`Spawner` の位置と回転と溜め。
  - `SceneSpawnGeneratorTests`(CodeAnalysis、4): 根に Transform がある Scene だけが `ISpawnableScene`、契約が無いコンパイルでは何も出ない、根だけ置き換わって子はそのまま(コンパイルして実行)、`sealed` で `IDisposable` でないサービス。
  - `ClickToMoveVerificationTests`(+4): 目印が出て消えることの判定(出ない、消えない、最初からある数)。
- 実走(WARP、`verify-thirdperson-character --template TopDown`): 80 フレーム、クリックはフレーム 20。描画物の数は 56 → 57(フレーム 21 から 5 フレーム)→ 56。移動 10.845 m、最後の 10 フレームの動き 0.000 m、足音 7 回、D3D12 のエラー 0。目印は `SM_CursorMesh` の矢印 3 本で、クリックした地面の上に影つきで出て、消えた。画像: `.artifacts/m0105/frame_24.png`(出ているフレーム)と `frame_80.png`。
- ほかのテンプレートへの影響なし(WARP): ThirdPerson 移動 15.700 m・上昇 1.228 m、FirstPerson 移動 16.000 m・上昇 0.863 m、Vehicle 移動 48.281 m・曲がり 38.3°・HUD 28 状態。SDK パッケージから作った ThirdPerson と Vehicle(`verify-template-package`)も同じ値。
- 全テスト(`dotnet test CSharpGameEngine.slnx -c Debug`): 失敗 0、成功 3,047、スキップ 2。

## 実装中に見つけた事実
- 空いたスロットの再利用で `Entity` の番号が同じになるとは限らない(壊した Entity と、その包みの Entity の両方が空く)。試験は「同じ番号」ではなく「Generation が 2 以上」で確かめる。
- エンジン名前空間の型は、`[SceneComponentOrder]` か `[SceneComponentIcon]` を付けないと Scene の Component として使えない(SCENE201)。`Lifetime` と `ParticleBurst` には付けた。

## 範囲外(未実装)
- ビルボードの向き、透明・加算の粒子、粒子ごとの色、UE の `TexCoordY` のような材質の時間変化(描画契約の変更が要る)。
- 後から作った `SkeletalMesh` の描画(リスポーンのキャラクターの生成は M0107 で扱う)。→ M0107 で実装した(`SkeletalMeshBindNotice` は無くなった)。
- 物理の体を持つ Scene の同時の複数生成(`PhysicsStableId`)。
- 押していた時間の測定(短押しだけで目印を出す UE の条件は M0108 で実装した)。
- 実機の GPU での見た目とフレーム時間、最初の 1 体の引っかかり(`WaitForGpuIdle`)の測定(WARP のみ)。
