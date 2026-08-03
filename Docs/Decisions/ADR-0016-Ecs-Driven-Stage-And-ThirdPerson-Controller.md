# ADR-0016: ECS駆動ステージとThirdPersonキャラクターコントローラー

## ステータス
承認済み

## 背景

直前のADR-0015でアーキタイプ型ECS(`CSharpGameEngine.Ecs`)を実装した。今回は`UserGameProject`を
実行可能ファイル(exe)にし、ECSでプレイヤーキャラクターと地面のエンティティを作成して画面に
「ステージ」を表示する依頼があった。操作感はUnreal Engine 5のThirdPersonテンプレートに似せる:
マウスでキャラクター周りにカメラを旋回させ、WASDでカメラ基準の方向にキャラクターを移動させ、
キャラクターは移動方向へ回転する。簡易的な重力とJump(スペースキー、Y=0を地面としてクランプ)も
実装するが、本格的な物理/衝突判定エンジンは実装しない。

調査の結果、これに必要な要素(複数オブジェクト・変換行列を使った描画、カメラの移動/回転、
キーボード/マウス入力、地面/キャラクターのメッシュ、delta timeを使ったゲームループ)は
このエンジンに1つも存在しなかったため、新規エンジン機能の追加と`UserGameProject`側の
アプリケーションコードの両方を実装した。

### 実装途中で追加された要件と、それに伴う技術的制約

実装完了後、プレイウィンドウを「3Dモデルプレビューウィンドウと同じ仕組みでゲームドッキング
ウィンドウとして実現してほしい」という追加要望があった。既存の3Dモデルプレビュー
(`ViewerToolWindowPane`/`ViewerHwndHost`/`VorticeRenderer`、ADR-0007/0008)は、
`tools/CSharpGameEngine.VSExtension`(net472、.NET Framework)プロセス**内**で
`Vortice.Windows`ベースのD3D12レンダラーが直接ネイティブ子ウィンドウに描画する仕組みである。

一方、`CSharpGameEngine.Ecs`の`IEcsArchetype<TSelf>`はC# 11の`static abstract`インターフェース
メンバーを使っている。これは言語機能だけでなくランタイム(CLR)側の対応が必要な機能であり、
.NET Framework 4.7.2のCLRはこれをサポートしていない。したがって`CSharpGameEngine.Ecs`を
`net472`のVSIXプロセスへ直接参照・実行することはできない(そもそも`net472`プロジェクトから
`net10.0`専用プロジェクトへの`ProjectReference`自体、通常のターゲットフレームワーク互換性解決で
成立しない)。

このリポジトリには「別プロセスの描画結果をVSのドッキングパネルに埋め込む」仕組みの前例が無い。
既存の`OpenViewerCommand`(ADR-0006)は「別プロセスを起動する」ことは行っているが、
**VSに埋め込まない、独立したトップレベルウィンドウ**として起動するだけであり、
`ViewerHwndHost`(ADR-0007)はまさに「別プロセスを埋め込むのは面倒」という判断から
同一プロセス内実装を選んだという経緯がある。ユーザーに確認の上、今回は`OpenViewerCommand`と
完全に同じ機構(Toolsメニューコマンドで`dotnet run --project UserGameProject`を別プロセス・
別トップレベルウィンドウとして起動する)で実装し、本当の意味でのドッキング埋め込み
(`SetParent`によるウィンドウ再親化、またはDXGI共有テクスチャ+入力IPC)は今回のスコープ外とし、
将来の課題として残すことを確定した。

## 決定事項

### ランタイム拡張: `Camera`(新規、`src/CSharpGameEngine.Graphics.D3D12/Camera.cs`)
`System.Numerics`のみに依存する純粋struct。ターゲット位置・yaw/pitch・距離からview/projection
行列を計算する、UE5風スプリングアーム式オービットカメラ。TerraFX型を含まないためGPU無しで
単体テスト可能(ADR-0003 §5の「pure-logic typeは分離してテスト可能にする」前例)。

### `StageRenderer`(新規、`ModelRenderer`は無変更)
ADR-0004の「複製して後で統合(duplicate now, consolidate later)」の前例に従い、`ModelRenderer`
(単一メッシュ・テクスチャ有り・固定カメラ)とは別に新規クラスを作った。定数バッファを
**FrameCB(b0, ViewProjection)+ObjectCB(b1, World+BaseColorFactor)の2種**に分け、
`FrameCount`(2)分ダブルバッファして毎フレーム更新できるようにした(既存のper-frameフェンス
待ち機構でCPU/GPUの競合を防ぐ)。テクスチャ・SRVは使わない(単色Lambertianのみ、
`assets/Shaders/Stage.hlsl`新規作成)。

