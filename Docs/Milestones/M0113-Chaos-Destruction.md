# M0113 — Chaos 相当の破壊(Destruction プラグイン)
- 日付: 2026-10-25
- 状態: 完了(2026-10-25。下の「範囲外(未実装)と不確実性」に、UE との差と未実装を行ごとに書いた)

## 目的
ADR-0078 §2 の「Chaos の破壊(Geometry Collection、Fracture、Field に相当)」を作る。計画の段階 1 の 8。UE の 4 テンプレートには壊れる物体が無いので、テンプレートのレベルは変えず、エンジン機能(物理の `Runtime.Physics` とプラグイン `plugins/CSharpGameEngine.Destruction`)と専用の見本(`samples/CSharpGameEngine.Samples.Destruction`)にする。
- 調理: シード付き・決定的なボロノイ分割。断面の材質の区別、1 段のクラスター、接触面積から結合の強さ。閉じていない・凹んだ・退化したメッシュと、破片数 0/1 は診断で拒否する。
- 実行時: 衝撃と接触の力積で結合のひずみを溜め、強さに届いた結合を切る。離れた塊は動的剛体。アンカー、破片の上限、眠り、一定時間後の除去。`PhysicsEventType.BondBroken` で通知する。決定的。
- Field 相当: 範囲のひずみ(`StrainField`)と外へ押す力(`ForceField`)。
- 描画: 破片ごとの Entity(M0105 の描画物の差分反映)。
- ベイク: 切れた結合を `.cgephyscache` に記録して再生する(旧版の読み込み互換、リアルタイムと再生の一致)。
- VSIX: Fracture のプレビュー(分割数・シード・クラスター数を Inspector で変えると、ビルド前に破片の輪郭と結合が見える)。

## 引き継ぎ時の状態(2026-10-25、事実)
前任のエージェントが途中で止められ、作業ツリーに未コミットの実装が約 43 ファイル残っていた。引き継いで確かめた事実:
- ソリューションは `dotnet build CSharpGameEngine.slnx -c Debug` で通った。1 回目は `CSharpGameEngine.Cook.Cli.dll` を「別のプロセスが使用中」(CS2012)で失敗したが、詳細ログでは Cook.Cli のビルドは全部同じプロジェクト インスタンス(1 つの構成番号)で、入れ子のビルドどうしの競合ではなかった。同時に動いていた別のプロセスがファイルを開いていたと推測する(2 回目は成功。同じ作業ツリーで別の作業(Asset Workspace、ADR-0081)が並行して進んでおり、そのビルドの可能性がある。推測)。
- `MathF` の net472 への置き換え(前任の止まった作業)は済んでいた(VSExtension に `MathF` の使用は無い)。
- 関連の試験はすべて合格: Destruction.Tests 47、Windows.Tests の物理と破壊 122、VSExtension の `Prefracture` 13、FeatureRunner の `DestructionSampleVerification` 12、`SolutionProjectRegistration` 4。
- `verify-destruction`(WARP)も合格した(下の「検証」)。
- 見つけた欠陥: 眠っている物体は積分されないのに、支えている物体が消えても起こされなかった(下の「実装中に見つけた事実」の 4)。破片の寿命が尽きて消えると、その上で眠っていた破片が宙に浮く。修正前に失敗する試験を書いて直した。
- `verify-destruction` は前任がキャラクター操作の段(`rigid.character-controller`)に足していた。破壊の段 `destruction.prefracture`(F-L5-049)を作って移した。

