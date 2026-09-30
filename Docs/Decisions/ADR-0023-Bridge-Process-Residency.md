# ADR-0023: ブリッジプロセスの常駐化(Named Pipeサーバー)

## ステータス
承認済み

## 背景

ADR-0010追記(本ADRの直前の変更)で、`ExternalBridgeProcess.RunProcess`が`dotnet run --project`
(MSBuild評価コストのため1回あたり約2,000〜2,570ms)ではなく、事前ビルド済みapphost `.exe`を
直接起動する(`BridgeExecutableLocator`、約150〜180ms)ように変更し、シーンタブの体感速度問題を
解消した。

ユーザーからその実装内容(`ExternalBridgeProcess.RunProcess`が「メッシュ1個読むごとに
使い捨てプロセスを起動している」という説明)を受けて、「常に別プロセスを常駐し、共通化することで
さらに高速化できるのでは?」と提案があった。これは妥当な発想(Roslynの`VBCSCompiler.exe`やtsserver
等、同じ理由でワーカープロセスを常駐させる前例は一般的)だが、ADR-0010が最初にプロセス分離を
選んだ理由(不正な`.fbx`/`.glb`がパーサーをクラッシュさせてもVS本体を道連れにしない)との
トレードオフがある - 常駐プロセスは1回のクラッシュで以降のリクエストも巻き添えになりうる。

この安全性とのトレードオフを3択(見送る/全面実装する/クラッシュ分離の恩恵が薄いPrimitive.Cliのみ
実装する)でユーザーに提示したところ、**「常駐プロセス化を実装する」**(全面実装)が選ばれた。
トレードオフを承知の上での判断のため、3ブリッジ(`Import.Gltf.Cli`/`Import.Fbx.Cli`/
`Primitive.Cli`)全てを対象に、共通基盤で実装した。

## 決定事項

### 1. 新規`CSharpGameEngine.Bridge.Hosting`(net10.0、`tools/`配下)

3ブリッジと同じ`tools/`階層に置いた(`src/`はエンジン/ドメインコード用、これはVSIX支援
インフラ)。`ProjectReference`は持たない(特定ブリッジのドメイン型に依存させず汎用に保つ)。

- `BridgeServerHost.RunAsync(pipeName, handler, idleTimeout, cancellationToken)`: Named Pipeの
  接続待受ループ。**1リクエスト=1接続**(1本のパイプで複数リクエストを多重化しない) - 現状の
  「1リクエスト=1プロセス」という理解しやすいモデルをそのまま踏襲でき、`SceneHwndHost`が
  既に並列発行する複数の`MeshLoader.Load`呼び出しもそれぞれが独立して接続するだけで自然に
  対応できる。接続ごとに独立した`Task`にディスパッチする(直列化しない)。アイドルタイムアウト
  (15分、`GltfBridgeWorker`等呼び出し元が指定)で自己終了する - 1つの`CancellationTokenSource`
  を使い回し、接続を1つ受け付けるたびに`CancelAfter`し直す実装。
- `BridgeWireMessage`: 長さプレフィックス付きJSON(`PipeTransmissionMode.Byte`、`Message`モードは
  `MemoryStream`相手のテストができないため使わない)。`Stream`(`PipeStream`ではなく)を相手にする
  ことで、実パイプ無しにテスト可能にした。

### 2. 3ブリッジの`Program.cs`を「ワーカー抽出+`--serve`分岐」に変更

各ブリッジに`GltfBridgeWorker`/`FbxBridgeWorker`/`PrimitiveBridgeWorker`
(`internal static (int ExitCode, string Output) Run(string, string)`)を新設し、元々
`Program.cs`にあった処理をそのまま移設した。**`Console.WriteLine`は使わない** -
プロセス全体で共有される状態であり、常駐サーバーが並行リクエストを処理する際に取り違いが
起きるため、診断テキストを戻り値として返すようにした(一発モードでは`Program.cs`が最後に
まとめて出力する形に変わった - 進行に伴う逐次出力から完了後の一括出力への軽微な変更)。

`Program.cs`は`BridgeServeCommand.TryParse`で`--serve <pipe> [--parent-process-id <pid>]`を
受け取ると常駐サーバーモードに分岐し、
`BridgeServerHost.RunAsync(pipeName, XyzBridgeWorker.Run, TimeSpan.FromMinutes(15), OwnerProcessId: pid)`
を呼ぶ。それ以外は既存の2引数モードのまま。

