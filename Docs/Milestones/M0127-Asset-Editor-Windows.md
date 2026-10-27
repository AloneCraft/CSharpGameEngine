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
- ホスト: プレビュー protocol 13（`Frame.Lighting`、背景色、粗さ・金属度、`WorldGrid` の床、`EnvironmentRevision`）、`RenderPreviewLightingApplier`、`EnvironmentLightingSettings.Background`。
- スケルタル／アニメーション（ADR-0090）: ホストが ECS 非依存のソース（`Skeleton.cs`、`AnimationClip.cs`、`StageSceneDataBuilder` から切り出した `StageGltfAssets.cs`）をリンクし、マネージドの `Import` を参照して、スキン付き glTF（テクスチャ付き PBR）を自分で読み、クリップを評価してパレットとボーンの線に反映する（`RenderPreviewSkeletalAsset`）。ゲーム側の `StageSceneDataBuilder` も同じ `StageGltfAssets` を呼ぶ。
  ワイヤは protocol 11 の拡張（`Scene.Asset`、応答の `AssetSummary`、フレームの `Animation` / `ShowSkeleton` / `SelectedJoint`、検証 `RenderPreviewSkeletalValidation`）。VSIX は glTF を読まず、`GltfSkeletalProbe`（JSON だけを見る）で種類を決め、`SkeletalEditorPanel`（タイムラインとスケルトンのツリー）と `AssetAnimationTimeline`（再生状態）を持つ。
  スキン付き `.glb` は Skeleton のツリー・ボーン表示・選択ジョイントの強調・クリップ再生、アニメーションだけの `.glb` はプロジェクト内のスキン付きメッシュを選んで再生する。`Windows.Tests` は同じ型が `UserEcsProject` と重なるため、ホストを extern alias `PreviewHost` で参照する。
- 付随: Cook の `thumbnail-image` に最大辺の指定（最大 2048 px）、アセット作成ブリッジの音声要約（`WorkspaceCommand=3`）、ローカライズ（1033/1041）。
- 修正したバグ: `ThumbnailPng.Encode` が 128 px を超える画像を拒否していた。`PreviewAudio` が `InvalidDataException` を捕捉していなかった。VSIX が PreviewHost に大気などのコンピュートシェーダー成果物 7 個を載せていなかったため、Outdoor がホストエラーで失敗した（Stage だけを同梱していた）。

## 未実装・対象外

- 静的 glTF の実画面でのテクスチャ表示と複数ノードのカメラ・床の確認。FBX はテクスチャなしで、スケルタルではない。
- ブレンドスペース・IK・ステートマシンの再生、複数のスキンを持つ glTF（理由を表示して静的な形状だけを出す）。
- マテリアル単体・Niagara・Physics Asset・入力アクションなど、実装のない機能。

## 検証

コマンドは `dotnet test <プロジェクト> -c <構成> -nologo` で、ログは `.artifacts/AssetEditors/` にある。

VSIX 全体はフォントブリッジ修正後の Debug／Release で確認した。ブリッジ全体と VSIX 同梱物の確認は下記に記載する。新規ボタンと縮退動作の実画面操作は未確認。

| スイート | Debug | Release |
|---|---|---|
| VSExtension.Tests | 1452 合格 | 1452 合格 |
| Contracts.Tests | 71 合格 | — |
| Import.Tests | 87 合格 | — |
| CodeAnalysis.Tests | 339 合格 | — |
| FeatureRunner.Tests | 268 合格 | — |
| Bridge.Hosting.Tests | 44 合格 | 44 合格 |
| Cook.Tests | — | 35 合格 |
| Windows.Tests（WARP） | 918 合格、0 失敗、2 スキップ | 914 合格、2 失敗、2 スキップ |

- Windows.Tests の Release の 2 失敗は `StageGoldenFrameTests.LogarithmicFrames_MatchTheirPinnedHashes`（environment と wireframe のハッシュ）と `VolumetricCloudGpuIntegrationTests.Clouds_Warp_WithZeroDensityRenderTheCloudlessFrameBitIdentically`（最大チャンネル差 1）。
  **Debug では同じ 2 件が合格する。** 本作業の背景クリア色の変更（`StageRenderer.BackgroundClearVector`）を一時的に無効にしても Release では同じ 2 件が失敗したため、この変更が原因ではない。
  原因は未特定。この比較だけでは他の変更との関係までは特定できない。期待ハッシュは再固定していない。
