# M0119 — 対数ネイティブの描画(標準深度の削除)
- 日付: 2026-10-25
- 状態: Verified(2026-10-26。対数ネイティブ深度の完了条件を満たした。描画レイヤー分離・4描画パス・全スケール物理はM0120〜M0123)
- 決定: [ADR-0084](../Decisions/ADR-0084-Logarithmic-Native-Depth.md)(ADR-0076 を一部 Superseded)
- ABI: `stage-v14`、プレビューのプロトコル 9

## 目的
透視ビューの深度経路を 1 系統(最初から対数深度を書く Stage シェーダー)にする。標準(双曲)深度の選択(型・欄・プロトコル・PSO・ゴールデン)を削除し、双曲深度のまま残っていた透視パス(Lumen の表面前パス、DDGI のプローブ撮影、AO の深度前パス)を対数にする。画素は削除前の対数と同じ。

## 段階
1. **最後の比較と対数のゴールデン**: `StageGoldenFrameTests`(対数の 7 ケース)を作り、同じ 7 ケースを標準と対数で描いて基準内(各チャンネル 1 階調以内、違う画素 1 % 以下。ワイヤーフレームは違う画素 1 % 以下)であることを確かめてから SHA を固定する。外れたら何も消さずに原因を調べる。`LogarithmicDepthOracleTests`(板を 4e-6 〜 5e18 m に置き、中央 4 ulp 以内、全画素で深度 < 1.0、正射影は 2 ulp 以内)、`GoldenFrameDump`(`CGE_GOLDEN_DUMP`/`CGE_GOLDEN_BASELINE`)を足す。
2. **予行**: 標準を明示しているだけの試験(Lumen、ミップ、距離場、雲)を対数で流し、落ちたものと原因を記録する。
3. **Stage を最初から対数にする**(1 項目 1 コミット、それぞれ付け替えの規則で確かめる): (a) `VertexOutput` の一本化・`VSMain` の保守的ラスタ深度・`PSMain` の `SV_DepthGreaterEqual` と `SceneDistance`・`Depth.cgesubgraph`・空の合成が `SceneDistance` を読む、(b) DDGI のプローブ撮影を対数にし `CaptureDistance` を足す、(c) Lumen の表面前パスを対数にする(先に far 1e18 m で落ちることを記録)、(d) AO が `SceneDistance` を読み、AO の深度前パスを消す。
4. **標準の削除**(ADR-0084の追記に従い検証可能な境界ごとに分ける。付け替えは認めない。7 SHA は段階 3 の後と同じ): ラッパーのエントリーと標準の PSO 系統、`RenderDepthMapping.Standard`・`CameraDepthMapping`・`RenderPreviewDepthMapping`・`Camera.DepthMapping`、`StandardModeGoldenFrameTests`、`ModelRenderer`/`TriangleRenderer` と `model-v1`/`triangle-v1` を消す。`RenderDepthEncoding`・`RenderViewDepth`(L4)を足す。プロトコル 9。ADR-0076 を一部 Superseded にする。
5. **実機の証拠と文書・版**: RTX 4060 Ti で `RadialDepthPrecisionEvidence`(中央 4 ulp 以内)と `RadialDepthPerformanceEvidence` を記録する。

## 完了条件
- `git grep -nE 'RenderDepthMapping\.Standard|CameraDepthMapping|RenderPreviewDepthMapping|RadialVSMain|RadialPSMain|PreviewRadialPSMain|PreviewCleanRadialPSMain|StandardModeGoldenFrameTests' -- src tools tests assets` が 0 件。
- 7 SHA が固定され、オラクルに合格。`dotnet test CSharpGameEngine.slnx -c Debug` が失敗 0、D3D12 のデバッグ層のエラー 0。
- 4 テンプレートの実走が M0111 と同じ(ThirdPerson 15.700 m・上昇 1.228 m、FirstPerson 16.000 m・0.863 m、TopDown 9.606 m・(6.79, 6.84)・17.414 m、Vehicle 57.150 m・30.5°)。
- `rendering.performance` の予算(中央値 6 ms、p95 8.33 ms、割り当て p95 0)を満たす。

## 範囲外
ランタイムがシーンカメラの正射影を無視する既存の未実装、reversed-Z、MSAA、モーションベクトル、アセンブリ間の移動(M0120)、主 DSV への Lumen の表面前パスの統合。

