# M0120 段階 1 の残り: フレーム計画を L4 へ移し、L0 を実行器だけにする

## 用語

この文書の記号の意味。

| 記号 | 意味 |
|---|---|
| L0〜L8 | エンジンのコードの階層の番号。数字が小さいほど機械に近い。上の層は下の層を使えるが、逆はできない |
| L0 | 一番下の層。Windows と Direct3D 12(GPU に絵を描かせる Windows の仕組み)を直接操作するコード。`CSharpGameEngine.Windows` |
| L4 | 「何をどの順で描くか」を決める層。GPU の種類に依存しない普通の C# だけで書く。`CSharpGameEngine.Rendering` |
| L5 | カメラの見え方など、ゲームの場面に近い決まりを持つ層 |
| D3D12 | Direct3D 12 の略 |
| PSO | GPU に「この描き方で描け」と渡す設定の束(使うシェーダー、塗りつぶしか線か、透明の混ぜ方など)。描き方を切り替えるたびに別の PSO を使う。今のコードには 11 種類ある |
| シェーダー | GPU の上で動く、色や位置を計算する小さなプログラム |
| パス | 1 フレームの絵を作る工程の 1 つ(影を描く、本体を描く、空を合成する、色調を整える、など) |
| フレーム計画 | 1 フレームで、どのパスをどの順に行うかの一覧 |
| 実行器 | 計画を受け取って、そのとおりに GPU へ命令を出すだけの部分 |
| ADR-0087 | 設計の決定を記録した文書の 87 番。「描画と物理のコードをどの層に置くか」を決めた |
| M0120 | 作業の単位(マイルストーン)の 120 番。「描画を L4 へ移し、描き方を選べるようにする」 |
| F-L0-011 | 機能の台帳の番号。「L0 の 11 番目の機能」で、中身は「L4 が作ったフレーム計画を D3D12 で実行する仕組み」 |
| SHA | ファイルや画像の中身から計算する指紋のような値。1 画素でも違えば値が変わる |
| 固定の 7 画像 | 描画が変わっていないことを確かめるために、指紋を固定してある 7 枚の試験の画像 |
| WARP | GPU が無くても CPU で Direct3D 12 を動かす Windows の仕組み。試験で使う |
| デバイス喪失と復旧 | ドライバーの更新などで GPU との接続が切れること。切れたら作り直し、それまでの状態を流し直して描画を続ける |
| Replay | 復旧のとき、それまでに登録したメッシュ・材質・光源などを新しい GPU の接続へ流し直す処理 |
| Forward+ | 光源が多い場面を速く描くため、画面を小さな升目に分け、升目ごとに効く光源だけを計算する描き方 |
| カスケード | 太陽の影を、近くは細かく遠くは粗く描くため、距離ごとに分けた影の範囲 |
| ABI | エンジンとシェーダーの成果物の間の、データの並び方の取り決め |

## 背景

[目標、ロードマップ、戦略.md](../目標、ロードマップ、戦略.md) の段階 A は「描画の L4 化を、フレーム計画の移行まで終える(M0120 の段階 1 まで)」を含む。Forward / Forward+ の作り直し(M0120 の段階 2〜3)は段階 D へ送った([ADR-0098](../Decisions/ADR-0098-Product-Goal-Phases-And-Compatibility.md))。

目標の形は [ADR-0087](../Decisions/ADR-0087-Rendering-And-Physics-Layers.md) §1 が決めている。L0(`CSharpGameEngine.Windows`)は D3D12 の実行だけを持ち、カメラ・光源・物体の状態、パスの順、深度の方針は持たない。フレーム計画は、キャッシュする `StagePipelinePlan` と、毎フレームの `StageFrameData` の二段にする。

[M0120](../Milestones/M0120-Render-Layers-And-Forward-Paths.md) の段階 1 は、値・CPU 計算・カメラ数学・型付き CB と実反射・ビュー提出・読み戻し値・シーン Component・公開診断・提出ポート・Server/FrameState の配置までを移した(各単位で画素一致)。残りは記録の最後にある「Native の CPU Planner 呼び出し・カメラ互換入口と初期化・フレーム順序と描画状態」と、実際の `StagePipelinePlan`/`StageFrameData` である。

## 現状の観察

2026-11-15 の main(`c98c39f8`)で確認した事実。

