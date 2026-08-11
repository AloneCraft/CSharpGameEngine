# M0018: AudioSource/AudioListener(RUFと同じゼロオーバーヘッド抽象化パターン)

## 目的

ユーザーの依頼: 「オーディオソース/オーディオリスナーを設計して、現在シーンを作るユーザーコードに
オーディオを配置し、音が再生できるようにSGで生成して実行できるようにしてほしい」。

当初XAudio2への直接依存で計画したが、ユーザーから「Windowsに依存させず抽象レイヤーを作り、
プラットフォーム依存はそこへ集中させる。実行時オーバーヘッドはSGで消すか最適化で呼び出しを消したい。
座標系xyz→RUFのように」という明確な差し戻しがあり、[[ADR-0025-RUF-Semantic-Coordinates]]と同じ
`static abstract`+struct制約ジェネリックFacadeパターンを採用する方針へ設計をやり直した。
設計判断は[[ADR-0039-Audio-Backend-Abstraction]]に記録した。

新しいSource Generator機能は追加していない — `AudioSource`/`AudioListener`は既存の
`[StartupScene]`/`GameSystemGenerator`パイプライン(M0017)がComponent集合を一切気にしない構造的
推論であるため、新しいComponent型を追加するだけでそのまま乗った。

## 実装したこと

### `src/CSharpGameEngine.Core/Audio/`(プラットフォーム非依存、L1 Core Kernel)
- `IAudioBackend.cs`: 全メンバーが`static abstract`のバックエンド契約。
- `AudioBackend.cs`: `Spatial<TConvention>`と同型のジェネリックFacade。
- `ProceduralAudioClip.cs`: サイン波PCM生成(ファイルI/O・アセットパイプライン不要)。

### `src/CSharpGameEngine.Windows/Audio/`(XAudio2、L4 Domain Servers)
- `XAudio2EngineHandle.cs`/`XAudio2VoiceHandle.cs`: `nint`だけを持つ安全なハンドルstruct。
- `XAudio2Backend.cs`: `IAudioBackend<XAudio2EngineHandle, XAudio2VoiceHandle>`実装。
  `TerraFX.Interop.DirectX`経由のXAudio2 COM呼び出し(追加パッケージ不要、既存
  `TerraFX.Interop.Windows`に同梱)。`StageRenderer`/`GraphicsDevice`と同じCOM作法を踏襲。
- `AudioException.cs`/`AudioDiagnostics.cs`: `D3D12Exception`/`D3D12Diagnostics`と同型。

### `UserEcsProject/`
- `AudioBackend.cs`(新規): プラットフォーム選択の集中点。`global using AudioEngineHandle = ...`/
  `AudioVoiceHandle = ...`の2エイリアス + `GameAudio`非ジェネリックFacade(`Spatial.cs`のXMLコメントが
  推奨する規約に倣う)。
- `Components.cs`: `AudioListener`/`AudioSource`を新規追加。`Level`へ`Listener`/`Source`/
  `AudioEngine`を、`FramePresent`へ`AudioEngine`を追加。
- `Systems/RuntimeSystem.cs`: `LevelLoad`を拡張(`StageRenderer`生成と同じタイミングで
  `GameAudio.CreateEngine()`→Listener/Source実Entity化)。新規`AudioListenerSpawn`(Spawn専用、
  ディスパッチ無し)/`AudioPlayback`(毎フレームVolume同期+終了時破棄)を追加。`Present`が
  `AudioEngine`を終了時に破棄するよう拡張。ファイル冒頭のArchetype出現順コメントを7→9件に更新。
- `Scenes/MainScene.cs`: `Application`の`Level`初期化子へ`Listener`/`Source`を追加(Volume=0.3、
  Loop=true)。これが「ユーザーコードへオーディオを配置する」の本体。

### テスト
- `tests/CSharpGameEngine.Core.Tests/Audio/ProceduralAudioClipTests.cs`(6件): サンプル数・振幅範囲・
  引数検証。
- `tests/CSharpGameEngine.Core.Tests/Audio/AudioBackendFacadeTests.cs`(2件): ハードウェア非依存の
  フェイクバックエンド(`struct FakeBackend : IAudioBackend<int,int>`)で、ジェネリックFacadeが全
  メンバーを実際に転送することを検証(抽象化が実際に機能する証明)。
