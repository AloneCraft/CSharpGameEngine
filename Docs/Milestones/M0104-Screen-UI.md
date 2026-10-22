# M0104 — 画面 UI
- 日付: 2026-10-22
- 状態: 完了(2026-10-22)

## 目的
ADR-0078 の「画面 UI は作る」(UE の `UI_Vehicle` の HUD など)。文字と画像を、描き終えたフレームの上に重ねる。Vehicle テンプレートの速度計をタイトルバーから画面の文字へ移す。

## 決定(ADR-0078 に従う細部)
- 描く場所: トーンマッピングとオーバーレイの後、`R8G8B8A8_UNORM` のバックバッファへ直接。値は sRGB のまま書く(ガンマをかけ直さない)。
- 頂点はピクセル座標で受け、CPU で NDC にして `SetScreenLayer` へ渡す。レイヤーはそのフレームだけ有効(毎フレーム作り直す)。デバイスを失ったときは `RenderFrameState` が作り直す。
- 上限: 頂点 24,576、バッチ 32、常駐テクスチャ 64、1 フレームの要素 4,096。超えた分は描かず、1 回だけ診断に出す。
- 描画契約は `stage-v11`(`ScreenVSMain`、`ScreenPSMain`。契約の項目は 16)。ピクセルシェーダーは、画像なら `texture × color`、文字(距離場)なら `smoothstep(0.5 ± fwidth)` の覆い。項目はエンジンが持つ `StagePresentationShaderSource` の接尾辞に足したので、Stage グラフの本体は変わらない。ただし ABI 版が上がるので、成果物は作り直しになる(ADR-0076 の `stage-v10` と同じ扱い)。
- フォント: `CGEFONT1`(em 単位の寸法、平面と UV の四角形、8 ビットの符号つき距離、128 = 輪郭)。ゲームは焼いた `.cgefont` だけを読み、GDI+ も元のフォントも要らない。
- 配置: UMG と同じアンカーとピボット(`ScreenAnchor` の 9 点)とオフセット。サイズとオフセットは「短辺 1080」の画素で書き、実際の面へ拡縮する(`ScreenUi.ReferenceShortestSide`)。四角形の左上は整数の画素に丸める。
- 文字の配置: 字詰め(カーニング)なし。無い文字は `?`。改行は `\n`、行揃えは `TextAlignment`。

## 作ったもの
| 層 | 中身 |
|---|---|
| L1 描画の契約 | `RenderScreenVertex`、`RenderScreenBatch`、`RenderScreenShading`、`RenderScreenLayer.Validate`(`src/CSharpGameEngine.Rendering/RenderScreenLayer.cs`) |
| L1 フォント | `FontAtlas`(読み書きと検査)、`ScreenTextLayout`(測る・並べる)(`src/CSharpGameEngine.Rendering/Fonts/`) |
| L0 描画 | `StageRenderer.ScreenLayer.cs`(`SetScreenLayer`、常駐テクスチャ)、画面用のルートシグネチャとパイプライン、`RenderServer` と `RenderFrameState` の経路 |
| L7 ツール | `tools/CSharpGameEngine.FontBaker`: GDI+ の輪郭 → 高解像度のマスク → 厳密な EDT → 平均した符号つき距離 → 8 ビット。4 倍の超標本化、サイズ 64 で広がり 8 画素 |
| L5 ランタイム | `ScreenText`・`ScreenImage`(Component)、`ScreenUi`(サービス)、`ScreenTextCollect`・`ScreenImageCollect`・`ScreenUiSubmit`(Presentation の System。`Present` は `ScreenUiSubmit` の後) |
| 資産 | `assets/Fonts/Roboto-Regular.cgefont`、`Roboto-Light.cgefont`(UE5.8 が持つ Roboto を焼いた。Apache-2.0、`ROBOTO_LICENSE.txt`)。SDK パッケージに入れ、ゲームの出力の `Fonts\` へコピーする(`CgeIncludeDefaultFonts`) |
| 型つきアセット | `.cgefont` → `FontAsset`(`Assets.Fonts.Display`) |
| 自動実行 | レポートに `screenLayerFingerprints`(フレームごとの画面レイヤーの FNV-1a、16 桁の 16 進) |

## Vehicle テンプレート
- UE の `UI_Vehicle` の配置に合わせた(右下の余白 32、速度の列は右端から 148、ギアの列はその左 295.5、数字は Roboto Regular 40・ラベルは Light 15、中央揃え)。
- 車の子に `Hud_SpeedValue`・`Hud_SpeedLabel`・`Hud_GearValue`・`Hud_GearLabel` を置いた。数字の 2 つにマーカー `SpeedReadout`・`GearReadout` を付け、`SpeedometerSpeed`・`SpeedometerGear` が親の `VehicleState` を `in Parent<VehicleState>` で読んで `ScreenText` を書く(速度は |前進速度| × 3.6 を偶数丸めして 3 桁のゼロ詰め、ギアは R / N / 段の数字)。`Hud.SetTitle` は使わなくなった。

## 検証
- 単体(GPU なし): `FontAtlasTests`、`FontBakerTests`(17。距離変換を全探索と比べる)、`ScreenUiTests`(配置・拡縮・レイヤー順とバッチ・欠けたフォント・非有限値・指紋)、`AssetCatalogTests`(`FontAsset`)、`ShaderGraphCompilerTests`(16 項目)。
- GPU(WARP): `ScreenLayerTests`(3。四角形が頼んだ画素に出る・ブレンドされる・1 フレームで消える・デバイス復旧後も出る・距離場の閾値が 0.5)。D3D12 のデバッグ層のエラー 0。
- 実走: Vehicle を WARP で 60 フレーム、W を押し続ける。走行中の画面は `3` と `073 Km/h` を Roboto で描いた(停止時は `N` と `000`)。`VehicleDriveVerification` は、走り出したあとの画面レイヤーが 3 種類以上の状態を取ること(速度の数字が変わること)を判定に足した。結果は 28 状態、移動 48.281 m、曲がり 38.3°、D3D12 のエラー 0。SDK パッケージから作った「My Vehicle」も同じ値で合格し、`Fonts\` がパッケージ経由で届くことを確かめた。
- 全テスト(`dotnet test CSharpGameEngine.slnx`): 失敗 0。

## 実装中に見つけた事実
- D3D12 のデバッグ層は、ポストプロセスのルートシグネチャに `ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT` が無いのに頂点バッファを使うパイプラインを拒否する(MISSING_ROOT_SIGNATURE_FLAGS)。画面用に別のルートシグネチャを作った(共通の作り方 `CreateSrvAndConstantsRootSignature(flags, context)` を使う)。
- 距離変換の試験の期待値は、画素の中心を使って数える(画素 14 の中心は円の中心から 6.5 離れる)。実装ではなく試験の数え違いだった。

## 範囲外(未実装)
- 画像の 9 分割(UMG の Border)、字詰め、複数のフォントのフォールバック、`DefaultCodePoints`(ASCII、Latin-1、一部の約物とユーロ)の外の文字(日本語を含む。`?` になる)、入力を受ける UI(ボタンなど)。計画の範囲は HUD の表示まで。
- 見た目の仕上げ(材質のテクスチャなど)は M0112 以降。
