# M0127 — アセットエディターウィンドウとプレビューレベルテンプレート

- Date: 2026-10-27
- Status: PartiallyImplemented
- Feature: F-L8-036、関連 F-L8-035
- Intended contract: [ADR-0089](../Decisions/ADR-0089-Asset-Editor-Windows.md)、[ADR-0090](../Decisions/ADR-0090-Preview-Host-Skeletal-Loading.md)、[Asset-Editors.md](../Specifications/Asset-Editors.md)
- 前提: 未コミットの M0123（アセットワークスペース）の上に積んだ。

## 実装した範囲

- 基盤: `AssetEditorRegistry`（拡張子 → 種類）、`AssetEditorFactory`（1 つの `IVsEditorFactory`）、`AssetEditorPanes`（バイナリ用とテキストバッファー用）、`AssetEditorShell`（ツールバー・中央・Details・ステータス）、
  `AssetEditorFramePlacement` / `AssetEditorFrameFloater` / `AssetEditorOpenLocationPolicy`（既定フローティング、遅延位置決め、復元の尊重）、オプションページ「C# Game Engine / Asset Editors」。
  旧 `GlbFbxPreviewEditorFactory` / `AssetPreviewWindowPane` / `AssetPreviewControl` は削除した。シェーダーグラフは同じフロート処理を使う。シーンは中央のまま。
- エディター: Static Mesh（オービットビューポート、表示モード、統計、Details）、Texture（拡大縮小・パン・チャンネル・派生プレビュー）、Sound Wave（波形・再生・ループ）、Font（SDF アトラス・見本）、Data Asset（型に従った編集・JSON で開く）。
- プレビューレベル: `PreviewLevelTemplate`（`.cgepreview`）、標準 4 種（Default / Studio / Outdoor / Neutral、保護）、保存・複製・名前変更・削除・既定、プロジェクトごとの最後に使った選択、欠落・破損時の Default 縮退、ホストが適用できないときの Default 縮退、管理ダイアログ、ファイル変更の追従。
- ホスト: プレビュー protocol 11（`Frame.Lighting`、背景色、粗さ・金属度、`WorldGrid` の床、`EnvironmentRevision`）、`RenderPreviewLightingApplier`、`EnvironmentLightingSettings.Background`。
- スケルタル／アニメーション（ADR-0090）: ホストが ECS 非依存のソース（`Skeleton.cs`、`AnimationClip.cs`、`StageSceneDataBuilder` から切り出した `StageGltfAssets.cs`）をリンクし、マネージドの `Import` を参照して、スキン付き glTF（テクスチャ付き PBR）を自分で読み、クリップを評価してパレットとボーンの線に反映する（`RenderPreviewSkeletalAsset`）。ゲーム側の `StageSceneDataBuilder` も同じ `StageGltfAssets` を呼ぶ。
  ワイヤは protocol 11 の拡張（`Scene.Asset`、応答の `AssetSummary`、フレームの `Animation` / `ShowSkeleton` / `SelectedJoint`、検証 `RenderPreviewSkeletalValidation`）。VSIX は glTF を読まず、`GltfSkeletalProbe`（JSON だけを見る）で種類を決め、`SkeletalEditorPanel`（タイムラインとスケルトンのツリー）と `AssetAnimationTimeline`（再生状態）を持つ。
  スキン付き `.glb` は Skeleton のツリー・ボーン表示・選択ジョイントの強調・クリップ再生、アニメーションだけの `.glb` はプロジェクト内のスキン付きメッシュを選んで再生する。`Windows.Tests` は同じ型が `UserEcsProject` と重なるため、ホストを extern alias `PreviewHost` で参照する。
- 付随: Cook の `thumbnail-image` に最大辺の指定（最大 2048 px）、アセット作成ブリッジの音声要約（`WorkspaceCommand=3`）、ローカライズ（1033/1041）。
- 修正したバグ: `ThumbnailPng.Encode` が 128 px を超える画像を拒否していた。`PreviewAudio` が `InvalidDataException` を捕捉していなかった。VSIX が PreviewHost に大気などのコンピュートシェーダー成果物 7 個を載せていなかったため、Outdoor がホストエラーで失敗した（Stage だけを同梱していた）。

