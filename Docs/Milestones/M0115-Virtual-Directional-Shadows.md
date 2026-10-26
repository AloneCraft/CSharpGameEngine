# M0115 — 仮想シャドウと TopDown の法線

- 日付: 2026-10-25 (最終確認は 2026-10-26)
- Feature: F-L4-020、F-L0-007、F-L4-014
- Decision: [ADR-0082](../Decisions/ADR-0082-Virtual-Directional-Shadows.md)
- 状態: Implemented / TestVerified / RuntimeVerified。Conformance: MatchesWithDocumentedExtension。

## 実装したもの
太陽の 4 段のクリップマップ、4096 texel の仮想解像度、128 texel のページ、最大 1024 ページの CPU 計画と D32 物理アトラス。ページのキーは世界の光源座標の格子。カメラの移動は新しく要求するページを加え、物体の追加・削除・移動は古い範囲と新しい範囲を無効化する。スキニングは各関節で変換した境界の包絡を使い、毎フレーム無効化する。光源方向・光源方向の深度原点・予算の変更はキャッシュを再構築する。

設定は `DirectionalShadowRendering` → `EnvironmentLightingSettings.DirectionalShadows` → RenderServer の保存フレーム → StageRenderer。ページテーブルと射影定数は fence 保護された 2 フレームのスロットを使う。t18 は深度アトラス、t19 はページテーブル、Stage ABI は `stage-v14`。通常モードでは 1×1 のダミー深度を使い、最初の仮想モードで GPU が空のときに物理プールと記述子を更新する。深度方向は 2048 m 幅、原点は 256 m ごとに固定し、量子化の境界でも受光点と画面外の遮蔽物の余白を残す。

比較は同じフレームの同じオブジェクトを左右の同寸法の viewport に描く。左右の FrameCB は別スロット。左は CSM、右は仮想ページ。HDR・固定露出・出力は共通。比較の見本は太陽と環境光だけで、空・GI・AO・自動露出は比較モードでは使わない。1/2/3 で通常単独・仮想単独・左右、P で停止・再生、矢印でカメラ移動、R でカメラを戻す。

## TopDown の確認
修正前を現在のソースからビルドして再現した。32 フレーム、実機 GPU、カメラ位置 `(-6.363272,13.784691,-6.363271)` で、添付画像と同じ坂の暗さを確認した。

画面への射影で左上は `SM_Ramp11` (シーン原点 (5,0,-2)、上面中心は画素 (315.6,198.3))、右上は `SM_Ramp10` (原点 (2,0,5)、中心 (964.4,198.3)) と確認した。カメラの FOV はシーンの 0.09067 turn。

非均一スケールに `normal × World` を使っていた。Stage の VSMain を逆転置変換と接線の直交化へ変更した。同じ VSMain を標準・Radial・プレビュー・GI のサーフェス描画が使う。一様な正のスケールでは以前と同じ演算を保ち、既存のピクセル基準を維持する。逆転置計算は最大のスケールで正規化して、大きなスケールでの cofactor のオーバーフローを防ぐ。

| 測定領域 (1280×720、sRGB の重み付き輝度 0–255) | 修正前 | 修正後 |
|---|---:|---:|
| 左上の坂 (250,180)–(310,210) | 27.79 | 94.42 |
| 右上の坂 (900,180)–(960,210) | 82.08 | 117.96 |
| 床 (610,120)–(670,145) | 128.37 | 127.38 |

照明・材質・露出設定は変更していない。両方の撮影で D3D12 エラーは 0。床の小さな変化には自動露出や間接光の寄与も含まれるため、坂の輝度だけから物理的な照度の倍率とは解釈しない。

## 証拠と検証
- `.artifacts/virtual-shadows/topdown-before.{png,json}`、`topdown-final.{png,json}`。
- `.artifacts/virtual-shadows/comparison-hardware.{png,json}`: 524 ページ常駐、521 再利用、移動物体で 3 再描画、要求不足 0、D3D12 エラー 0。これは初期撮影。最終版は下記の `comparison-final-*`。
- `VirtualDirectionalShadowTests` + `RenderQualityProfileTests`: Debug / Release とも 15/15。静止、負の座標、移動・削除・変形、同じ境界の回転とメッシュ差し替え、LRU、深度の量子化境界、光源・原点、予算超過。
- `VirtualShadowGpuTests`: Standard/Radial の影、キャッシュ再利用・失効、幾何法線との比較を WARP で確認。比較・復旧・リサイズ・スキニングの古い影の消去・解析的な細い影の基準も通過。
- 共有の Windows.Tests 出力を別プロセスがロックしていたため、検証には `-p:OutDir=G:/GitHub/CSharpGameEngine/.artifacts/virtual-shadows/tests-debug/` を使う。共有プロセスは停止していない。