**寿命は起動したVisual Studioに従う(2026-10-01追記)**: 常駐ブリッジは拡張のインストール先から
起動されるため、終了したVSより長く残ると、次のF5デプロイで拡張フォルダーのDLLがロックされて
失敗した(実験用ハイブで観測)。そこで次のようにした。
- `ResidentBridgeClient`は自分のプロセスIDを`--parent-process-id`で渡す。
- `BridgeServerHost`は`ParentProcessLifetime`(プレビューホストと共通、`Bridge.Hosting`)で所有者を監視し、
  所有者が終了するとアイドルタイムアウトと同じ経路で受け付けを止め、処理中の要求を片付けて終了する。
- パイプ名とスポーン用Mutexは所有者のプロセスIDも含めて決める。別のVS(別のインストール先・別の
  バージョン)が起動したブリッジには接続しない。
- アイドルタイムアウト(15分)はそのまま残る。

**`FbxBridgeWorker`のみ`SemaphoreSlim(1, 1)`で`FbxImporter.Import`呼び出しを排他する**:
`FbxImporter.Import`(`src/CSharpGameEngine.Import.Fbx/FbxImporter.cs`)は呼び出しのたびに
AssimpNetの`LogStream.Attach()`/`Detach()`を呼んでおり、これはプロセス全体で共有される
ネイティブロギング登録である。一発モードでは1プロセスにつき1回しか呼ばれないため問題に
ならなかったが、常駐サーバーは複数のFBXリクエストを本当に並行実行しうる(例: 複数の`.fbx`を
参照するシーンを`SceneHwndHost`が並列ロードする場合)。Attach/Detachの競合は診断メッセージの
混線、最悪ネイティブ層の状態破壊につながりうるため、実際のインポート呼び出しだけをセマフォで
直列化した(常駐化によるプロセス起動コスト削減という効果自体は失わない)。

### 3. VSIX側`ResidentBridgeClient`(net472、`tools/CSharpGameEngine.VSExtension/`)

`CSharpGameEngine.Bridge.Hosting`とはアセンブリを共有しない(net472はnet10.0を
`ProjectReference`できない)- ワイヤープロトコルの**形**だけを独立実装で踏襲する
(ADR-0019/0021の「重複させて後で統合」方針と同じ)。

- `ComputePipeName(repoRoot, bridgeProjectRelativePath)`: SHA256による決定的なパイプ名生成。
  `GetHashCode()`は使わない - プロセスごとにランダム化されるため、2回目のdevenv.exe起動が
  既存サーバーを見つけられなくなる。
- `TryRun(...)`: (1)まず接続を試す(タイムアウト100ms)。(2)失敗したら`Mutex`
  (`Global\...`)を取得してから常駐サーバーを`--serve`付きで起動する - N人の呼び出し元が
  同時に「サーバーが無い」と判断するケース(`SceneHwndHost`の並列メッシュロードで実際に起こる)
  でも、二重起動ではなく1プロセストだけが起動するようにするため。`Mutex`取得後にもう一度
  接続を試す二重チェックを挟み、直前の保持者が起動を終えていた場合の無駄な多重起動を防ぐ。
  `AbandonedMutexException`(前回のVSがクラッシュしていた場合等)は「取得できた」扱いで
  継続する。(3)起動後、接続をリトライ(最大10秒)。全て失敗すれば`false`を返し、
  `ExternalBridgeProcess.RunProcess`が既存の直接exe起動(tier2)にフォールバックする。
- **応答待機の実装で実機検証により設計変更が必要になった**(後述「検証結果」参照): 当初
  `Task.Run(...).Wait()`ベースの実装はVS threading analyzer(VSTHRD002)に拒否され、
  代替として提案されがちな`ThreadHelper.JoinableTaskFactory.Run(...)`に切り替えたところ、
  実際にはVSホストが存在しない環境(xunitテストプロセス等)で機能せず、毎回タイムアウトして
  常駐パスが常に失敗・tier2にフォールバックしていた(実測で発覚 - 後述)。最終的に生の
  `Thread`+`ManualResetEventSlim`による待機に変更し解決した - `Task`/`JoinableTaskFactory`
  依存が一切無いため、VSTHRD002の対象にもならず、VSホストの有無にも依存しない。

### 4. 3段フォールバック(`ExternalBridgeProcess.RunProcess`)

1. 常駐サーバー経由(`useResidentServer: true`の場合のみ、`RunAndReadJson`/`RunProducingFile`に
   必須引数として追加 - 呼び出し側に明示させる)。インフラ的な失敗(接続不可・タイムアウト)
   時のみ次段へ。リクエストが実行され`ExitCode != 0`で返ってきた場合はそれが最終結果であり、
   フォールバックの対象にしない。
