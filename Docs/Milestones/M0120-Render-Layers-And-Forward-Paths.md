# M0120 — 描画の L4 化と Forward / Forward+(URP 方式)
- 日付: 2026-10-25
- 状態: Implementing(2026-10-26。M0119完了後、段階1のポータブル値型から移動中。RenderServer/計画とForward系は未移行)
- 決定: [ADR-0085](../Decisions/ADR-0085-Urp-Render-Paths.md)、[ADR-0087](../Decisions/ADR-0087-Rendering-And-Physics-Layers.md) §1
- ABI: `stage-v15`

## 目的
パス構成・光源の振り分け・深度の方針を L4/L5 の移植可能なコードへ移し、L0 は D3D12 の実行だけにする。Forward+ を URP 方式(タイル × 対数殻の Z-bin)にし、Forward(物体ごとに最大 8 灯)を足す。

## 段階
1. **境界を動かす(画素は変えない。対数の 7 SHA と既存の WARP 画素の期待値を 1 つも書き換えない)**: `src/CSharpGameEngine.Rendering` に `View/`・`Pipeline/`・`Backend/`・`Server/` を置く。値型、`StageGpuLayouts`(DXC の反射で検査)、CPU の光源・影・フレーム計画、`StagePipelinePlanner`(今の `RenderFrame` の順)と `StagePipelineDesc`、ポート `IStageRenderBackend`、`RenderServer`/`RenderFrameState` の L4 への移動と `D3D12RenderServer.Create`、`Camera` → `RenderCamera`、シーンの Component `Camera`/`PrimitiveMesh` を Runtime.Windows の共有ソースへ(名前空間は不変)、`EngineLayerSpec` の新しい規則、`EveryStageInputPathExists`、`GeneratedEditorPreviewBridgeEmitter` と InputPaths の付け替え。TestHost の `frame-plan`。
2. **Forward+ を URP 方式にする**: `ClusterLightGrid`(タイルは 8 px から倍々、Z-bin は深度の値 `d` で切る、t6 の `ClusterWords` 1 本)。`ClusterLightGridTests`(総当たりのオラクルで取りこぼし 0)。ヒートマップ。
3. **Forward と選ぶ入口**: `ForwardObjectLightAssignment`、`RenderPath`、`Level.RenderPath`、`CGE_RENDER_PATH`。Deferred 系の指定は型付きのエラー。
4. **カタログ・文書・版**: Stage `rendering.render-paths`、F-L0-011・F-L4-020 を Implementing に。

## 完了条件
- Forward+ の 7 SHA が M0119 と同じ。Forward と Forward+ がビット一致(各物体の局所光が 8 以下)。
- 層の試験に合格。`ShaderArtifactHotReloadTests`(PSO キャッシュが成果物の差し替えで捨てられる)。
- 性能の予算を満たす。

## 範囲外
Deferred 系(M0121)、触らない L0 の CPU アルゴリズム(`AreaLightLtc`・`LumenSurfaceCacheLayout` など)、入力・音声サーバー、実行時の glTF/WIC。

## 機能 ID
F-L0-011、F-L4-020、F-L0-002、F-L4-002、F-L4-004、F-L4-007

## 段階1の移行単位
1. 先にEnvironmentLighting/StageRenderInputsをRenderingへ移し、公開値型からネイティブ名前空間を除く。演算・シェーダー・既定値・画像基準は変更しない。実際の参照先だけusingを更新し、カタログ入力パスも同時更新する。
2. 光源/影/発光パッチのCPU計算と値を移す。StageRendererを呼ぶ提出アダプターは、ポート接続の段階までL0に残す。
3. カメラ/シーンComponent、GPU配置、RenderServerとフレーム計画を順に移し、最後にL0からPlanner/Policyの呼出しを禁止する。途中の値型移動だけでL0分離完了とは扱わない。
- 検証は移動した単位のCPU契約・ビルド・固定7画像から行い、段階1完了時に層/ホットリロード/実行計画をまとめて確認する。

