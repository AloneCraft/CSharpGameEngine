# M0119 — 対数ネイティブの描画(標準深度の削除)
- 日付: 2026-10-25
- 状態: 実装中(段階 1・2 と、段階 3 の Depth サブグラフ分離・Lumen 表面前パスまで。標準経路の削除は未実施)
- 決定: [ADR-0084](../Decisions/ADR-0084-Logarithmic-Native-Depth.md)(ADR-0076 を一部 Superseded)
- ABI: `stage-v14`、プレビューのプロトコル 9

## 目的
透視ビューの深度経路を 1 系統(最初から対数深度を書く Stage シェーダー)にする。標準(双曲)深度の選択(型・欄・プロトコル・PSO・ゴールデン)を削除し、双曲深度のまま残っていた透視パス(Lumen の表面前パス、DDGI のプローブ撮影、AO の深度前パス)を対数にする。画素は削除前の対数と同じ。

## 段階
1. **最後の比較と対数のゴールデン**: `StageGoldenFrameTests`(対数の 7 ケース)を作り、同じ 7 ケースを標準と対数で描いて基準内(各チャンネル 1 階調以内、違う画素 1 % 以下。ワイヤーフレームは違う画素 1 % 以下)であることを確かめてから SHA を固定する。外れたら何も消さずに原因を調べる。`LogarithmicDepthOracleTests`(板を 4e-6 〜 5e18 m に置き、中央 4 ulp 以内、全画素で深度 < 1.0、正射影は 2 ulp 以内)、`GoldenFrameDump`(`CGE_GOLDEN_DUMP`/`CGE_GOLDEN_BASELINE`)を足す。
2. **予行**: 標準を明示しているだけの試験(Lumen、ミップ、距離場、雲)を対数で流し、落ちたものと原因を記録する。
3. **Stage を最初から対数にする**(1 項目 1 コミット、それぞれ付け替えの規則で確かめる): (a) `VertexOutput` の一本化・`VSMain` の保守的ラスタ深度・`PSMain` の `SV_DepthGreaterEqual` と `SceneDistance`・`Depth.cgesubgraph`・空の合成が `SceneDistance` を読む、(b) DDGI のプローブ撮影を対数にし `CaptureDistance` を足す、(c) Lumen の表面前パスを対数にする(先に far 1e18 m で落ちることを記録)、(d) AO が `SceneDistance` を読み、AO の深度前パスを消す。
4. **標準の削除**(1 変更。付け替えは認めない。7 SHA は段階 3 の後と同じ): ラッパーのエントリーと標準の PSO 系統、`RenderDepthMapping.Standard`・`CameraDepthMapping`・`RenderPreviewDepthMapping`・`Camera.DepthMapping`、`StandardModeGoldenFrameTests`、`ModelRenderer`/`TriangleRenderer` と `model-v1`/`triangle-v1` を消す。`RenderDepthEncoding`・`RenderViewDepth`(L4)を足す。プロトコル 9。ADR-0076 を一部 Superseded にする。
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