- `tests/CSharpGameEngine.Windows.Tests/Audio/XAudio2BackendTests.cs`(2件): 実オーディオデバイスに
  対するCreateEngine→CreateVoice→SubmitBuffer→Start→Stop→DestroyVoice→DestroyEngineのスモークテスト
  (`[WindowsOnlyFact]`、既存GPUテストと同じ規約)。

### ドキュメント
`ADR-0039`(新規)、`M0018`(本書)、`Docs/Implementation-Status.md`へF-L1-007(このプロジェクト初の
Audio関連L1エントリ)・F-L4-001(このプロジェクト初のL4 Domain Serversエントリ)を追加。

## 検証結果(実際に実行して確認)

```
dotnet build src/CSharpGameEngine.Core/CSharpGameEngine.Core.csproj -c Debug
  → 成功、警告0/エラー0

dotnet test tests/CSharpGameEngine.Core.Tests/... -c Debug
  → 119/119 成功(既存111件 + 新規8件)

dotnet build src/CSharpGameEngine.Windows/CSharpGameEngine.Windows.csproj -c Debug
  → 成功、警告0/エラー0

dotnet test tests/CSharpGameEngine.Windows.Tests/... -c Debug
  → 69/69 成功(既存67件 + 新規2件、実XAudio2デバイスでのスモークテストが実機で成功)

dotnet build UserEcsProject/UserEcsProject.csproj -c Debug
  → 成功、警告0/エラー0

dotnet build CSharpGameEngine.slnx -c Debug → 成功、警告0/エラー0
dotnet test  CSharpGameEngine.slnx          → 全9プロジェクト成功、418件・失敗0
  (Entities 19, Core 119, Bridge.Hosting 8, SceneAuthoring.Cli 13, Import 30,
   Windows 69, UserGameProject 21, CodeAnalysis 99, VSExtension 40)
```

生成物の目視確認: `UserEcsProject/Generated/.../GameSystem.Types.g.cs`のArchetype出現順
(`Archetype0`〜`7`)が本変更の前後で完全に同一であることを確認した。新規`Archetype8 {AudioListener}`/
`Archetype9 {AudioSource}`が末尾に追加されただけで、既存Archetypeの番号は一切ずれていない
(既存Systemが`{AppHost, Level}`等を先に登録済みのため、`Level`/`FramePresent`へのフィールド追加
だけでは既存の出現順が動かない設計、ADR-0039参照)。

実機: `UserEcsProject.exe`を`Start-Process`で起動し、4秒後もプロセスが生存
(`AudioEngine.Create()`が実アプリ内で例外なく成功していることの証拠)、ウィンドウタイトルが
期待値(`"C# Game Engine - ECS FBX Rendering PoC"`)と一致することを確認した。

## 意図的に除外したこと(次マイルストーン候補として報告)

- **距離減衰・3D定位**。`AudioListener`と`AudioSource`は別Entityであり、閉集合Archetypeの制約上、
  同一`Update`呼び出し内で両者を横断参照できない(ADR-0033)。実装するには両者を同一Entityへ
  まとめるか、値を毎フレームコピーする別の仕組みが要る。
- **音声アセットのインポートパイプライン**(.wav/.mp3等)。現状はサイン波の実行時PCM生成のみ。
- **複数の同時再生音源**。`Level.Source`は単一値であり、`MainScene`が宣言する`AudioSource`は
  現状1個のみ(複数化する場合は`[SceneEntity]`プロパティを複数追加するだけで、既存の
  `[StartupScene]`APIがそのまま対応できる見込み — M0017で複数Scene Entityの決定論的順序生成は
  既に検証済み)。
- **2つ目のオーディオバックエンド**の実装(WASAPI直叩き等)。`AudioBackendFacadeTests`のフェイク
  実装で拡張性は検証済みだが、実際の2つ目のバックエンドは未着手。

## 残課題

- **可聴性そのものはコマンドラインから確認できていない**。ビルド成功・例外なし・HRESULT成功・
  プロセス生存確認・実デバイスでのスモークテスト成功までが自動検証の限界であり、実際に音が聞こえる
  かどうかはユーザー側での実機確認が必要。
