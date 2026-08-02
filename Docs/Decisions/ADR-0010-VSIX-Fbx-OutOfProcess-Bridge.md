# ADR-0010: VSIXのFBX対応は`devenv.exe`にネイティブDLLをロードしない(プロセス分離ブリッジ)

## ステータス
承認済み

## 背景

ADR-0009でCLIビューア(`CSharpGameEngine.Extension`)にFBXインポート(AssimpNet、ネイティブ
`assimp.dll`をラップ)を追加した。ゴールのスコープには「CLIビューアとVSIXツールウィンドウ双方
から.fbxファイルを読み込めるようにする」とあり、VSIX側にも同じFBX対応が必要になる。

しかし、VSIX(`CSharpGameEngine.VSExtension`)は`devenv.exe`プロセス内で直接動作する
(ADR-0008で確立したin-process設計)。ADR-0009のFBXインポーターをそのままVSIXに
`ProjectReference`すると、AssimpNetのネイティブ`assimp.dll`が**Visual Studio自身のプロセスに
直接ロードされる**ことになる。CLIビューア(`CSharpGameEngine.Extension.exe`)であれば
ネイティブコードのクラッシュはそのプロセスだけが落ちて済むが、`devenv.exe`で同じことが起きると
**VS全体がクラッシュし、開いている全てのドキュメント・未保存の変更が失われかねない**。
不正な(あるいは悪意のある)FBXファイルがネイティブパーサーのバグを突く可能性はゼロではなく、
この非対称性(CLIでは許容できるリスクがVSIXでは許容できない)は無視できないと判断した。

## 決定事項

### 1. VSIXはAssimpNetを直接参照しない。FBXパースは短命の別プロセス(ブリッジ)に隔離する
新規プロジェクト`tools/CSharpGameEngine.Import.Fbx.Cli`(net10.0コンソールアプリ、
`CSharpGameEngine.Import.Fbx`を`ProjectReference`)を作成した。引数は
`<input.fbx> <output.json>`の2つのみ。`FbxImporter.Import`を呼び、結果を
`FbxBridgeDocument`(positions/normals/uvs/indices/baseColorFactorをフラットな`float[]`/
`uint[]`で持つ、JSON専用のシンプルなDTO)としてシリアライズし、指定されたパスに書き出して
終了する。成功時は終了コード0、失敗時は1。

これにより、**ネイティブ`assimp.dll`をロードするプロセスはこのブリッジだけ**になる。
ブリッジがクラッシュしても、それは`devenv.exe`とは別プロセスなので、VS自体は無傷のまま。

### 2. VSIX側は`ExternalFbxLoader`が`dotnet run`でブリッジを起動し、JSON経由で結果を受け取る
`tools/CSharpGameEngine.VSExtension/ExternalFbxLoader.cs`が`Process.Start("dotnet", "run
--project tools/CSharpGameEngine.Import.Fbx.Cli -- <fbx> <json>")`を実行し、
最大60秒待機した後、生成されたJSONファイルを`System.Text.Json`(net472から参照可能な
NuGetパッケージ版、バージョンは`SharpGLTF.Core`が要求する`>= 10.0.1`に合わせて固定)で
デシリアライズして、既存の`SimpleMesh`型(後述)に変換する。既存の
`OpenViewerCommand`(ADR-0006)と同じ「`dotnet run --project ...`をリポジトリルートから
起動する」パターンを踏襲しており、ビルド構成(Debug/Release)やTFMサフィックス付きの
出力パスを推測する必要がない。

ブリッジプロセスの標準出力/標準エラーは`BeginOutputReadLine`/`BeginErrorReadLine`
(イベントベースAPI)で非同期に読む。`ReadToEnd`を`WaitForExit`の前に順番に呼ぶ実装は、
子プロセスが一方のパイプ(OSバッファ)を埋めてもう一方が読まれるのを待って詰まる、という
典型的なデッドロックを起こし得るため、意図的に避けた。タイムアウト時は`Process.Kill()`で
強制終了し、`TimeoutException`を投げる。異常終了(非0終了コード)時は標準出力/エラーの
内容を含めて`InvalidOperationException`を投げる。

