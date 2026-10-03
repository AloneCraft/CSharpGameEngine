# ADR-0076 — 対数（放射）深度をアダプターではなくカメラの性質として描画経路に組み込む
- 状態: Accepted（2026-10-03、ユーザーの依頼:「Omni を参考にネイティブに実現する設計をして。回帰しないようにして」。計画はユーザーが承認）
- 関連: ADR-0063（正準数値と階層 World）、ADR-0064（型付きライト）、ADR-0069（物理的な環境照明）、ADR-0075（ワールド空間のグリッド）、M0099、[Omni 仕様](../Specifications/Omni.md)

## 背景
- Omni（`D:\Unity\6.6\Omni\Assets\Omni`、Unity 6000.6 / URP 17.6）は、高精度の Logical 座標から写像中心（通常はカメラ）への相対ベクトルを、方向を保ったまま距離だけ圧縮して描く。曲線は Smooth `F(q) = S·log2(1 + q/S)`（`q = d·M`）と Threshold の 2 つ。方向が保たれるので画面上の位置は標準描画と同じで、変わるのは深度だけ。
- Omni の実装の大半は URP への「アダプター」である: Shader／Shader Graph／VFX の変換と注入、Material の差し替えと復元台帳、MaterialPropertyBlock の配布、BRG の sidecar、Unity のカリングと float の world 座標の回避策。テスト約 2,800 件の多くがこの変換と台帳を検証している。
- Omni が頂点を写像するのは、Unity の投影・カリング・float の world 座標の制約のためで、写像した三角形の補間を物理へ戻すために `(pos·w, w)` の同次補間を、深度のためにフラグメントでの物理点の復元と `SV_Depth` を使う。
- このエンジンは Stage という単一の固定 ABI シェーダーを自前で持つ（独自の材質シェーダーは無い）。物体は Q64.64 からカメラ相対の float へ一度だけ変換され（`FixedTransform.TryToCameraRelativeMatrix`）、カメラは描画空間の原点にある。標準 VS は物理のカメラ相対座標を透視補間するので、補間は最初から物理的に正しい。
- 標準の深度は D32 float・1.0 クリア・LESS の非 reversed-Z で、far が近く以外は精度が足りない。far/near が約 2^24 を超えると float の投影行列の深度項が丸まり、NDC z = 1 の逆投影が w = 0 になる（`RadialDepthTests.StandardProjection_WithAHugeFar_CannotBeUnprojected`）。

## 決定

### 1. 通常の対数カメラは頂点を写像せず、画素ごとに対数距離を深度へ書く
- 標準の `VSMain` をそのまま使う。新しい `RadialPSMain` は色を既存の `PSMain` に任せ（勝った面が同じ画素は標準とビット一致）、`SV_Depth = min(G(r) / G(rMax), 0.99999994)` を書く。`r` はカメラ相対の物理位置の長さ（最大成分で割ってから長さを取り、二乗の桁あふれを避ける）。
- 1 画素のレイの上では物理距離の順序が深度の順序と一致するので、遮蔽は全スケールで正確。0.99999994 は 1 未満の最大の float で、1.0 のクリアと空の合成（depth < 1 の判定）がそのまま動く。
- 論理の far（カメラの FarZ）は `SV_Position.w`（物理の視線方向の深度）で画素ごとに捨てる。near は標準の投影がそのまま正しくクリップする。
- 採らなかった頂点の写像: このエンジンでは補間もクリップも標準 VS で正しく、写像は何も足さない。写像した三角形の弦から深度を取ると、大きな床と近くの小物体の前後が崩れる。

### 2. 曲線は Omni の Smooth だけ（メートルで表す）
- `G(r) = log2(1 + r / T)`。`T` は `RadialTransitionMeters`（既定 1 m）。正規化した深度では Omni の view scale `M` と倍率 `S` が打ち消し合うので持たない。
- Threshold は採らない（ユーザー指示 2026-10-03）。曲線の種類を選ぶ欄は作らない。
- `log2(1 + x)` は float の四則演算だけで求め、CPU（`RadialDepthCurve.Log2OnePlus`）とシェーダーで同じ式を使う（追記「GPU の log2 を使わない」。当初は丸めた和の対数を Kahan の補正で直していたが、GPU の `log2` が近似だったため置き換えた）。

### 3. 精度予算
- D32 float の 1 付近の刻みは 2^-24。2 つの面を見分けられる相対距離はおよそ `2^-24 · ln2 · log2(rMax / T) · (1 + T / r)`。near 0.1 m・far 1e18 m で約 3e-6（`RadialDepthTests.Encoding_SeparatesSurfacesAtTheBudgetedRelativeDistanceOnEveryScale`）。`r ≪ T` では float の相対精度がそのまま効く。
- 標準 Z の float 深度（far 付近で数 %）より桁違いに良いので、reversed-Z への全体の変更はしない。

