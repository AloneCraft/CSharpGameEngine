# M0111 — Lumen 相当の動的 GI と反射
- 日付: 2026-10-24
- 状態: 完了(2026-10-24)。実機の GPU は未確認。範囲外と残りは末尾の表

## 目的
ADR-0078 §2・§5 の「Lumen 相当の動的 GI と反射」を作る。M0110 のメッシュの距離場とグローバル距離場の上に、サーフェスキャッシュ、最終収集(画面のプローブ → 距離場 → 放射輝度キャッシュ)、反射(画面 → 距離場 → 空)を置き、UE のテンプレートと同じく光を毎フレーム解く。静的なライティングのベイクは作らない(ユーザーの決定)。ADR-0069 の DDGI プローブは、低品質と WARP の代替の段として残す。DXR は使わない。

## 決定
| 項目 | 決定 | 理由 |
|---|---|---|
| 段(スケーラビリティ) | `RenderScalability`(UE の `sg.GlobalIlluminationQuality`・`sg.ReflectionQuality`)。GI が High 以上で Lumen、反射は GI が Lumen で反射が High 以上のとき Lumen。Medium 以下は DDGI と空の光の反射(M0110 までのフレームそのもの)。環境変数 `CGE_SCALABILITY_GI`・`CGE_SCALABILITY_REFLECTIONS`(low/medium/high/epic か 0〜3。反射だけ省くと GI に従う。不正な値は起動時に止める) | UE の BaseScalability.ini と同じ切り替え。DDGI を捨てずに代替の段にする(ADR-0078) |
| 既定の段 | 実機の GPU は High、WARP は Medium(DDGI)。Epic(UE のテンプレートの値)は環境変数で選ぶ | WARP は CPU で描くので、既存のゴールデンと実走の値を変えない。Epic は WARP で原因不明の装置の削除と 2 通りの結果が出た(後述)。実機で確かめるまでは High を既定にする |
| High と Epic の違い | 画面のプローブ 32 / 16 画素ごと、反射 半分 / 全解像度、カードのアトラス 1024² / 2048²(1 枚の最大 64 / 128 テクセル)、1 フレームに照らすテクセル 65536 / 262144、撮影 256 / 512、放射輝度キャッシュ 3 段 × 12³ / 4 段 × 16³(間隔 2 m から 2 倍ずつ)、1 フレームに追跡するプローブ 256 / 1024 | UE の r.Lumen.ScreenProbeGather.DownsampleFactor(16)などを `probe_lumen.py` で読み、WARP でも回る数にした |
| 設定の値 | `PostProcessVolume` に UE の Lumen の欄(Scene Lighting Quality、Scene Detail、Scene View Distance 200 m、Lighting Update Speed、Final Gather Quality と Update Speed、Screen Traces、Max Trace Distance 200 m、Diffuse Color Boost、Skylight Leaking、Reflection Quality、Max Roughness To Trace Reflections 0.4、Reflection Screen Traces、Surface Cache Resolution)と `GlobalIlluminationMethod`・`ReflectionMethod`(既定 Lumen)。既定値が UE の値 | `tools/UnrealExport/probe_lumen.py` で 4 テンプレートのレベルと DefaultEngine.ini を読んだ。ThirdPerson と Vehicle の PostProcessVolume は Lumen の欄を上書きしておらず(override がすべて false)、FirstPerson・TopDown には Lumen の欄を持つ PostProcessVolume が無い。どれも UE の既定値なので、テンプレートのシーンは変えなくてよい |
| サーフェスキャッシュ | 物体ごとに箱の 6 面のカード(メッシュの空間の正射影で基本色×(1 − 金属)、法線、発光、深度を撮る)。アトラスは 128² のページに 2 のべき乗の矩形を詰める。毎フレーム、新しいカードの撮影(近い順)と、撮影したカード・発光の変わった物体・巡回のカードを予算内で照らす: 太陽(カスケードの影、範囲外は距離場の影)、局所光と影(点・スポット・面の近似、128 個まで)、前のフレームの放射輝度キャッシュ(多重の跳ね返り)、空、発光 × 物体の発光の倍率(`MeshEmissive`、ブレーキランプ) | Lumen の Surface Cache と同じく、光を当てる面を少ないテクセルで持つ。物体の空間なので動く物体についていく |
| 最終収集 | 画面のプローブ(High 32 画素・Epic 16 画素ごと、8 × 8 の八面体)が、画面の深度をたどり(当たれば前のフレームの色)、外れたら近くの距離場(グローバル距離場の候補をメッシュの距離場で確かめる)、その先は放射輝度キャッシュ。プローブを近傍の 3 × 3 で平面と法線の重みで均して SH にし、画素は 4 つのプローブを補間して前のフレームと混ぜる(再投影、距離と法線で棄却) | UE の Screen Probe Gather と Radiance Cache の構成。距離場の当たりを物体のメッシュの距離場で確かめるのは、0.5 m のボクセルでは 0.2 m の壁を素通りした(試験で見つけた)ため |
| 放射輝度キャッシュ | 世界のボクセル格子に固定したクリップマップのプローブ(カメラが動いても泳がない)。新しく覆ったプローブを細かい段・近い順に、残りの予算を巡回(段 L に 2/3 ずつ)で追跡する。プローブは毎回テクセルの中心の方向を追跡し、前の値と 0.75 で混ぜる | 方向を揺らすと値が揺れて収束が遅れた(試験で測った) |
| 反射 | GGX の可視法線の標本で 1 画素 1 光線: 画面の深度(交差を二分法で詰める)、外れたら距離場 → カード、さらに外れたら空。粗さが 0.4(UE の Max Roughness To Trace)以上なら画面のプローブの SH。限界の手前で混ぜる。解決のパスで粗さに応じた近傍の重み付き平均と、前のフレームの色の範囲で切った履歴 | UE の Lumen Reflections の流れ |
| 空 | 大気のパスに空の放射輝度の八面体マップ(128²、太陽の照度を掛けた値)を足した(Lumen が有効なときだけ作る) | 距離場の外れが空の色を読む |
| 描画契約 | `stage-v13`: FrameCB に `LumenParams`(x 拡散、y 反射)、t16 `LumenDiffuseTexture`、t17 `LumenReflectionTexture`、接尾辞に `LumenSurfacePSMain`(法線・粗さと距離)と `LumenCardPSMain`(カードの撮影)。Stage グラフの PSMain は、Lumen の拡散があればプローブボリューム・空の光の拡散を置き換え、反射があれば反射の照度を置き換える | 契約の文字列を正直にする。Lumen が無効(LumenParams = 0)なら式は M0110 と同じで、標準フレームの 7 つのハッシュは変わらない(試験で確認) |
| コンピュート | `assets/Shaders/Lumen.hlsl`(`compute-v2`、パスは定数で選ぶ 8 つ: カードの照明、放射輝度キャッシュ、画面のプローブの追跡と均し、統合、反射の追跡と解決、可視化)。ルートシグネチャは `StageLumen` が持つ | 既存のコンピュートと同じ扱い。パスごとのシェーダーへの分割は ABI の変更が要るので見送った(後述) |
| 可視化 | `CGE_LUMEN_VISUALIZE`(diffuse・reflections・surfacecache。UE の r.Lumen.Visualize) | 比較と調査のため |
| UE との差 | ハードウェアのレイトレーシング無し(ソフトウェアの距離場だけ)、ナナイト無し、カードは箱の 6 面だけ(UE はメッシュの形に合わせて複数)、半透明・二重の反射・屈折・AO の Lumen 版は無し、放射輝度キャッシュは全域の密な格子(UE は画面のプローブが使う所だけ)、反射は 1 回の跳ね返り | 範囲と WARP の制約 |