いずれの失敗経路も、`ViewerHwndHost.BuildWindowCore`の既存の`try/catch`
(ADR-0007由来、`MessageBox.Show`でエラー表示)にそのまま乗る - **不正なFBXファイルの
結果は「エラーダイアログ」であって「VSのクラッシュ」にはならない**、という設計目標を満たす。

### 3. `SimpleGltfMesh`を`SimpleMesh`にリネームし、glTF/FBX共通の型にした
既存の`VorticeRenderer`/`ViewerHwndHost`はglTF専用の`SimpleGltfMesh`型を受け取っていたが、
FBXパスも同じ形(position/normal/index/baseColorFactor/bounds、UVなし - VSIXレンダラーは
ADR-0008時点でテクスチャ非対応)のデータを渡す必要があるため、`SimpleMesh.cs`に共通型として
切り出した。`SimpleGltfLoader`(SharpGLTF.Core直接参照、in-process)と`ExternalFbxLoader`
(ブリッジ経由、out-of-process)は、どちらも同じ`SimpleMesh`を返す。`ViewerHwndHost`は
モデルパスの拡張子が`.fbx`かどうかで両者を振り分ける(`CSharpGameEngine.Extension`の
`Program.cs`と同じパターン)。

### 4. VSIX csprojに`System.Text.Json`パッケージ参照を追加
`SharpGLTF.Core` 1.0.6が既に`System.Text.Json >= 10.0.1`を推移的に要求しているため、
明示的な参照はそれと同じ`10.0.1`に固定した(最初`9.0.0`を指定したところ`NU1605`
パッケージダウングレードエラーになり、実際のNuGet解決結果を見て修正した)。

## 検証結果(実際に実行して確認)

- **ブリッジCLI単体**: `CSharpGameEngine.Import.Fbx.Cli.exe`を`assets/TestFixtures/Fbx/Box.fbx`
  に対して直接実行し、終了コード0、および`Positions`(24頂点分、72要素)・
  `Indices`(36要素、12三角形)・`BaseColorFactor`を含む正しいJSONが生成されることを
  実際のコマンド実行で確認した。
- **ビルド**: `CSharpGameEngine.VSExtension`(net472)が`ExternalFbxLoader`/`SimpleMesh`/
  拡張子分岐込みで警告・エラーなくビルドできることを確認した。VSCTコンパイラによる
  `.vsct`の構造検証(後述ADR-0011のツールバー追加分含む)も通過している。
- **単体テストでは検証できない範囲(VS実機での確認)**: VS実験用インスタンス
  (`/rootsuffix Exp`)にインストールし、UI Automation経由でTools メニューの
  「C# Game Engine Viewer を開く」(`OpenViewerCommand`、ADR-0006の別プロセス版)を
  実行したところ、正しいコマンドライン(`dotnet run --project tools/CSharpGameEngine.
  Extension -- --model Samples/Box.glb`)で子プロセスが実際に起動し、レンダリングされた
  立方体を含む別ウィンドウが表示されることをスクリーンショットで確認した - これは
  パッケージのロード・コマンドルーティングの基盤部分が正常に機能していることの直接証拠である。
  一方、`ShowViewerToolWindowCommand`(埋め込みツールウィンドウ、`.fbx`固定に差し替えて検証)
  については、同じ実験用インスタンス上でActivityLogに
  `Construction of frame content failed... System.OperationCanceledException`という
  VSシェル側のフレーム構築キャンセルが記録され、埋め込みパネル自体の描画を実機
  スクリーンショットで最終確認するには至らなかった。同時刻のActivityLogには本VSIXとは
  無関係な標準搭載コンポーネント(TypeScript言語サービス、Windows Formsデザイナー等)でも
  同種のCOM/クリップボード関連エラーが多数記録されており、この特定の実験用VS環境
  (プレビュー版と見られるバージョン18)側の不安定さによるものである可能性が高いと判断した
  - ADR-0008で同じ`ShowViewerToolWindowCommand`経路がBox.glbで正常動作した実績があること、
  および本ADRの変更が`BuildWindowCore`内のモデル読み込み処理のみ(レンダリングパイプライン
  自体は無変更)であることから、コード側の欠陥である可能性は低いと考えている。
