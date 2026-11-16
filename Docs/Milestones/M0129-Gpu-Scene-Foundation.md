# M0129 — 仮想化描画の共通の下回り(GPU シーン、間接描画、番号引き)
- 日付: 2026-11-17
- 状態: Implementing(単位 0〜4 を終えた)
- 段階: ADR-0098 の段階 D の前倒し(ユーザーの判断。`Docs/目標、ロードマップ、戦略.md` は変えない)
- 決定: [ADR-0100](../Decisions/ADR-0100-Virtualized-Rendering.md) 決定 1〜3
- ABI: `stage-v18`(単位 3 で `stage-v17`、単位 4 で `stage-v18` に上げた)

## 用語
| 語 | 意味 |
|---|---|
| GPU シーン | 全物体の位置と材質を GPU 上の表にまとめたもの |
| 間接描画(ExecuteIndirect) | 描く数を GPU の計算結果で決める描画命令 |
| 番号引き(bindless) | 材質のテクスチャを 1 本の配列に並べ、番号で引く方式 |
| ゴールデン | 固定した 7 枚の基準画像(`StageGoldenFrameTests`) |
| 関門 | ここが通らなければ計画を見直す、と決めた試験 |

## 目的
従来のメッシュを、見た目を変えずに「GPU 側で選んで描ける形」へ載せ替える。後の段階(仮想ジオメトリ、仮想シャドウ、仮想テクスチャ)が共有する部品を作る。

## 単位
| 単位 | 内容 | 合格の条件 | 状態 |
|---|---|---|---|
| 0 | ADR-0100 とこの文書 | 文書の参照が正しい | 済 |
| 1 | デバイスの能力の問い合わせと関門試験 | WARP と実機で通る | 済 |
| 2 | `PSMain` を「表面の評価」と「照明」に分ける。ABI は変えない | ゴールデン 7 枚が同じ | 済 |
| 3 | GPU シーン化。物体ごとの定数をやめ、物体と材質の表にする。ABI を `stage-v17` へ | ゴールデン 7 枚が同じ | 済 |
| 4 | 材質テクスチャの番号引き化。影の画像を環境側の表へ移す | 同上。材質数の上限が消える | 済 |
| 5 | アップロードの輪と読み戻しの輪 | 材質・メッシュの追加で GPU を待たない | 未 |
| 6 | L4 の共通部品(画面上の大きさの式、空き枠の割り当て、報告) | CPU の試験 | 未 |

## 試験で固定する約束
- ゴールデン 7 枚が変わらない。
- 描画層(L0)は計画を呼ばない(`EngineLayerSpec.NativeBackendDoesNotPlanFrames`)。
- 空き枠の割り当て: 予算を超えない。固定した項目は追い出さない。同じ入力で同じ順。
- 画面上の大きさの式: CPU とシェーダーで結果が一致する。
- デバイスを作り直した後、物体と材質が元に戻る。
- 物体の値(位置、発光の倍率など)は、1 度設定すればどのフレームの枠でも同じ絵になる(単位 3 で足した)。

## 範囲外
仮想ジオメトリ(M0130・M0131)、Hi-Z(M0131)、仮想シャドウ(M0132)、仮想テクスチャ(M0134・M0135)、スキニングの経路の変更。

## 機能 ID
F-L4-023(GPU シーン)。単位 3 で機能台帳(`Docs/Implementation-Status.md`)に登録した。FeatureRunner のカタログ(Stage)への登録は M0129 の締めで行う。

## 実行記録: 単位 1 関門(2026-11-17)
- **確かめたこと**: 後の段階が前提にする 3 つの D3D12 の機能が、エンジンの描画器を通さずに動くか。
  1. 計算シェーダーが書いた「描く数」で間接描画する。
  2. 頂点シェーダーが、頂点番号とインスタンス番号だけから構造化バッファを引いて形を作る(頂点バッファなし)。先頭の位置は CPU がルート定数で渡す。
  3. 画素シェーダーが、上限なしのテクスチャ配列を番号で引く。
- **方法**: `tests/Windowsのテスト/Windows/Graphics/GpuDrivenDrawGateTests.cs`。8 個の物体のうち 5 個に「見える」印を付ける。計算シェーダーが見える物の番号を詰め、描く数を引数バッファへ書く。CPU は数を渡さない。4×2 のマスに描き、マスごとに 5 点の画素を読む。
- **合格条件**: 見える 5 マスが、その物が番号で指したテクスチャの色と完全に一致する。見えない 3 マスがクリアした色のまま。引数バッファの中身が (6, 5, 0, 0)。デバッグ層のエラーが 0。
- **結果**: WARP(`Warp_ComputeWrittenIndirectDrawSamplesIndexedTextures`)合格。実機(`Hardware_...`、`CGE_PERFORMANCE_EVIDENCE=1`)合格。
- **コマンド**: `dotnet test "tests/Windowsのテスト/Windowsのテスト.csproj" -c Debug --filter "FullyQualifiedName~GpuDrivenDrawGateTests"`
- **製品側の変更**: `GraphicsDevice.ResourceBindingTier`(D3D12 のリソース束縛の段)を足した。必須にするのは単位 4。
- **判断**: 関門を通過した。材質ごとに描画を分ける代案(ADR-0100「採らなかった案」)へ戻す必要は無い。
- **未確認**: 実機は開発機の 1 台だけ。別の GPU では確かめていない。