### 4. 逆行列を取るパスには補助のビュー投影を渡す
- 空の方向、太陽のカスケード（`DirectionalShadowCascades.Build`）、AO の深度前パスは、カメラのビュー投影の逆行列を使う。対数モードに限り、同じ回転と FOV で near = max(NearZ, min(0.01 m, FarZ/2))、far = min(FarZ, near · 65536) の投影を渡す（`RadialDepthProjection.CreateAuxiliaryProjection`）。
- ゲームの通常の範囲（near ≥ 0.01 m、far ≤ near · 65536）では補助の投影はカメラの投影と完全に同じなので、空・影・AO は標準と同じ。1 cm より手前の面には AO が掛からない。
- 標準モードは今の `CurrentViewProjection` を渡し続ける（ビット一致のため）。

### 5. 外から見る観察ビューだけが頂点を写像する
- Omni の RadialObserverRig 相当。中央カメラの位置 `C` を写像中心にし、観察者の目 `E` から `dir · T·G(|P − C|) − (E − C)` を見る（Omni の比較表示と同じ式。`F(P−C) − F(E−C)` は大きさが膨らむので採らない）。
- 写像後の平らな三角形を通常のラスタ深度で描く。写像後の世界は半径 `T · G(rMax)` の有限の大きさなので標準の near/far で足りる。照明は物理の値だが、補間は写像空間での透視補間になる（可視化用の近似。Omni の観察者も同じ）。
- 既存のカメラプレビュー矩形（最大 3 枚）の 1 パスとして、ゲームのランタイムでだけ描く。

### 6. 標準の経路を変えない構造
- 既定は標準（追記「対数を既定にする」で対数に変えた）。`VSMain`/`PSMain` と標準の PSO は変更しない。対数用は別のエントリー（`RadialPSMain`、`PreviewRadialPSMain`、`PreviewCleanRadialPSMain`、`RadialObserverVSMain`）と別の PSO にする。
- 深度方式のパケットは FrameCB の末尾に 32 バイト足す（標準のエントリーは読まない）。
- 標準モードのフレームは M0099 Phase 1 で SHA-256 に固定した（`StandardModeGoldenFrameTests`）。

## Omni との対応
| Omni | このエンジン |
|---|---|
| Logical 座標（Q95.32）、仮数／指数のパケット | Q64.64 からカメラ相対 float へ一度だけ変換（ADR-0063）。範囲内は float で表せるので仮数／指数は不要 |
| 頂点の写像と `(pos·w, w)` の同次補間 | 不要（標準 VS が物理を正しく補間する） |
| フラグメントで物理点を写像して `SV_Depth`、全パスで同じ関数 | 物理距離の `G` を `SV_Depth` に。対数で深度を書くパスは同じ関数 |
| 照明・影・フォグは物理空間、法線にヤコビアンを掛けない | 同じ（`PSMain` をそのまま使う） |
| Smooth / Threshold | Smooth だけ |
| 深度を読む画面空間効果（SSAO 等）を逆写像に書き換える | AO は独自の物理の深度前パスを使うので不要。主深度を読むのは空の合成の depth < 1 だけ |
| Shader・Material の変換、復元台帳、MPB、BRG sidecar | 不要（固定 ABI の Stage を自前で持つ） |
| RadialObserverRig | 観察ビュー（カメラプレビュー矩形） |

## 帰結
- 対数の主パスは保守的深度（`SV_DepthGreaterEqual`）で早期の深度棄却を保つ（追記）。サムネイルの対数パスは `SV_Depth` のままで Early-Z が効かない。
- Stage の ABI にエントリーが 4 つ増え、FrameCB が 912 → 944 バイトになる。複製した独自の Stage グラフは読めなくなる（CHANGELOG に書く）。
- 非目標: カリング（Stage は今も全物体を描く。将来の可視性経路では near/far を放射距離で判定し、判定できなければ表示する）、モーションベクトル（将来 TAA を入れるときは前後のフレームの深度方式パケットが要る）、正射影の対数グリッド、エディタの切替 UI、天文規模でのエディタのナビゲーション、観察者の全画面表示とその中のピッキング・ギズモ、天体間の影。

## 検証
- CPU: `RadialDepthTests`（倍精度の基準との一致、単調性と原点の傾き、錐台の角で 1 未満、不正な入力、精度予算、観察者の半径、補助の投影が逆行列を持つこと、通常の範囲では標準と同じ行列）。
- 標準の回帰: `StandardModeGoldenFrameTests` と既存の厳密画素テスト。
- 以後のフェーズで GPU（標準とのビット一致、全スケールのほぼ同一平面、深度の読み戻し、復旧）、ランタイム、エディタ、観察ビュー、FeatureRunner の `rendering.radial-depth` を足す（M0099）。

