# ADR-0089 — アセットエディターウィンドウ（既定フローティング）とプレビューレベルテンプレート

- Status: Accepted
- Date: 2026-10-27
- Feature: F-L8-036、関連 F-L8-035
- 置き換えるもの: ADR-0081 の「ダブルクリックは既存の対応エディターを開く」（モデル／テクスチャ／音声／フォント／データの行）、ADR-0013 のモデルプレビュー専用エディター（`GlbFbxPreviewEditorFactory` / `AssetPreviewWindowPane`）

## Context

UE5 ではアセットをダブルクリックすると、そのアセット専用のエディターが前面のウィンドウとして開き、後からドッキングもできる。
C# Game Engine のダブルクリックは、モデルの読み取り専用プレビューとシェーダーグラフとシーンだけが専用エディターを持ち、
テクスチャ・音声・フォント・`.cgeasset` には Inspector しかなかった。モデルのカメラは固定で、ライトも背景もホストが固定していた。

## Decision

### 1. ウィンドウの種類

アセットエディターは **VS のドキュメントウィンドウ**（`IVsEditorFactory`）として作る。RDT・保存・dirty・ネイティブ Undo・二重オープン防止を VS 標準のまま使うため。
1 つのファクトリー（`AssetEditorFactory`）が `AssetEditorRegistry` の対応表に載る拡張子すべてを受け持ち、表にない拡張子は辞退する。
種類ごとの中身は `AssetEditorContent` を差し込む（Static Mesh / Texture / Sound / Font / Data Asset）。共通の枠（ツールバー・中央・Details・ステータス）は `AssetEditorShell`。
Skeletal Mesh / Animation は同じ基盤の上に載せる。ホストがスケルタル glTF を読む層の判断は [ADR-0090](ADR-0090-Preview-Host-Skeletal-Loading.md)。

### 2. 既定フローティング（実機で確かめた API の事実）

- 浮かせる操作は `IVsWindowFrame.SetProperty(VSFPROPID_FrameMode, VSFM_Float)`。**`IVsWindowFrameNotify3.OnShow` の中で、`fShow` が最初に表示を示したときだけ**呼ぶ。
  `OnCreate` の時点ではフレームがまだ親を持たず `E_UNEXPECTED` になる。
- 成功の判定は **HRESULT** で行う。ドキュメントフレームでは `VSFPROPID_FrameMode` / `Window.IsFloating` の読み戻しが浮いた後も古い値を返す場合があり、信頼できない。
  実際にどこへ置かれたかは、フレーム HWND のルートがメインウィンドウと異なるかで判定する（診断ログ用）。
- 位置とサイズは `SetFramePos(SFP_fMove|SFP_fSize)`、だめなら DTE の `Window.Left/Top/Width/Height`。**フロート直後すぐではなく約 1.2 秒遅らせて**一度だけ行う。
  直後に大きさを変えると、WPF の描画が一部しか塗られない状態を観測した。メイン画面の 85%（最大 1280×800、最小 640×480）を中央に置く（`AssetEditorOpenLocationPolicy.CenteredBounds`）。
  この遅延は以前の観測に基づく暫定回避であり、最初の描画完了や根本原因の解消を保証しない。最終ビルドの全面表示はM0127の未確認項目として維持する。
- VS の復元・「プログラムから開く」では `CreateFlags` に `CEF_OPENSPECIFIC (0x10)` が立つ。この場合は保存されたレイアウトを尊重し、**フロート化も位置決めもしない**。
  これを見ないと、復元のたびにユーザーが置いたウィンドウが中央に戻された。
