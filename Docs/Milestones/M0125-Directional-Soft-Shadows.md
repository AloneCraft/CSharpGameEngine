# M0125 — 太陽の半影と同条件の左右比較

- 日付: 2026-10-26
- Feature: F-L4-020
- Decision: [ADR-0083](../Decisions/ADR-0083-Directional-Soft-Shadows.md)
- 状態: Implemented / TestVerified / RuntimeVerified。Conformance: MatchesWithDocumentedExtension。

## 実装
`DirectionalShadowSettings` と `DirectionalShadowRendering` に opt-in の SoftShadows、SourceAngularDiameterDegrees (0.5357 度)、MaximumSoftShadowRadiusMeters (2 m) を追加する。環境フレームの保存・再生を経由し、Stage ABI stage-v15 の FrameCB 末尾の float4 で渡す。ページの深度は変えず、CPU のページ要求の halo を最大フィルタ半径と双線形参照分だけ広げる。

Shader Graph の正本を maintenance の編集スクリプトで更新し、外部 subgraph を再抽出する。CSM と仮想の双方が共通の PCSS (中心+32 探索、64 フィルタ、同じ disk、世界距離のバイアス、受光面の深度勾配、双線形比較) を呼ぶ。半影半径は平均遮蔽物間隔 × tan(角直径/2)。0 度は共通の硬い比較、未指定の旧シーンは以前の PCF。仮想参照が不足すると受光点全体を CSM の共通 PCSS へ渡す。

比較シーンは左右で同じ 2 度・2 m 上限を共有する。2 度は観察しやすい見本の値。S で 0/2 度、4/5 で ±0.5 度。HUD と capture JSON に値を表示する。床から下面まで 0.3 m / 4.9 m の板を追加し、距離による半影の違いを見る。

## 制約
深度マップからの PCSS 近似であり、太陽円盤の幾何学的な厳密積分ではない。探索と半影半径には設定上限がある。有限サンプルのむらや、深度マップが表現できない遮蔽は残る。フィルタも物理アトラスの描画時間とは別にメインのピクセル描画コストを使うため、既存の shadow GPU timestamp は PCSS 全体の時間ではない。

## 検証
- Windows.Tests 関連回帰: Debug 39/39 (`regression-debug.trx`)。その後、ソフトシャドウ有効の比較・復旧を追加した最終 Debug の VirtualShadowGpuTests と EnvironmentComponentTests は 23/23 (`final-soft-debug.trx`)。Release の全関連回帰は 40/40 (`regression-release.trx`)。既定の標準フレーム基準、方向光、局所光、GI、Radial、リサイズ、デバイス復旧を含む。テストソースの除外は行っていない。
- CPU ページ・品質設定は Debug / Release とも 21/21 (`pages-*.trx`)。大きなフィルタ halo の割当、既存深度の再利用と停止後の再描画 0、角直径と半径の不正値を含む。
- ShaderGraphCompilerTests は Debug / Release とも 14/14 (`graph-*.trx`)。Stage 18 entry と ABI、Model/Triangle、決定的な成果物と source map、外部 subgraph、合成 HLSL SHA を確認した。CLI の一時成果物が sandbox の既定 TEMP に書けず最初は 3 件失敗したが、TEMP/TMP を `.artifacts/soft-shadows/temp` に指定して同じテストが通った。テストの期待値は弱めていない。
- WARP の半影計測 (384 px、境界幅は 16 行の遷移画素数 / 16): CSM は角直径 0 度で 3 px、8 度/高さ 2 m で 9 px、8 度/6 m で 24 px、2 度/6 m で 6 px。仮想は順に 0、8、25、6 px。Radial の仮想も一致。これは sRGB 輝度からの 12–88% 遷移の測定であり、厳密な光量の半影幅ではない。生の値は `tests-*/soft-shadow-*.json`。
- Release 比較ビルドは警告 0・エラー 0。RTX 4060 Ti で 24 フレーム、P@8-9 の固定時間・固定カメラの停止状態で `comparison-hard` (0 度)、`comparison-soft` (2 度)、`comparison-four-degrees` (4 度) の png/json を保存した。すべて D3D12 エラー 0、不足したページ要求 0。652 常駐・652 再利用・再描画 0。4 度は 5 キーの入力で左右同時に変更した。
- 最終 Release 実行ファイルを実機で対話起動し、ウィンドウと HUD を確認した。移動物体の状態は 652 常駐、3 再描画、649 再利用、不足要求 0。

## シーンの増分ビルド
最初の実画面撮影で、既存出力に `CgeShadowComparison=true` を付けても通常 showcase の古い DLL が残る問題を確認した。比較ビルド時に `CGE_SHADOW_COMPARISON` を DefineConstants へ入れ、追加 JSON の選択変更をコンパイラの入力キャッシュへ含める。再ビルド後は比較画面になった。比較 → 通常の同じ Debug 出力の切替もビルド・実行し、JSON の Cascaded / comparison=false / softShadows=false を確認してから比較へ戻した。通常/比較の起動手順は変わらない。

すべての証拠は `.artifacts/soft-shadows/`。実行ファイルは `tests-release/UserEcsProject.exe`。GPU 時刻の値から PCSS の速度向上は主張しない。

`comparison-evidence.json` は 3 撮影の同じカメラ配列・同じオブジェクト数・0 エラー・0 不足要求を検査し、画像と Release Stage artifact の SHA-256、生の Debug/Release 半影測定をまとめる。

実行したテストコマンドの構成は次のとおり (Config は Debug / Release、OutDir は各設定の `tests-debug/` / `tests-release/` の絶対パス)。結果は前掲の TRX と log に保存する。

```powershell
dotnet test tests/CSharpGameEngine.Rendering.Tests/CSharpGameEngine.Rendering.Tests.csproj -c $Config --no-restore -m:1 -nr:false -p:OutDir=$OutDir --filter 'FullyQualifiedName~VirtualDirectionalShadowTests|FullyQualifiedName~RenderQualityProfileTests'
dotnet test tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj -c $Config --no-restore -m:1 -nr:false -p:OutDir=$OutDir --filter 'FullyQualifiedName~VirtualShadowGpuTests|FullyQualifiedName~DirectionalShadowGpuTests|FullyQualifiedName~DirectionalShadowCascadesTests|FullyQualifiedName~AreaEmissiveShadowGpuIntegrationTests|FullyQualifiedName~StandardModeGoldenFrameTests|FullyQualifiedName~LumenGpuIntegrationTests|FullyQualifiedName~EnvironmentComponentTests|FullyQualifiedName~RadialViewProjection_SurvivesDeviceRecovery'
dotnet test tests/CSharpGameEngine.ShaderGraph.Cli.Tests/CSharpGameEngine.ShaderGraph.Cli.Tests.csproj -c $Config --no-restore -m:1 -nr:false -p:OutDir=$OutDir --filter FullyQualifiedName~ShaderGraphCompilerTests
```