- `src/CSharpGameEngine.Windows/Graphics/StageRenderer*.cs` は 5 ファイル 5,469 行(本体 4,809 行)。
- `StageRenderer.RenderFrame`(`StageRenderer.cs:4248` から約 220 行)が、パスの順と D3D12 の命令を 1 つのメソッドで記録している。順は次のとおり。
  1. デバイス喪失の確認、シェーダー成果物のホットリロード、サイズ 0 なら終了。
  2. PSO の選択(Wireframe なら Wireframe、Lit 以外か選択ありなら Presentation、それ以外は通常)。
  3. Skinning の compute、Lumen の有効化と履歴の破棄、影の時間の読み取り。
  4. CPU の計画: `PrepareDirectionalLights`、`PrepareForwardPlusLocalLights`(局所光の順位付けとタイルの振り分け)、`PrepareLocalShadowAtlasProjections`。
  5. 影のアトラス、仮想影の準備、タイムスタンプ。
  6. 定数の書き込み(環境光、霧、深度、対数のラスター深度、カメラプレビュー、露出、環境)。
  7. 自動露出の初期化、GDF、GI プローブ、Lumen、AO。
  8. 主パス(HDR と距離のクリア、深度のクリア、`DrawShadowComparison` か `DrawObjects`)。
  9. 空の合成、自動露出の測光、Lumen のシーン色の写しと可視化。compute が走ったら主パスの束縛を戻す。
  10. カメラプレビュー、ポストプロセス(トーンマップ定数)、オーバーレイ、画面レイヤー。
  11. 読み戻し(検証時)、Present、フレームごとの光源の状態のリセット。
- L0 には、ADR-0087 §3 の禁止リストに当たる計画の呼び出しが 9 か所残る。
  - `RenderViewPolicy.FromCamera/FromOrthographic/FromPerspective`(`StageRenderer.cs:2190,2263,2274`、カメラ互換入口)
  - `PhysicalLightBudget.SelectDirectionalShadowIndex`(`:2346,2432`)
  - `RadialDepthProjection.TryCreate`(`:2688`、GI の撮影の投影)
  - `DirectionalShadowCascades.Build`(`:3175`)
  - 局所光の順位付けと Forward+ のタイル範囲は、L4 の関数ではなく L0 の中の自前の処理(`:3230` からの `PrepareForwardPlusLocalLights`)。
- PSO は 11 個のフィールド(`StageRenderer.cs:295-345`)で、`StagePipelineDesc` は無い。
- `IStageRenderBackend`(`src/CSharpGameEngine.Rendering/Backend/IStageRenderBackend.cs`)は setter 群と `RenderFrame(bool)` で、要約コメントにも「次の抽出段階で、明示的なフレーム計画の実行に置き換える」とある。
- `RenderServer.RenderFrame`(`Server/RenderServer.cs:359`)は、デバイス喪失から復旧するとき `RenderFrameState.Replay` で setter を再生してからバックエンドの `RenderFrame` を呼ぶ。
- `RenderServer.DeclaredQualityPlan` は `RenderQualityProfile` の宣言だけのグラフで、実際のパス(Lumen、GDF、AO、大気、プレビュー、画面レイヤー)と一致しない(ADR-0085 の調査)。
- 層の試験 `NativeBackendDoesNotPlanFrames` はまだ無い(ADR-0087 §3 は計画抽出の後に足すとしている)。
- 画素の基準は `tests/CSharpGameEngine.Windows.Tests/Graphics/StageGoldenFrameTests.cs` の対数 7 SHA と、Stage 成果物の生 SHA-256 `E485F055…B6A5`(M0120 の記録)。`GoldenFrameDump` が差分の画素数と最大差を出せる。

## 過去設計から引き継ぐ仕様

- ADR-0087 §1: L0 の責務の範囲、`Rendering.dll` は `Windows.dll` を参照しない、PSO のキーは値で比べる `StagePipelineDesc`、`DeclaredQualityPlan` は実際に実行する計画で置き換えて消す、深度の方針は L5 の `RenderViewPolicy` だけが決める。
- ADR-0087 §1-9: `AreaLightLtc`、`LumenSurfaceCacheLayout`、画素検証器は触らない。
- ADR-0087 §1-10: Lumen の履歴と露出の状態は Import の資源。資源の別名による共有はしない。
- M0120 段階 1 の進め方: 移行単位ごとに、計算を移す前後で式が同じこと(空白・名前空間以外の一致)を記録し、固定 7 画像と Stage 成果物の SHA を書き換えない。単位の検証と、全ソリューションの検証を混同しない。
- M0120 の教訓: 型の移動で利用側を探すとき、文字列・コメント中の名前と実際の型参照を区別する(2026-10-26 のインポーターの誤った using)。
- ADR-0098 §5: 1.0 までは公開 API を壊してよい(互換の並行経路を残さない)。