## 決定
### 1. 層と置き場所(ADR-0078 §3)
- L5 `Runtime.Physics`(ソルバー本体。UE でも Chaos の本体はエンジンにある): `FractureSolver.cs`(`FractureState`・`FracturePiece`・`FractureStep`・`FractureSettings`・`PrefractureAssetCache`)、`PrefractureCooking.cs`(ボロノイの調理と `.cgefracture` 版 2)、`PhysicsCooking.cs`(`PrefractureAsset` のサイト・クラスター・面積)、`PhysicsShapeAssets.cs`(凸多面体 `CreateConvexPolyhedron` と複合 `Compound`)、`PhysicsNarrowPhase.cs`(多面体と複合の接触)、`DeterministicPhysicsWorld.cs`(`ReportBondBreak`・`BondBreaks`・眠りの扱い)、`PhysicsCache.cs`(スキーマ 3)。
- L5 プラグイン `plugins/CSharpGameEngine.Destruction`(ADR-0079。UE の GeometryCollection プラグインに当たる): Component(`Destructible`・`DestructibleState`・`DestructiblePiece`・`DestructibleFragment`・`DestructionImpact`・`StrainField`・`ForceField`)、サービス `[EngineService] Destruction`、System(`DestructibleLoad`・`DestructionImpacts`・`StrainFieldApply`・`DestructionStep`・`ForceFieldApply`・`DestructionFollow`・`DestructionOrphans`)、Cook モジュール `Cook/CSharpGameEngine.Destruction.Cook.csproj`(`FractureCooker`)。`Destructible` は `Runtime.Physics` からこのプラグインへ移した(M0055 の未使用の `Destructible`・`PrefractureRuntime`・`FractureBreak` は削除)。
- L5 描画(`Runtime.Windows`): `FragmentMesh`(1 破片を描く Component)、`FragmentMeshBind`、`RenderRoster.BindFragmentMesh`(破片ごとに外側と断面の 2 メッシュを 1 度だけ作り、色の組ごとに材質を 2 つ)。描画契約は変えない。
- L3 ECS(`Entities`、生成器): `World.TakeSceneOrdinal` と `SceneStableIds.ForInstance`。同じシーンの 2 つ目以降のインスタンスの物理の stable ID を変える(下の「実装中に見つけた事実」の 3)。
- L7: `Physics.Cli` の `preview-prefracture`(箱を切って輪郭と結合の JSON)と `replay`(焼いたキャッシュを再生して書き出す。生成した Bake Host の `TryReplay`)。Cook は `Cook.Cli` がプラグインの Cook モジュールを別プロセスで動かす(ADR-0079)。
- L8 VSIX: `PrefractureDebugService` のプレビュー(下の §6)。
- L9: 見本 `samples/CSharpGameEngine.Samples.Destruction`、UserEcsProject の検証シーン(`DestructionBallScene`・`DestructionDropScene`・`DestructionFieldScene`・`DestructionWallScene`)。テンプレートは変えない。

### 2. 調理(UE の Fracture モード)
- 入力は `Destructible` の節点の `Collider` の箱(`Center` と `HalfExtents`)か、`SourceMesh` の閉じた凸の `.glb`。節点の拡大縮小は 1 でなければならない(大きさは衝突形状で決める)。
- ボロノイのサイトは、シード・ソースの指紋から決まる SplitMix64 の乱数で外接箱の中に置き、ソースの内側の点だけを採る。セルは各サイトについてソースの多面体を他のサイトとの垂直二等分面で切って作る(Q64.64)。
- 断面の材質: 隣のセルの面で切られた三角形は `FractureMesh.InteriorSlot`(1)、ソースの表面は `OuterSlot`(0)。三角形ごとに 1 バイト。
- 結合: 2 つのセルが共有する面の面積を両側で測り(0.1% を超えて食い違えば FRACTURE007)、小さいほうを面積とする。1 mm² 以下は角の接触として結合にしない。強さ = 面積 × `BondStrength`(N·s/m²)。
- クラスター(1 段): 目標の大きさ ⌈破片数 / クラスター数⌉(クラスター数 0 は √破片数に最も近い整数)。番号の小さい未割り当ての破片から結合を幅優先でたどって作るので、クラスターは常につながっている。小さすぎるクラスターは結合が最も多い隣へ併合する。
- 拒否(診断): 開いたメッシュ・凹んだメッシュ・退化した三角形(FRACTURE001〜004)、破片数 2〜256 以外・密度や強さが正でない(FRACTURE002)、サイトが置けない(FRACTURE005)、潰れたセル(FRACTURE006)、正準でない結果(FRACTURE007)。Cook モジュールはこれを `scene.json: error DST008` などのビルドのエラーにする。
- `.cgefracture` 版 2(`CGEFRAC2`): ソースの指紋、シード、密度、面積あたりの強さ、破片(番号・形の ID・中心・質量・サイト・クラスター・頂点・添字・材質の区別)、結合(2 つの破片・面積・強さ)。版 1(M0055)も読む(サイト・クラスター・面積なし)。途中で切れた・余分なバイト・範囲外・正準でないファイルは例外を出さずに拒否する。同じ入力は同じバイト列になる。

