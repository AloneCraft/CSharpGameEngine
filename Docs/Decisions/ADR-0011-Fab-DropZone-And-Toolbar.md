# ADR-0011: Fabダウンロード受け皿機能とVSツールバーへの統合

## ステータス
承認済み

## 背景

ゴールの残るスコープは、実際のFabアセット導入フローを支援する機能(ログイン・ダウンロード
自体はユーザーがFab公式サイト上で行う前提)と、既存コマンド群をToolsメニューだけでなく
正式なVSツールバーからもアクセスできるようにすることだった。

**前提となる制約(ゴールに明記、逸脱不可)**: Fabには購入者ライブラリを自動ダウンロードする
公式APIが存在しない(2026年8月時点で調査済み、`ADR`本文中には未記載だがこのプロジェクトの
ゴール策定時に確認済みの事実)。したがって、自動ログイン・非公式API利用は一切実装しない。
実装できるのは「ユーザーが手動でダウンロードしたファイルを自動整理する」受け皿機能と、
「fab.comをブラウザで開く」ボタンのみである。

## 決定事項

### 1. `FabDownloadWatcher` - OSダウンロードフォルダの監視とAssetsフォルダへの自動コピー
`FileSystemWatcher`をラップし、監視フォルダ(既定はOSの実際のダウンロードフォルダ)に
新規作成/リネームされたファイルのうち、拡張子が許可リスト
(`.fbx .glb .gltf .obj .zip .png .jpg .jpeg .tga .usd .usdz`)に一致するものを検出して
`UserGameProject/Assets/Fab/`へコピーする。

- **許可リストにした理由**: ダウンロードフォルダには無関係なファイル(インストーラー、
  PDF等)も混在するため、「新規ファイルを全てコピーする」ままではゲームプロジェクトの
  Assetsフォルダが無関係なファイルで汚染される。Fabが実際に配布する3Dアセット関連の
  拡張子に絞ることで、この問題を避けた(ゴール本文に明示の指定はなく、実装上の
  独自判断として記録する)。
- **書き込み完了待ち**: ブラウザは一般的にダウンロード中は一時名(例:
  `*.crdownload`)で書き込み、完了時に最終ファイル名へリネームする。`Created`イベントだけを
  見ると一時ファイルや書き込み中の不完全なファイルを掴む恐れがあるため、`Renamed`
  イベントも監視対象にし、さらにコピー実行前に`FileShare.None`でオープンを試すリトライ
  ループ(最大30秒)で「他プロセスが書き込み中でない」ことを確認してからコピーする。
- **ファイル名衝突時**: 上書きせず、`Prop (1).glb`のような連番サフィックスを付けて
  コピーする。
- **ダウンロードフォルダの実パス取得**: .NET Frameworkの`Environment.SpecialFolder`には
  Downloadsが存在しないため、`SHGetKnownFolderPath(FOLDERID_Downloads)`をP/Invokeで
  直接呼ぶ`KnownFolders.GetDownloadsFolder()`を実装した - ユーザーがダウンロード
  フォルダを既定から変更している場合でも正しいパスを取得できる。

### 2. `ToggleFabWatchCommand` - 監視の開始/停止をトグルするコマンド
常時バックグラウンドで動く設計ではなく、ユーザーが明示的にTools メニュー/ツールバーから
開始・停止する`OleMenuCommand`(`BeforeQueryStatus`でボタンのテキストと
チェック状態を実行中/停止中に応じて切り替える)にした。既存コマンド群が全てユーザー
起動型であることと一貫性を持たせるための判断。ファイルコピー成功/監視エラーは
VSのステータスバー(`IVsStatusbar`)に非モーダルで通知する - コピーはバックグラウンド
スレッドの`FileSystemWatcher`コールバックから発生するため、`MessageBox`のような
モーダルUIで割り込むのは適切でないと判断した。パッケージの`Dispose(bool)`で
確実に`FabDownloadWatcher`を停止・破棄するようにした。

### 3. `OpenFabCommand` - fab.comをブラウザで開くだけのコマンド
`Process.Start(new ProcessStartInfo("https://www.fab.com/") { UseShellExecute = true })`
のみ。ログイン・閲覧・購入・ダウンロードは完全にユーザー自身のブラウザ操作に委ねる。
自動化・スクレイピング・非公式APIは一切行わない。