## 目的

1. L0 から計画の呼び出しを無くす。ADR-0087 §3 の `NativeBackendDoesNotPlanFrames` を足して固定する。
2. L4 の `StagePipelinePlanner` が `StagePipelinePlan`(パスの列)を作り、L0 は `Execute(StagePipelinePlan, StageFrameData, bool)` でそれを実行するだけにする。
3. 局所光の順位付けと Forward+ のタイルの振り分けを L4 の `StageLightPlanner` へ、方向光の影の選択・カスケード・局所光の影のアトラスの投影を `StageShadowPlanner` へ移す。結果は `StageFrameData` の型付きの配列で L0 へ渡す。毎フレームの割り当ては 0。
4. PSO を `StagePipelineDesc` の値をキーにした L0 のキャッシュから作る。
5. `RenderServer.DeclaredQualityPlan` を、実際に実行する計画(`CurrentPipeline`)で置き換える。TestHost に `frame-plan`(GPU なしでパスの一覧を出す)を足す。
6. 以上をすべて画素一致で行う。固定 7 画像の SHA、Stage 成果物の SHA、既存の WARP 画素の期待値を 1 つも書き換えない。

## 非目的

- Forward+ の URP 方式化(`ClusterLightGrid`)、Forward、`RenderPath` の選択(M0120 段階 2〜3、段階 D)。
- Deferred 系(M0121)、物理の分離(M0122・M0123)。
- シェーダー・ABI(`stage-v14`/`stage-v15`)・定数の配置の変更。
- 機能実行器(大気、GI、AO、自動露出、GDF、Lumen、Skinning)の中身の書き換え。各実行器が内部で持つ「やることが無ければ何もしない」判定はそのまま残す。計画へ上げるのは、`RenderFrame` 自身が分岐している条件だけにする。
- 復旧の作り直し。ADR-0087 §1-6 の「`RenderFrameState.Replay` は不要になる」は、この段階では行わない(下の「未決事項 1」)。
- 性能の改善。計画の導入で遅くしないことだけを求める。

## 提案設計

### パスの列(L4 `Rendering.Pipeline`)

`StagePass` は今の `RenderFrame` の順をそのまま名前にした列挙。

`Skinning`、`ShadowAtlas`、`VirtualShadows`、`FrameConstants`、`AutoExposureInitialize`、`GlobalDistanceField`、`GlobalIllumination`、`Lumen`、`AmbientOcclusion`、`MainPass`(または `ShadowComparison`)、`SkyComposite`、`ExposureMetering`、`LumenSceneCopy`、`LumenVisualize`、`RestoreMainBindings`、`CameraPreviews`、`PostProcess`、`Overlay`、`ScreenLayer`、`Capture`、`Present`

`StagePipelinePlanKey` は値型で、計画が変わる条件だけを持つ。

- 描画面のサイズ、表示モード、選択の有無
- 機能フラグ: Skinning、Lumen、Lumen の可視化、大気、自動露出、影の比較
- カメラプレビューの数、検証用の読み戻し

`StagePipelinePlanner.Build(in StagePipelinePlanKey)` が `StagePipelinePlan` を作り、キーごとにキャッシュする。計画は次を持つ。
- パスの列
- 主パスの `StagePipelineDesc`
- 主パスの後で compute が走ったか(束縛を戻すかどうか)

計画を作るときは、既存の `RenderGraphBuilder` でパスが読み書きする資源(HDR 色、距離、深度、バックバッファ、影のアトラス)を宣言して検証する。

### 毎フレームの値(L4 `StageFrameData`)

`StageFrameData` は事前に確保した型付きの配列と件数を持ち、フレームごとに件数だけを戻す。
- 選ばれた方向光と影の番号
- 選ばれた局所光と、落とした件数
- Forward+ のタイルの候補
- 局所光の影の投影
- カスケード
- GI の撮影の投影

GPU へ行く要素は unmanaged の構造体だけにする。