## 作ったもの
| 層 | 中身 |
|---|---|
| L0 描画 | `StageLumen`(新規。計画、カードのアトラス、撮影、計算パス、履歴、試験用の読み戻し)、`LumenSurfaceCacheLayout.cs`(新規。`LumenCardAtlas`、`LumenCards`)、`StageRenderer.Lumen.cs`(新規。表面の前処理、撮影、パスの記録)、`StageRenderer`(t16/t17、LumenParams、Lumen の間は DDGI を止める)、`StageAtmosphere`(空の放射輝度マップ)、`StageGlobalDistanceField`(メッシュの記録の公開)、`StageAutoExposure`(状態の資源)、`EnvironmentLighting.cs`(`LumenSettings`、`RenderScalability`、`RenderScalabilityResolver`、`LumenVisualization`)、`RenderServer`(段と可視化をデバイスの復旧の後も保つ) |
| シェーダー | `assets/Shaders/Lumen.hlsl`(新規)、`Atmosphere.hlsl`(パス 7)、`Stage.cgeshadergraph` と 4 つのサブグラフ(`tools/ShaderGraphMaintenance/edits/lumen_2026_10_24.py` で編集)、`StagePresentationShaderSource`、`ShaderArtifactAbi`(`stage-v13`) |
| L5 ランタイム | `PostProcessVolume` の Lumen の欄と `GlobalIlluminationMethod`・`ReflectionMethod`、`EnvironmentCollection.ToLumenSettings`、`EngineHost`(段と可視化)、自動実行のレポートの `lumenActive`・`lumenFrames` |
| L7 ビルド | `Game.props` と Windows.Tests に `Lumen.hlsl`、`FeatureStageCatalog` の `rendering.environment-lighting` に Lumen の試験 |
| 取り込み | `tools/UnrealExport/probe_lumen.py`・`run_probe_lumen.cmd`(出力は `.artifacts/UnrealExport/lumen/`) |
| SDK | 0.1.23 |