## 追記（2026-10-04）: 保守的深度で Early-Z を取り戻す
- 観測した事実: GPU のタイムスタンプで主パスを測ると（全画面の板 24 枚、1280×720、RTX 4060 Ti）、`SV_Depth` を書く対数の主パスは、手前から奥へ描く場面で標準の約 22 倍（0.132 ms 対 2.972 ms）だった。奥から手前では同じ（2.98 ms）で、コストは Early-Z を失った分だけだった（`RadialDepthPerformanceEvidence`）。
- 決定: 主パスのラスタ深度を、符号化した深度の下界になる双曲線 `a − b/w`（w はビューの深さ）にし、画素シェーダーは `SV_DepthGreaterEqual` を宣言する。ハードウェアはラスタ深度で早期に棄却でき、棄却した画素は書く深度でも必ず失敗する。
  - 双曲線は `G(w)/G(rMax)` に `w0 = 8·max(T, NearZ)` で接する（追記「物体ごとの接点」で、物体ごとの接点に置き換えた）。差 `G(w)/G(rMax) − (a − b/w)` は `w²/(T + w)` が増加するので w0 だけで最小値 0 を取り、全域で下界になる。画素の距離 r は w 以上なので、`G(r)` に対しても下界。丸めのために a を 1/4096 下げる（`RadialDepthProjection.ConservativeDepthLimit/Slope`、`RadialDepthTests.ConservativeRasterDepth_NeverExceedsTheEncodedDepth`）。
  - この深度は near 面より遠くで 0 に達するので、主パスは深度クリップを無効にし、代わりに標準の投影の near/far の距離を `SV_ClipDistance` に出す（`RadialVSMain`、FrameCB `RadialRasterViewProjection`）。`SV_DepthGreaterEqual` を出す画素シェーダーは `SV_Position` を centroid・no-perspective で受ける必要があるので、専用の入力構造体 `RadialPixelInput` から `VertexOutput` へ写して `PSMain` を呼ぶ。
- 結果: 同じ計測で対数の主パスは 0.171 ms（標準 0.124 ms の 1.4 倍、以前の約 17 倍速）。WARP は深度を書くシェーダーで Early-Z を使わないので変わらない（正しさの確認用）。
- 帰結: クリップされる三角形では、クリップ距離による切断の丸めが視錐台の z 面と変わり、色が 8 ビットで最大 1 階調ずれることがある。クリップされない三角形は標準とビット一致のまま（`Radial_RendersTheStandardPixelsBitForBitOnTrianglesItDoesNotClip`、`..._WithinRoundingOnClippedTriangles`）。Stage ABI は `stage-v9`（14 エントリー）、FrameCB は 1008 バイト。

## 追記（2026-10-04）: 対数を既定にする
- 指示（ユーザー 2026-10-04）: 最終的にはすべての描画を対数にする。順々にテストし、修正しながら進める。
- 決定: 対数を既定にし、標準は明示的な選択として残す（標準の基準フレーム `StandardModeGoldenFrameTests` と、対数のテストで比べる相手）。段階: (1) 描画カメラのファクトリー `Camera.CreateDefault`/`CreateFirstPerson`/`CreateBoundsFit`、(2) シーンの Camera コンポーネントの既定、(3) エディタの分離プレビューとカメラのサムネイル。
- 段階 1 の観測: ファクトリーの既定を対数にして Windows.Tests を全件流すと、失敗は 4 件だけで、どれも標準を前提か比べる相手として使うテストだった（標準の基準フレーム、「標準ならほぼ同一平面が潰れる」側の 2 件、「標準のカメラには観察者を作らない」）。これらは標準を明示した。照明・影・AO・GI・空・スキニング・ギズモ・デバイス復旧を含む他のテストは、対数のまま変更なしで合格した。
- 段階 2（シーンの Camera コンポーネント）: 既定を Radial にした。Scene JSON は既定と違う値だけを書くので、キーの無い既存のシーン（ThirdPerson、UserEcsProject）は対数になり、標準にするには `"DepthMapping": "Standard"` を書く。エディタのランタイムビューの範囲は、対数ではエディタの範囲とシーンカメラの範囲の両方を含む（近い方の near、遠い方の far）。M0099 では対数のシーンカメラの範囲に置き換えていたが、既定が対数になるとエディタで引いたときにシーンカメラの far（既定 300 m）より先が切れるので、両方を含めることにした（対数ではどんな範囲も精度を失わない）。
  - 観測: Windows.Tests は変更なし（新しい範囲のテストを除く）で全件合格した。