## 実行記録: 値とCPU計算の移動(2026-10-26)
- EnvironmentLighting、StageRenderInputsとEmissivePatchの値はRendering、PhysicalLighting、DirectionalShadowCascades、EmissivePatchBuilderの計算はPipelineへ移した。ネイティブ提出はStagePhysicalLightApi/EmissivePatchSubmissionとして分離。Environmentの露出計算がPhysicalLightUnitsを使う依存を最初のビルドで確認し、同じ移行単位に含めた。発光値型のusing不足もビルドで修正した。
- 計算・既定値・光源順位・影タイル・発光形状の5箇所を、空白と名前空間/参照定数の所有者以外は旧ソースと完全一致することを検査。方向光のタイル0はL4定数を正本とし、既存の公開L0定数も同じ値を参照する。証拠は `.artifacts/M0120/algorithm-preservation.json`。
- 移動後のWindows回帰54件成功。固定7画像のSHAはM0119と同じ。環境/影、物理光量、ホットリロード/復旧、シーン入力を含む。その後、既存の純粋CPUテスト23件をWindows.TestsからRendering.Testsへ移し、Rendering全体136件成功。式の期待値は変更していない。
- EngineLayerSpecは5件成功。Rendering全ソースのネイティブ型/名前空間禁止と、net10.0/ポータブル参照/unsafe・ネイティブfriend禁止を追加。L0からPlannerを呼ぶ箇所はまだ残っているため、その最終禁止は計画抽出後に追加する。
- EveryStageInputPathExistsが既存のSpatialディレクトリ3件の不在を検出。実在する対応ソース/試験へ置換し、新しい描画入力とSpatial入力が変更ステージ選択へ届くことを4ケースで確認。カタログ/変更選択/リリース証拠は35件成功。
- 証拠は `.artifacts/M0120/portable-regression.log`、`portable-rendering-tests.log`、`portable-core-tests.log`、`portable-catalog-final.log` と各memory.json。最大物理RAM使用率68%、閾値停止なし。公開API/梱包変更に合わせSDKを0.1.26へ更新。シェーダーと画素基準は無変更。
- 適合はこの移動単位でMatches、M0120全体はPartial。RenderCamera/ViewPolicy、GPU配置、RenderServer、フレーム計画、Forward/Forward+は後続。

## 実行記録: カメラ数学のL5移動(2026-10-26)
- Graphics.D3D12.CameraをRendering.View.RenderCameraへ移した。L5のビュー数学であり、SceneFactory/RUF用に既存Core参照を追加。シーンComponentのRuntime.Windows.Cameraの型名・データは変更していない。F-L0-001は安定した履歴IDを維持し、台帳/カタログのLayerをL5へ修正した。
- 既存CameraTests21件をRendering.Testsへ移し、Rendering全157件成功。型名/名前空間/空白を除いたstruct本体が移動前と完全一致することをcamera-preservation.jsonへ記録。古いネイティブ説明を含むヘッダーコメントは整理した。SceneViewのnet472側には新しい依存を持ち込まず、既存の式による実装を維持した。
- GPU/復旧/シーン/観察カメラ43件成功。固定7画像は同じSHA。Stage成果物の生SHA256はE485F055EE871A6214EAD7558EC27E05AA8C5E5E1134DB6FAE9C4F74DF56B6A5で移動前と一致。最大RAM67%、閾値停止なし。
- 層6件成功。L4からL5 View型を参照しない規則も追加。カタログ/変更選択31件成功。デバッグ層を持たないCPU計算の試験もGPU試験と区別する。SDKは0.1.27。証拠はportable-camera-cpu.log、portable-camera-gpu.log、camera-layer-final.log、camera-catalog-final.logと各memory.json。
- カメラ数学の所在だけを移した段階で、StageRenderer.SetCameraと初期化はまだその型を使う。RenderView/RenderViewPolicyによる提出・フレーム計画/状態の抽出、RenderServer移動は未完。全体回帰を次に実行する。

### 全体ビルドで見つかったインポーター参照(2026-10-26)
- 9689b0aの全体実行はテスト失敗0だったが、Unreal.SceneImporterに誤って追加したRenderingのusing2行がCS0234となり、終了コード1。Import.Testsはビルドできず未実行で、実行済みは21プロジェクト3466件成功・2件スキップ。全体成功とは扱わない。
- 原因は移動時の型名検索がコメント/生成用文字列にも一致し、実際の型依存がないツールへusingを追加したこと。不要な2行だけを削除し、ポータブルなインポーターに新しい描画参照は追加していない。修正後のImport.Tests73件は成功。失敗元ログはportable-solution-final.log、修正確認はportable-importer-fixed.log。各memory.jsonで最大68%/63%、閾値停止なし。
- 型移動の消費側を判定する際は、文字列/コメント中の名称と実際の型参照を区別する。修正を含むコードで全ソリューションを再実行する。