### 3. 実行時(UE の Chaos のクラスター)
- ひずみは力積(N·s)。1 tick の間に集め、tick の終わり(物理の Step の後の `DestructionStep`)に、ひずみ ≥ 強さの結合を結合の番号順に切る(**ちょうど等しければ切れる**)。切れなかった結合のひずみは次の tick に持ち越さない(置いてあるだけの荷重で結合が少しずつ弱らない。UE の内部ひずみとの差は「範囲外」)。
- ひずみの元: (1) 物理の接触の法線の力積(`CollectContactManifolds` の各点。その点を含む破片 = サイトが最も近い破片)、(2) `DestructionImpact` を持つキャラクターがぶつかる速さ × 質量(キャラクターは運動学のカプセルで接触の力積が無いため)、(3) ゲームの `Destruction.ApplyImpulse`、(4) `StrainField`(`Destruction.ApplyStrain`)。
- 力積は当たった破片の結合に全部、そこから結合 1 つ離れるごとに `DamagePropagation`(既定 0.5)倍を、最大 4 環まで(UE のダメージの伝播)。同じクラスターの中の結合の強さは `ClusterStrengthScale`(既定 2)倍(先にクラスター単位で外れ、強く当たるとクラスターも割れる。UE の階層ごとのしきい値)。
- 切れたあと、つながっている破片の組ごとに 1 つの塊(`FracturePiece`)。塊 0 は元の物体で、最初に割れるまでは作者の置いた物体そのもの。割れたら元の `Collider` を無効にし、各塊を新しい Entity(1 破片なら凸多面体、複数なら複合形状)にして、割れる前の物体の速度(その点の速度)を引き継ぐ。
- アンカー: `Anchor=Bottom`(既定。`RigidBody` の無い物体だけ)は最下面から 1 cm 以内の破片を固定する。アンカーを含む塊は静的な物体のまま。
- 破片の上限 `FragmentBudget`(既定 64。0 以下は無制限)は動く塊の数の上限で、新しい塊を作る前に古いものから除く。新しい塊だけで超える分は作らない(その tick に作った物体はその tick に消せないため)。`FragmentLifetime`(既定 10 秒。0 以下は無期限)で、外れてからの時間が過ぎた塊を除く(tick に丸め、最低 1)。
- 眠り: 塊は `AllowSleep`。眠っている物体どうし・静的な物体との接触は狭域判定も解法もしない(積もった破片は 1 tick に何もしない)。起きている物体が触れると起きる。
- 通知: 切れた結合ごとに `DeterministicPhysicsWorld.ReportBondBreak` → `BondBreaks` と `PhysicsEventType.BondBroken` のイベント(`First` が元の物体、力積が切ったひずみ、ペアの ID は物体の stable ID と結合の番号のハッシュ)。
- 決定性: すべて整数と Q64.64、物体の stable ID の順、結合と破片の番号の順。塊の物体の stable ID は物体の ID と塊の番号のハッシュ(最上位ビットを立てて Entity 番号の ID と衝突しない)。
- 問題は `DestructibleState.Problem` に残し 1 度だけ出力する(DST001 名前、DST002 資産が無い・壊れている、DST003 設定、DST004 拡大縮小、DST005 stable ID の重複)。その物体は壊れないまま。

### 4. Field 相当
- `StrainField`(UE の External Strain): 半径の中の結合の中点に、距離に比例して 0 まで減る `Strain`(N·s)。最初の tick に 1 度(`Repeat` なら毎 tick)。
- `ForceField`(UE の放射のベクトル場): 半径の中の動く物体を、中心からの向きに `Speed` × (1 − 距離/半径) の速度変化になる力積で 1 度だけ押す(次の tick)。場の生きている間に外れた塊も押す。`Lifetime` と組み合わせて爆発にする(見本の `Explosion`)。

### 5. ベイクと再生(`.cgephyscache` スキーマ 3)
- 各フレームのイベントの後に、その tick に切れた結合(物体の stable ID、結合の番号、ひずみ。stable ID と番号の昇順で一意)を書く。スキーマ 2(M0055)のファイルも、スキーマ以外の識別が一致すれば読める(結合の切断なし)。スキーマ 2 の書き手は切断を書けない。
- 再生中は、ひずみを無視してキャッシュの記録どおりの結合を切る(`FractureState.Apply`)。同じ結合からは同じ塊ができるので、物体の集合が一致して再生が続く。
- 回転の正規化の許容差: Q64.64 の正規化は冪等でない(正規化した四元数を再び正規化すると最後のビットが動く)ので、キャッシュは長さの二乗が 1 から 2⁻³⁰ 以内の回転を受け入れる(以前は正規化の不動点を要求し、回る物体のベイクがすべて拒否された)。
- `Physics.Cli replay --assembly <game.dll> <シーン> <入力> <出力> <tick 数> <依存ハッシュ>` が、焼いたキャッシュを再生しながら同じ形式で書き出す。焼いた結果と同じバイト列なら再生がリアルタイムと一致する。

