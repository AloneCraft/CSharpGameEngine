# ADR-0101 — Android を対象にしたビルドと F5 デバッグ(段階1: 描画なし)
- 状態: Accepted(2026-11-19。ユーザーの依頼:「ビルド対象を android にできるようにして。F5 で実行したらエミュレーターと接続している android でデバッグできるようなプロファイル追加して」。範囲はユーザーが「段階1: 描画なしで動かす」を選んだ)
- 位置づけ: ADR-0098 の「1.0 まで Windows x64 と Direct3D 12 だけ」の例外。段階1は**ゲームの処理を Android で動かし、VS から配置・デバッグできる**ところまで。描画・音・入力は Windows だけのまま。2 つ目のプラットフォーム(段階 D)を本格的に始める決定ではない
- 関連: ADR-0003(L0 の境界)、ADR-0065(ゲームの取り込み規約 `Game.props`)、ADR-0070 B1(エディターの外部描画先)、ADR-0087(描画の層)、ADR-0098(目標と段階)、ADR-0099(名前と置き場所)

## 用語
| 語 | 意味 |
|---|---|
| TFM(対象フレームワーク) | .NET のビルドの対象。`net10.0-windows10.0.19041.0` と `net10.0-android` |
| ソリューションのプラットフォーム | VS のツールバーで `x64` などを選ぶ欄。ここに `Android` を足した |
| 複数対象(マルチターゲット) | 1 つの csproj を複数の TFM で同時にビルドすること。最初はこれで作ったが、ビルドのたびに Android 版も作って遅くなるのでやめた |
| ワークロード | .NET SDK に後から足す道具一式。Android 向けには `android` が要る |
| APK | Android のアプリの配布ファイル |
| エミュレーター | PC の中で動く仮の Android 端末 |
| 逐次解釈(インタープリター) | コードを機械語にせず 1 命令ずつ解釈して動かす方式。遅い |
| `GameActivity` | Android でアプリを開いたときに最初に出る画面 |
| ソースジェネレーター | ビルド時にコードを自動で作る仕組み。ゲームの `Main` もこれが作る |

## 背景
- 描画は Direct3D 12 だけ(約 12,400 行)。音は XAudio2、ウィンドウと入力は Win32。どれも Android には無い。
- ゲームは `Runtime.projitems` の共有ソースをそのまま取り込み、`EngineHost` が Win32 のウィンドウ・Direct3D 12・XAudio2 を直接持っていた。
- `InputServer`(入力の窓口)とキー・ゲームパッドの名前は Windows に依存しないのに、Windows 専用の `Windows.csproj` に置かれていた。[InputActions] から生成したコードも `Win32InputEventAdapter` を参照していた。
- Android の SDK はアプリの `OutputType=Exe` を `Library` に変える(`Microsoft.Android.Sdk.DefaultProperties.targets`)。そのため、ソースジェネレーターは Main を作らなかった(観測: 最初の試行で「入口が無い」と表示)。

## 決定