- 追加比較: 作業前コミット `9cf5da07` を独立ディレクトリへ展開して同じ Release / WARP 条件で実行すると、黄金画像は environment `6C53BA39C27FABDF658F2AE9E673D31ECF46B175E716ADE356FFAD1C2024E8BB`、wireframe `B67AC8E7AEF503FDF20DB55056778374B71591E6A6BD1F487D976E2FA04E502C` で不一致だった。両ハッシュは現在の実行と同一。雲の旧テストは最大1階調差を許容して合格したが、比較コピーだけで完全一致条件に変えると最大差1で失敗した。この環境では両差分が作業前から再現するが、根本原因は未特定。旧テストの原本は比較ディレクトリに保存した。
- 最初の Debug の全体実行では `LumenGpuIntegrationTests.Reflections_MirrorFloorShowsTheBoxWhereGeometryPutsItAndRoughnessBlursIt` が `HRESULT=0x887A0005`（DXGI_ERROR_DEVICE_REMOVED）で 1 回失敗したが、単独再実行と、スケルタル実装後の全体再実行（918 合格、0 失敗）では合格した。WARP が間欠的にデバイスを失う既知の事象（M0111 の未解決項目）と同じ現れ方で、原因は調べていない。
- WARP 描画テストの `RenderPreviewLightingSessionTests` はライトの向き・強さ・影・背景・床が画素として現れることを確かめる。`RenderPreviewSkeletalSessionTests` はテンプレートのマネキン（`SKM_Manny_Simple.glb`、89 関節）とアニメーションだけの `MM_Idle.glb` を、ホストが自分で読んで要約（関節・クリップ・バインドしたトラック）を返し、クリップの時刻で画素が変わり、バインド姿勢が安定し、ボーンの線と選択ジョイントで画素が変わり、スキンのないメッシュが理由つきで拒否されることを確かめる。`RenderPreviewSkeletalProtocolTests` は入力と時刻・番号の範囲の検証、`AssetSkeletalModelTests` は glTF の判定（実アセットを含む）と再生状態を確かめる。VSIX 同梱テストは 7 個のコンピュート成果物がパッケージの `PreviewHost/Shaders/` に入っていることを確かめる。
- protocol 11 の契約テストでは旧版 10 の要求をアセット読み込み前に拒否することも検証した。ソリューション登録検証は `.artifacts` 内の保存済みソースコピーを候補から除外する修正後、5 件合格（`final-registration-debug.log`）。
- 文書名変更でスケルタル入力に旧パスが残る不具合を修正した。開いている文書自身のメッシュ／クリップ参照だけを新名へ付け替え、外部のプレビューメッシュと空のクリップ指定は保持する。アニメーション用パネルも新パスを保持するため、後からメッシュを選び直しても旧クリップへ戻らない。3件の回帰ケースを追加し、最新x64 Debug／ReleaseのVSIX全体が各1430件合格（`retarget-vsix-full-debug.log`、`retarget-vsix-full-release.log`）。
- 最終の `dotnet test CSharpGameEngine.slnx -c Debug -m:1 -nr:false -p:UseSharedCompilation=false --no-restore -nologo` は VSIX の 1427 件合格まで実行した。登録検証が修正前に 1 件失敗したため全体成功ではない。Windows 描画スイートの重複再実行は途中で停止し、生成した x64 Debug 成果物を `--no-build --no-restore -p:Platform=x64 --filter FullyQualifiedName~RenderPreviewSkeletalSessionTests` で検証して 4 件合格した（`final-solution-debug-normal.log`、`final-skeletal-x64-debug.log`）。登録検証の修正後の結果は上記の別実行で確認した。

### 実機（Experimental Instance の `CgeAssets` ハイブ、日本語・ダーク）

DTE（ROT）と UIA のパターンで操作し、ホストへの入力は使っていない。画面は他のウィンドウが重ならない領域を `CopyFromScreen` で撮った。