2. 事前ビルド済みexeを直接起動(ADR-0010追記、既存・無変更)。
3. `dotnet run --project`(既存・無変更)。

`ExternalGltfLoader.cs`/`PrimitiveSpawner.cs`は`useResidentServer: true`を指定。
`ExternalFbxLoader.cs`も`useResidentServer: true`を指定しつつ、実運用でクラッシュ頻度が
問題になった場合に一行で`false`に戻せる旨のコメントを添えた - ネイティブクラッシュの
実リスクを持つ唯一のブリッジであるため、セマフォ(§2)と合わせて二重の安全弁にしてある。

## 検証結果(実際に実行して確認)

- **ビルド**: `dotnet build CSharpGameEngine.slnx` → 警告0、エラー0(全13プロジェクト+
  新規2プロジェクト)。
- **テスト**: `dotnet test CSharpGameEngine.slnx` → 12アセンブリ、234件全成功
  (`CSharpGameEngine.Bridge.Hosting.Tests`新規8件 - `BridgeWireMessage`のフレーミング往復、
  `BridgeServerHost`を実際のNamed Pipeで検証(同一プロセス内、子プロセスは起動しない):
  単発往復・8件並行リクエストの無混線・ハンドラ例外後もサーバーが使え続けること・
  アイドルタイムアウトでループが実際に終了すること。`ResidentBridgeClientTests`新規3件 -
  `ComputePipeName`の決定性・repoRoot違いでの別名・ブリッジ違いでの別名)。
- **`--serve`モードの実プロセス実地確認**: ビルド済み`CSharpGameEngine.Import.Gltf.Cli.exe`を
  `--serve <pipeName>`で実際に起動し、PowerShellから生のNamed Pipeクライアント
  (`System.IO.Pipes.NamedPipeClientStream`)で接続・リクエスト送信・応答受信を行い、
  `{"ExitCode":0,"Output":"..."}`という正しい応答と、期待通りの出力ファイル生成を確認した。
- **`ThreadHelper.JoinableTaskFactory`が実際には機能しなかった発見**: `ExternalBridgeProcess.
  RunAndReadJson(..., useResidentServer: true)`をxunitテストから実際に呼び出したところ、
  常に約10秒(`ResidentBridgeClient`のスポーン再試行タイムアウト値)かかり、`"Success":true`
  自体は返るもののtier2(直接exe起動)にフォールバックしていることが判明した。原因は
  `ReadMessage`内の`ThreadHelper.JoinableTaskFactory.Run(...)`- VSホストが存在しない
  プロセス(xunit)では正しく動作せず、例外を投げて`TryConnectAndRun`の`catch`に静かに
  飲み込まれていた。生の`Thread`+`ManualResetEventSlim`に置き換えたところ解決した。
- **常駐化の効果を実測**(`Box.glb`、`ExternalBridgeProcess.RunAndReadJson`を同一プロセス内で
  3回連続呼び出し):

  | 呼び出し | 所要時間 | 備考 |
  |---|---|---|
  | 1回目(コールド、サーバーを新規起動) | 約222 ms | ADR-0010追記の直接exe起動(~150-180ms)と同程度 |
  | 2回目(常駐サーバーを再利用) | 約2 ms | |
  | 3回目(常駐サーバーを再利用) | 約1 ms | |

  2回目以降は直接exe起動と比べてさらに約100〜150倍、当初の`dotnet run --project`
  (約2,000〜2,570ms)と比べて約1,000〜2,000倍高速化した。
- **並行コールドスタートの検証**: 常駐サーバーが1つも起動していない状態から、6つの呼び出しを
  `Task.Run`で完全に並行実行したところ、全て成功し(`"Success":true`)、生成された常駐サーバー
  プロセス(`Process.GetProcessesByName`で確認)は**1つだけ**だった - `Mutex`による二重起動
  防止が実際に機能することを確認した。

## 未解決・保留事項

- VS実機での確認(シーンタブ・シェイプパレット追加時の体感速度が実際に改善したか、複数の
  異なるメッシュを含むシーンを開いた際に常駐サーバーが正しく機能するか)は本ADR執筆時点で
  未実施。
- `Import.Fbx.Cli`の常駐化除外は「一行で切り替え可能」な形で保留してあるのみで、実際に
  切り替える判断基準(クラッシュ頻度の閾値等)はまだ定義していない。実運用で問題が出た
  場合に改めて検討する。
- 常駐サーバーのアイドルタイムアウト(15分)経過後の実際の自動終了は、`BridgeServerHostTests`
  で短いタイムアウト値により機構自体は確認済みだが、実際に15分間放置した場合の実機確認は
  行っていない。