## 検証
- 単体(GPU なし): `LumenSurfaceCacheLayoutTests`(7。アトラスの詰め込みと返却・満杯、カードの正射影、箱の太らせと解像度、段の表、段の環境変数、設定の範囲)、`EnvironmentComponentTests`(+2。PostProcessVolume の既定値が UE の値でレンダラーへ写る、Lumen なし・使えない値は DDGI のまま)、`ShaderGraphCompilerTests`(Stage の項目 18、HLSL のハッシュの更新)。
- GPU(WARP、High 段、160 × 90、D3D12 のデバッグ層のエラー 0)。`LumenGpuIntegrationTests`(9):
  - Medium では Lumen の設定があってもフレームがビット単位で同じ。グローバル距離場が無ければ High でも Medium のフレームと同じ。
  - サーフェスキャッシュ: 最初のフレームで物体 5・カード 30 を撮って照らす。日の当たる床の上面のカード (20015, 18669, 17601)(大気の上の太陽だけなら 22776)、裏面は 1/3 以下、赤い壁のカードは赤・緑の壁は緑。
  - 色のにじみ: 日の当たる赤い壁の側の床の R/G 1.412、緑の壁の側 0.627。
  - 局所光と発光: 点光源で赤い壁のカード (538, 33, 33)、反対側の床に赤の跳ね返り (80, 14, 10)。青く光る箱の前の床 134 → 発光を切ると 34 → 戻すと 24 フレームで 162。
  - 収束と決定性: 40 フレームの平均の照度は 20 フレーム目から最後の 5 % 以内(2 % 以内は 38 フレーム目から)。同じ描画を 2 回すると最終の値がビット単位で同じ。
  - 反射: 粗さ 0.045 の金属の床に映る赤い箱は、幾何から求めた 440 画素に対して IoU 0.84・重心の誤差 1.19 画素(画面の追跡なしでは 0.60・3.68)。粗さ 0.25 で 0.80・1.46、0.8 は UE の限界 0.4 を超えるのでプローブになり赤は 0。赤みの最大は 6.69 > 6.58 > 0.76 と粗さで下がる。
  - 動く物体: 赤く光る箱を 2.5 m 動かすと、古い場所の赤 205 → 116(初めからそこに置いた場面 105)、新しい場所 136 → 179(同 195)。
  - 境界: 光が無い(照度 1e-3 未満)、壁の無い床は空の光の照度に 0.75〜1.15 倍で一致、距離場の無いメッシュと NaN の行列の物体はカードに入らず有限、10⁵ 倍の巨大な物体も有限。
- 実走(WARP、既定の Medium、`verify-thirdperson-character`): ThirdPerson 15.700 m(上昇 1.228 m)、FirstPerson 16.000 m(0.863 m)、TopDown ナビなし 9.606 m・ナビあり (6.79, 6.84)・長押し 17.414 m、Vehicle 57.150 m・30.5°(右トリガー 0.5 は 23.173 m、左スティック 0.5 は 9.4°)。すべて M0110 と同じ。D3D12 のエラー 0。標準フレームの 7 つのハッシュは不変。
- 実走(WARP、`CGE_SCALABILITY_GI=high`、入力なし 8 フレーム、1280 × 720): ThirdPerson・Vehicle とも 8 フレームすべて Lumen、D3D12 のエラー 0。画像は `.artifacts/m0111/compare/`。
- フレーム時間(WARP、同じ機械でユーザーの別のビルドと試験が動いている): High は最初のフレームが ThirdPerson 16.9 s・Vehicle 18.7 s(シェーダーの初回の JIT と全カードの撮影)、2 フレーム目から 0.68〜1.94 s。Medium(DDGI)は 1.05〜3.39 s。実機の GPU は未確認。
- 全テスト `dotnet test CSharpGameEngine.slnx -c Debug`: 22 プロジェクトで失敗 0(Windows.Tests 804 合格・2 スキップ、VSExtension.Tests 1197、CodeAnalysis.Tests 338、FeatureRunner.Tests 222)。Lumen の試験一式は単独でも 3 回続けて合格。
- パッケージ(`verify-template-package`、SDK 0.1.23): ThirdPerson 15.700 m、TopDown (6.79, 6.84)、Vehicle 57.150 m・30.5°。D3D12 のエラー 0。