### 値/計算/カメラ移動後の全体確認(2026-10-26)
- ff432438896f49734122774a63b1dc25039a3c33で全ソリューション22プロジェクト、3539件成功・失敗0・実機測定2件スキップ、終了コード0。Windows774件、Rendering157件、Core157件、FeatureRunner239件、VSIX1197件、Import73件を含む。移動した既存44件と新規の層/パス検査9件が合計と一致する。
- 証拠は `.artifacts/M0120/portable-solution-verified.log`、同summary.json/memory.json。最大物理RAM使用率69%、閾値停止なし。Stageバイナリと固定7画像は移動前と同じ。M0120全体のPartialは継続し、次はGPU配置/実反射情報とビュー/フレーム計画の抽出。

## 実行記録: 型付きCBと実反射(2026-10-26)
- L4 PipelineのStageFrameConstants/StageObjectConstantsを正本にし、StageGpuLayoutsがMarshal.OffsetOf/SizeOfで35オフセットとサイズを得る。NativeのFrame*/Object*の手書き定義を削除し、通常描画とLumenカード撮影の両方を接続。バッファの256バイト境界への切り上げはL0に残す。FrameCB960/ObjectCB192、フレームスロット1024、物体スロット512は同じ。
- L7 HlslCompilerがDXC_OUT_REFLECTIONを取得し、HlslShaderReflectionが実際のバッファ/変数配置を読む。Stageの通常/観察/表面/カードとLumen計6条件で全フィールドのオフセット/サイズと行列の行配置を照合。LumenだけのStageInverseViewProjectionは明示した別名。カードPSはFrameCBを参照せず最適化で消えるため、不在とObjectCBを検証する。初回の一律FrameCB必須というテスト仮定は実コンパイラ出力と矛盾し、製品コードを変えず修正した。
- 成果物エントリーのReflection欄は従来のentry/profile/stage/ABIのJSONメタデータのまま。実反射の生データは別のコンパイル結果APIであり、RuntimeにDXCは持ち込んでいない。シェーダーとstage-v14のABIは変更していない。SDKは0.1.28。
- 接続後のGPU回帰31件成功(固定7画像、2フレームスロット、主深度、遠距離Lumen、環境/GI/面光源、ホットリロード/復旧)。CLI全15件(新しい実反射6件を含む)、Rendering157件、層7件、FeatureRunner240件も成功。新規CB変更後に全ソリューションを再実行したとは扱わず、直前の全体3539件と今回の対象検証を区別する。
- Stage成果物の生SHA256はE485F055EE871A6214EAD7558EC27E05AA8C5E5E1134DB6FAE9C4F74DF56B6A5で移動前と完全一致。固定画像の基準は変更していない。最大RAM62%、閾値停止なし。証拠は `.artifacts/M0120/gpu-layout-native-final.log`、`gpu-layout-shader-tests.log`、`gpu-layout-portable-tests.log`、`gpu-layout-layer-tests.log`、`gpu-layout-catalog-tests.log` と各memory.json。
- 初回のNative接続ビルドはLumenのpartialファイルへのstatic using不足で失敗し、共有のオフセットを明示して修正した。DXCバインディングのGUIDポインター形式も既存APIに合わせて修正。失敗ログはgpu-layout-native-regression.log/gpu-layout-reflection-first.log、合格ログは上記へ別保存した。
- M0120全体はPartial。構造化光源のGPUレコード/計画データ、RenderViewPolicyと提出、RenderServer、パス実行計画、Forward/Forward+の実装は後続。

## 既定起動の補足
- SetCamera未提出のStageの深度不整合を追加再現し、既定カメラの初期化を共通処理へ揃えた(M0119追記)。修正前は深度0.9759626/期待0.22822031、修正後のGPU38件は成功。SDK0.1.29。これはビュー/フレーム計画の抽出完了を意味せず、その後の分離で保持すべき起動契約である。