- 確認した: `Torus.glb` のダブルクリックで VS の前面にフローティングで開く。既定の 1275×876 で全面が描画される。ドロップダウンで Studio（3 灯と影の落ちる床とグリッド）、Outdoor（大気・太陽・地面と影）、Neutral（グレーの背景）、Default を切り替えられ、切り替えでモデルとカメラは維持される。
  最後に使ったテンプレートは再起動後に復元される。VS を開いたまま閉じて再起動すると、保存した位置のまま復元され全面が描画される。
- 失敗の例: Outdoor を選ぶと、PreviewHost にコンピュート成果物がなかった間は `PREVIEW_COMMAND_FAILED: The sky atmosphere requires ...Atmosphere.cgeshaderartifact` でビューポートが止まった。同梱の修正後は描画される。成果物 `final-torus-*.png`。
- スケルタル（同じハイブ、`Templates` のマネキン）: `SKM_Manny_Simple.glb` を開くと、右にスケルトンのツリー（関節 89 個、`root` から `pelvis`、`spine_01`…の階層）、下にタイムライン（クリップなしなのでバインド姿勢）が出る。「ボーンを表示」をオンにするとビューポートに骨の線が重なる（UI オートメーションの切り替えで確認。最初の実装は Click イベントだけを見ていたため切り替えが反映されず、Checked／Unchecked へ直した）。ツリーの `spine_03` を選ぶと該当の骨が水色になる。
  アニメーションだけの `MM_Idle.glb` を開くと「プレビューするメッシュ」の候補にマネキンが並んで自動で選ばれ、クリップが再生される（時刻表示が 0〜7.57 秒で進んで折り返す。ホストが評価した姿勢がビューポートで変わる）。成果物は `.artifacts/AssetEditors/skeletal-live/`。
- 画面の撮影について: 最終ビルドの実機では、私が窓を空き領域へ動かしたあとの撮影で WPF の層が白く写る状態になった。同じ状態のインスタンスで、スケルタルとは無関係の `Torus.glb` のエディターも同様に白く写り、UI オートメーションでは配置が正しく時刻表示も更新されていた（UI スレッドは生きている）。スケルタルの変更が原因ではないとみているが、原因は特定できていない（仮説：窓を動かしたあとの合成の問題、またはリモート操作中の画面状態）。そのため、最終ビルドでの全面表示のスクリーンショットはない。タイムラインの折り返し表示（WrapPanel）の見た目は UI オートメーションの配置（1 行に収まる幅）で確認した。
- 確認していない: ホストが環境を拒否したときの Default への縮退（復旧方針の単体テストは追加済み。描画面再生成と初回描画を含む実機受入は未確認）、実際のオービット・パン・ドリーのマウス操作（式は単体テストのみ）、スクラブとジョイント選択のマウス操作（UI オートメーションでの選択は確認）、プレビューするメッシュを手で切り替えたときの読み込み直し、Texture / Sound / Font / Data Asset の各エディターの実機操作、
  ドッキングとドッキング後の再アクティブ化、設定を「ドキュメントタブ」にした場合、英語 UI、高コントラスト、150% / 200% DPI、Release ビルドの VSIX の実機。Default縮退の復旧方針は後述の単体テストを追加したが、描画面再生成・初回描画を含む実機受入は未確認。
- 追加の実画面確認では、専用CgeAssetsインスタンスを非表示で起動したが、computer-useのウィンドウ一覧に撮影対象が公開されなかった。前面化やホスト入力は行わず、所有インスタンスをDTEで通常終了した。この試行はUnavailableで、白い表示の解消を示す証拠には数えない。
- 外部DTEからの `QueryService(IVsUIShellOpenDocument)` と `OpenDocumentViaProject` 後の `ShowNoActivate` はそれぞれS_OKを返した（`sdk-ui-probe.log`、`sdk-ui-open.log`）。ただし非表示のメインシェルでは撮影対象が公開されず、コンテンツの実画面は確認できなかった。フレーム表示のHRESULTだけでは描画済みとは判定しない。

## 既知の事項