`StageLightPlanner` と `StageShadowPlanner` は、今の L0 のコード(`PrepareDirectionalLights`、`PrepareForwardPlusLocalLights`、`PrepareLocalShadowAtlasProjections` の CPU 部分、`PhysicalLightBudget`・`DirectionalShadowCascades` の呼び出し)を、式と比較の順を変えずに移したものにする。順位付けの比較(優先度 → 画面上の寄与 → 安定した提出順)と浮動小数点の演算順を保つことが、画素一致の条件になる。

### ポート(L4 `Rendering.Backend`)

`IStageRenderBackend.RenderFrame(bool)` を `Execute(StagePipelinePlan, StageFrameData, bool)` に置き換える。

光源の提出(`Submit*`)と `SetView` が受け取っていた値は、RenderServer が持つ。計画の材料にするためで、`RenderFrameState` は今も同じ値を保持している。

バックエンドは解決済みの値(ビュー、`StageFrameData`)だけを受け取る。資源の登録(メッシュ、材質、物体)、変換、可視性、オーバーレイ、画面レイヤーの setter は、L0 の資源の更新なので残す。

### 実行器(L0)

`StageRenderer.Execute` は計画のパスの列を `switch` で順に記録する。各パスの中身は、今の `RenderFrame` の該当部分を移すだけにする(バリア、クリア、束縛、描画、タイムスタンプ)。

PSO は `StagePipelineDesc` を値で比べるキャッシュから作る。今の 11 個の PSO は、それぞれの desc の定数で置き換える。

カメラ互換入口(`SetCamera`/`SetViewProjection` と初期化の `RenderViewPolicy` の呼び出し)は L0 から消し、L5 の拡張メソッドだけにする。既定のカメラの初期化は M0119・M0120 の起動の契約(深度 0.22822031)を保つ。

### RenderServer(L4)

`RenderServer.RenderFrame` は次の順で動く。
1. 今の状態からキーを作る。
2. 計画をキャッシュから取る。
3. `StageFrameData` を埋める。
4. `Execute` を呼ぶ。

`DeclaredQualityPlan` を `CurrentPipeline` で置き換え、`RenderQualityProfile` の宣言だけのグラフを削除する。利用側は試験と診断だけであることを移行のときに確かめる。

## 影響範囲

- `src/CSharpGameEngine.Windows/Graphics/StageRenderer*.cs`、`D3D12RenderServer.cs`、`StagePhysicalLightApi.cs`、`TypedLightBackend.cs`
- `src/CSharpGameEngine.Rendering/Backend/IStageRenderBackend.cs`、`Server/RenderServer.cs`、`Server/RenderFrameState.cs`、`RenderQualityProfile.cs`、新しい `Pipeline/StagePipelinePlanner.cs`・`StagePipelinePlan.cs`・`StagePipelineDesc.cs`・`StageFrameData.cs`・`StageLightPlanner.cs`・`StageShadowPlanner.cs`
- `src/CSharpGameEngine.CodeAnalysis/GeneratedEditorPreviewBridgeEmitter.cs`(バックエンドの型名を出している)
- `tools/CSharpGameEngine.TestHost`(`frame-plan`)、`tools/CSharpGameEngine.FeatureRunner/FeatureStageCatalog.cs`(InputPaths)
- 試験: `tests/CSharpGameEngine.Rendering.Tests`、`tests/CSharpGameEngine.Windows.Tests/Graphics`、`tests/CSharpGameEngine.Core.Tests` の `EngineLayerSpec`
- 文書: M0120、Rendering.md、Architecture.md、Implementation-Status(F-L0-011)、Conformance
- 公開 API の破壊: `IStageRenderBackend.RenderFrame`、`RenderServer.DeclaredQualityPlan`、L0 のカメラ互換入口。ADR-0098 §5 で許す。SDK の版は `0.1.0-dev.N` の N を上げる。

## 移行手順

各単位は、ビルド → 移した計算の CPU 試験 → GPU の回帰 → 固定 7 画像と Stage 成果物の SHA の順で確かめ、単位ごとにコミットする。移した計算は、移す前のコードとの一致を記録する(M0120 の `algorithm-preservation.json` と同じ形)。

