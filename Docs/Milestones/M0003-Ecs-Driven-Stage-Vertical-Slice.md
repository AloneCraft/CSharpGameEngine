# M0003: ECS駆動ステージ・ThirdPersonキャラクターコントローラーの追加

## 目的
`UserGameProject`を実行可能ファイル(exe)にし、アーキタイプ型ECS(`CSharpGameEngine.Ecs`)で
プレイヤーキャラクターと地面のエンティティを作成して画面に「ステージ」を表示する。操作感は
Unreal Engine 5のThirdPersonテンプレートに似せる(マウスオービットカメラ+カメラ基準WASD移動+
移動方向への回転+簡易重力/Jump)。詳細な設計判断は
[[ADR-0016-Ecs-Driven-Stage-And-ThirdPerson-Controller]]に記録した。

## 実装したこと
- `src/CSharpGameEngine.Graphics.D3D12/Camera.cs`(新規): UE5風スプリングアームオービットカメラ。
  純粋な`System.Numerics`ロジックでGPU無しにテスト可能。
- `src/CSharpGameEngine.Graphics.D3D12/StageRenderer.cs`(新規)+`assets/Shaders/Stage.hlsl`
  (新規): 複数オブジェクト・毎フレーム可変カメラに対応したレンダラー。`ModelRenderer`は無変更。
  FrameCB/ObjectCBをフレーム数分ダブルバッファし、per-object個別のWorld行列を毎フレーム更新する。
- `src/CSharpGameEngine.Graphics.D3D12/ProceduralMeshFactory.cs`(新規): 地面用平面・
  キャラクター用ボックスの手続き的メッシュ生成。このリポジトリで初の手続きメッシュ生成コード。
- `src/CSharpGameEngine.Platform.Windows/Win32Window.cs`(変更): `IsKeyDown`(ポーリング)、
  `SetMouseCaptured`/`ConsumeMouseDelta`(カーソルセンタリング方式)、Alt-Tab時のキャプチャ解除/
  復帰とキー状態クリアを追加。
- `UserGameProject/UserGameProject.csproj`(変更): `OutputType=Exe`、
  `TargetFramework=net10.0-windows10.0.19041.0`、`Platform.Windows`/`Graphics.D3D12`への
  `ProjectReference`を追加。
- `UserGameProject/Archetypes.cs`(変更): 地面用の軽量アーキタイプ`StaticProp`を追加。
- `UserGameProject/PlayerController.cs`(新規): `CharacterMotion`(重力・Jump・カメラ基準WASD移動の
  純粋関数)、`LerpAngle`(最短経路角度補間)。
- `UserGameProject/Program.cs`(新規): Window+StageRenderer+ECS(World/Player/StaticProp)+
  Camera+CharacterMotionを配線するゲームループ(`Stopwatch`ベースdelta time)。
- `tests/CSharpGameEngine.Graphics.D3D12.Tests/`に`CameraTests`(8件)・
  `ProceduralMeshFactoryTests`(13件)・`StageGpuIntegrationTests`(2件、WARP実描画検証)を追加。
- `tests/UserGameProject.Tests/`(新規プロジェクト)に`CharacterMotionTests`(11件)を追加。
- `tools/CSharpGameEngine.VSExtension/OpenGameCommand.cs`(新規): `OpenViewerCommand`(ADR-0006)
  と同一パターンで、Toolsメニューから`dotnet run --project UserGameProject`を別プロセス・
  別トップレベルウィンドウとして起動するコマンド。`.vsct`にボタン登録、
  `CSharpGameEngineViewerPackage.InitializeAsync`に配線を追加。
- `CSharpGameEngine.slnx`にADR-0016・本Milestone文書・`assets/Shaders/Stage.hlsl`・
  `tests/UserGameProject.Tests`を登録。

## 意図的に除外したこと
- VSドッキングパネルへの本当の意味での埋め込み(`SetParent`再親化、またはDXGI共有テクスチャ+
  入力IPC)。実装途中でユーザーから「ゲームドッキングウィンドウ」という追加要望があったが、
  `CSharpGameEngine.Ecs`の`static abstract`インターフェースメンバーが.NET Framework 4.7.2の
  CLRでは動作せず、`net472`のVSIXプロセスへ直接持ち込めないこと、かつこのリポジトリには
  別プロセスの描画結果をドッキングパネルへ埋め込む前例が無いことを説明した上で、
  ユーザーの判断により`OpenViewerCommand`と同じ「別プロセス・別トップレベルウィンドウ」方式で
  今回は実装することを確定した。本当のドッキング埋め込みは将来の課題として残す。