- 空の `.cgefont` や不正なフォントプレビュー要求で、入力検証の `InvalidDataException` が診断応答へ変換されずブリッジ処理から漏れる不具合を修正した。空ファイルでは修正前の例外を再現し、修正後は相関 ID と `ASSET_FONT_PREVIEW_SIZE_INVALID` を持つ失敗応答を返す。ブリッジ全体は Debug／Release 各44件合格。VSIX 同梱の Debug／Release ブリッジも空フォントで診断を返し、元ファイルを変更しないことを確認した。サンドボックス内の全体実行は5件失敗し、原因を特定せず通常ユーザー環境で再実行した。
- テンプレート選択時、同期的なホスト失敗や Default への縮退通知が届いた後に「適用」表示で上書きされる不具合を修正した。選択表示をホスト呼び出し前に更新し、後から届く失敗・縮退の表示を保持する。VS SDK モック上の実際の WPF 選択と偽ホストで、修正前は `Preview level: Studio` に上書きされる失敗を再現し、修正後は成功・失敗・縮退の3ケースが Debug／Release ともに合格した。既存の復旧・保存を含む対象29件も両構成で合格した。縮退イベントはテストで模擬しており、実ホストでの縮退は未確認。
- 実ホストの復旧に必要な描画側の動作を非表示 WARP サーフェスで確認した。大気シェーダーがない環境で描画すると `PREVIEW_COMMAND_FAILED` が返り、そのセッションは破棄される。別のサーフェスで Default 相当の環境を使う新セッションを開くと、初回フレームの取得に成功した。照明関連11件は Debug / Release ともに合格した。このテストは VSIX が自動的にサーフェスを作り直す一連の動作を検証するものではなく、実画面受入は引き続き未確認。
- 使用中のテンプレートファイルが破損したとき、警告だけで旧内容を維持していたため、組み込み Default へ戻して理由を表示するよう変更した。削除時も他のプロジェクト既定ではなく組み込み Default を使う。WPF 選択欄と一時ファイルを使った再現では修正前に破損時が失敗し、修正後は削除・破損の両方で Default、警告、保存済み選択と既定値の維持を確認した。関連32件と VSIX 全体1,454件は Debug / Release ともに合格。VS 実画面は未確認。
- 背景モードに未定義の数値が指定された JSON を誤って受け入れる問題を修正した。修正前に読み込みの失敗を再現し、修正後は理由付きで拒否され、使用中のファイルなら Default に戻る。テンプレート検証・保存・選択の関連69件が Debug / Release で合格。
- 方向ライトでは位置が不要だが、変換時に位置配列を無条件で参照していたため、検証済みテンプレートの `null` または空の位置で例外になっていた。点・スポットライトだけで位置を計算するよう修正し、関連71件が Debug / Release で合格した。
- 初期カメラの方位角に非常に大きい有限値を設定すると、ラジアン変換の途中でオーバーフローしていた。角度を一周に正規化してから変換するよう修正し、`float` の正負の端と通常の操作を含む関連71件が Debug / Release で合格した。
- VS SDK モックと2つのビューポートで、片方のテンプレートを床付きに切り替えても両方のカメラともう一方のテンプレートが維持されること、送信シーンが現在のカメラと既存メッシュを使うこと、床が変わらない照明変更ではシーン置き換えが発生しないことを確認した。関連29件は Debug / Release で合格。実ホストの再読み込みや VS 実画面の操作をこのテストで確認したものではない。
- プレビューメッシュ変更・文書再読み込み時に、旧メッシュのスケルトンと再生状態が残る問題を修正した。変更前に再生中の状態が残ることを再現し、変更後は旧情報を消し、新しい要約を受け取ると表示と再生を再構築する。VS SDK モックを使う関連22件と VSIX 全体1,462件が Debug / Release で合格。VS 実画面は未確認。
- 音声を再読み込みした直後に前の時刻表示と波形の再生位置が残っていたため、読み込み開始時に両方をゼロへ戻す。修正前の再現テストと、修正後の関連46件を Debug / Release で確認した。再生中の VS 実画面での切り替えは未確認。
- アセット詳細欄を在庫コントローラーの生成前に開くと、後から生成されても接続されない問題を修正した。コントローラーの生成・交換・終了時に購読先を更新し、詳細欄の破棄時に通知を解除する。修正前に未接続を再現し、修正後は VS SDK モックで接続状態と表示を確認した。VSIX 全体1,464件が Debug / Release で合格。VS 実画面は未確認。
- 詳細欄が在庫外の対象へ切り替わると、非表示の Inspector に旧選択が残っていた。対象がないときは選択を解除し、旧バッファー監視と非同期処理の世代を更新する。修正前に旧選択の残存を再現し、修正後は関連11件と VSIX 全体1,465件が Debug / Release で合格した。VS 実画面は未確認。
- 音声 Inspector の失敗通知が選択解除後に遅れて届くと、旧アセットのエラーを表示に追加していた。通知の選択世代を確認し、現在の音声のエラーは表示しつつ旧通知は無視する。WPF の通知をディスパッチする再現テストで変更前の混入を確認し、関連5件が Debug / Release で合格。VS 実画面は未確認。
- データアセットの保存で文書表が保存対象を返さない場合、実際には保存していないのに成功扱いになっていた。対象不在・保存非対応時は理由を返し、通常の保存とキャンセルを区別する。文書表のスタブで修正前の誤成功を再現し、関連13件が Debug / Release で合格。VS の実バッファーと保存操作は未確認。
- データアセットの共有テキストバッファーは名前変更されても表示側のパスが更新されない問題を修正した。ペインが文書表を購読し、自分の文書のモニカー変更で表示内容を新しいパスへ向け、破棄時に購読を解除する。VS SDK モックと文書表スタブで名前変更・無関係な通知・購読と解除を確認し、保存や詳細欄を含む関連18件と VSIX 全体1,472件が Debug / Release で合格。実際の VS 文書表からの通知と保存は未確認。
- 作業ツリーのソリューション登録を `SolutionProjectRegistrationSpec` で再確認し、5件が Debug / Release で合格した。`git diff --check` は成功し、改行コードに関する警告は残っている。
- 静的 glTF の先頭プリミティブ事前読み込みを廃止し、ホストの描画ノード全体の境界とユニークなプリミティブ数を利用する。先頭が点描画で後続が三角形のfixtureは旧単一プリミティブ読み込みでは失敗するが、ホストは後続2プリミティブを読み、複数ノードへの参照を重複計上しない。描画・通信対象 Debug／Release 各14件と VSIX の分類・構成・日英リソース対象 Debug／Release 各43件が合格。実画面の受入は未確認。
- protocol 12 で静的 glTF のパスをホストに渡し、ゲームと共通の `StageGltfAssets.AppendStatic` でノード変換・UV・テクスチャ・PBR を組み立てる。床変更時は読み込み済みCPUデータを再利用する。全ノードの変換済み境界をホストから返して初回カメラ・床・照明に反映し、復旧時のカメラを維持する。Debug の WARP・ゲーム変換対象11件、名前付きパイプ・セッション対象6件、契約72件、VSIX対象58件と、Release の WARP・ゲーム変換・通信対象17件、VSIX対象58件が合格。複数ノードの境界は移動・拡縮した2ノードのfixtureで確認した。実画面は未確認。
- protocol 13 ではスケルタルのレスト姿勢の境界と統計をホストから返し、初回カメラ・床・照明に使う。先頭プリミティブが点描画でも後続のスキンをホストが扱う WARP テスト、複数関節と剛体ノードの境界計算、パイプ転送を含む対象16件が Debug / Release で合格。契約72件と VSIX 全体1,452件も両構成で合格。アニメーションの全姿勢を囲む境界は保証しない。今回の変更後の VS 実画面は未確認。
- 管理画面には複製しかなく、仕様にある「新規」が欠けていた。`PreviewLevelStore.Create` は Default から独立したユーザーファイルを作り、管理画面の「新規…」から名前を指定できる。初期値、永続化、一意なID、空名拒否と日本語・英語リソースを Debug／Release 各対象29件で確認した。実画面は未確認。
- 同一パスの`ReloadDocData`が早期終了して内容を読み直さない不具合を修正した。初回の重複ロード抑止は維持し、明示的再読み込みだけは各コンテンツの再読み込みへ渡す。VS SDKモック上で実際のPaneを生成する`AssetEditorReloadTests`で確認し、基盤・スケルタルと合わせて対象47件が合格した。共通ツールバーの移動先は現在の文書パスを参照し、画像の再読み込み中・失敗時は旧画像を隠し、音声の旧要約時間をリセットする。初期ステータスも共通枠へ引き継ぐ。最終VSIX全体はx64 Debug／Releaseで各1441件合格（`reload-lifecycle-debug.log`、`reload-vsix-full-debug.log`、`reload-vsix-full-release.log`）。これらの再読み込みの実画面受入は未確認。
- Assetsのグラフ作成が未同梱の`Model.cgeshadergraph`を参照する不具合を修正した。`PlanStageBundle`が現行Stageと依存サブグラフをメモリー上で計画し、既存ウィザードとID再割り当てを共有する。依存循環の検出をキャッシュより先に行い、循環を見逃す経路も修正した。`CreateBundle`は全作成先の衝突・プロジェクト境界を先に検査し、依存ファイルからルートの順に作成して1つのネイティブUndo取引へ載せる。途中失敗時は取引を明示的にCancelし、Undo登録そのものが失敗した直後の操作も戻す。実機での作成ダイアログ・一式のUndo／Redo・途中失敗時の復旧は未確認。
- グラフ作成の検証: 計画時に作成先へ書かないこと、IDと参照の整合、循環拒否、同梱Stage一式を同梱コンパイラーで成果物へビルドできることを`AssetGraphBundleTests`で確認する。同一依存パスを異なるIDで参照すると、修正前は例外を出さず失敗した（`graph-bundle-identity-before.log`）。キャッシュ済みの依存についても元のIDを照合する修正後、対象4件とVSIX全体がx64 Debug／Releaseで各1445件合格した。独立CLIでも生成した6ファイルのvalidateとapphost経由のbuildが成功した（`graph-bundle-cli-validate.log`、`graph-bundle-cli-build-exe.log`）。`dotnet <cli.dll>`による最初のbuild試行はDXCのDLL解決に失敗したため、エディターと同じapphost実行で検証し直した。そのDLL起動経路の原因調査・対応は行っていない。
- Defaultへの再読み込みが失敗しても縮退成功を通知する経路を修正した。`LoadIntoSurface`は成功値を返し、Openだけでなく最初の照明／フレーム適用と描画まで成功してからReadyと縮退成功を通知する。初回読み込みでホストが失敗した場合も同じ復旧方針へ渡す。Defaultの失敗診断は元のエラーで上書きせず、停止後のテンプレート選択は明示的な再読み込みとして扱う。`AssetViewportRecoveryTests`は成功時だけの通知、通知順序、読み込み失敗、新しい描画面での自動復旧1回、明示的リセット、Default再試行の抑制を検証する。サイズ変更キューと合わせて対象10件がDebugで合格（`recovery-outcome-debug.log`）。
- 上記の最終コードでVSIX全体はx64 Debug／Releaseで各1440件合格（`recovery-vsix-full-debug.log`、`recovery-vsix-full-release.log`）。これは復旧方針と契約の検証であり、実画面での縮退・初回描画の受入を代替しない。
- 実画面に現れたBox／Torusのメタデータエラーは、該当`.meta`が旧形式の`guid:`であることを確認した。`AssetWorkspaceService`は現行のJSONメタデータを検査し、旧形式は`AssetAuthoringService.TryMigrateLegacy`の明示的移行対象となる。今回の確認ではアセットファイルを変更していない。
- サイズ変更時のホスト例外がWin32コールバックへ漏れ、VS自体を終了させる不具合を実機で再現した（OSイベント1026、`resize-unhandled-before.json`）。`AssetViewportHost`のResizedハンドラーから同期通信を除き、`AssetViewportResizeQueue`へ最新サイズだけを保存し、例外処理のある描画tickで適用するよう修正した。キューと既存ホストクライアントの対象9件がDebugで合格（`resize-boundary-debug.log`）、修正後のVSIX全体はx64 Debug／Releaseで各1435件合格（`resize-vsix-full-debug.log`、`resize-vsix-full-release.log`）。同じ可視化手順で修正版VSが生存することは確認したが、画面の正常描画までは証明していない。
- 非表示起動した検証用VSを可視化する診断では、メインウィンドウへのNOACTIVATE・NOZORDER指定時は前面ウィンドウが不変だったが、DTEのVisible更新を含む手順全体では不変性を満たさなかった（`resize-fixed-ui-visible.log`）。この方法は非干渉の受入を満たさないため、それ以上の可視化操作を停止した。診断用VSは有効状態へ戻して通常終了した。操作中の撮影は全面の正常描画を示しておらず、白い表示の解消とは扱わない。
- スケルタルの床切り替え時の再インポートを修正した。同じソースパスならセッション内のCPUデータと姿勢を再利用し、床の有無に合わせて描画番号を再配置する。元のメッシュ・アニメーションファイルを読み込み後に削除してから床を追加・削除し、同じ姿勢・ボーンの画素が保たれる回帰テストを追加した。スケルタル5件と既存の照明・床10件、計15件がx64 Debug／Releaseで各合格（`reuse-skeletal-debug.log`、`reuse-skeletal-release.log`）。GPUリソースは描画サーバーとともに作り直す。
- Default テンプレートは従来のホスト固定の +Y 向きライトを維持する。実機では上面が平坦なグレーで、穴の内側など下面が明るく見える。これは従来の描画と同じ（ホストのテストで固定）で、不自然に見える場合は Studio を使う。向きを改めるかどうかは製品判断。
- `build/CSharpGameEngine.Version.props` は作業ツリーで 0.1.39 になっているが、本作業では意図して変更していない（別作業の変更とみられる）。SDK の内容が変わる（Contracts の protocol 13）ため、リリース前に確認が必要。