### 6. VSIX の Fracture のプレビュー
- Scene Designer の物理の重ね描き(結合の表示を有効にしたとき)が、シーンの `Destructible` ごとに、節点の箱の `Collider` を `Physics.Cli preview-prefracture` で別プロセスで切り(ビルドの Cook と同じ調理)、破片の輪郭(薄い橙)と結合(桃色)を描く。Inspector で `FragmentCount`・`Seed`・`ClusterCount` を変えると、ビルドの前にその切り方が見える(UE の Fracture モードのプレビュー)。
- `SourceMesh` で切る物体は、最後のビルドが調理した `bin\**\Assets\Destruction\<名前>.cgefracture` を表示する。範囲外の値(破片数、体積の無い箱、球の衝突形状)は切らずに理由を表示する。
- VS の実機での操作は検証していない(net472 の単体試験まで)。

### 7. 見本と実走の検証
- 見本 `samples/CSharpGameEngine.Samples.Destruction`(ThirdPerson の操作とカプセルのキャラクター): 球が当たる壁(24 片、アンカー、密度 2400・強さ 1200)、上から落ちる木箱(12 片、`RigidBody` 500 kg)、床の木箱 3 つとその上の 1 つ(各 10 片、強さ 600)。F で球を撃ち、E で 3 m 先に爆発(`StrainField` 20000 N·s・半径 2 m と `ForceField` 5 m/s・半径 3 m、0.1 秒)。焼きの検証用シーン `DestructionBake`(床の上の木箱の中で爆発)。
- `FeatureRunner verify-destruction`(段 `destruction.prefracture`): 見本を自動入力(W 10〜40、F 50、E 70、170 フレーム)で WARP で動かし、6 個すべてが順に壊れること、破片の重心が床より下に行かないこと、最後に最も速い破片が 0.2 m/s 以下、D3D12 のデバッグ層のエラー 0 を判定する。続けて `DestructionBake` を 2 回焼き、1 回目を再生して書き出し、3 つが同じバイト列であることを判定する。

## 実装中に見つけた事実(再現して直したもの)
1. **同じ形を共有する 2 つの物体が自分自身と衝突判定されていた**: 狭域判定は多面体の資産ごとにワールドの座標の枠を 1 つ持っていたので、同じ調理の 2 つの木箱の破片(同じ形の資産)の組では、2 つ目の物体の枠が 1 つ目のものを上書きし、物体が自分自身と完全に重なると判定された。上の物体が下の物体を抜け、積もった破片が跳ね続けた。組の 1 つ目が持つ枠を 2 つ目は使わない(`FragmentContactTests.TwoBodiesOfOneFragmentShapeDroppedOneOnTheOtherComeToRestApart`)。
2. **位置の補正が眠りを妨げていた**: 静止した物体は毎 tick 重力で g·Δt² 沈み、位置の補正で戻される。この補正を「動き」として物体を起こしていたので、置かれた物体が永遠に眠らなかった。位置の補正は物体を起こさない。
3. **同じシーンの 2 つのインスタンスが同じ物理の stable ID を持っていた**: 生成器は節点ごとに「シーンと節点のパス」から ID を作るので、`Spawner` で同じシーンを 2 つ作ると物理の物体が 1 つに混ざった。2 つ目以降のインスタンスは、その順番(`World.TakeSceneOrdinal`)と混ぜた ID を使う(1 つ目は今までと同じ。ワールドのセルのインスタンスは今までと同じ)。`DestructionTests.TwoInstancesOfOneSceneAreTwoBodiesThatEachBreak`。
4. **支えが消えても眠った物体が起きなかった**(引き継ぎ後に修正): 眠っている物体は積分されない。支えの物体を `Remove`(または形を変える `Upsert`・`Synchronize`)すると接触が消えるだけで、上で眠っていた物体は宙に浮いたままだった。消えた接触の相手で眠っている物体に起こす要求を付け、次の `Step` の最初(命令の後、積分の前)に `Wake` イベントとともに起こす。`FragmentContactTests.ASleepingFragmentWakesAndFallsWhenWhatHoldsItIsRemoved`(修正前は「起きなかった」で失敗、修正後に合格)。
5. **回転の正規化が冪等でない**(§5): 回る物体のベイクが「正規化されていない回転」で拒否された。