## 制約
- 点光源・スポット・面光源・発光メッシュの仮想化、GPU feedback、ExecuteIndirect は対象外。
- 仮想モードも欠落ページ用の CSM を毎フレーム描く。GPU 時間は CSM と仮想ページを別々に示す。仮想単独の総コストは両方を含む。速度向上は主張しない。
- `Missing` は不足したページ要求数で、実際にフォールバックを通った画素数ではない。
- WARP の時間は性能の根拠にしない。実機の単独モード測定と左右同時表示の時間を分ける。

## 復旧で発見して修正した問題
時刻クエリーを含む描画コマンドを削除済みのデバイスへ記録すると、WARP が古い世代の解放済みメモリーへアクセスするケースを再現した。`RadialViewProjection_SurvivesDeviceRecovery` の単独実行でもネイティブのアクセス違反になり、cdb は `d3d10warp` と解放済みメモリーのアドレスを示した。フレーム開始時に `GetDeviceRemovedReason` を確認して、コマンド記録の前に既存の RenderServer の復旧へ渡す。クエリーを無効化せずに、仮想シャドウと Radial の復旧を含む 22/22 が通った。

## 画質の数値基準
薄い箱の影を 256×256 の WARP で描き、各画素の受光点から太陽への光線と箱の厳密な交差で遮蔽の有無を求める。日向と環境光だけの実測画像を色の基準にして、受光面の絶対誤差を加算する。通常 CSM は 35,308、仮想は 15,501 (43.9%、約 56.1% 小さい)。これはこの固定場面の画質の測定で、一般的な速度や全場面の画質向上を意味しない。生の値と Shader Artifact SHA は `tests-debug/virtual-shadow-reference.json` に保存する。

## 最終の実行証拠
- GPU と関連回帰: Debug 47/47、Release 48/48 (Release は Radial 行列の復旧も追加)。最終機能と automation の Debug 27/27。結果は `results/final-debug.trx`、`final-release.trx`、`final-feature-debug.trx`。
- カタログ・網羅性: 18/18。ソリューション/共有プロジェクト登録: 4/4。Stage Pack の `run rendering.virtual-shadows`: passed (生成・所有権・破棄)。通常の描画/GI/局所光/スキニング/プレビュー/復旧を含む。
- Release 比較ビルド: 警告 0、エラー 0。`comparison-final-hardware.png/json`: NVIDIA GeForce RTX 4060 Ti、524 常駐、3 再描画・521 再利用、要求不足 0、D3D12 エラー 0。
- `comparison-final-paused.png/json`: P を 8 フレーム目に押下、最終フレームは再描画 0・再利用 524。`comparison-final-warp.png/json`: Microsoft Basic Render Driver、524 常駐、3 再描画・521 再利用、要求不足 0、D3D12 エラー 0。
- `single-csm-hardware.png/json`、`single-virtual-hardware.png/json`: 1 / 2 の単独モードを別プロセスで実行。CSM の最後のフレームの GPU 時間は 0.0184 ms、仮想は CSM 0.017408 + 仮想ページ 0.014336 ms。これは単一フレームの参考値で、統計的な速度比較ではない。仮想のフォールバック分を除外しない。
- `comparison-evidence.json`: 5 warm-up + 30 frame interval samples、adapter、実際の WARP 判定、解像度、ページ統計、画像 SHA-256。フレーム間隔は VSync と CPU を含む。完了済みスロットの時間を画面に表示し、キャプチャ時の JSON は GPU idle を待って撮影フレーム自体の時刻を読む。

- 最終ソースの `topdown-final.png/json` も同じ 32 フレーム・同じカメラで確認した。左の坂 94.42、右 117.96、床 127.38、D3D12 エラー 0。撮影フレーム自身の GPU 時間読み取りを加えた後の Release 再確認も `capture-timing-release.trx` の 7/7 が通過。

## 並行作業との境界
最後の Debug 再ビルド時、別作業で追加された `tests/CSharpGameEngine.Windows.Tests/Physics/MeshMultiFaceContactTests.cs` の private な `Shape` が public な TheoryData/テスト引数に使われ、CS0051/CS0053 でテストプロジェクト全体のコンパイルが止まった。対象ソースは変更していない。最後の機能確認だけは `.artifacts/virtual-shadows/ExcludeUnrelatedTest.targets` を `CustomBeforeMicrosoftCommonTargets` で読み、当該 1 ファイルを Compile から外して隔離する。これは通常ビルドの修正ではない。前掲の Debug 47/Release 48 はこの並行追加前の完了した実行結果である。

隔離した最終 Debug 検証は `results/capture-timing-debug.trx` の 7/7 が通過した。撮影フレーム自身の時刻読み取りを含む最終変更は、Debug / Release の両方で仮想シャドウと Radial のデバイス復旧を再確認済み。