1. **`StagePipelineDesc` と PSO のキャッシュ。** 11 個の PSO を desc から作る。パスの順は変えない。
2. **`StageLightPlanner`。** 局所光の順位付け、上限、落とした件数、Forward+ のタイル範囲を L4 へ移し、L0 は `StageFrameData` の配列を書くだけにする。方向光の選択(`PhysicalLightBudget`)も同じ単位で移す。
3. **`StageShadowPlanner`。** カスケード(`DirectionalShadowCascades.Build`)、局所光の影のアトラスの投影、GI の撮影の投影(`RadialDepthProjection.TryCreate`)を移す。
4. **カメラ互換入口の削除。** L0 から `RenderViewPolicy` の呼び出しを消し、起動の契約の試験で確かめる。
5. **`StagePipelinePlan` と `Execute`。** `RenderFrame` の本体をパスごとのメソッドへ分け、計画の列で呼ぶ。RenderServer が計画を作る。`DeclaredQualityPlan` を `CurrentPipeline` で置き換え、TestHost の `frame-plan` を足す。
6. **固定と記録。** `NativeBackendDoesNotPlanFrames` を足す。全ソリューションを `-m:1` で検証し、文書・台帳・カタログを更新する。F-L0-011 は、Forward 系を除いた範囲で Implemented にできるかを台帳で判断する。

## テスト計画

- **画素:** `StageGoldenFrameTests` の 7 SHA と Stage 成果物の SHA を各単位で確かめる。食い違ったら `GoldenFrameDump`(`CGE_GOLDEN_DUMP`/`CGE_GOLDEN_BASELINE`)で差分の画素数と最大差を取り、基準は書き換えない。
- **GPU の回帰:** `RenderViewGpuIntegrationTests`、`VirtualShadowGpuTests`、`LumenGpuIntegrationTests`、`GlobalDistanceFieldGpuIntegrationTests`、`VolumetricCloudGpuIntegrationTests`、復旧・ホットリロード・カメラプレビュー・スキニングの既存の試験を、WARP の High で行う。
- **新しい CPU の試験(Rendering.Tests):**
  - `StagePipelinePlannerTests`: すべてのキーで、エントリー名が `ShaderArtifactAbi.GetEntries` に含まれる(ADR-0087)。今の `RenderFrame` と同じ順になる。資源の宣言が検証に通る。
  - `StageLightPlannerTests`: 同じ優先度・寄与・提出順の境界、上限を超えたときの落とした件数、画面外の光、タイルの端。
  - `StageShadowPlannerTests`: 影を落とす方向光が 0・1・複数のとき、カスケードの境界、アトラスの上限。
  - `StageFrameData` の割り当て 0(`GC.GetAllocatedBytesForCurrentThread` で 2 フレーム目以降が 0)。
- **層:** `EngineLayerSpec.NativeBackendDoesNotPlanFrames`。既存の `PortableRenderingDoesNotNameNativeBinding` などは維持する。
- **全体:** 最後の単位の後、ソリューション全体を `-m:1` で再ビルドし、GPU 以外と WARP の試験をすべて実行する。
- **見た目:** 画素一致が目的なので、修正前後の画像は 7 画像の SHA の一致を証拠にする(AGENTS.md の見た目の比較の規則に当たる変更は無い想定)。

## リスクと対策

| リスク | 対策 |
|---|---|
| 浮動小数点の演算順や比較の順が変わり、画素が 1 つでもずれる | 式を移すだけにし、移す前後の一致を記録する。単位ごとに 7 SHA を確かめ、ずれたらその単位を戻して原因を切り分ける |
| L0 のフレームごとの状態(光源の件数、落とした件数、方向光が次のフレームへ残るかどうか)の結びつきが、移すと崩れる | リセットの規則(`DirectionalLightsPersistAcrossFrames` など)を `StageFrameData` と RenderServer 側の試験で固定してから移す |
| 復旧の再生(`RenderFrameState.Replay`)と新しい計画の食い違い | 復旧は今の再生を残す。復旧の GPU 試験(第 2 世代のデバイスで同じ画像)を各単位で回す |
| 公開 API の破壊で、生成されるエディターのプレビューのブリッジや VSIX が壊れる | `GeneratedEditorPreviewBridgeEmitter` と VSIX の試験を単位 5 で回す。プレビューのプロトコルは変えない |
| 計画を毎フレーム作って遅くなる | 計画はキーでキャッシュし、`StageFrameData` は割り当て 0。既存の性能の Stage の中央値で比べる |
| WARP の Epic でデバイスが失われる既知の不安定(M0111) | GPU の試験は High で行う(既存の方針) |
| 並行セッションの未コミットの変更 | 各単位の前に `git status` を確かめ、自分の変更だけをコミットする |