### 4. `.vsct`に正式なツールバー(`<Menus><Menu type="Toolbar">`)を追加
既存の2コマンド(`OpenViewerCommand`、`ShowViewerToolWindowCommand`)に上記2コマンドを
加えた計4コマンドを、引き続きToolsメニューにも残しつつ、`<CommandPlacement>`要素で
新規`CSharpGameEngineToolbar`(`DefaultDocked`)にも配置した。1つの`<Button>`定義を
複数箇所に配置する標準的なVSCTパターン(ボタン定義自体は1箇所、`CommandPlacement`で
追加のホスト先を指定)を使っており、コマンドの重複定義はしていない。アイコンは
`KnownImageIds.vsct`から実在するモニカー(`OpenFolder`/`ToolWindow`/`FolderOpened`/
`ViewInBrowser`)を選定した - 存在しないモニカー名(`WebBrowser`)を最初に指定した際は
VSCTコンパイラが`VSCT1103`/`VSCT1136`で明確にビルドエラーにしてくれたため、実際の
ビルドエラーを見て修正した。

## 検証結果(実際に実行して確認)

- **`FabDownloadWatcher`の単体テスト(`tests/CSharpGameEngine.VSExtension.Tests`、新規、
  net472、`InternalsVisibleTo`経由)**: 一時フォルダを実際のダウンロード/コピー先
  フォルダに見立て、実ファイルを書き込んで動作を検証する統合寄りのテストを5件作成し、
  全て成功を確認した。
  - 対象拡張子(`.glb`)のファイルが実際にコピー先へコピーされ、内容も一致すること。
  - 対象外拡張子(`.exe`)のファイルは無視されること。
  - `Stop()`後は新規ファイルがコピーされないこと。
  - 同名ファイルが既にコピー先に存在する場合、上書きせず連番サフィックス付きの
    別名でコピーされ、両方のファイル内容が保持されること。
  - `KnownFolders.GetDownloadsFolder()`が実際に存在するディレクトリを返すこと
    (このマシン上で実測)。
- **ビルド**: `CSharpGameEngine.slnx`全体(16プロジェクト)が警告・エラーなくビルドできる
  ことを確認した。`.vsct`のツールバー/CommandPlacement/シンボル定義もVSCTコンパイラの
  検証を通過している。
- **VS実機でのツールバー表示・動作確認(実際にスクリーンショットで確認済み)**: VS実験用
  インスタンスにインストールし、実機で以下を確認した。
  1. `dte.CommandBars`(COM自動化、`EnvDTE.CommandBars`)経由で`"C# Game Engine"`
     ツールバーの`Visible`を`true`に設定し、実際に画面上へ表示されることを確認した
     (初回は既定で非表示 - VSの新規ツールバーは`DefaultDocked`指定でも既定では
     表示されず、`表示 > ツール バー`メニューまたは`CommandBars`から明示的に
     有効化する必要があることが実機で判明した)。
  2. UI Automationで`OpenViewerCommand`/`ShowViewerToolWindowCommand`/
     `ToggleFabWatchCommand`/`OpenFabCommand`の4ボタン全てを名前で検索し、標準ツール
     バーとは独立した1つのグループとして実際に配置されていることを、スクリーンショットと
     ボタンごとの座標(`BoundingRectangle`)の両方で確認した。
  3. ツールバー上の「Fab を開く」ボタンを実際にクリックし、その直後に新規Chromeレンダラー
     プロセスが起動する(既存のChromeプロセスに新規タブとして開かれる、`UseShellExecute
     =true`の期待通りの挙動)ことをプロセス一覧で確認した。
  4. ツールバー上の「C# Game Engine Viewer を開く」ボタンについては、Tools メニュー経由
     (ADR-0010に記載)で同一の`OpenViewerCommand`が子プロセス起動・レンダリングまで
     正常動作することを別途確認済み。
  - なお、この検証の過程で当初「VS自体の不具合でスクリーンショットが白紙になる」と
    誤診断していたが、真因は自動化スクリプト側の`Process.MainWindowHandle`の誤検出
    だったことが判明した(詳細はADR-0010)。ユーザーからの「白紙再現できない」という
    指摘が、この誤診断に気づく決定的な端緒となった。

## 未解決・保留事項
- 埋め込みツールウィンドウ(`ShowViewerToolWindowCommand`)経由でのFBX表示の実機
  スクリーンショットのみ、時間の都合で本セッションでは未取得(ADR-0010参照)。ツールバー
  自体の表示・動作は上記の通り確認済み。
- ダウンロードフォルダの監視は`FabDownloadWatcher`の生存期間中(トグルで開始してから
  停止/VS終了まで)のみ有効。VS再起動をまたいだ自動再開は行わない(明示的に
  ユーザーが毎回開始する設計、上記参照)。
- 新規ツールバーが既定で非表示である点(上記1.)はVSの標準的な仕様であり、ユーザーには
  初回のみ`表示 > ツール バー > C# Game Engine`から手動で有効化してもらう必要がある
  ことをREADME等に記載することが望ましい(今回のスコープでは未対応)。