## 実装中に見つけた事実
- グローバル距離場(0.5 m のボクセル)だけで当たりを決めると、光線が厚さ 0.2 m の壁を素通りし、赤い壁の色が床に届かなかった。候補を物体のメッシュの距離場で確かめる(`TraceSurface`)ようにして直した。鏡の床に映る箱も、直す前は 30 % 広く、重心が 4 画素ずれていた。
- 放射輝度キャッシュのプローブの方向を毎フレーム揺らすと、値が揺れて収束が遅れた。テクセルの中心を毎回追跡し、前の値と 0.75 で混ぜ、新しいプローブを優先し、残りを巡回するようにした(20 フレームで 5 % 以内)。
- Vehicle の Lumen で明るい点が出たのは、画面の追跡が前のフレームの色を今のフレームの露出で読んでいたため。前のフレームの露出を残して使い、非有限と飽和した履歴を捨て、出力を範囲に切った。
- 試験の場面は、物理的な空が太陽を要る(太陽なしでは空が暗い)。「空だけ」の試験は「壁の無い床」に直した。
- Epic 段を WARP の試験一式で回すと、断続的に D3D12 の装置が削除された(約 8 割の実行で 1 件以上)。デバッグ層の通知は `DEVICE_REMOVAL_PROCESS_NOT_AT_FAULT`(Plug-and-play stop)で、エラーは他に無く、削除が見えるのは試験の読み戻しの `Map` かフレームの途中。メモリの予算(48 GB 中 約 200 MB)や参照の漏れ(破棄ごとに参照 0)ではない。DRED を有効にすると起きず、GPU ベースの検証層では Lumen のカードの照明・統合・反射のパスで `ExecuteCommandLists` の中のアクセス違反になった(GBV 自体の問題か不明)。Epic のうち放射輝度キャッシュだけを High の大きさにすると 3 回とも起きず、High 段では 6 回とも起きなかった。Epic では、収束の試験の 2 回の描画が 2 通りの値(6 桁目の違い)のどちらかになることもあった。High では毎回ビット単位で同じ。原因は未解決(推測: WARP の巨大な単一のシェーダーの扱い。パスごとのシェーダーへの分割で確かめる)。このため既定は実機でも High、WARP の試験も High にした。

## UE の参照画像との再比較
- 方法: エンジンの 8 フレーム目(入力なし、WARP、1280 × 720)を Medium(DDGI)と High(Lumen)で撮り、UE の `ue_reference_spawn_camera`(ThirdPerson)と `ue_reference_chase`(Vehicle)と並べ、領域の平均輝度(0.2126R + 0.7152G + 0.0722B)を測った(`.artifacts/m0111/compare/`)。
- ThirdPerson: 全体の平均 UE 141.3・Medium 153.4・High 155.4。床の中央は 185.3・184.9・186.5 でほぼ同じ。陰の面は UE よりかなり明るい(右奥の暗い壁 7.8・53.6・68.4、右の斜面の陰 95.0・138.1・146.4)。Lumen にすると陰はさらに明るく、UE から離れた。High ではマネキンの下半身に床の緑の照り返しが乗り(UE の Lumen と同じ現象)、Medium の床の左に出ていた三角形の明るい漏れ(DDGI)が消えた。
- Vehicle: 床の材質とトラックが違う(M0112 の範囲)ので全体の数値は比べられない(全体 UE 141.8・Medium 107.6・High 106.8)。High では車の後ろの影と車体の下が床の橙の照り返しで赤みを帯び、車の金属の部品に空の青が映る。車の塗装は Medium より少し赤く明るい(下半分の R 175 → 186)。UE の塗装の明るい反射(クリアコート)は無い。
- 結論: GI と反射の仕組みは働いているが、陰の明るさは UE より明るいまま(差の原因は推測: UE の Lumen の短距離の AO と、空の光の遮りが粗いこと)。

## 範囲外・残り
| 項目 | 状態 |
|---|---|
| サーフェスキャッシュ(カード、撮影、太陽・局所光・影・空・前のフレーム・発光での照明、予算) | 完了 |
| 最終収集(画面のプローブ、距離場、放射輝度キャッシュ、時間と空間のフィルター) | 完了 |
| 反射(画面、距離場 → カード、空、粗さの混ぜ、鏡と粗さの段階の試験) | 完了 |
| 段(DDGI を Medium 以下の代替に、UE の値、環境変数) | 完了。既定は実機 High(UE は Epic) |
| Epic 段の WARP での装置の削除と 2 通りの結果 | 未解決(上の事実) |
| 実機の GPU での見た目・フレーム時間 | 未確認(WARP のみ) |
| 陰の明るさを UE に合わせる(短距離の AO、空の光の遮り) | 未着手 |
| メッシュの形に合わせた複数のカード、半透明、多重の反射、屈折、ハードウェアのレイトレーシング | 範囲外 |
| `Unreal.SceneImporter` で PostProcessVolume の Lumen の欄を読む | 未実装(4 テンプレートは UE の既定値のままなので、Component の既定値で足りる) |
| パスごとのシェーダー(実機の占有率、WARP の JIT) | 未着手(コンピュートの ABI の変更が要る) |