## 実行記録: 単位 2 表面と照明の分割(2026-11-17)
- **変えたこと**: Stage の画素シェーダー `PSMain` を、`EvaluateSurface`(材質を読んで `SurfaceData` を作る)と `EvaluateSurfaceLighting`(`SurfaceData` と画素の位置から色を作る)に分けた。`PSMain` は遠い面を捨てた後、この 2 つを呼ぶだけになった。編集は `tools/ShaderGraphMaintenance/edits/surface_split_2026_11_17.py`。
- **変えなかったこと**: 計算式と、浮動小数点の足し算の順。動かしたのは発光テクスチャを読む位置だけ(照明の後から `EvaluateSurface` の中へ)。契約(`stage-v16`)、エントリー 14 個、後置ソース(`StagePresentationShaderSource`)。照明側の細かい分割(方向光・局所光・間接光・霧)は ADR-0085 の実施時に行う。
- **修正前**: ゴールデン 7 枚が合格(`.artifacts/M0129/unit2-golden-before.log`)。
- **修正後**: ゴールデン 7 枚が同じハッシュで合格(`.artifacts/M0129/unit2-golden-after.log`、`StageGoldenFrameTests.LogarithmicFrames_MatchTheirPinnedHashes`)。シェーダーグラフ・成果物・関門の試験 64 件のうち、合格 63、実機専用で対象外 1(`.artifacts/M0129/unit2-after.log` と上のログ)。
- **意図して更新した固定値**: 合成した Stage の HLSL のハッシュ(`ShaderGraphCompilerTests.ProductionStage_ExternalSubgraphsPreserveGoldenHlsl`)。`621BCDE3…` から `5E85B8CD…` へ。シェーダーの文面を変えたため。
- **部品ファイルの差分**: `Pbr`・`Shadow`・`Pcss`・`Aces`・`Depth` の `.cgesubgraph` にも差分が出る。JSON として比べると、変わったのは道具が編集のたびに振り直すノードの ID と図の配置だけで、バイト数は同じ。
- **見つけて直したこと**: `tools/ShaderGraphMaintenance/edit_stage_graph.py` が `Cge.exe dump-hlsl` と呼んでいて動かなかった(道具を `tools/Cge` にまとめた 511a22eb の取りこぼし)。`shader-graph` を前に付けるように直した。
- **コマンド**: `python tools/ShaderGraphMaintenance/edit_stage_graph.py tools/ShaderGraphMaintenance/edits/surface_split_2026_11_17.py`、`dotnet test "tests/Windowsのテスト/Windowsのテスト.csproj" -c Debug --filter "FullyQualifiedName~StageGoldenFrameTests|FullyQualifiedName~ShaderGraphCompilerTests|FullyQualifiedName~GpuDrivenDrawGateTests"`
- **適合**: この単位は Matches。M0129 全体は Partial(単位 3〜6 が残る)。
- **未確認**: ソリューション全体の試験と、4 つのテンプレートの画像の前後比較は、単位 3(GPU シーン化)の後にまとめて行う。この単位では、主パスの画素を固定する 7 枚と、シェーダーの組み立ての試験だけを通した。

## 実行記録: 単位 3 GPU シーン化(2026-11-17)
- **変えたこと**: 物体ごとの定数バッファ(ObjectCB、b1)をやめた。代わりに、物体の表(`Instances`、t20、1 件 84 バイト)、材質の表(`Materials`、t21、1 件 112 バイト)、描画ごとのルート定数(`DrawCB`、b1、物体の番号・表示モード・描画の印の 3 個)にした。契約は `stage-v17`。
  - シェーダー: `tools/ShaderGraphMaintenance/edits/gpu_scene_2026_11_17.py`(グラフ本体)と `StagePresentationShaderSource`(表示用・Lumen 用)。頂点シェーダーが物体の番号を `VertexOutput.Instance`(補間なし)で画素シェーダーへ渡す。
  - L4: `StageInstanceData`・`StageMaterialData`・`StageDrawConstants`・`StageDrawFlags`、フレーム計画の `StagePass.SceneUpload`(スキニングの直後)。
  - L0: `StageRenderer.Scene.cs` が表を 1 フレームに 1 回、今の値で全部書く。物体を描く入口 9 か所は、物体の番号を渡すだけになった。Lumen のカード撮影は、物体の写しを作らず「メッシュ自身の空間で描く」印で描く。
  - L7: DXC の反射を読む部品が、構造化バッファの要素の配置も返すようにした。配置の試験はエントリーごとに、使う表の配置と、使わない表が無いことを確かめる。