## 未決事項

1. **復旧の作り直しをこの段階に含めるか。** ADR-0087 §1-6 は、持続状態を L4 が持つので「新しい世代へ同じ計画を Execute するだけになり、Replay は不要」とする。この設計では、資源の登録(メッシュ・材質・物体)の再生が残るため、Replay を残す(推奨)。含める場合は、資源の登録の再生を `Execute` の前段へ移す作業が増える。
2. **F-L0-011 の状態。** Forward 系を段階 D へ送ったので、この段階の完了で F-L0-011 を「Implemented(Forward 系を除く範囲)」にするか、PartiallyImplemented のまま段階 D で閉じるかは、単位 6 で台帳の定義を見て決める。

## 合意状態

- 2026-11-15: 設計を作成した。
- 2026-11-15: 所有者が「この設計で始める」と合意した。未決事項 1 は推奨どおり、復旧の再生(Replay)を残す。未決事項 2 は単位 6 で判断する。

## 実装記録

### 単位 1: 描き方の設定(PSO)の一覧と作り方の統一(2026-11-15)
- L4 に `StagePipelineDesc`(描き方の設定を値で表す型)と、使う 14 通りの一覧 `StagePipelines` を置いた。L0 は `StageRenderer.Pipelines.cs` の 1 つの関数で、値から D3D12 の設定を組み立てる。作ったものは値をキーにした表に入れ、描画のコードは表から取り出したものを使う。描く順番と描画のコードは変えていない。
- 削除: L0 の作成関数 5 つ(物体用・オーバーレイ用・影用・仕上げ用・画面レイヤー用)と、シェーダーの再読み込み時と破棄時の、設定ごとの個別の解放と巻き戻し。再読み込みは「新しい表を全部作れたら差し替え、古い表を解放する。途中で失敗したら新しい表を解放する」になった。
- 試験: `StagePipelinesTests` 6 件(入口名がシェーダーの成果物の一覧にあり種類が合う、使われていない入口が無い、重複が無い、深度だけの描画は影だけ、距離を書かない描画、画面に重ねる描画)。Rendering.Tests 206 件合格。
- 画素: 書き換え前(`F:\CodexTemp\cge-m0120\baseline`)と書き換え後(`unit1`)の固定 7 画像は、生の画素のバイト列が完全に一致した。描画の試験(`CSharpGameEngine.Graphics.D3D12.Tests`)は 204 件中 202 件合格・2 件対象外・失敗 0。
- 書き換え前の基準の実行では `RenderViewGpuIntegrationTests.TypedViewMatchesLegacySubmissionAndSurvivesDeviceRecovery(Mode: 2)`(正射影で、装置の作り直しの後に絵が同じか)が 1 回失敗した(実行時間 22 分、機械が混んでいた)。書き換え後の実行と、単独の 5 回(各 4 通り)ではすべて合格した。負荷のあるときだけ起きる不安定さと推測する。原因は未特定で、単位 6 の全体の検証で再度確かめる。

### 単位 2: 光源の計画を L4 へ(2026-11-15)
- L4 に `StageLightPlanner` を置いた。局所光・方向光の画面上の寄与の見積もり、使う順の比べ方(局所光は優先度 → 寄与 → 提出順、方向光は寄与 → 提出順)、局所光が届く範囲から Forward+ のタイル範囲を出す計算を、L0 から式と比較の順を変えずに移した。比べ方は提出順が異なれば全順序なので、並べ方の手順によらず結果は一つに決まる。
- L0 の `CalculateScreenContribution`・`CalculateDirectionalScreenContribution`・`TryGetForwardPlusTileBounds` と、並べるための入れ物 2 つを削除した。L0 はまだ L4 の計画の関数を呼ぶ(呼び出しを RenderServer へ移すのは単位 5)。
- 方向光の影の選び方 `PhysicalLightBudget` はもともと L4 にあるので、移すものは無かった(呼び出しの位置は単位 5 で移す)。
- 試験: `StageLightPlannerTests` 8 件(並べる順、深度の 2 乗で弱まる寄与、有限でない値、画面の中・外、カメラの後ろへ回る光、画面いっぱいの光)。Rendering.Tests 214 件合格。
- 画素: 固定 7 画像は書き換え前と生の画素が完全に一致(`F:\CodexTemp\cge-m0120\unit2`)。描画の試験 204 件中 202 件合格・2 件対象外・失敗 0。光の試験(`TypedLightFrameTests`・`RenderPreviewLightingSessionTests`・`AmbientLightSyncTests`)22 件合格。