### 1. Android はソリューションのプラットフォームで選ぶ
- ゲームの csproj で、`Game.props` を読み込む**前に** `<CgeTargetPlatforms>Windows;Android</CgeTargetPlatforms>` と書くと、そのゲームは Android 向けにもビルド**できる**ようになる(`Platforms` に `Android` が加わる)。書かなければ今まで通り Windows だけ。
- 実際に Android 向けを作るのは、プラットフォーム(`Platform`)が `Android` のときだけ。そのとき TFM は `net10.0-android` だけになり、それ以外のプラットフォームでは `net10.0-windows10.0.19041.0` だけになる。普段のビルドは Android 版を作らない。
- `CSharpGameEngine.slnx` のプラットフォームに `Android` を足し、ThirdPerson だけを `*|Android` → `Android` に対応づけた。対応づけの無い C# プロジェクトは今まで通り `AnyCPU`、`x64` 指定のものは `x64` でビルドされる(MSBuild が作る一覧で確認)。そのため `Android` に切り替えても、ほかのプロジェクトは同じ出力を使い回し、作り直さない(観測: `Any CPU` → `Android` → `Any CPU` と切り替えたとき、戻した直後にコンパイルされたのは ThirdPerson の Windows 版と、毎回コンパイルされる VS 拡張とその試験だけ。ThirdPerson は、パッケージの復元結果 `obj/project.assets.json` が対象ごとに書き換わるため 1 回作り直す)。
- ソリューションのプラットフォームの名前 `AnyCPU` を `Any CPU` に変えた。プラットフォームを指定しないビルド(`dotnet build CSharpGameEngine.slnx`、CI の `dotnet test`)は、`Any CPU` という名前があればそれを、無ければ名前の順で最初のものを選ぶ。`AnyCPU` のままだと `Android` が先頭になり、既定のビルドが Android 版を作っていた(観測)。名前を変えた後、既定は `Debug|Any CPU` で、`Any CPU` と `x64` での各プロジェクトの対応づけは変更前と同じ(MSBuild が作る一覧で比較)。
- CI はプラットフォームを指定しないので `Any CPU` でビルドし、Android の道具が無くても変わらない。
- このリポジトリでは `Templates/ThirdPerson` だけを Android 対応にした。利用者の新しいゲームの csproj は `tools/ProjectTemplates/GameProjectGenerator.cs` が別に作るので、影響しない。
- 最初は複数対象(Windows と Android を同時に作る)で作ったが、ビルドのたびに Android 版も作るのでやめた(ユーザーの判断)。

### 2. Android のビルドの中身(`Game.props`)
- `CgePlatform`(Windows / Android)を TFM から決める。`-f net10.0-android` で TFM を直接指定しても Android になる。ゲームが自分で `TargetFrameworks` を書いた場合の外側のビルド(各 TFM のビルドを束ねるだけで、自分ではコンパイルしない)では空にする。複数対象で作っていたとき、外側のビルドでもシーンのスキーマとシェーダーの出力が動き、`Cge.dll scene-schema ""` の異常終了とプロジェクト直下への `Shaders` の出力が起きた(観測)。スキーマは Windows のビルドだけが書く。
- プラットフォーム `Android` はCPUの名前ではないので、`PlatformTarget` を `AnyCPU` にする。APK には android-arm64 と android-x64 の両方が入る。Android では `CGE_PLATFORM_ANDROID` を定義し、`Windows.csproj` を参照しない。
- Direct3D 12 のシェーダー、距離場、フォント、アセットのコピーは既定で作らない(Android で読むものが無い)。
- Android SDK が自動で足す `Android.Widget` などの using を外す。ゲームのコードは Android の型を使わない(`Button` が `CSharpGameEngine.Input.Button` とぶつかった)。
- Android SDK がアプリに付ける XML ドキュメントの出力を、コンパイルの直前に外す(`Game.targets` の `CgeDropAndroidDocumentationFile`)。Windows のビルドは出力しないので、両方で同じ検査になる。
- Debug でも逐次解釈を使わない(`UseInterpreter=false`)。測定: API 36 x86_64 のエミュレーターで ThirdPerson が、逐次解釈では毎秒約 6 フレーム・CPU を使い切り、切ると毎秒約 59 フレーム(60 回/秒の上限)・CPU 20%。ブレークポイントでのデバッグはどちらでも使える。Android での C# ホットリロードは使えない。

### 3. プラットフォームの違いは `EngineHost` に集める
- `EngineHost` に次を置き、Windows と Android の違いをこの中の `#if CGE_PLATFORM_ANDROID` に閉じ込める。
  - `PumpPlatformEvents`: ウィンドウのメッセージと入力。Android では、アプリの画面が閉じられたら false。
  - `HasRenderSurface` / `CreateLevelRenderer`: 描く先とレンダラー。Android には描く先が無いので、レベルは読み込まれない(ウィンドウを作る前のフレームと同じ扱い)。
  - `PlayOneShot` / `Audio`: 音。Android では鳴らさず、ミキサーはどこにもつながない。
