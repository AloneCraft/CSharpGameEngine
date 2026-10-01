# M0095 — テンプレート・シーンエディター・プログラミングモデルの探索的改善
- 日付: 2026-10-01
- 状態: 進行中

## 目的
テンプレート、編集フロー、シーンエディター、プログラミングアーキテクチャ、エディターアーキテクチャを、
再現 → 最小修正 → 回帰確認の順で改善する。各項目は「修正前に失敗し、修正後に成功する」証拠を残す。

## 進捗
| 項目 | 内容 | 状態 |
|---|---|---|
| 0-1 | FeatureStages のシーンが `$schema` に指す `.cge/scene.schema.json` が生成されなかった（stages は独自の `Directory.Build.targets` を持ち、スキーマ書き出しターゲットを取り込んでいなかった） | 完了（6584ad5） |
| 0-2 | `dotnet test CSharpGameEngine.slnx --arch x64` の後、SceneSchema.Cli の出力下に `win-x64\` が残り、SDK パッケージの再帰 glob が二重に取り込んで NU5118 で VSIX ビルドが失敗した | 完了（c23c7f4） |
| 1-1 | ランタイムビューで値の編集（ギズモ、Inspector、JSON テキスト）が画像に反映されない | 完了（b7765c1） |
| 1-2 | シーンタブを複数開き Hierarchy / Inspector にフォーカスがあると、1回の Ctrl+Z / Ctrl+Y / Ctrl+S がタブの数だけ実行されうる | 完了（40e1f11）。キー操作の実機確認は未実施 |
| 1-4 | シーンのタブを閉じても、次に前面へ来たのがシーン以外の文書だとピンが残り、Hierarchy / Inspector が閉じた文書を表示・編集し続ける | 完了（472c8c9）。空の Hierarchy の案内文が C# `IScene` 前提のままだったので JSON シーンの説明に直した（95c70e8） |
| 1-5 | `SceneWorkspaceService.Dispose` が実行中の解析を join しない | 不具合ではなかった（b692dbe でコメントを修正） |
| 1-3 | Design ビューで VS 標準の Undo とキーポーリングの Undo が二重に走る疑い | 未確認（キー入力が必要なため手動確認に残す） |
| 2-1 | ThirdPerson テンプレートの名前空間置換が別の識別子を壊さないこと、起動シーンが生成プロジェクトのシーンであること | テストで固定（11e9d5d）。現状のテンプレートに壊れる箇所はなかった |
| 2-2 | Scenes フォルダーの外に追加したシーン項目が、何の表示もなくシーンとして扱われない | ステータスバーで警告（758a80b） |
| 2-3 | VSIX がテンプレート原本の `.vs` や入れ子の `obj` を同梱する | 生成器と同じ除外に揃えた（b2c6297） |
| 3-1 | C# シーン時代のデッドコード（先読み Undo、コミット失敗通知、未使用の文言 5 件） | 削除（590880c）。到達しない防御的チェック（`IsJsonDocument` 等）は害がないので残した |
| 3-3 | 文書切り替えのたびに UI スレッドで `*.csproj` を探すディスク走査 | 変更なし。非再帰の列挙が数回で、負荷は小さい |
| 3-5a | ギズモ操作が手書きの書式（`"ScaleX":1`、1 行に 2 メンバー）を正規形に書き直す | 完了（本コミット）。変わった値だけを元のテキストへ書き戻す（`SceneJsonTextMerge`） |
| 3-5b | 未知のトップレベルキーを含む文書を Scene Designer が解析できない（SCENE204）。ADR-0070 は「Designer では警告、未知のエントリは保持」と定めている | ユーザー判断で ADR を実装に合わせた（1a0ca64）。未知のキー・メンバー・型はビルドと同じくエラー |
| 4-0 | （ユーザー要望）Alt+LMB のオービットで選択の中心が画面上で動かないこと、オービット中はギズモを隠すこと | 完了（25e124a、オービット中のギズモ非表示は直後のコミット）。数式はテスト済み、マウス操作の実機確認は未実施 |
| 3-4 | ランタイムビューを開く処理（ビルドのコピー、ホスト起動、Open）がシーンを開くたび・ゲームをビルドするたびに UI スレッドで 1.3〜1.9 秒走る | 完了（5043fde）。スレッドプールで開き、UI スレッドでは結果を受け取るだけ |
| 3-7 | ソリューションの読み込み中（NuGet の復元中など）にシーンのタブが表示されると、Design ビューが「Roslyn workspace に対象ファイルがありません」のまま止まる | 完了（dcd41d8）。ワークスペースの変更通知で文書の到着を待つ。当初「自動で回復するのでログのノイズのみ」と判断したが誤りだった（下記） |

## 1-1 ランタイムビューへの値編集
- 原因: 変換だけの差分は `SceneHwndHost.TryApplyIncrementalSnapshot` が設計用の描画オブジェクトだけを更新し、`ReplaceScene` を送らなかった。ランタイムビュー（`RenderPreviewRuntimeSession`）はフレームの描画オブジェクトの変換を使わず、文書の JSON からだけ値を受け取る。
- 修正: ランタイムビューのときは増分経路でも編集後の文書を `ReplaceScene` で送る。シーンプレビューの増分経路は変えない。
- 実機確認（CgeAutomation ハイブ、WARP、`UserEcsProject` の `PrimitiveShowcaseBoxScene.json`）: DTE で `Position.R` を 3 → 0 に書き換え、PrintWindow で Scene ビューを比較した。

| DLL | 編集前の2枚の差（ノイズ） | 編集前後の差 | 箱の位置 |
|---|---|---|---|
| 修正前 | 0 px | 864 px（タブの `*` のみ） | 変わらない |
| 修正後 | 0 px | 334,711 px | 移動する |

`SceneHwndHost` は WPF の HwndHost のため単体テストを置けず、確認はこの実機比較で行った。

## 1-2 キーボードの Undo / Redo / Save の持ち主
- 原因（コード経路）: 各シーンタブの `SceneHwndHost` は `BuildWindowCore` から `DestroyWindowCore` まで `CompositionTarget.Rendering` でキーをポーリングする。背面のタブも含め、Hierarchy / Inspector のフォーカスは全タブで「デザイナーにフォーカスあり」と判定され、各タブがグローバルなピン中シーンに対して `TryUndo` を呼んでいた。
- 修正: `SceneDesignerUndoCommandPolicy.IsKeyboardOwner` で、ピン中シーンを表示しているタブ（固定シーンを持たないツールウィンドウはピンに追従するので常に対象）だけがキー入力を処理する。
- テスト: `KeyboardOwner_IsExactlyTheTabShowingThePinnedScene`、`KeyboardOwner_DesignerWithoutAFixedScene_OwnsThePinnedScene`（ポリシーがない修正前はビルドエラー）。
- 未確認: 実際にキーを押す確認は、ユーザーのフォーカスを奪わない方針のため行っていない。手動確認項目「シーンタブ2枚 + Hierarchy フォーカスで Ctrl+Z を1回押し、1段だけ戻る」。

## 1-4 タブを閉じたときのピン解除
- 原因: `SceneJsonEditorPane.OnClose` がデザイナーを破棄するだけで、セッションのピンを外さなかった。次に前面へ来たタブがシーンならそのタブが新しくピンするが、シーン以外の文書ならピンが残る。
- 修正: 閉じるタブがピン中のシーンを表示しているときだけ `ClearPin` する（`SceneDesignerTargetFilter.ReleasesPinOnClose`）。別のタブのピンには触れない。
- テスト: `ClosingATab_ReleasesThePinOnlyWhenItShowsThePinnedScene`。
- 実機確認（CgeAutomation、DTE と UIA のみ。キー入力なし）: `README.md` と `PrimitiveShowcaseBoxScene.json` を開き、シーンを前面にして Hierarchy を表示 → `README.md` を前面にしてシーンのタブを閉じる → UIA で `ShowcaseBox` ノードを探す。

| DLL | シーンを開いている間 | シーンのタブを閉じた後 |
|---|---|---|
| 修正前 | 表示 | 表示されたまま |
| 修正後 | 表示 | 消える（空の状態） |

## 1-5 解析中の Dispose（仮説を否定）
- 仮説: `Dispose` が実行中の解析を join しないため、破棄後にコールバックが走る。
- 結果: 解析は配信の直前にロック内で `Disposed` を確認して抜けるため、破棄後の配信は起きない。UI スレッドの `Dispose` で join すると、コンパイル完了までソリューションのクローズが止まる。誤っていたのは「Dispose が join する」というコメントで、コードは変更していない。

## Phase 2 で否定・保留した仮説
- 「VS 経由と E2E 経由の出力差（ReadMe.md）を無くす」: ADR-0068 により、vstemplate の ReadMe は VSSDK にプロジェクトファイルをアーカイブさせるための項目で、外せない。差は ReadMe だけでビルドに影響しないため、生成器のコメントに例外として明記した。
- 「`ThirdPerson.csproj.template` と `GameProjectGenerator.ProjectFile` の二重管理」: 一致は既存の `GameProjectGeneratorTests` が守っており、現状維持。
- 「`PublishToFeed` が同サイズのパッケージを同一とみなすため古い中身が残る」: 起こりうるが、NuGet のグローバルキャッシュも同じバージョンを再利用するため、運用は修正ごとのバージョン更新で回っている。未対応。

## 3-4 プレビューの同期呼び出しの計測
- 計測（WARP、640×360、`PrimitiveShowcaseBoxScene`、一時的な計測テスト）: ホスト起動 64〜183 ms、ランタイムビューの Open 242〜1,159 ms、最初の Render 278〜515 ms。毎フレームの UpdateFrame+Render は中央値 30〜51 ms だが、UpdateFrame だけの往復は中央値 0.5 ms で、「リクエストごとのパイプ再接続が遅い」という仮説は否定された（フレームの時間は WARP の描画そのもの）。
- 実機（CgeAutomation、ActivityLog）: 修正前は開き直しの 1.34 秒を UI スレッド上で実行、修正後は初回 1,853 ms・開き直し 1,352 ms をスレッドプールで実行。
- 限界: UI の停止を `WM_NULL` の `SendMessageTimeout` で測ったが、修正前でも 42 ms しか出なかった。UI スレッドがパイプ応答を同期的に待つ間、STA の待機が送られてきたメッセージを処理するためで、この方法では判別できない（キーボードやマウスの入力はこの間処理されない）。
- 残り: 毎フレームの Render も UI スレッドで同期実行している（GPU ではより短いはず）。描画ループの非同期化は別の設計変更として扱う。

## 3-7 起動時の Design ビュー（当初の判断の訂正）
- 当初、シーンのタブを前面にしたまま起動して表示が回復したことから「ログのノイズのみ」と判断した。
- その後、NuGet の復元中にタブをアクティブにした実行で、Design ビューが「…を開けませんでした（Roslyn workspace に対象ファイルがありません）」のまま止まった。再試行はタブを前面に出し直したときだけで、前回は偶然そのきっかけがあった。
- 修正: `SceneWorkspaceService.WaitForDocumentAsync` が `WorkspaceChanged` で文書の到着を待ち、Design ビューはその間「起動中」を表示する。タブを閉じると待機は取り消される。修正後は同じ条件でシーンが表示された。

## 3-5 手書きの書式と未知のキー
- 3-5a の再現: `SceneJsonCommitPlannerTests` の文書で `"ScaleX": 1` を `"ScaleX":1` に、`ScaleY` と `ScaleZ` を 1 行にまとめてからキューブを動かすと、期待（`"R": 1` → `"R": 3` だけが変わる）に対し、書式を整えた行も書き換わる。既存のテストは正規形の文書だけを使うため検出していなかった。M0093 C1 の「span-preserving writer」はコミット経路で使われていない。
- 3-5b の再現: 同じ文書に `"editorNote"` を足すと、`SceneJsonCommitPlanner.TryAnalyze` が `SCENE204: 'editorNote' is not a document key` で失敗する。何も書き込まれないのでデータは失われないが、その文書は Design ビューで編集できない。
- 3-5a の修正: `SceneJsonCommitPlanner.Plan` は正規形の全文を作った後、`SceneJsonTextMerge.Merge` で元のテキストと JSON の木どうしで比べ、値の変わったスカラーだけを差し替える。キーの並びや配列の長さが変わったオブジェクト・配列（ノード追加など）はその部分だけ正規形で置き換える。結果が新しい文書と同じ値として読めなければ正規形を使う。数値は型へ変換せず、符号・有効数字・指数の文字列で厳密に比べる（`1.0` と `1` は同じ、`16777217` と `16777216` は別）。`Move_KeepsHandFormatting` は修正前に失敗し、修正後に通る。

## 未解決: VSIX ビルドの nupkg ロック
- 観測: VSIX をビルドした直後の次のビルドで、`obj\Debug\net472\Packages\CSharpGameEngine.Sdk.*.nupkg` の削除または書き込みが「別プロセスが使用中」で失敗することがある（このセッションで 5 回）。すぐに再実行すると成功する。
- 否定: dotnet の常駐ビルドサーバー（`dotnet build-server shutdown` 後も再発）、同一ビルド内での Pack の二重実行（詳細ログで 1 回だけ）。失敗後に Restart Manager で調べても保持プロセスはいなかった。
- 仮説（未確認）: 書き出した直後の大きな nupkg をウイルス対策などが一時的に開いている。