## 次の作業

### 2026-10-27 ユーザー受入からの修正

- ユーザー確認: ドッキングは合格。浮動ウィンドウの前面維持、「ソリューション エクスプローラーで表示」、「Assetsで表示」は不合格。添付画面のMM_Idle.glbには、現在のゲームプロジェクト外である旨が表示されていた。この確認は時間経過を示さないため、アニメーション再生の合格とは数えない。
- 原因と対応: `FrameMode=Float`は前面維持の所有関係を設定しない。`AssetEditorFloatingOwner`で同じプロセスのVS本体を浮動ルートHWNDの所有者にし、表示・Loaded・ソース変更・ドッキング変更時に更新する。位置決めとは独立させ、復元時にも所有関係だけを適用する。ドッキング中の本体や他アプリのHWNDを変更せず、Topmost・前面化を使わない。イベントはペインの破棄時に解除する。シェーダーグラフにも適用する。
- 旧ツールバーはAssets登録一覧にない文書で無通知終了していた。Solution Explorerへの移動は`IVsUIShellOpenDocument.IsDocumentInAProject`から実際の階層・項目を解決し、親を展開して対象を選択する。Assetsへの移動は対象のプロジェクト・フォルダーへ移動し、名前・種類・提供元・状態のフィルターを解除して選択位置へスクロールする。プロジェクト外・一覧外・準備中・失敗は共通枠に理由を表示する。ファイルを自動追加しない。
- 検証: `AssetEditorFloatingOwnerTests`は非表示のネイティブウィンドウで所有関係、ドッキング時の無変更、既存の拡張スタイルと前面ウィンドウの維持、複数ウィンドウの独立を確認する。`AssetEditorNavigationTests`は隠れる全フィルターの解除、Assets登録なしでのVS階層選択、対象外の診断、実際のツールバーボタンからの未準備通知を確認する。SDKの呼び出しはモックで、実VSのUI受入とは区別する。
- 実行結果: `dotnet test tests/CSharpGameEngine.VSExtension.Tests/CSharpGameEngine.VSExtension.Tests.csproj -c Debug -p:Platform=x64 -p:DeployExtension=false -p:BuildProjectReferences=false --no-restore`で1480件合格。Releaseは依存先のビルド・復元を含む同じプロジェクトの`-c Release -p:Platform=x64 -p:DeployExtension=false`で1480件合格。ログは`.artifacts/AssetEditors/owner-navigation-full-debug.log`、`owner-navigation-full-release.log`。初回の復元省略ではAutomationの既存資産にRIDがなくNETSDK1047で停止したが、通常の復元後に通った。テスト用COMプロキシのout引数の配列コピーも修正した。
- Debug／Releaseの生成VSIX内DLLと各ビルド済みDLLのSHA-256は一致。専用ハイブへの差し替え・修正後の実画面確認はユーザーが行う。プロジェクト内のTorus.glbと、一覧にないMM_Idle.glbの診断を分けた手順は`.artifacts/AssetEditors/owner-navigation-recheck.md`。実画面での再合格まではF-L8-036をPartiallyImplemented／Conformance Partialのまま維持する。
- その後の明示依頼により、AIが修正版をCgeAssetsへインストールし、`/updateconfiguration`成功後にUserEcsProject.slnxを可視起動した（PID 42036、2026-10-27 13:19:58 JST）。インストール済みDLLのSHA-256は`F0FD8E0072ECA0D9029CED5622D6935BF0044ED1E45F8927AC0A5028C371D988`で検証済みDebug VSIXと一致。通常VSとCgeFixedは操作対象外。
- 実画面で復元されたMM_Idle.glbの89関節モデルの再生時間の進行・ループと描画を確認した。VS本体のメニューのクリックと本体の明示的なアクティブ化後にも、非アクティブの浮動エディターが本体の上に残った（`.artifacts/AssetEditors/live-owned-above-active-ide.png`）。これは復元時の前面維持の確認であり、ドッキング往復後の前面維持と2つの表示ボタンはまだ受入未完了。
- Windows操作ヘルパーの`Computer Use helper already has an active request`とクリックのタイムアウトが発生した。別の公開UI接続でも対象を再観測し、共有デスクトップの入力が混在する状態を確認したため、確認用ファイルダイアログをEscapeで閉じ、入力占有の調整待ちとした。文字列の内容は記録しない。インストールログ・起動記録・現在の合否は`.artifacts/AssetEditors/owner-navigation-cgeassets-install.log`、`owner-navigation-cgeassets-launch.json`、`owner-navigation-live-result.json`。
- 入力占有の返答後、同じCgeAssetsでMM_Idle.glbをタブの移動メニューからメインタブへドッキングし、再び新しいウィンドウへ移動した。再フロート後にVS本体をクリックしても非アクティブのエディターが上に残り、描画と再生が続いた（`live-editor-docked.png`、`live-refloat-above-active-ide.png`）。Torus.glbの2つの表示コマンドの成功表示と、Solution ExplorerのAssets/Primitives展開・対象行の選択、Assetsの同フォルダーのTorusタイル選択を確認した（`live-reveal-torus-selected.png`）。フィルターの全組み合わせはこの実画面確認の対象にしていない。
- 同じ実画面確認で、プロジェクト外MM_Idle.glbのAssets診断は正常だったが、Solution Explorer側は「選択しました」と誤表示した（`live-external-assets-diagnostic.png`、`live-external-solution-false-success.png`）。[VS公式のMiscellaneous Files Project](https://learn.microsoft.com/en-us/visualstudio/extensibility/internals/miscellaneous-files-project?view=visualstudio)はソリューションのどのプロジェクトにも属さないファイルを扱う。SDKの`__VSDOCINPROJECT`はNotInProject=0、DocInProject=1、DocInExtFilesProject=2を定義するが、旧判定は非ゼロをすべて受け入れていた。回帰テスト`AnOpenMiscellaneousFileIsNotReportedAsASelectionInTheLoadedSolution`が修正前DLLに対して失敗することを確認し（`external-solution-before.log`）、DocInProjectだけを受け入れるよう修正した。この追加修正版の実画面受入は別途必要。
- 追加修正後の検証: VSIX全体のx64 Debugは1481件合格（`external-solution-full-debug.log`）。Releaseはナビゲーション対象6件が合格（`external-solution-targeted-release.log`）。両構成のVSIXを再生成し、パッケージ内DLLとビルド済みDLLのハッシュ一致を確認した（`external-solution-verification.json`）。Debugの追加修正版DLLは`7364C65585B11979D5459483743F2CCF3DDB582492EDBA549A6FBF514343C341`。現在起動中のCgeAssetsは旧検証版F0FD8E...であり、この新しい版への再インストール・実画面再確認は未実施。最初の入力占有期間は終了し、次の短い占有期間の返答待ち。

1. 未確認の実機項目（上記）を専用ハイブで確認する。窓を動かさずに全面の画面を撮れる環境で、スケルタルのタイムラインとツリーの見た目も撮る。
2. 静的な glTF のテクスチャ付き表示（protocol 12、ホスト読み込み）は WARP で検証済み。実機（専用ハイブ）での見た目、複数ノードのカメラと床の確認が残る。