- ウィンドウ、外部描画先(エディター用)、カーソル、自動検証の入力の差し込み、WIC による画像の読み込みは Windows だけ。
- 後で Vulkan などを足すときは、`EngineHost` のこの部分を変える。

### 4. 入力の名前はエンジン本体に置く
- `InputServer` を `src/Engine/Core/InputServer.cs` へ移した(名前空間は `CSharpGameEngine.Input` のまま)。
- キーとゲームパッドの名前を `CSharpGameEngine.Input.InputControls` に置いた。`Win32InputEventAdapter` の同名の公開メンバーは残し、これを呼ぶ。生成コードは `InputControls` を参照する。

### 5. Android での起動
- `src/Runtime/Android/`(`Runtime.projitems` に Android のときだけ入る)。
  - `GameActivity`: アプリを開くと出る画面。描画の代わりに、状態・フレーム数・経過時間・エラーを文字で出す。
  - `AndroidGameHost`: 生成された Main を専用のスレッドで動かし、1 フレームごとに約 60 回/秒へ抑える。画面が閉じられたら終わるよう頼む。
- ソースジェネレーターは、コンパイルに `AndroidGameHost` があるとき、ライブラリでも Main を生成し、アセンブリを読み込んだときに `[ModuleInitializer]` で `AndroidGameHost.SetEntryPoint(Main)` を呼ぶコードも作る。リフレクションを使わないので、リリース版で不要コードが削られても Main は残る。
- エディターのプレビュー用の生成コード(`GameSystem.EditorPreviewBridge.g.cs`)は `#if !CGE_PLATFORM_ANDROID` で囲む。Windows のエディターだけが使う。

### 6. F5
- VS のツールバーでプラットフォームを `Android` にすると、ThirdPerson の対象が Android になり、F5 の横につながっている端末とエミュレーターが一覧に出る(VS の Android 拡張が端末ごとの起動プロファイルを自動で作る)。そのまま F5 で、ビルド・配置・起動・デバッガー接続まで行う。
- 起動プロファイル(`launchSettings.json`)は起動のしかたを決めるだけで、何をビルドするかは決められない。そのため、ビルドの対象はプラットフォームで選ぶ。`Templates/ThirdPerson/Properties/launchSettings.json` には Windows 用の 1 件だけを置く。

## 結果
- 観測(2026-11-19): ThirdPerson を `dotnet build -t:Run -p:Platform=Android` でエミュレーター(MaterialFall_API36、x86_64)へ配置・起動し、`Running`・フレーム数が毎秒約 60 増えることを画面の撮影で確認した。「戻る」で閉じるとループが終わり(CPU 0%)、開き直すと新しく始まる。
- ビルド時間は記録しない。一度測った「普段のビルド」の数字は、既定のプラットフォームが `Android` になっていた間のもので、比較にならなかった。
- 回帰の確認: ソリューション全体のビルド(`x64` と `Android` の両方で警告・エラーなし)、`エンジンのテスト` 1690 件、`Windowsのテスト` の入力・実行時・音 723 件(1 件は名前付きパイプの時間切れの試験で、単独の 3 回はすべて合格)、Windows 版 ThirdPerson の自動実行 30 フレーム(D3D12 のエラー 0、効果音 16/16、スクリプトの入力で移動)。
- 段階1でできないこと: 描画、音、タッチや画面のキー入力、アセットの APK への同梱、パッケージモード(SDK パッケージ経由のゲーム)、VS の F5 からのデバッガー接続の実地確認(VS の画面操作が要るため未実施)。
- `src/Runtime/Windows/` という置き場と `CSharpGameEngine.Runtime.Windows` という名前空間は、Android でもそのまま使う。名前の整理は段階2以降で決める。

## 段階2以降(未決定)
- Vulkan(または OpenGL ES)のレンダラー、シェーダーの SPIR-V への変換、AAudio などの音、タッチ入力、アセットの同梱、パッケージモード。