- カプセル型キャラクターメッシュ(ボックスのみ)。
- 本格的な物理/衝突判定エンジン(Y<=0の簡易接地クランプのみ)。
- ECSのQuery/System基盤(プレイヤー・地面はProgram.cs内でローカル変数として直接操作)。
- `OpenGameCommand`専用の単体テスト(`OpenViewerCommand`にも無い薄い配線コードのため)。

## 変更したFeature ID
- `F-L0-001` — `Camera`(新規、Implemented/TestVerified)
- `F-L0-002` — `StageRenderer`(新規、Implemented/TestVerified)
- `F-L0-003` — `ProceduralMeshFactory`(新規、Implemented/TestVerified)
- `F-L0-004` — `Win32Window`キーボード/マウス入力(新規、Implemented/TestVerified)
- `F-L9-001` — `UserGameProject`ゲームループ(新規、Implemented/RuntimeVerified)
- `F-L9-002` — ThirdPersonキャラクターコントローラー(新規、Implemented/TestVerified)
- `F-L8-001` — `OpenGameCommand`(新規、Implemented/CodeVerified)

## 実行したコマンドと結果
```
dotnet build src/CSharpGameEngine.Graphics.D3D12/...csproj -c Debug   → 成功、警告0/エラー0
dotnet test tests/CSharpGameEngine.Graphics.D3D12.Tests/...csproj      → 66件成功(新規21件含む)
dotnet build src/CSharpGameEngine.Platform.Windows/...csproj -c Debug → 成功、警告0/エラー0
dotnet build UserGameProject/UserGameProject.csproj -c Debug          → 成功、警告0/エラー0
dotnet test tests/UserGameProject.Tests/...csproj                     → 11件成功
dotnet build tools/CSharpGameEngine.VSExtension/...csproj -c Debug    → 成功、警告0/エラー0(.vsixパッケージング含む)
dotnet build CSharpGameEngine.slnx -c Debug                           → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Release                         → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx                                     → 159件成功、失敗0
```
`UserGameProject.exe`を実際に起動し4秒間クラッシュしないことを確認した上でスクリーンショットを
撮影し、空・地面・キャラクターボックスが意図通り描画されていることを目視確認した。

## リバースエンジニアリングした挙動
- `StageRenderer`は`ModelRenderer`のスワップチェーン/RTV・DSVヒープ/フレームフェンス/`Resize`
  骨格をそのまま流用しつつ、ルートシグネチャを2つのroot CBV(FrameCB/ObjectCB、テクスチャ
  descriptor tableなし)に変更した。`SetCamera`/`SetObjectTransform`は呼び出し時点の
  `_frameIndex`にのみ書き込むため、毎フレーム(RenderFrame呼び出し前)に必ず呼ぶ必要がある
  という暗黙の契約になっている(`InitializeConstantsToDefaults`が全フレームスロットを
  デフォルト値で初期化するため、呼び忘れても即座にクラッシュはしないが、古い変換行列が
  描画され続ける)。
- `Win32Window`のマウスキャプチャは、`WM_KILLFOCUS`でカーソル表示/`ClipCursor`解除を行い、
  `WM_SETFOCUS`で再キャプチャする。キー状態は`WM_KILLFOCUS`で全クリアされる
  (Alt-Tab中に押されたキーが「押しっぱなし」として残留するのを防ぐため)。
- `CharacterMotion.Compute`は`JumpPressed`のエッジ検出を呼び出し側(`Program.cs`)に委譲しており、
  それ自体は毎フレーム呼ばれる前提の純粋関数である。接地判定は`newY <= 0f`のみで、
  地面の実際の`Position.Y`(常に0)とは独立している。
- `OpenGameCommand`は`OpenViewerCommand`と1文字違わず同じ構造(`ProcessStartInfo`+
  `Process.Start`)で、引数だけが`--model Samples/Box.glb`から無し(`UserGameProject`は
  CLI引数を取らない)に変わっている。

## 意図と観測の一致
Conformance: Matches。ADR-0016に記載した意図した設計(レンダラー複製方針・入力方式・
ECS運用方針・ドッキング埋め込み見送りの判断)と、実際にビルド・テスト・目視確認で確認した
挙動の間に既知の乖離は無い。マウス/キーボードの対話的操作感そのものは自動検証していない
(ADR-0016「未解決事項」参照)。

## 残課題
- VSドッキングパネルへの本当の埋め込み(将来のADRで再検討)。
- カプセルメッシュ、物理/衝突判定エンジン、Query/System基盤。
- カメラの前後左右の直感一致の実機確認。
- 次のマイルストーンに進む前提条件: ドッキング埋め込みが必要になった場合、またはQuery/System・
  動的Add/Removeの具体的な要件が確定すること。