## 機能 ID
F-L5-038(「Logarithmic depth of every perspective view」に改名)、F-L5-039、F-L0-002、F-L4-002、F-L8-018

## 実行結果

### 段階 1(2026-10-25、WARP、192 × 108)
- 標準の 7 ハッシュは共有シーン(`GoldenFrameScene`)へ付け替えた後も不変(`StandardModeGoldenFrameTests.StandardFrames_MatchTheirPinnedHashes` 合格)。
- 標準と対数の最後の比較(`LogarithmicFrames_EqualTheStandardFramesWithinOneLevel`、基準: 各チャンネル 1 階調以内・違う画素 1 % 以下、ワイヤーフレームは違う画素 1 % 以下)は 7 ケースすべて合格。証拠は `.artifacts/M0114/golden/`(PNG・raw・`parity.json`)。

| ケース | 違う画素(20736 中) | 最大の階調差 | 標準の SHA-256 | 対数の SHA-256(固定) |
|---|---|---|---|---|
| legacy-lit | 6 | 1 | 3F7940D2... | 3E56DB8C... |
| environment | 1 | 1 | E16BAAAA... | 9D47DDD9... |
| selection | 6 | 1 | 2A65C348... | 19F90F94... |
| base-color | 0 | 0 | C1321971... | C1321971...(同一) |
| normal | 0 | 0 | A0C9623A... | A0C9623A...(同一) |
| wireframe | 29 | 192 | 25DB6AB1... | E115C10F... |
| previews-and-overlay | 6 | 1 | A823A163... | 5A7F4B3A... |

- 違う画素は、主パスの床が目の面と近平面をまたぎ、クリップされる三角形があるため(ADR-0076 追記の帰結)。ワイヤーフレームの 29 画素は、共有辺の同じ深度の決着が、標準では描画順、対数では丸めになるため(Omni 仕様の不変条件 3)。
- 対数の 7 ハッシュを `StageGoldenFrameTests` に固定した。
- 深度オラクル `LogarithmicDepthOracleTests`(`DepthOracle`、倍精度との比較)は 4e-6 m・0.5 m・50 m・2e4 m・1e15 m・5e18 m で、画面中央 4 ulp 以内、全画素で深度 < 1.0 に合格(WARP)。
- `GoldenFrameDump`(`CGE_GOLDEN_DUMP`・`CGE_GOLDEN_BASELINE`)を足した。付け替えの規則(ADR-0084 §7)で使う。

### 段階 2: 予行(2026-10-25、WARP)
- 標準を明示していただけの試験(`LumenGpuIntegrationTests`、`GlobalDistanceFieldGpuIntegrationTests`、`MaterialMipmapGpuIntegrationTests` の `DepthMapping=Standard`)を `Radial` に替えて、`VolumetricCloudGpuIntegrationTests`(標準と対数の両方を既に持つ)と一緒に流した: 25 件中 23 件が合格。
- 失敗した 2 件(`Reflections_MirrorFloorShowsTheBoxWhereGeometryPutsItAndRoughnessBlursIt`、`Lumen_ConvergesWithinTwentyFramesAndIsDeterministic`)は、どちらも読み戻しの `Map` で `0x887A0005`(デバイス削除)。M0111 にも WARP のデバイス削除が記録されているが、原因は未確定。対数深度との因果関係は、この再実行だけでは断定しない(測定値は出ていた: 鏡の IoU 0.84・重心の誤差 1.19 px、収束は 20 フレームで 5 % 以内)。この 2 件を単独で再実行して両方合格した。
- 3 ファイルは対数のまま固定した。標準と比べる試験は段階 4 で扱う。

