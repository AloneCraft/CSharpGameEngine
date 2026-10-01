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

## 未解決: VSIX ビルドの nupkg ロック
- 観測: VSIX をビルドした直後の次のビルドで、`obj\Debug\net472\Packages\CSharpGameEngine.Sdk.*.nupkg` の削除または書き込みが「別プロセスが使用中」で失敗することがある（このセッションで 5 回）。すぐに再実行すると成功する。
- 否定: dotnet の常駐ビルドサーバー（`dotnet build-server shutdown` 後も再発）、同一ビルド内での Pack の二重実行（詳細ログで 1 回だけ）。失敗後に Restart Manager で調べても保持プロセスはいなかった。
- 仮説（未確認）: 書き出した直後の大きな nupkg をウイルス対策などが一時的に開いている。