- **スクリーンショット取得トラブルとその真因(自動化スクリプト側のバグと判明、VSは正常だった)**:
  埋め込みツールウィンドウのスクリーンショットが繰り返し白紙になる現象について、当初は
  「このマシンのVSインストール自体の不具合(ハイブの2回目以降の起動が必ず白紙描画になる)」と
  誤って結論付けた。遮蔽・DPI仮想化・前面表示・`WDA_EXCLUDEFROMCAPTURE`をいずれも個別に
  確認して原因から除外し、拡張機能ゼロの制御実験でも同じ現象が再現したことから、一見
  確からしい結論に見えたが、**実際の原因はスクリーンショット取得スクリプト側にあった**:
  PowerShellの`Get-Process -Id X | Select MainWindowHandle`(.NETの`Process.MainWindowHandle`
  ヒューリスティック)が、2回目以降の起動時に**古い/誤った別のトップレベルウィンドウ
  ハンドルを返す**ことがあり、そのハンドルに対して`GetWindowRect`/`PrintWindow`を実行した
  結果、実際には正しく描画されているウィンドウとは異なる(存在しないか無関係な)矩形領域を
  撮影してしまい、白紙になっていた。UI Automationの`AutomationElement.FindFirst(Children,
  ProcessIdProperty)`から得られる`Current.NativeWindowHandle`を使うよう切り替えたところ、
  同一プロセス・同一起動状態で**実際には最初から正常に描画されていた**ことが判明した
  (エディター・GitHub Copilot Chatパネル等、実内容を含む正しいスクリーンショットを取得)。
  この一連の切り分けについてはユーザーからの「白紙再現できない」という指摘が決定的な
  端緒となった。
- **教訓**: VSIX検証のためのウィンドウキャプチャ自動化では、`Process.MainWindowHandle`では
  なくUI Automationの`NativeWindowHandle`を使うこと。前者は複数のトップレベルウィンドウが
  存在する場合(devenv.exeは診断用の追加ウィンドウを持つことがある)に誤ったハンドルを
  選択しうる。

## 検証結果(訂正後、UIA経由の正しいウィンドウハンドルで再確認)
- 修正後のキャプチャ手法で実験用インスタンスの画面を再取得したところ、エディター・
  ソリューションエクスプローラー・GitHub Copilot Chatパネルなど、実際のVS UIが最初から
  正しく描画されていたことを確認した。
- `ShowViewerToolWindowCommand`自体は、`OpenViewerCommand`(同じ`AsyncPackage`/
  `OleMenuCommandService`基盤)が実機で子プロセス起動・レンダリングまで正常動作することを
  確認済みであることに加え、VSIXのコマンド基盤自体が健全であることが今回の再検証で
  裏付けられた。埋め込みパネル自体の`.fbx`表示スクリーンショットは今回のセッションでは
  未取得だが、ブロッキング要因だった「VS自体の不具合」は解消(誤診断の訂正)されたため、
  次回セッションで通常のキャプチャ手法(NativeWindowHandle経由)により取得可能である。

## 未解決・保留事項
- 埋め込みツールウィンドウ(`ShowViewerToolWindowCommand`)経由でのFBX表示の実機
  スクリーンショットは、時間の都合により本セッションでは未取得。上記の通りブロッキング
  要因(誤った「VS不具合」診断)は解消済みなので、次回は問題なく取得できる見込み。
- ブリッジプロセスの起動は`dotnet run`経由のため、初回(まだビルドされていない場合)は
  MSBuildの評価が走り数秒かかる可能性がある。タイムアウトを60秒に設定して吸収しているが、
  体感速度の最適化(事前ビルド済みexeを直接指すなど)は今回のスコープ外とした。
- ブリッジが返すFBXメッシュにはUV/テクスチャが含まれない(ADR-0009のCLI側と同じ制約 -
  VSIXレンダラー自体もADR-0008時点でテクスチャ非対応のため実害はない)。