### 段階 3: Lumen 表面前パスの対数深度(2026-10-25)
- 再開時、共有ブランチのモデル取り込みが ADR-0080/M0114 を使用していたため、この描画・物理の計画を ADR-0084〜0087、M0119〜M0123 へ変更した。全スケール物理の予約 ID は F-L5-047。既存の描画用ブランチ名はそのまま。
- `LumenSurfaceDepthTests` で、同じ大きさの箱 2 個を距離 2e7 m・少し奥に置いた。far は 1e18 m。主画面は箱の手前の面(画面中心付近で約 1e7 m)を描くが、表面前パスは約 3.00e7 m の奥の面を返した。描画順を反転しても同じ誤遮蔽になった。画素中心の解析光線で期待距離を計算した試験は、修正前が近距離 1 件合格・遠距離 2 件失敗、修正後は 3 件合格。
- 表面前パスが、カメラの深度方式に対応するラスタ深度を使い、共有の `EncodeRadialDepth` による `SV_Depth` を書くようにした。対数側は既存の `RadialVSMain` とクリップ距離を使い、通常の深度クリップを無効にする。物理距離は二乗の桁あふれを避ける `RadialSafeLength` で計算する。法線・粗さと物理空間の照明は同じ。専用 DSV は継続使用。
- `stage-v14` の移行を開始した。現在は 18 エントリーと既存の CB 配置を保ち、Lumen 表面出力に `SV_Depth` が追加された段階。計画にある 14 エントリーと FrameCB の最終配置は、主シェーダーの一本化・標準経路の削除に合わせて切り替える。
- シェーダーの再コンパイルで、Depth サブグラフのビット式 `bits & mask | bias` に括弧警告が出ることを確認した。仮数のマスクと指数の結合を別の文に分け、式の意味を保って警告を解消した(`edits/depth_mask_2026_10_25.py`)。ソースのハッシュは 7C3F7E07... → 153236B9...。画素のハッシュは付け替えていない。
- 証拠: `.artifacts/M0119/surface-before-corrected.log`、`surface-after.log`、`surface-clean-build.log`、各 `*.memory.json`。Windows.Tests ビルドは警告 0・エラー 0。
- メモリ使用率は自分のビルド・テストの起動前と実行中に測定し、88% に達したら自分の子プロセス群だけを停止する監視を使用。ここまでの最大値は 71%。他のアプリや別作業の処理は停止していない。
- 先に再現できた Lumen の誤遮蔽を独立して修正した。次は段階 3(a) の Stage 本体・物理距離ターゲットと、3(b) の DDGI、3(d) の AO。

- 回帰確認: Windows.Tests の Lumen・新しい表面深度・標準/対数ゴールデン・深度オラクル・ホットリロードは 23 件中 22 件合格。`Lumen_FollowsAMovingBox` のみ GPU 読み戻しの Map でデバイス削除(0x887A0005)。使用率の最大値は 71% なので、90% を超える物理メモリ負荷は観測していない。読み戻しの失敗に `GetDeviceRemovedReason` とデバッグ層のエラーを加え、標準・対数の 2 条件で単独比較すると両方合格。発生条件と原因は未確定のため、全回帰が一括で成功したとは扱わない。元の失敗は `surface-regression.log`、比較は `moving-control.log` に保存。
- シェーダーの検査: `ShaderGraphCompilerTests` は 15 件合格(全 Stage エントリーの DXC コンパイル、構文グラフのソースハッシュ、外部依存、超越関数の禁止)。
- 明示的なステージのフィルターに `LumenSurfaceDepthTests` を追加。先行段階で追加した `StageGoldenFrameTests` と `LogarithmicDepthOracleTests` も `rendering.radial-depth` に登録した。

- FeatureRunner のカタログ整合性・描画ステージ接続は 2 件合格。今回の全コマンドで測定した物理メモリ使用率の最大値は 72%。

### 段階 3(a) の主エントリー統合(2026-10-25)
- `VSMain`/`PSMain` に対数深度を統合した。単一の `VertexOutput` と、色・`SV_DepthGreaterEqual` を返す `StagePixelOutput` に移行。`RadialPixelInput`・`RadialVertexOutput`・`ToVertexOutput` を削除。旧エントリー名は ABI の別名として直接委譲するだけとなった。標準 API と PSO の削除は段階 4。
- 正射影のカード撮影で主カメラの対数パケットを引き継がないよう、撮影用の線形パケットを明示。別ビューはラスタ深度の重み 0、主ビューは物体ごとの接線を重み 1 で使用。観察ビューの深度は写像後の位置から同じ PS が書く。
- Windows.Tests: 対数深度・境界条件、標準と対数のゴールデン、深度オラクル、Lumen 表面の遠距離遮蔽、カード撮影、スキニングの 43 件合格。対数 7 SHA は付け替え無しで合格。`ShaderGraphCompilerTests` 15 件、`StagePresentationShaderSourceTests` 1 件合格。ビルドは警告 0・エラー 0。
- Stage グラフのソースハッシュは意図した構造変更により 153236B9... → A72EB7E8...。画素ハッシュは不変。
- 証拠: `.artifacts/M0119/native-build.log`、`native-regression.log`、`native-shader-tests.log`、`native-presentation-tests.log`、各 `*.memory.json`。この段階の最大メモリ使用率は 71%。
- 次: 物理距離ターゲット `SceneDistance` と空の合成、DDGI の撮影距離、AO、旧 API と PSO の除去。全マイルストーンは未完了。