- **見つけて直したこと(副次)**: 以前は、物体の値を 1 度だけ設定すると、その時のフレームの枠にしか書かれなかった。設定を毎フレーム呼び直さない使い方では、枠ごとに古い値と新しい値が交互に出る。表を毎フレーム全部書く形にしたので起きなくなった。
- **修正前**: 単位 2 のコミット(cfc72c83)の写しに新しい試験だけを入れると、`StageFrameSlotPersistenceTests.ObjectValuesSetOnce_Warp_RenderIdenticallyInEveryFrameSlot` が「frame 1 differs from frame 0」で失敗した(`.artifacts/M0129/unit3-before-slot.log`)。
- **修正後**: ゴールデン 7 枚が同じハッシュで合格。Windowsのテストの全体 1017 件のうち、合格 1014、対象外 3(実機専用など)、失敗 0(`.artifacts/M0129/unit3-windows-all.log`)。エンジンのテストの関連 18 件(表の形、計画の順、層の検査)が合格(`.artifacts/M0129/unit3-engine.log`)。
- **意図して更新した固定値**: 合成した Stage の HLSL のハッシュを `5E85B8CD…` から `56E4D505…` へ。
- **配置の事故(2026-11-17 07:54)**: この単位の書き換えの途中で、VS の F5 が拡張を組み立てた。拡張に、新しいシェーダーと古い描画層 `Windows.dll`(x64、02:48 の版)が混ざって入り、プレビューとサムネイルが `CreateGraphicsPipelineState` の `E_INVALIDARG` で失敗した。今の作業ツリーから x64 でプレビュー用ホストを組み立てると、すべて新しい版でそろうことを確かめた。取り決めの版を確かめているのは `Contracts.dll` だけで、描画層は確かめていない。描画層にも版の確認を持たせることを、この段階の続きで検討する。
- **適合**: この単位は Matches。M0129 全体は Partial(単位 4〜6 が残る)。
- **未確認**: 実機の GPU での確認、4 つのテンプレートの画像の前後比較、作り直した拡張での実画面のプレビュー。

## 実行記録: 単位 4 材質テクスチャの番号引き(2026-11-17)
- **変えたこと**: 材質の 4 枚のテクスチャ(t0〜t3)と、それを材質ごとに切り替える記述子の束をやめた。描画器が持つテクスチャの見え方(テクスチャと sRGB かどうかの組)ごとに 1 つの番号を付け、上限の無い配列 `MaterialTextures[]`(t0 space1)に並べる。材質の表(1 件 128 バイト)が 4 つの番号を持ち、シェーダーは `NonUniformResourceIndex` で引く。契約は `stage-v18`。
  - 影の画像(t4)はレジスターを変えず、環境の表(t12〜t19)の末尾に置いた。影のシェーダーは変えていない。
  - 記述子の置き場は「環境の表、影の画像、材質のテクスチャ」の順。材質やテクスチャが足りなくなったら、材質の表と記述子の置き場を倍に広げる。材質の数の上限は無くなった(`FreeMaterialSlots` は上限なしを表す大きな値を返す)。
  - D3D12 のリソース束縛の段が 2 未満の GPU は、描画器を作るときにアダプターの名前を添えて断る。
  - 編集は `tools/ShaderGraphMaintenance/edits/bindless_textures_2026_11_17.py` と `StagePresentationShaderSource`。描画器は `StageRenderer.MaterialTextures.cs`。
- **修正後**: ゴールデン 7 枚が同じハッシュで合格。Windowsのテストの全体 1019 件のうち、合格 1016、対象外 3、失敗 0(`.artifacts/M0129/unit4-windows-all.log`)。エンジンのテストの関連 19 件が合格(`.artifacts/M0129/unit4-engine.log`)。
- **足した試験**: `MaterialTextureArrayGpuTests`。初めの材質の枠(1 + 256)と記述子の置き場の両方を越えて 520 個の材質を足し、最初・途中・最後の材質で描いた画素が、その材質だけを持つ別の描画器で描いた画素と完全に一致する。3 つの画素は互いに違う。束縛の段が 1 の GPU を断ることも確かめる。
- **修正前の確認**: この試験は、試験用の入口(`MaterialTextureViewsForTesting`)が単位 3 のコミットに無いので、そのままでは組み立てられない。単位 3 のコードを読むと、`AddMaterial` は空きの 256 個を使い切ると -1 を返すので、258 個目で失敗する(実行ではなく、コードを読んだ推論)。
- **途中で外れた見込み**: 初めは「基本色の表示は 8 ビットの値がそのまま出る」と見込んで期待値を書いたが、露出とトーンマップを通るので (0, 0, 40) が (0, 0, 3) になった。色の向きは合っていた。期待値を、同じ材質だけを持つ描画器の画素との完全一致に変えた。
- **意図して更新した固定値**: 合成した Stage の HLSL のハッシュを `56E4D505…` から `7DF321CA…` へ。
- **適合**: この単位は Matches。M0129 全体は Partial(単位 5〜6 が残る)。
- **未確認**: 実機の GPU、4 つのテンプレートの画像の前後比較、作り直した拡張での実画面のプレビュー。材質を足すときは、まだ GPU の完了を待つ(単位 5 で直す)。