### `ProceduralMeshFactory`(新規)
`CreatePlane(width, depth)`(地面用)・`CreateBox(size)`(キャラクター代替形状)。
このリポジトリで初めての手続き的メッシュ生成コード(それまでは全てのメッシュがglTF/FBX
インポート経由だった)。カプセルは作らなかった(見た目の忠実度より操作感が主眼のため)。

### `Win32Window`の入力拡張
`WM_KEYDOWN`/`WM_KEYUP`(`WM_SYSKEYDOWN`/`UP`含む)による`bool[256]`キー状態配列+
`IsKeyDown(int virtualKeyCode)`(ポーリング方式)。マウスは**カーソルセンタリング方式**
(`GetCursorPos`/`SetCursorPos`/`ClipCursor`/`ShowCursor`、Raw Inputは不採用)で
`SetMouseCaptured(bool)`・`ConsumeMouseDelta()`。`WM_KILLFOCUS`/`WM_SETFOCUS`で
Alt-Tab時のキャプチャ解除/復帰とキー状態クリアを行う。

### `UserGameProject`のExe化
`TargetFramework`を`net10.0-windows10.0.19041.0`に変更(`tools/CSharpGameEngine.Extension`と
同じTFM/`Platforms=x64`/`RuntimeIdentifier=win-x64`/`SelfContained=false`)、`OutputType=Exe`。
新規`ProjectReference`: `CSharpGameEngine.Platform.Windows`・`CSharpGameEngine.Graphics.D3D12`。
`Program.cs`は`tools/CSharpGameEngine.Extension/Program.cs`と同じWindow+Renderer+ループの
配線パターンを踏襲した。

### ECS側: `StaticProp`アーキタイプ + Player/Groundをローカル変数で直接操作
`UserGameProject/Archetypes.cs`に地面用の軽量アーキタイプ`StaticProp`(`Position`+`Rotation`)
を追加し、`world.Create<StaticProp>()`で地面を1回生成する。Query/System基盤は作らず
(ADR-0015の「要件確定時に再検討」の段階にまだ達していないため)、`Program.cs`内でプレイヤー・
地面のビュー構造体をローカル変数として毎フレーム直接操作する。

### `CharacterMotion`(新規、`UserGameProject/PlayerController.cs`)
`World`/`Win32Window`/GPUに一切依存しない純粋関数`Compute(in CharacterMotionInput) ->
CharacterMotionResult`。カメラのflat forward/right・WASD押下状態・現在Y・垂直速度・deltaTimeを
受け取り、水平移動ベクトル・新しいY・新しい垂直速度・接地判定を返す(重力減算、Y<=0でクランプ、
接地時のみJump可)。角度の最短補間`LerpAngle`も同ファイルに置く。`readonly record struct`として
実装し(`UserGameProject`は`net10.0-windows`なので`record`の`IsExternalInit`要件は問題にならない、
ADR-0015のnetstandard2.0の制約とは異なる)、テストで`with`式による構築を使えるようにした。

### `OpenGameCommand`(新規、`tools/CSharpGameEngine.VSExtension`)
`OpenViewerCommand`(ADR-0006)と完全に同一の実装パターン: Toolsメニューコマンドから
`dotnet run --project UserGameProject`を別プロセス・別トップレベルウィンドウとして起動する。
VS本体への埋め込みは行わない(上記「実装途中で追加された要件」参照)。

## 代替案として検討したもの

- **`ModelRenderer`を拡張して複数オブジェクト対応にする**: ADR-0004の前例(単一メッシュ用の
  `ModelRenderer`と2D三角形用の`TriangleRenderer`を意図的に別クラスにした)に反するため不採用。
- **`SetParent`によるウィンドウ再親化、またはDXGI共有テクスチャでのドッキング埋め込み**:
  技術的に実現可能だが、リサイズ・フォーカス・プロセス生存管理の新規配線が必要な、
  このリポジトリに前例のない領域。今回はユーザー確認の上、スコープ外とし将来の課題とした。
- **既存の`ViewerToolWindowPane`/`ViewerHwndHost`/`VorticeRenderer`も別プロセス化する**:
  Vortice/D3D12は`net472`のVSIXプロセス内で既に問題なく動作しており、別プロセス化する
  技術的必然性が無い。変更すれば無関係な手戻りになるため不採用、現状維持とした。