### 段階 3(a/b): 物理距離ターゲットと DDGI(2026-10-25)
- Stage の同じ最近面から色・深度とともに R32F の物理距離を出力する。`SceneDistance` は 0 で消去し、背景と区別する。空の合成はこの距離と画素の光線から物理位置を復元し、双曲深度・対数深度の逆変換を使わない。正射影では光線の横方向の位置を含める。
- DDGI の 6 面撮影も対数深度を使い、`CaptureDistance` から距離の一次・二次モーメントを作る。正射影の Lumen カードと光源のシャドウは従来の各投影のまま。古い物理距離出力のない成果物を拒否するため stage-v14 の契約に MRT1 を明記。
- プレビュー PSO は距離への書き込みを禁止。描画済みの主画面の距離はプレビュー前後でビット単位に同じ。リサイズ時に距離ターゲットも再作成する。
- 解析オラクルは距離 4e-6〜5e18 m の全画素で物理距離の相対誤差 3e-6 以下。GPU テスト 10 件(オラクル・距離・ゴールデン)と 40 件(空・雲・GI・対数深度・Lumen 表面)に合格。固定の対数 7 SHA は不変。ShaderGraphCompilerTests 15 件合格。ビルドは警告 0・エラー 0。
- 証拠: `.artifacts/M0119/distance-rebuild.log`、`distance-oracles.log`、`distance-regression.log`、`distance-shader-tests.log`、`distance-golden/`、各メモリ測定。最大使用率は 74%。既知の間欠的な WARP デバイス削除の原因を解消したとは扱わない。
- 次: AO の双曲深度依存と旧 API/PSO の除去。M0119 は未完了。

### 段階 3(d): AO の物理距離入力(2026-10-25)
- 修正前、通常寸法の接触部は暗くなるが、シーンを 1e4 倍に拡大し far を 1e18 m にすると AO が 1(陰り無し)になった。旧 AO は補助投影の far でジオメトリを切っていた。新試験の「拡大しても強度が等しい」という当初の仮定は、既存の SAO 式に半径への依存があるため撤回した。試験は通常の接触部の陰りと、遠距離で陰りが完全に失われないことを検証し、強度の式は変更していない。
- 専用の PrepassDepth、DSV/ヒープ、ShadowVSMain による AO 深度前パスと定数スロットを削除。既存の対数表面シェーダーで SceneDistance と主 DSV を使う。本描画の前に AO と同じ半解像度の有効領域へ描き、AO 後に全領域を本描画で消去・再描画する。HDR ターゲットは表面法線の一時出力に再利用する。Lumen の表面ターゲットとの統合は範囲外のまま。
- 全解像度の画素中心をそのまま選ぶ最初の実験は、環境画像の 2959/20736 画素・最大 25 階調の差が出て基準外となったため不採用。半解像度の位置を保つと、標準は 7 画素、対数は 6 画素、どちらも最大 1 階調になった。原因は双曲深度の逆投影から物理距離×光線への位置復元の丸めの差。他の 6 ケースは同一。ADR-0084 §7 に従い環境画像のみ更新。

| 環境画像 | 旧 SHA-256 | 新 SHA-256 | 変化 / 20736 画素 | 最大階調差 |
|---|---|---|---|---|
| 標準(削除前の比較用) | E16BAAAA34D474B4A683B2187C1988F2D57947F53BCC259482BF0775E7E62415 | 0352FE7EF84564BB1DC13646EF8E41E3B3600D2D7D83ABEC7E666C6A4905FFDA | 7 | 1 |
| 対数 | 9D47DDD9EE03EDA7D5E741BCD87F5ECFD06B13818DE2DE6F49C66135F57D15F7 | C2BB4D74FB5474A285B2C801E39D6811DAD1239BC4D13F2B1474D4B23FB62D3F | 6 | 1 |

- 証拠: `.artifacts/M0119/ao-before.log`(修正前)、`ao-after.log`(不採用の全解像度実験)、`ao-sampling.log`、`ao-sampling-golden/repin.json` と PNG/RGBA。削除・移動の次段階では、この新しい SHA を変えてはならない。