## 最新の全体回帰(2026-10-26)
- 5f4d647/SDK0.1.29の全22プロジェクトが3548件成功・失敗0・実機測定2件スキップ、終了0。値/計算/カメラ移動、型付きCB、実DXC反射6条件、既定Stageの起動修正をすべて含む。Core158、Rendering157、CLI15、FeatureRunner240、Windows775、VSIX1197、Import73件。以前の全体記録の測定元コミットは変更せず、最新結果を別保存した。
- 証拠は `.artifacts/M0120/native-startup-solution.log`、同summary.json/memory.json。最大RAM66%、閾値停止なし。Stageバイナリの生SHA256はE485F055EE871A6214EAD7558EC27E05AA8C5E5E1134DB6FAE9C4F74DF56B6A5、固定7画像も同じ。版はSDK0.1.29、ABIはstage-v14。
- 参照元G:/Unity/6.6/OmniのmanifestとPackageCacheでURP17.6.0を再確認。ForwardLights/Clustering/StencilDeferred/ClusterDeferredは現行の承認設計ADR-0085と同じ参照先で、Unity自体は変更/起動していない。
- M0120全体はPartialのまま。次はRenderViewPolicy/RenderView、RenderServer/フレーム計画/実行ポート、Forward+クラスタとForward。Deferred系と物理の後続も未実装。mainへは統合していない。

## ビュー提出の移行単位(2026-10-26)
- ADR-0087に従い、L4 RenderViewを検証済みの行列/補助投影/深度/カメラ位置/露出/任意のカメラ基底の値とする。L5 RenderViewPolicyは既存Cameraと行列提出からその値を作る。主ビューでの観察用符号化はまだ受け付けず、観察サムネイルは既存の専用契約を維持する。
- Native SetViewは検証済み値をアップロードし、旧SetCamera/SetViewProjectionの数式はL5へ移す。初期化も同じ経路を使い、既定起動の回帰契約を保つ。
- RenderServerにSetViewを加え、型付き提出はRenderFrameStateが値として保持/復旧する。旧APIの復旧用メタデータは、この移行単位では維持する。Cameraの自動aspectはリサイズ復旧時に再計算される既存契約があるため、未検証のまま解決済み行列だけへ置き換えない。旧APIの除去とL4 Server移動時にL5アダプターへ分離する。
- 値のdefault/不正行列/非有限値/投影と深度の不一致をCPUで検査し、透視/正射影/極端なfar/既定起動/型付き提出の復旧をGPUで確認する。固定7画像とシェーダーは変更しない。

- 型名の実装確認で、既存RenderViewがFrustum/LodHysteresisだけの可視性クエリ値として使われていることが判明。ADRの主ビュー提出名と衝突するため、この既存型はRenderVisibilityViewへ改名する。参照はRenderWorldSnapshot/同テスト/性能TestHostの3ファイルだけで、選別アルゴリズムとデータ配置は変更しない。SDKのAPI変更として記録する。

### ビュー契約の実行結果(2026-10-26)
- L4 RenderViewはdefault/非有限値/投影と深度の不一致/光線を変える補助投影/不整合な基底を拒否する。透視の物理行列が巨大farで特異でも、有限の補助投影の逆行列が得られれば保持できる。L5 RenderViewPolicyが既存のカメラ/行列から値を生成し、Nativeの旧APIも同じ検証済み値をSetViewへ渡す。
- RenderServer.SetViewとRenderFrameState.RecordView/Replayを接続。透視カメラ・透視行列・正射影の3条件で旧APIと画像一致し、型付き値だけの3フレーム継続とデバイス第2世代復旧も同じ画像を保持する。default提出は状態変更前に拒否。旧APIのCamera/行列パラメーター保存はリサイズ互換のため残す。
- Rendering全169件成功(新しいビュー契約12件と既存の可視性/LODを含む)。GPU回帰31件成功(型付きビュー3条件、固定7画像、主深度、サムネイル、復旧等)。基底整合を加えた最終GPU6件、層7件、FeatureRunner242件も成功。最大RAM41%、閾値停止なし。固定7画像とStage成果物SHA E485F055...B6A5は不変。
- 証拠は `.artifacts/M0120/view-validated-cpu.log`、`view-contract-gpu.log`、`view-validated-gpu.log`、`view-layer-tests.log`、`view-catalog-tests.log` とmemory.json。最初のビルドは既存の可視性RenderViewとの名前衝突で失敗し、用途をRenderVisibilityViewとして明示して修正した。SDK0.1.30、ABIはstage-v14。
- 全体のPartialは継続する。NativeにはまだL5 Policyを呼ぶ互換入口と初期化があり、実行器だけになっていない。次の移行でこの入口、旧リサイズ再生成、RenderServer/フレーム計画を分離する。今回の対象検証を過去の全ソリューション3548件と混同しない。