- ユーザーがドッキングしたウィンドウは、再表示や再アクティブ化で浮かせ直さない（フレームごとに最初の一度だけ）。
- フロート化と前面維持は別の契約である。2026-10-27のユーザー確認ではドッキングは合格だが、VS本体を操作したときの前面維持は不合格だった。VSの既定のFloating Windows設定はTool Windowsだけを本体の所有ウィンドウにするため、ドキュメントを浮かせるだけでは継続した前面維持にならない（[VS公式資料](https://learn.microsoft.com/visualstudio/ide/customizing-window-layouts-in-visual-studio?view=visualstudio#floating-window-ownership)）。
  L8の`AssetEditorFloatingOwner`は表示中の内容から最上位HWNDを解決し、同じプロセスのVS本体へ`GWLP_HWNDPARENT`を設定する。内容が本体内にドッキングされている場合は変更しない。表示・Loaded・PresentationSource変更・ドッキング変更で更新し、破棄時にイベントを解除する。保存済みの位置やフロート／ドッキング状態を変更せず、所有関係だけを適用する。浮動タブ群を共有する場合、所有関係はその最上位ウィンドウ全体に適用される。
  [所有ウィンドウは所有者より上に置かれる](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features#owned-windows)。VS全体の設定を変更する案は他の文書へ影響するため採用しない。Topmostや前面化の繰り返しも使わない。ネイティブ所有関係のテストと、VS実画面での前面維持の受入は区別する。
- 「開く場所」は Tools > Options > C# Game Engine > Asset Editors の「フローティング（既定）／ドキュメントタブ」で切り替える。
  シェーダーグラフも同じ仕組みで既定フローティング。シーン JSON は変えず、中央のドキュメントのまま。

### 3. プレビューレベルテンプレート

アセットプレビューの背景・環境・ライト・床・初期カメラを **PreviewLevelTemplate**（UE5 の Preview Scene / Environment 相当）として分離する。
プレビュー対象のアセットは実行時にだけ足し、テンプレートには書かない。

- 形式は JSON（`.cgepreview`）、プロジェクトの `Assets\PreviewLevels\` に置く。ファイル名（拡張子なし）が安定した Id。
  標準の 4 種（Default / Studio / Outdoor / Neutral）はコードに持ち、**保護**する。変更はコピーしてから行う。
- Default は、ニュートラルな背景・キー/フィル/リムの 3 灯・影が落ちる床の環境（当初の Studio と同じ）。当初は従来の見た目（レンダラー既定の青い背景、平坦なアンビエント 0.4、+Y 向きの 2 lux の方向ライト、床なし）を Default にしていたが、青い背景と下から当たる光は照明の確認に向かないため、ユーザーの指示（2026-10-28）で入れ替えた。従来の見た目はテストの内部に残し、ホストの従来経路の写像は引き続き固定している。
- Studio は床・壁・天井のある部屋で、壁と天井も影を落とす。平行ライトは天井で全部遮られるので、室内の点ライトで照らす。Assets のモデルのサムネイルは Studio で描く。
- 選択の保存は、プロジェクトごと（プロジェクトパスのハッシュ）の最後に使ったテンプレートと既定テンプレートを VS 設定ストアに書く。将来のアセット種類別の既定の入口（`KindDefault`）は用意済み。
- テンプレートが欠落・破損している場合は、安全な既定（Default）で開き、理由を表示する。壊れた項目はドロップダウンで選べない。
- ホストが環境（大気・露出など）を適用できずに失敗した場合も、ビューポートを止めずに Default へ戻して理由を表示する。
- 複数のプレビューウィンドウは独立したホストプロセス（RenderServer）を持つので、ライトはほかのウィンドウ・シーンのビューポート・ゲームのレベルへ漏れない。

### 4. 既存のレベル（シーン JSON）はテンプレートに使えない

シーン JSON にはライトを置く表現があるが、読み込むと実行時のシステムと生成コード（ビルド済みのゲーム）を前提にする。
VS 内のエディター（devenv）はそれらを持たず、ビルドなしでは解決できない。このため「既存レベルをテンプレートとして使う」案は採らず、
同じ型付きの照明単位（`Illuminance` / `LuminousFlux` / `LightRange` / `LinearLightColor`）を使う専用の小さな JSON にした。
単位と挙動はレベルのライトと同じ型を共有するので、見た目の差は出ない。

### 5. ホストとの契約（プレビュー protocol 10）

- フレームごとの加算ライト（`Frame.Lighting`）は毎フレーム送る。背景・アンビエント・環境などの持続項目は `EnvironmentRevision` が変わったときだけ適用する。
  `ReplaceScene` で RenderServer を作り直すときは、保持した直近の照明を最初のフレームから再適用する。
- 背景色は `EnvironmentLightingSettings.Background`（リニア、ACES の逆変換でクリア色へ）としてレンダラーに渡す。
- 床は 1 枚の片面平面（エンジンの `CreatePlane` と同じ頂点順）で、グリッドは `WorldGrid` マテリアル。
- マテリアルは粗さ・金属度を運ぶ。静的メッシュの glTF のテクスチャは運ばない（形状と基本色）。スキン付き glTF はホストが自分で読み、テクスチャも焼き込む（ADR-0090）。
- FBX は実行時に読めないため、既存の取り込みブリッジの `SimpleMesh` を使う。テクスチャなし。

## Alternatives considered

- ツールウィンドウとして作る: RDT・保存・dirty・Undo のドキュメント統合を自前で作ることになるため採らない。
- フロート化を `OnCreate` や表示後のコマンド（`Window.Float`）で行う: `OnCreate` は `E_UNEXPECTED`。コマンド経路は VS 内部の状態に依存して不安定だった。`OnShow` の `SetProperty` を採用。
- 直後にサイズを指定する: 描画の不完全な塗りが出たため遅延させる。
- 既存のシーン JSON をテンプレートにする（§4）。
- 環境の失敗を無視して黒のまま残す: 症状を隠すだけでなく操作不能になるため、Default へ戻して理由を示す。

## Consequences

- モデルの読み取り専用プレビューエディターと固定カメラは廃止した（旧経路は残さない）。
- ビューポートの入力は描画面に Win32 フォーカスを持たせず、WPF のフォーカスモデルを使う（ADR-0041 を維持）。
- 新しい拡張子は `AssetEditorRegistry` に足すだけで専用エディターを持てる。種類別の既定テンプレートはその上に積む。
- Skeletal Mesh / Animation は ADR-0090 の判断（ホストが ECS 非依存のソースをリンクして共有する）で実装した。静的メッシュのテクスチャ付き表示は、同じ読み込みの上に後から載せられる。

## Verification

M0127 を参照。