- 最終回帰: AO 5 条件、標準・対数ゴールデン、SceneDistance/奇数寸法リサイズ、遠距離深度、Lumen 表面、スキニング等の GPU 26 件合格(`ao-final.log`)。ビルドは警告 0・エラー 0。最大メモリ使用率 67%。

### 段階 4(a): 14 エントリーと PSO の一本化・L4 深度契約(2026-10-26)
- ラッパー 4 本を削除し、ObserverVSMain と SurfacePrepassPSMain に改名した。主・表示・ワイヤー・プレビュー・表面前パスの深度方式別 PSO を統合。ホットリロードの全作成・復旧・解放も統合。
- 未使用の ShadowPosition/ComputeDirectionalShadow と旧影行列の提出を削除。フレームの 304/368 バイト位置を InverseViewProjection/ViewportSize へ再利用し、DepthEncoding0/1 と ConservativeRasterDepth に改名。80 バイトの再利用なので、設計の「976 バイトへ拡張」は不要と確認し、960 バイトを維持した。
- L4 RenderViewDepth が透視・正射影・観察ビューの検証済みパケットを作る。主ビュー・DDGI・観察ビューに接続済み。旧 Standard の選択 API は次の段階で除く。
- 全リポジトリの API 一括置換は自動承認レビューで参照破壊・ABI 不整合のリスクにより却下され、実行していない。ADR-0084 の実装順序を検証可能な境界ごとに更新した。
- GPU 回帰 43 件、接続後の GPU 17 件、Rendering の数値・境界 32 件、シェーダー 15 件合格。7 SHA は AO 更新後と同一。ビルドは警告 0・エラー 0。最大メモリ使用率 69%。生成 HLSL のソースハッシュは 0A8C045A... (グラフ保守ツールの LF 正規化表示は 990CB9EB...)。
- 証拠: `.artifacts/M0119/native-abi-regression.log`、`native-contract-gpu.log`、`native-abi-shader-final.log`、`view-depth-final-tests.log`、`native-abi-final-build.log`、各 memory.json。初回の Rendering.Tests は未復元で --no-restore が無出力で終了したため実行結果として数えず、復元後に実際の件数 32 を確認した。

- ステージカタログ整合性・共通描画接続の 2 件、表示用接尾辞の超越関数禁止 1 件も合格。

### 直接生成カメラの既定値(2026-10-26)
- new Camera の直接生成が Standard/T=0 となる抜けを、型と GPU の 2 つの試験で再現した。GPU は 50 m・far 1e18 m の対数オラクルから約 1.21e8 ulp 外れた。コンストラクターで対数方式と T=1 m を初期化した後は中央 4 ulp 以内。
- カメラ・深度・ゴールデン・プレビュー 33 件合格。ビルドは警告 0・エラー 0。最大メモリ使用率 66%。証拠は `.artifacts/M0119/camera-default-before.log`、`camera-default-after.log`、`camera-default-build.log` と memory.json。

### 段階 4(b1): シーンカメラの選択型削除(2026-10-26)
- シーンの CameraDepthMapping と Camera.DepthMapping を削除。LevelLoad は必ず対数を提出する。SceneCameraPreviewBinding の旧メンバー解釈を削除し、投影方式から透視=対数・正射影=線形を導く。T と近遠範囲の合成は保持する。
- シーン・エディター・観察ビュー・ゴールデン 36 件、VSIX カメラポリシー/姿勢 13 件合格。生成スキーマから旧選択が消えたことを記録(`scene-native-schema.json`)。この区間の最大メモリ使用率 63%。VSIX は配置を無効にしてビルドのみ行った。
- VSIX の最初の試行は、SDK が通常の ProjectReference と別に梱包する Cooking/Cook CLI/Navigation Cook の未復元で失敗した。一覧から復元してから再実行し、13 件の成功を確認。証拠は `.artifacts/M0119/scene-native-tests.log`、`scene-native-vsix-final.log`、`scene-native-build.log` と memory.json。
- 低レベル描画カメラ、通信プロトコル 9、旧 Model/Triangle と Standard 比較試験の削除は後続。