### 単位 3: 影の計画を L4 へ(2026-11-15)
- L4 に `StageShadowPlanner` を置いた。スポットライトと面光源の影のアトラスへの投影、点光源の 6 面の投影と面の向き、点光源が影を落とせるかの判定、GI のプローブを撮るときの対数深度の符号化を、L0 から式を変えずに移した。点光源の面の数 6 はアトラスの割り当て(`PhysicalShadowAtlasPlan.PointTilesPerLight`)と一致することを試験で固定した。
- 方向光のカスケード(`DirectionalShadowCascades`)とアトラスの割り当て(`PhysicalShadowAtlasPlan`)はもともと L4 にあるので、移すものは無かった。L0 の `RadialDepthProjection.TryCreate` の呼び出しは消えた。
- 試験: `StageShadowPlannerTests` 4 件。Rendering.Tests 218 件合格。
- 画素: 固定 7 画像は書き換え前と生の画素が完全に一致(`unit3`)。描画と光の試験 226 件中 224 件合格・2 件対象外・失敗 0。

### 単位 4: カメラの互換入口の削除(2026-11-15)
- L0 から `SetCamera(RenderCamera)` と `SetViewProjection` の 3 つの重ね定義、覚えておいたカメラ(`LastShadowCamera`)を削除した。L0 は L5 の `Rendering.View` 名前空間を参照しなくなった。
- 代わりに L0 は内部の `SetViewSource(RenderViewSource)` で L4 のビューの元を受け取り、影の比較表示へ切り替わったときは半分の幅で解決し直す。以前は覚えておいたカメラで同じことをしていたので、比較表示の切り替えの動きは変わらない。固定のビュー(`SetView`)を受け取るとビューの元は消える(以前の `LastShadowCamera=null` と同じ)。
- 起動直後の既定のビューは L4 の `StageRenderDefaults.InitialViewSource` に置いた。L5 の `RenderCamera.CreateDefault(Vector3.Zero)` と同じ目の位置と上方向をビット列のまま持ち、`StageRenderDefaultsTests` が L5 の計算とビット単位で一致することを固定する(M0119・M0120 の起動の契約)。
- StageRenderer を直接使う試験のために、試験側に `StageRendererViewExtensions`(以前と同じ計算で L5 の `RenderViewPolicy` からビューを作って渡す)を置いた。製品の経路(EngineHost、プレビュー)は RenderServer を通すので変更なし。
- 画素: 固定 7 画像は書き換え前と生の画素が完全に一致(`unit4`)。Windows.Tests 全体 940 件中 938 件合格・2 件対象外・失敗 0。Rendering.Tests 219 件合格。

### 単位 5 の分割(2026-11-15、実装中の判断)
単位 5 の準備で、光と影の計画の呼び出しを L0 から無くすには、光源の提出そのもの(`Submit*`、`SetLight`)と、そのフレームごとの持続の規則(`DirectionalLightsPersistAcrossFrames`、そのフレームだけ明示した提出、落とした件数の数え方、提出の途中で書き込む経路)を RenderServer へ移す必要があると分かった。さらに、StageRenderer を直接作って描く試験(約 40 か所)も、RenderServer を通す形へ移す必要がある。単位 5 を一度に行うと、画素の食い違いが出たときに原因を切り分けにくいので、次の 2 つに分ける。
- **5a: パスの列と実行。** L4 の `StagePipelinePlanner` がパスの列(`StagePipelinePlan`)を作り、L0 は `Execute` でその順に記録する。RenderServer が計画を作り、`DeclaredQualityPlan` を `CurrentPipeline` で置き換える。各パスの中の「やることが無ければ何もしない」判定は、非目的のとおり実行器に残す。
- **5b: 光源と影の提出と計画を RenderServer へ。** 光源の提出と持続の規則を RenderServer へ移し、毎フレームの `StageFrameData` で L0 へ渡す。StageRenderer を直接使う試験を移す。これで L0 から光と影の計画の呼び出しが無くなり、単位 6 の `NativeBackendDoesNotPlanFrames` を足せる。