## 検証
| コマンド | 結果 |
|---|---|
| `dotnet build CSharpGameEngine.slnx -c Debug` | 成功(警告の増加なし) |
| `dotnet test tests/CSharpGameEngine.Destruction.Tests` | 47 合格 |
| `dotnet test tests/CSharpGameEngine.Windows.Tests --filter "…DestructionTests|FractureSolverTests|FragmentContactTests|PhysicsCacheBondBreakTests|PhysicsBakeTests|Prefracture|PhysicsCache|DeterministicPhysics|Vehicle|StaticMesh"` | 122 合格(眠りの修正の試験を含む) |
| `dotnet test tests/CSharpGameEngine.VSExtension.Tests --filter Prefracture` | 13 合格(net472) |
| `dotnet test tests/CSharpGameEngine.FeatureRunner.Tests` | 234 合格 |
| `FeatureRunner verify-destruction --adapter warp` | 合格: `destructibles=6 broken=2/5/5/6 bonds=203 loose=64 sleeping=18 lowest=0.059 fastest=0.014 d3d12Errors=0`、`bytes=636670 deterministic=True playbackMatches=True` |
| `dotnet test` CodeAnalysis・Entities・Cook | 338・76・29 合格 |
| `FeatureRunner verify-thirdperson-character --template ThirdPerson/FirstPerson/TopDown/Vehicle` | 4 つとも合格(下の基準値) |
| `dotnet pack packaging/CSharpGameEngine.Sdk -c Debug` と `verify-template-package`(4 テンプレート) | 合格(SDK 0.1.25。値は基準値と同じ) |
| パッケージ経由の破壊のゲーム(見本のソース + `<CgePlugins>Destruction</CgePlugins>` + SDK 0.1.25) | ビルドが `BakeCrate`・`FallingCrate`・`SampleCrate`・`ShotWall` の `.cgefracture` を焼いた。WARP 12 フレームで `destructibles=6 broken=2`、D3D12 のエラー 0 |
| `dotnet test CSharpGameEngine.slnx -c Debug`(`.artifacts/m0113/fulltest1.txt`) | M0113 に関係する失敗 0。失敗は 3 種で、どれも M0113 の変更によるものではない: (1) VSExtension 3 件(`VsixUiLiteralGuardTests`・`VsixLocalizationTests` 2 件。同じ作業ツリーで並行中の Asset Workspace の作業の `AssetWorkspaceCommands.cs`・`InspectorToolWindowControl.xaml`・`Assets_Reveal` が原因。残り 1218 合格)、(2) ShaderGraph.Cli 1 件(`ProductionStage_ExternalSubgraphsPreserveGoldenHlsl`。並行作業が `Stage.cgeshadergraph`・`ShaderArtifactAbi.cs` を変更中。単独で再実行して合格)、(3) Windows.Tests のテストホストのクラッシュ(697 件合格の後)。落ちたのは `TypedLightFrameTests.TypedLights_RenderIdenticalPixelsAfterDeviceRecovery` の実行中で、WARP(`d3d10warp.dll`)内のアクセス違反(c0000005)。x64 の出力(22:45 のビルド)では単独でも再現するが、M0113 の変更をすべて含む AnyCPU の出力(22:40 のビルド)では Windows.Tests 全体が 874 合格・2 スキップ・失敗 0。2 つのビルドの間に、並行中の別の作業(仮想シャドウ、描画契約 `stage-v14`: `StageRenderer.VirtualShadows.cs`・`StageRenderer.cs`・`ShaderArtifactAbi.cs`・`Stage.cgeshadergraph` を変更中)の描画の変更が入っており、その途中の状態が原因と推測する(M0113 は描画契約を変えていない)。ほかの試験プロジェクトはすべて合格 |

### 画像の観測(WARP)
- 終わり(`.artifacts/feature-evidence/destruction.sample/warp/final.png`): 木箱(茶)と壁(灰)の破片が床に散って積もり、止まっている。断面(内側の色)が見える。浮いた破片や床を抜けた破片は無い。
- 爆発の直後(`.artifacts/m0113/mid/explosion.png`、同じ入力で 76 フレーム、爆発は 70 フレーム): 最後の木箱が崩れ、破片が上へ飛び散っている(画面中央の上に宙の破片)。この時点で 6 個すべてが割れ、切れた結合 199、動く塊 61、最も速い破片 4.99 m/s。D3D12 のエラー 0。