### 段階 4(b2): 描画カメラの選択削除(2026-10-26)
- 最後の標準比較を 7 ケースすべて合格で保存してから、Graphics.Camera.DepthMapping と StandardModeGoldenFrameTests を削除。SetCamera は必ず検証済み対数投影を提出する。証拠は pre-camera-removal-last-comparison.log と pre-camera-removal-golden/parity.json。
- 比較専用試験は、継続するネイティブの検証へ整理した。遠距離面は両描画順で最近面を確認。プレゼンテーションのフレーム保持、微小シーン、曲線変更での物理画像の保持、リサイズ後と新規レンダラーの可視画素・深度一致を検証。
- カメラ・画像・深度・再生・エディター等 106 件、GI/雲/距離場/ミップ 27 件、カタログ 2 件合格。ビルドは警告 0・エラー 0。7 SHA は削除直前と同じ。最大メモリ使用率は 66%。
- 旧行列/プレビュー通信の選択、Model/Triangle の削除は後続。

### 実機の精度・主パス性能(2026-10-26)
- RTX 4060 Ti で 4e-6〜5e18 m の 6 スケールを確認。中央の最大誤差は順に 2.56,2.51,1.32,1.21,1.66,1.21 ulp(すべて 4 ulp 以内)。端の誤差はラスタ位置の影響を含み、最大 41.51 ulp を別に記録した。
- 1280×720・24 層の主パス、ウォームアップ 5・30 標本。近距離の中央値は手前順 0.198 ms/奥順 3.785 ms、遠距離は 0.133/2.516 ms。p95 は順に 0.198,4.003,0.135,2.721 ms。全フレームの予算とは区別する。
- 精度/性能の 2 件に合格。証拠は native-camera-hardware-evidence.log、precision/radial-precision-hardware.json、performance/native-depth-performance-hardware.json、native-camera-hardware-manifest.json。旧 M0099/M0100 の記録は上書きしていない。

### 段階 4(b3): プレビュー通信をプロトコル9へ移行(2026-10-26)
- DTOの深度選択型・欄を削除。透視はFOV・near/far・正のTを必須とし、ホストは必ず対数パラメーターを提出する。正射影は線形。旧版8はペイロード前に拒否。
- far=1e18の再現で、修正前は期待0.02203に対し0.93333を書いた。透視1件失敗・正射影1件合格を保存後、修正後は主画面・サムネイルとも独立した参照値の4ulp以内。シーンカメラの元のクリップ範囲を渡し、巨大farを丸めた行列から再計算しない。
- 契約12件、Windowsプレビュー/ホスト/実行ビュー28件、VSIXの姿勢・ポリシー・生成・実際のパッケージ済みホスト/実行ビュー20件合格。ビルドは警告0・エラー0、最大メモリ63%。VSIXの配置は無効、VS/Unity起動なし。
- 証拠は `.artifacts/M0119/preview-native-before.log`、`preview-native-after.log`、`preview-native-contract-test.log`、`preview-native-vsix-test.log`、対応するbuildログとmemory.json。パッケージTestAssetsは現在のビルドで再生成したVSIXを読み、プロトコル9で実際に描画した。
- 低レベル行列/サムネイル選択とModel/Triangleは後続。F-L5-038の全体適合は引き続きPartial。

### 段階 4(b4): 行列・サムネイルの旧選択を削除(2026-10-26)
- RenderDepthMappingを削除し、主ビューは検証済みRenderViewDepthを保持して提出する。裸の行列は正射影の線形深度だけに限定。透視は物理的なFOV・near/far・Tを受け取り、有限の行列と逆行列を持つ補助投影を検証してから状態を更新する。
- RenderCameraPreviewのDepthを必須化。投影との不整合・default値・主カメラ用の保守的ラスタ重みを拒否する。観察ビューの中心も型付きパケットで運ぶ。ShaderGraphCompilerTestsは廃止名の部分否定から、許可された14エントリーの集合完全一致へ更新。
- GPU回帰68件、シェーダー契約15件、L4数値/境界32件、VSIX/パッケージ済みホスト20件合格。7SHAは不変。透視・正射影の主画面とサムネイルはデバイス再生成後も画素と深度が一致する。ビルド警告0・エラー0。最大メモリ59%。
- 証拠は `.artifacts/M0119/typed-depth-final-regression.log`、`typed-depth-shader-tests.log`、`typed-depth-final-build.log`、`typed-depth-vsix-build.log`、`typed-depth-vsix-test.log`、`typed-depth-cpu-tests.log` とmemory.json。
- 完了条件の旧選択・ラッパー検索はsrc/tools/tests/assetsで0件。古い生成保守スクリプトの説明だけに残ったエントリー名を役割の説明へ直し、履歴の変換処理は変更していない。
- 全体は引き続きPartial。旧Model/Triangleの実装・ABI・テンプレート・生産者の削除、全体検証とテンプレート4実行が残る。