- **`GetAsyncKeyState`によるグローバルポーリング入力**: ウィンドウがフォーカスを持たない時にも
  反応してしまう(グローバル状態のため)。`WM_KEYDOWN`/`UP`をウィンドウメッセージとして処理する
  方式の方が、フォーカスのあるウィンドウにのみ入力が届くゲームウィンドウとして正しい挙動になる
  ため、こちらを採用した。
- **Raw Input(`WM_INPUT`)によるマウス相対移動量取得**: 実装コストが高く、TerraFX側の対応状況の
  追加調査も要る。カーソルセンタリング方式(`GetCursorPos`/`SetCursorPos`)で今回の用途(オービット
  カメラ)には十分な精度が出るため、こちらを採用した。

## 帰結

- `CSharpGameEngine.CodeAnalysis`は今回変更していない。`CSharpGameEngine.Ecs`は`net10.0`専用で
  あり、`net472`のVSIXプロセスからは参照できないという制約が、今回初めて実際の設計判断に
  影響した(ADR-0015時点では理論上の制約に留まっていた)。
- `UserGameProject`は初めて実行可能ファイルになった。README/ADR-0012/Architecture.mdに残る
  「エンジンとの統合は将来のマイルストーン」という記述は本ADRにより古くなった
  (ドキュメントの陳腐化として記録するに留め、当該ドキュメントの全面書き換えはスコープ外とする)。
- カメラのyaw=0基準方向・`GetFlatRight()`の符号は、実装時点では実機での前後左右の直感一致を
  確認できていない暫定値である(ADR-0004 §6と同じく、実機確認は今後の課題)。

## 検証結果(実際に実行して確認)

- `Camera`/`ProceduralMeshFactory`/`CharacterMotion`はいずれも純粋関数のため、GPU/ウィンドウ
  無しで単体テストを実装し、全て成功を確認した(`CameraTests`8件、`ProceduralMeshFactoryTests`
  13件、`CharacterMotionTests`11件)。
- `StageGpuIntegrationTests`(WARPアダプタ・非表示ウィンドウ)で、地面+キャラクターボックスが
  両方描画されること、Resize後も壊れないことを確認した(2件)。
- `dotnet build CSharpGameEngine.slnx -c Debug`/`-c Release`: 成功、警告0件・エラー0件。
- `dotnet test CSharpGameEngine.slnx`: 全159件成功、失敗0件。
- `UserGameProject.exe`を実際に起動し、4秒間クラッシュせず動作し続けることを確認した上で、
  ウィンドウのスクリーンショットを撮影し、空(水色)・地面(緑の平面)・キャラクター
  (赤い天面を持つ黒いボックス)が意図通り描画されていることを目視確認した。
- `tools/CSharpGameEngine.VSExtension`(`OpenGameCommand`追加後)がDebug/Release両方で
  ビルド成功し、`.vsix`パッケージングも成功することを確認した。
- マウスでカメラが実際に旋回する、WASDでキャラクターが意図通り動く、Jump/重力の感触といった
  対話的な操作感は、この環境からは操作を注入できないため自動検証できていない。ユーザー自身が
  手元で`dotnet run --project UserGameProject`(またはVSの「ゲームを開く」コマンド)を実行して
  確認する必要がある。

## 未解決事項

- VSドッキングパネルへの本当の意味での埋め込み(`SetParent`再親化、またはDXGI共有テクスチャ+
  入力IPC)は未実装。現時点では`OpenViewerCommand`と同じ「別プロセス・別トップレベルウィンドウ」
  方式のみ。
- カプセル型キャラクターメッシュ、本格的な物理/衝突判定エンジン、Query/System基盤は未実装。
- カメラの`GetFlatRight()`の符号・yaw基準方向は実機での前後左右の直感一致を確認できていない。
- `OpenGameCommand`自体には専用の単体テストを追加していない(`OpenViewerCommand`と同様、
  `ProcessStartInfo`を組み立てて`Process.Start`を呼ぶだけの薄い配線コードであり、このリポジトリの
  既存の`OpenViewerCommand`にも専用テストが無いという前例に合わせた)。

## 関連機能
- F-L0-001 — `Camera`(オービットカメラ)
- F-L0-002 — `StageRenderer`(複数オブジェクト・毎フレームカメラ更新対応)
- F-L0-003 — `ProceduralMeshFactory`(平面/ボックス手続き生成)
- F-L0-004 — `Win32Window`キーボード/マウス入力
- F-L9-001 — `UserGameProject`ゲームループ(delta time計測、ECS駆動更新)
- F-L9-002 — ThirdPersonキャラクターコントローラー(`CharacterMotion`)
- F-L8-001 — `OpenGameCommand`(VSIX Toolsメニューからのゲーム起動)