## 逆解析した挙動(最終のコードから)
- 結合はひずみ ≥ 強さで切れる(等号で切れる)。ひずみは tick ごとに捨てる。(High: `FractureSolverTests.ABondBreaksAtExactlyItsStrengthAndNotOneStepBelow`・`StrainDoesNotCarryFromOneTickToTheNext`)
- 同じシードと設定の調理は同じバイト列、別のシードは別のバイト列。破片は閉じて外向きで、合わせるとソースを満たす。結合は両側の断面と一致する。(High: `PrefractureCookerTests`)
- アンカーだけの板は、割れてもアンカーの塊だけになり動かない。(High: `FractureSolverTests.ASlabOfAnchorsBreaksIntoAnchoredPiecesOnly`)
- 上限を超える動く塊は古い順に除かれ、破片の描画の Entity も一緒に消える。寿命の前には消えない。(High: `DestructionTests`)
- 2 つの物体が同時にそれぞれの原因で割れ、毎回同じに割れる。焼いたキャッシュはリアルタイムと同じフレームを書き、再生は同じバイト列を書く。(High: `DestructionTests`、`verify-destruction`)
- 眠っている物体は、支えが消えた次の tick に起きて落ちる。(High: `FragmentContactTests`)

## 基準値(意図しない変化が無いこと)
物理の眠りと接触の変更、stable ID の変更の後に、`verify-thirdperson-character`(WARP)で 4 テンプレートを走らせた。すべて M0112 と同じ値で、D3D12 のエラー 0。

| テンプレート | 値 |
|---|---|
| ThirdPerson | 歩き 15.700 m、上昇 1.228 m |
| FirstPerson | 歩き 16.000 m |
| TopDown | ナビなし 9.606 m、ナビあり (6.80, 6.83) で停止(目標の誤差 0.01 m)、長押し 17.414 m |
| Vehicle | キー 62.613 m・27.8°、トリガー半分 23.306 m、スティック半分 9.3° |

## 範囲外(未実装)と不確実性
| 項目 | 状態 | 理由・次 |
|---|---|---|
| 調理(ボロノイ、断面の材質、1 段のクラスター、面積の強さ、診断) | 完了 | 凹んだソースは拒否(UE は凹んだメッシュも切る)。多段の階層は無い(1 段) |
| 実行時(ひずみ、切断、剛体化、アンカー、上限、眠り、除去、`BondBroken`、決定性) | 完了 | ひずみは tick ごとに捨てる(UE の Chaos は内部ひずみを持ち、強さを超えるまで減らすだけ)。接続グラフの再計算は塊の中だけ |
| Field(ひずみ、外へ押す力) | 完了 | UE の Field System の任意の組み合わせ(Falloff の種類、Mask、Culling)は無い。球の線形の減衰だけ |
| 描画(破片ごとの Transform) | 完了 | 破片は平らな陰影の 2 色(外側・断面)。元のメッシュの材質やテクスチャは使わない |
| ベイクと再生(スキーマ 3、旧版の読み込み) | 完了 | — |
| VSIX の Fracture のプレビュー | 完了(単体試験まで) | VS の実機での操作は未検証 |
| 見本と FeatureRunner の実走 | 完了(WARP) | 実機の GPU は未確認 |
| プラグインと SDK パッケージ | 完了 | SDK 0.1.25 に `plugins/CSharpGameEngine.Destruction/`(ソースと `Cook/lib/` の Cook モジュール)が入る。パッケージから見本と同じゲームを作ると、ビルドが 4 つの `.cgefracture` を焼き、WARP で 6 個が読み込まれて壊れる(手で確認。`verify-plugin-package` は Destruction を含まない) |
| 箱と三角形メッシュの接触の複数の面の向き(M0112 の報告) | 範囲外 | 破片の積もる動きには関係しない: 破片どうしは多面体どうしの接触(面の平面による。複数の向き)で、見本の床は箱。Vehicle の基準値(62.613 m)に影響する変更なので、縁石の衝突と合わせて別に行う |
| 実行時のブール演算・ボクセルの破壊(F-L5-021・F-L5-022) | 範囲外 | 事前破砕だけ |

## 次
- 箱と三角形メッシュの接触を複数の面の向きに対応させ、Vehicle の縁石に衝突を付ける(ADR-0078 §6)。
- 実機の GPU での見本の確認。