### 段階 4(c): 旧単体レンダラー・ABI・生産者の削除(2026-10-26)
- 生成元がない旧ModelRenderer/TriangleRendererと二つのルートグラフを削除。三角形・glTFのGPU試験は既にStage経由であり、削除せず維持した。Windows.Tests全体は818件合格・失敗0、通常は有効にしない実機測定2件スキップ、5分3秒。最大メモリ63%。7画像ハッシュも不変。
- 旧ABIの生産者、ターゲット列挙値、VSIXの旧テンプレートと選択UIを削除。成果物の値2/3、グラフの値1/2は再利用せず拒否する。チェックサムが正しい旧ターゲットでも拒否する試験を含む成果物10件、グラフ29件、現行Stage14エントリー/HLSLのCLI9件合格。過去の15件から減った6件は廃止した二つのルートターゲットに対する三種類の重複実行。
- FeatureStageCatalogとソリューションの旧ファイル項目を更新。リリース証拠はStage+5外部サブグラフ+表示接尾辞を記録し、Depth変更で証拠が無効になることを含むカタログ/リリース21件合格。梱包内容変更の版規則に従いSDKを0.1.24へ更新。
- 初回VSIX試験はサブグラフItem Templateに旧ターゲット値が残っていたため1件失敗・44件合格。検証を緩めず、ルート/サブグラフの両Item TemplateをStageへ更新した。全追跡グラフのターゲットはStageであることを読み取り確認。修正後はVSIXの編集/梱包/ホスト/自動化契約45件すべて合格。
- GUI自動化の移行でCLI専用型の誤参照が1件のビルドエラーになった。現在のStageの5依存が直接入力かつ再帰依存なしであることを確認し、既存のReadGraphで宣言された依存をコピーする形へ修正。最終ビルド警告0・エラー0。VS/Unityを起動していないためStageでのGUI実走は未検証と明記。
- 証拠は `.artifacts/M0119/retired-renderer-*.log` と各memory.json。全体の状態はPartial。全ソリューション、4テンプレート実走、性能予算の最終閉鎖は後続。

### 最終ゲートと診断設定の補強(2026-10-26)
- 全ソリューションは22テストプロジェクト、3520件合格・失敗0・実機測定2件スキップ。全ソリューションの22登録と集計が一致。最大メモリ65%、閾値停止なし。証拠はsolution-final.log/summary.json/memory.json。
- 初回の4テンプレート実走は8ケースとも正常終了し、M0111の9つの基準値に一致した。ただし診断設定の追加監査で、既定のCGE_D3D12_DEBUG_LAYERが0であることを確認。この初回のエラー0はデバッグ層の検証として数えない。
- ThirdPerson/クリック/車両の検証コマンドがデバッグ層を明示的に必須化するよう修正。ゲームの通常起動設定は変えない。FeatureRunner全体233件合格(無効/未報告を拒否する10ケースを含む)。ネイティブ層がGraphics Tools不足時に要求を無視して続行する場合も区別するため、ランタイム報告に実際のd3d12DebugLayerEnabledを追加し、全評価側がtrueを必須とする。報告フィールドは既存schemaへの追加。梱包ソース変更に合わせSDKを0.1.25へ更新。デバッグ層ありの4テンプレート再実行を別のdebug-manifestとログへ記録する。
- Release性能は100000物体の可視性/バッチングが中央値4.10015ms、p95 4.34760ms、割り当てp95 0byteで予算内。1280x720 WARPは中央値16.7173ms、p95 17.2304ms、割り当てp95 928byte、画像オラクル合格・D3D12エラー0。WARPのフレーム時間は正しさの記録であり、実機の時間予算とは区別する。5ウォームアップ/30標本、Tiered JIT無効を生データで確認。証拠はrelease-performance.log/memory.json、.artifacts/RenderingPerformanceのrawとPerformanceの2報告。
- RTXの既存測定に使ったStage成果物と現在のSHA256は完全一致(E485F055...B6A5)。元の測定コミットの記録を保持し、照合のみfinal-hardware-artifact-match.jsonへ記録した。
- FirstPersonの最終移動画像が緑の面で埋まるため、移動前8フレームを追加確認。毎フレーム58物体、床・壁・空・配置物を画像で確認、明示的に有効化したデバッグ層のエラー0。FirstPerson-spawnの画像・report/summary.jsonを別保存。

