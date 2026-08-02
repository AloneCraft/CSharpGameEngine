# ADR-0007: VSツールウィンドウへのHWND再親化埋め込み(ADR-0008で置き換え)

## ステータス
承認済み(ただしADR-0008により置き換え済み - 下記「その後の経緯」参照)

## 背景

ADR-0006で実装した「VSコマンドから別ウィンドウを起動する」方式について、ユーザーが実際にVSで
試したところ、「イメージしてたものと違う」というフィードバックがあった。ユーザーが想定していた
のは、VSの画面の中(ドッキング可能なパネル)に直接3Dモデルが表示される動作であり、ADR-0006で
実装した「別ウィンドウが開く」挙動ではなかった。ADR-0006作成時に提示していた2案のうち保留に
していた案B(HWND再親化によるVSツールウィンドウへの直接埋め込み)を、ユーザーが選択した。

ADR-0006で既に整理していた通り、`Graphics.D3D12`/`Import.Gltf`は`net10.0`(CoreCLR)、
`devenv.exe`は`net472`(.NET Framework CLR)であり、レンダラーを本当の意味でVSプロセスに
in-processで埋め込むことはそもそも不可能だった(当時の技術調査の範囲では)。そのため、レンダラー
は引き続き別プロセス(`CSharpGameEngine.Extension.exe`)として動かしつつ、その**ウィンドウだけ**
をVSのツールウィンドウにネイティブHWND再親化で組み込む、という設計を採用した。

## 決定事項(当時の実装)

### 1. `Win32Window`/`Win32WindowOptions`に埋め込み用の起動モードを追加
`ParentHwnd`オプションを追加し、指定時は`WS_CHILD | WS_VISIBLE`スタイルで、指定した親HWNDの
子として直接作成するようにした。`ViewerRunOptions`/`ViewerOptionsParser`に`--parent-hwnd`
CLIオプションを追加した。

### 2. VSIX側の新規コンポーネント
`ViewerContainerWindow.cs`(WPFの`HwndHost`に返す軽量なネイティブ`WS_CHILD`コンテナウィンドウ、
`WM_SIZE`を子(グランドチャイルド)に転送)、`ViewerHwndHost.cs`(`dotnet run --project
tools/CSharpGameEngine.Extension -- --model Samples/Box.glb --parent-hwnd <コンテナHWND>`
を起動し、ポーリングでグランドチャイルドのHWNDを検出して初回リサイズを行う)、新規コマンド
「C# Game Engine Viewer パネルを表示」を追加した。既存の`OpenViewerCommand`(別ウィンドウ方式)
はそのまま残した。

## 実際に判明した内容(実装・検証を通じて - 正直に記録)

- **埋め込み表示自体は成功した**: 実際にVSツールウィンドウの中に`Box.glb`の赤いボックスが
  描画されることを実スクリーンショットで確認した。リサイズにも追従した。
- **VSの「閉じる」ボタンは`DestroyWindowCore`を呼ばない**: ツールウィンドウの標準的な
  「閉じる(Shift+Esc)」ボタンは、VSの設計上ペインを`Hide()`するだけで、`HwndHost`の
  `DestroyWindowCore`は実際には呼ばれないことが判明した。このため、閉じるボタンを押しても
  外部プロセス(`CSharpGameEngine.Extension.exe`)は生き続けた。
- **`DestroyWindow`によるカスケード削除は、子プロセスのメッセージキューに`WM_DESTROY`を
  確実には届けない**: 親(コンテナ)ウィンドウが破棄されると、Win32はその子ウィンドウ(別プロセス
  所有でも)を実際にカスケード的に破棄するが、それを所有するプロセス(`Extension.exe`)の
  メッセージループには`WM_DESTROY`が届かず、`IsClosed`が`true`にならないことが実験で判明した。
  対策として、`Win32Window.PumpEvents()`内で親HWNDの生存を`IsWindow`でポーリングし、消えて
  いたら自発的に終了する方式に変更し、これは実際に機能することを確認した。
- **Windows Job Object(`JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`)による「親プロセス終了時に子プロ
  セスを確実に道連れにする」対策を実装したが、実際には期待通りに機能しなかった**: `CreateJob
  Object`→`SetInformationJobObject`→`AssignProcessToJobObject`→(親プロセス終了または明示的
  `CloseHandle`)という標準的な手順を実装し、単体でのAPI呼び出し(`AssignProcessToJobObject`
  等)は全て成功(`true`)を返していたにもかかわらず、実際にVSプロセスを強制終了しても
  子プロセスが道連れにならないことを複数回の実験で確認した。根本原因は特定できていない
  (Windows 11/VS 2026環境固有の制約の可能性、あるいはジョブオブジェクトのネスト/継承に関する
  未知の制約の可能性がある)。最終的には、上記の「親ウィンドウ生存ポーリングによる自発的終了」が
  唯一確実に機能する対策だった。

## その後の経緯: ADR-0008による置き換え

上記の実装・検証が完了しユーザーにスクリーンショットで確認してもらった直後、ユーザーから
「現在csharpgameengine.vsextensionを実行するとvsプロセスを起動しを開き3d描画プロセスを起動し
vsプロセスのツールウィンドウに3d描画プロセスのウィンドウハンドルを渡しているのか？つまり２つの
プロセス起動しているのか？」という質問があり、その構成(2プロセス構成)を明確に確認した上で、
「それはやめてvsixインプロセスにして」という明確な指示があった。

これを受けて、`Vortice.Windows`(netstandard2.0対応版、`2.4.2`)を使えば本当にVSプロセス内で
D3D12描画が可能であることを新たに調査・確認し、[[ADR-0008-VSIX-InProcess-Vortice-Renderer]]
として実装し直した。本ADR-0007で実装した外部プロセス方式(`ViewerContainerWindow.cs`、
`ViewerHwndHost.cs`のプロセス起動/ポーリング/Job Objectロジック、`Win32Window`の`ParentHwnd`/
親ウィンドウ生存ポーリング)は、ADR-0008の実装時にコードごと削除された。本ADRは、その決定に
至った経緯と、実装中に実際に判明した技術的事実(特にJob Objectが機能しなかった件)を記録として
残すために保持する。

## 未解決・保留事項(ADR-0008により解消済みのものを含む)
- 2プロセス構成であること自体(ADR-0008でインプロセス化により解消)。
- Job Objectのkill-on-closeが機能しなかった根本原因(未解決のまま、ADR-0008では別プロセスが
  存在しないためこの問題自体が発生し得ない設計に変更された)。