- 段階 3（エディタの分離プレビューとカメラのサムネイル）: 分離プレビューはエディタが作ったビュー投影の行列をそのまま使い、対数に要る行列は深度の列だけを行列自身の W から作り直す（ギズモ・ピッキングとずれない）。プロトコルは 8 にし、カメラに深度方式と T を載せる。VSIX は透視のビューを Radial、正射影を Standard にし、サムネイルはシーンカメラの `DepthMapping`（既定 Radial）に従う。ThirdPerson を WARP で 90 フレーム動かして（W で歩いてジャンプ）撮影すると、対数と標準の画像は 99.98% の画素でビット一致し、違う画素も最大 2 階調だった（`.artifacts/M0100/step2/`）。

## 追記（2026-10-04）: GPU の log2 を使わない
- 観測した事実: 対数の GPU テストを一時的に実機（RTX 4060 Ti）で流すと 25 件中 2 件が失敗した。20 km 先で 5 cm 離れた 2 枚で 0.5 % の画素に奥の面が見え、4 µm の場面で標準と違う面が勝った。WARP ではどちらも合格する。深度を画素ごとに倍精度の基準と比べると、実機の誤差は最大 7.7（20 km）・11.4（50 m）・35.8（0.5 m）・52 万 ulp（4 µm、中心の深度が 3 % ずれる）、WARP は 4 ulp 以下だった。
- 原因: シェーダーの `log2` はハードウェアの近似命令で、`1 + x` が 1 に近いと絶対誤差が値そのものを覆い、そうでなくても数 ulp ずれる。Kahan の補正は `log2` が正確なことを前提にしている。
- 決定: `log2(1 + x)` を float の四則演算だけで求める（`ln(1 + x) = 2 atanh(s)` の奇数次の級数を `s⁹` まで。`s = x / (2 + x)`、または `1 + x` をビットで `m · 2^e` に分けて `s = (m − 1) / (m + 1)`、`|s| ≤ 0.1716`）。CPU の `RadialDepthCurve.Log2OnePlus` も同じ式にする。
- 結果: 実機でも画面中心の誤差は 4 ulp 以内（4 µm の誤差は 52 万 → 2.6 ulp）になり、対数の GPU テスト 25 件が実機で全件合格した。残る誤差は画面の端ほど大きく（4 µm で 41 ulp、20 km で 6 ulp）、中心ではほぼ 0 なので、シェーダーの計算ではなくラスタライザーが頂点をサブピクセル格子に丸めることによる（深度の勾配 × 1/256 画素）。標準の深度も同じ丸めた頂点から補間される。
- ハードウェアの `log2` は WARP では見えないので、`StagePresentationShaderSourceTests` が表示用のエントリーから超越関数を締め出し、`RadialDepthPrecisionEvidence`（`CGE_PERFORMANCE_EVIDENCE=1`、`CGE_PERFORMANCE_ADAPTER=hardware`）が実機での精度を測る（旧実装に戻すと両方とも失敗することを確かめた）。

## 追記（2026-10-04）: 物体ごとの接点
- 観測した事実: 対数が既定になったので遠景の重なりを測ると（RTX 4060 Ti、全画面の板 24 枚を手前から奥へ）、100〜1020 m の層で標準 0.097 ms に対し対数 1.738 ms だった（2〜48 m では 0.133 対 0.173 ms）。保守的深度の双曲線は 8 m の接点の近くでしか符号化した深度に近づかず、遠くでは低い一定値になるので、早期の棄却が起きない。接点を一時的に約 500 m へ移すと遠い層は 0.262 ms になり、接点の位置が原因と確かめた。
- 決定: 接点を物体ごとにする。双曲線はどの w0 > 0 で接しても全域で下界なので（上の導出は w0 によらない）、各物体の `(a, b)` をそのメッシュの境界の中心のビューの深さ（ビュー投影の W から求めるので、カメラでも行列で渡されたビューでも同じ）で接するように毎フレーム求め、`ObjectCB.RadialRasterDepth` に書く（`RadialDepthProjection.ConservativeDepthAt`、`StageRenderer.WriteRadialRasterDepths`）。`RadialVSMain` は標準のクリップ座標の深度だけを `a·w − b` に置き換える（X・Y・W は標準のまま）。FrameCB の `RadialRasterViewProjection` は無くなり、Stage ABI は `stage-v10`（FrameCB 944 バイト）。
- 結果: 遠い層 1.738 → 0.123 ms（標準 0.091 ms）、近い層 0.171 ms（標準 0.133 ms）。対数の GPU テストは WARP でも実機でも全件合格し、標準の基準フレームは不変。

## 関連する機能
- F-L5-038（対数深度カメラ）、F-L5-039（観察ビュー）、F-L0-002（StageRenderer）、F-L4-002（RenderServer）