- 診断の修正前再現: 本番の出力を変更せず、全体試験時の旧FeatureRunner DLLを隔離フォルダで読み、同じ10個の無効/未報告テストがすべて失敗することを確認。修正後は233件合格。旧DLLと新テストのSHAをdiagnostic-before-proof/provenance.jsonへ保存。証拠はtemplate-diagnostics-before-proof.log、template-diagnostics-required-tests.log。

### 最終閉鎖(2026-10-26)
- 最終コードa8c6df88ca86529f0734c8010a369ea25627aa3eで、全ソリューション22プロジェクトが3530件合格・失敗0・実機測定2件スキップ。コマンドは `dotnet test CSharpGameEngine.slnx -c Debug -p:Platform=x64 -p:DeployExtension=false -m:1 -nr:false -p:UseSharedCompilation=false`。最大物理RAM使用率65%、閾値停止なし。ログのSHAとプロジェクト別件数は `.artifacts/M0119/solution-diagnostics-final-summary.json`、生ログと監視値は同名のlog/memory.json。
- デバッグ層を実際に有効化した4テンプレートの8ケースはすべて正常終了。各reportの `d3d12DebugLayerEnabled=true` とD3D12エラー0を確認。M0111の9基準値に一致し、初回実行との全フレームのカメラ軌跡・最終PNGも8ケースすべて完全一致した。証拠は `.artifacts/M0119/templates/debug-manifest.json` と `baseline-comparison-debug.json`、実行IDは20261025T174831Z。再実行の最大物理RAM使用率68%、閾値停止なし。
- 旧選択型・ラッパーエントリー・StandardModeGoldenFrameTests・ModelRenderer/TriangleRenderer・model-v1/triangle-v1の検索はsrc/tools/tests/assetsで0件。14エントリー、960バイトのFrameCB、AO更新後の7画像ハッシュ、物理距離の解析オラクルを全体テストで維持した。
- RTX 4060 Tiでの別実行は6距離すべて中央4ulp以内(最大約2.56ulp)。主パスだけのGPU時間を記録したもので、ゲーム全体のフレーム時間の証明ではない。測定時のStage成果物との完全一致を確認し、元の測定履歴は書き換えていない。Releaseの100000物体のCPU性能予算も前項のとおり合格。
- 適合結果: M0119の範囲はMatches。VS/Unityを起動するGUI実走は今回の検証に含めない。既存のシーンカメラ正射影のランタイム未接続、および過去の間欠的なWARPデバイス削除の原因は範囲外/未確定として保持する。後者が解決したとは主張しない。mainへの統合は行っていない。

### 既定Stageの初回描画への追加検証(2026-10-26、SDK0.1.29)
- M0120の起動状態調査で、SetCameraを一度も呼ばず描く低レベルStageは、初期化した透視行列に対し線形の深度パケットを残していることを再現。約4.145584mの床で期待深度0.22822031に対し0.9759626を書き、追加GPU試験は修正前1件失敗。従来の明示的SetCamera/直接生成Cameraの既定値試験では、この起動経路を通っていなかった。
- InitializeConstantsToDefaultsで既定カメラにも通常のSetCamera処理を適用し、パケット/補助投影/基底を揃えてから全フレームスロットを初期化する。重複した別の曲線は追加していない。追加試験はSetCamera未提出の3フレームで実デバッグ層有効を確認し、物理SceneDistanceに対する対数深度を両スロットで検証する。
- 修正後の起動/距離/固定7画像/スロット/三角形/glTF/主深度/ホットリロード/復旧の38件成功。画素基準とシェーダーは不変。証拠は `.artifacts/M0120/default-view-before.log` と `default-view-after.log`、各memory.json。修正前後の最大RAM57%/57%、閾値停止なし。全体回帰を後続で確認する。

- 起動補足の全体回帰: 5f4d647/SDK0.1.29で22プロジェクト3548成功・失敗0・実機測定2スキップ、最大RAM66%、終了0。M0120のnative-startup-solution.log/summary.json/memory.jsonに保存。M0119の既定起動契約もこの結果で再確認した。