## 未実装・対象外

- 静的な glTF（スキンなし）のテクスチャ・PBR マップのプレビュー表示（スケルタルはテクスチャ付き。静的メッシュは形状と基本色のまま。同じホストの読み込みの上に後から載せられる）。FBX はテクスチャなしで、スケルタルではない。
- ブレンドスペース・IK・ステートマシンの再生、複数のスキンを持つ glTF（理由を表示して静的な形状だけを出す）。
- マテリアル単体・Niagara・Physics Asset・入力アクションなど、実装のない機能。

## 検証

コマンドは `dotnet test <プロジェクト> -c <構成> -nologo` で、ログは `.artifacts/AssetEditors/` にある。

| スイート | Debug | Release |
|---|---|---|
| VSExtension.Tests | 1427 合格 | 1427 合格 |
| Contracts.Tests | 70 合格 | — |
| Import.Tests | 87 合格 | — |
| CodeAnalysis.Tests | 339 合格 | — |
| FeatureRunner.Tests | 268 合格 | — |
| Bridge.Hosting.Tests | — | 42 合格 |
| Cook.Tests | — | 35 合格 |
| Windows.Tests（WARP） | 918 合格、0 失敗、2 スキップ | 914 合格、2 失敗、2 スキップ |

- Windows.Tests の Release の 2 失敗は `StageGoldenFrameTests.LogarithmicFrames_MatchTheirPinnedHashes`（environment と wireframe のハッシュ）と `VolumetricCloudGpuIntegrationTests.Clouds_Warp_WithZeroDensityRenderTheCloudlessFrameBitIdentically`（最大チャンネル差 1）。
  **Debug では同じ 2 件が合格する。** 本作業の背景クリア色の変更（`StageRenderer.BackgroundClearVector`）を一時的に無効にしても Release では同じ 2 件が失敗したため、この変更が原因ではない。
  原因は未特定で、作業ツリーにある別作業の未コミット変更（`assets/Shaders/*` と描画の変更）との関係を疑っているが、確認はしていない（仮説）。再固定していない。
- 最初の Debug の全体実行では `LumenGpuIntegrationTests.Reflections_MirrorFloorShowsTheBoxWhereGeometryPutsItAndRoughnessBlursIt` が `HRESULT=0x887A0005`（DXGI_ERROR_DEVICE_REMOVED）で 1 回失敗したが、単独再実行と、スケルタル実装後の全体再実行（918 合格、0 失敗）では合格した。WARP が間欠的にデバイスを失う既知の事象（M0111 の未解決項目）と同じ現れ方で、原因は調べていない。
- WARP 描画テストの `RenderPreviewLightingSessionTests` はライトの向き・強さ・影・背景・床が画素として現れることを確かめる。`RenderPreviewSkeletalSessionTests` はテンプレートのマネキン（`SKM_Manny_Simple.glb`、89 関節）とアニメーションだけの `MM_Idle.glb` を、ホストが自分で読んで要約（関節・クリップ・バインドしたトラック）を返し、クリップの時刻で画素が変わり、バインド姿勢が安定し、ボーンの線と選択ジョイントで画素が変わり、スキンのないメッシュが理由つきで拒否されることを確かめる。`RenderPreviewSkeletalProtocolTests` は入力と時刻・番号の範囲の検証、`AssetSkeletalModelTests` は glTF の判定（実アセットを含む）と再生状態を確かめる。VSIX 同梱テストは 7 個のコンピュート成果物がパッケージの `PreviewHost/Shaders/` に入っていることを確かめる。

### 実機（Experimental Instance の `CgeAssets` ハイブ、日本語・ダーク）

DTE（ROT）と UIA のパターンで操作し、ホストへの入力は使っていない。画面は他のウィンドウが重ならない領域を `CopyFromScreen` で撮った。

- 確認した: `Torus.glb` のダブルクリックで VS の前面にフローティングで開く。既定の 1275×876 で全面が描画される。ドロップダウンで Studio（3 灯と影の落ちる床とグリッド）、Outdoor（大気・太陽・地面と影）、Neutral（グレーの背景）、Default を切り替えられ、切り替えでモデルとカメラは維持される。
  最後に使ったテンプレートは再起動後に復元される。VS を開いたまま閉じて再起動すると、保存した位置のまま復元され全面が描画される。
- 失敗の例: Outdoor を選ぶと、PreviewHost にコンピュート成果物がなかった間は `PREVIEW_COMMAND_FAILED: The sky atmosphere requires ...Atmosphere.cgeshaderartifact` でビューポートが止まった。同梱の修正後は描画される。成果物 `final-torus-*.png`。
- スケルタル（同じハイブ、`Templates` のマネキン）: `SKM_Manny_Simple.glb` を開くと、右にスケルトンのツリー（関節 89 個、`root` から `pelvis`、`spine_01`…の階層）、下にタイムライン（クリップなしなのでバインド姿勢）が出る。「ボーンを表示」をオンにするとビューポートに骨の線が重なる（UI オートメーションの切り替えで確認。最初の実装は Click イベントだけを見ていたため切り替えが反映されず、Checked／Unchecked へ直した）。ツリーの `spine_03` を選ぶと該当の骨が水色になる。
  アニメーションだけの `MM_Idle.glb` を開くと「プレビューするメッシュ」の候補にマネキンが並んで自動で選ばれ、クリップが再生される（時刻表示が 0〜7.57 秒で進んで折り返す。ホストが評価した姿勢がビューポートで変わる）。成果物は `.artifacts/AssetEditors/skeletal-live/`。
- 画面の撮影について: 最終ビルドの実機では、私が窓を空き領域へ動かしたあとの撮影で WPF の層が白く写る状態になった。同じ状態のインスタンスで、スケルタルとは無関係の `Torus.glb` のエディターも同様に白く写り、UI オートメーションでは配置が正しく時刻表示も更新されていた（UI スレッドは生きている）。スケルタルの変更が原因ではないとみているが、原因は特定できていない（仮説：窓を動かしたあとの合成の問題、またはリモート操作中の画面状態）。そのため、最終ビルドでの全面表示のスクリーンショットはない。タイムラインの折り返し表示（WrapPanel）の見た目は UI オートメーションの配置（1 行に収まる幅）で確認した。
- 確認していない: ホストが環境を拒否したときの Default への縮退（実機では再現できる失敗がなくなったため、コードのみ。単体テストなし）、実際のオービット・パン・ドリーのマウス操作（式は単体テストのみ）、スクラブとジョイント選択のマウス操作（UI オートメーションでの選択は確認）、プレビューするメッシュを手で切り替えたときの読み込み直し、Texture / Sound / Font / Data Asset の各エディターの実機操作、
  ドッキングとドッキング後の再アクティブ化、設定を「ドキュメントタブ」にした場合、英語 UI、高コントラスト、150% / 200% DPI、Release ビルドの VSIX の実機。

## 既知の事項

- Default テンプレートは従来のホスト固定の +Y 向きライトを維持する。実機では上面が平坦なグレーで、穴の内側など下面が明るく見える。これは従来の描画と同じ（ホストのテストで固定）で、不自然に見える場合は Studio を使う。向きを改めるかどうかは製品判断。
- `build/CSharpGameEngine.Version.props` は作業ツリーで 0.1.39 になっているが、本作業では意図して変更していない（別作業の変更とみられる）。SDK の内容が変わる（Contracts の protocol 11）ため、リリース前に確認が必要。

## 次の作業

1. 未確認の実機項目（上記）を専用ハイブで確認する。窓を動かさずに全面の画面を撮れる環境で、スケルタルのタイムラインとツリーの見た目も撮る。
2. 静的な glTF のテクスチャ付き表示を、スケルタルと同じホストの読み込みの上に載せるか検討する。
