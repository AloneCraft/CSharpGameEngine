# ADR-0039: `IAudioBackend<TEngine, TVoice>`によるゼロオーバーヘッドAudioバックエンド抽象化

## ステータス

承認済み。[[ADR-0025-RUF-Semantic-Coordinates]]が座標系(xyz→RUF)で確立した「`static abstract`
インターフェース + `struct`制約付きジェネリックFacade」パターンを、AudioSource/AudioListenerの
再生バックエンド(XAudio2)へそのまま適用する。ADR-0001の層構造・依存方向は変更しない。

## 背景

`AudioSource`/`AudioListener`を実装するにあたり、当初はXAudio2(`TerraFX.Interop.Windows`、既存
PackageReference)を`UserEcsProject`から直接呼ぶ設計を検討した。ユーザーから明確な差し戻しがあった:
「Windowsに依存させるのではなく抽象レイヤーを作り、プラットフォーム依存はそこへ集中させる。実行時
オーバーヘッドはSGで消すか最適化で呼び出しを消したい。座標系xyz→RUFのように」。

調査の結果、`CSharpGameEngine.Spatial.Abstractions`(RUF)が既にこの要求を満たす実例だった:

- `ICoordinateConvention`(`src/CSharpGameEngine.Core/Spatial/ICoordinateConvention.cs`)は全メンバーが
  `static abstract`(C# 11 static abstract interface members)。
- `Spatial<TConvention>`(`Spatial.cs`)は`where TConvention : struct, ICoordinateConvention`という
  ジェネリック制約付き`static class`で、内部呼び出しは`TConvention.ToXyz(Value)`という型引数への
  直接静的呼び出し。
- 具体的Convention(`UnityCoordinateConvention`等)は`readonly struct`・フィールド無し・全メンバー
  `static`実装。

JITはジェネリック型引数(値型)ごとに個別コンパイルする(モノモーフィゼーション)ため、
`Spatial<UnityCoordinateConvention>.ToXyz(...)`は`UnityCoordinateConvention.ToXyz(...)`という直接
静的呼び出しに解決される — 仮想ディスパッチ・Boxing・Reflectionが原理的に存在しない
(`Docs/Specifications/Spatial-RUF.md`「Performance原則」節)。この構造をAudioへそのまま適用した。

## 決定事項

### 1. 層構造

```
CSharpGameEngine.Core(プラットフォーム非依存、外部依存ゼロ)
  Audio/
    IAudioBackend.cs      … 契約(static abstract、struct制約)
    AudioBackend.cs         … Facade(Spatial<TConvention>と同型)
    ProceduralAudioClip.cs  … 純粋関数(PCM生成)

CSharpGameEngine.Windows(windows TFM、unsafe、TerraFX依存)
  Audio/
    XAudio2EngineHandle.cs … readonly struct { internal readonly nint Pointer; }
    XAudio2VoiceHandle.cs
    XAudio2Backend.cs       … readonly unsafe struct : IAudioBackend<...>
    AudioException.cs / AudioDiagnostics.cs

UserEcsProject/AudioBackend.cs … プラットフォーム選択の集中点(global using 2行 + GameAudio Facade)
```

### 2. `IAudioBackend<TEngine, TVoice>`

```csharp
public interface IAudioBackend<TEngine, TVoice>
    where TEngine : struct
    where TVoice : struct
{
    static abstract TEngine CreateEngine();
    static abstract void DestroyEngine(in TEngine engine);
    static abstract TVoice CreateVoice(in TEngine engine, int sampleRate, int channels);
    static abstract TVoice SubmitBuffer(in TVoice voice, ReadOnlySpan<short> samples, bool loop);
    static abstract void Start(in TVoice voice);
    static abstract void Stop(in TVoice voice);
    static abstract void SetVolume(in TVoice voice, float volume);
    static abstract void DestroyVoice(in TVoice voice);
}
```

`SubmitBuffer`が`TVoice`を返す(`void`ではない)のは、`TVoice`が不変な値型であるため:
`XAudio2Backend`はPCMバッファを`NativeMemory.Alloc`で確保して`XAUDIO2_BUFFER.pAudioData`へ渡すが、
その解放は`DestroyVoice`まで遅延する必要があり、確保したポインタをハンドルへ折り込んで返す以外に
「後で覚えておく」手段が無い(mutableな`ref TVoice`ではなく不変値のやり取りに統一するための設計)。

`DestroyEngine`/`DestroyVoice`という技術中立な命名にした。XAudio2固有の実装詳細(EngineはCOM
`Release`、VoiceはCOM `Release`ではなく`IXAudio2Voice::DestroyVoice()`という非COM解放メソッド)を
契約名に漏らさないため。

### 3. `AudioBackend<TBackend, TEngine, TVoice>`(Facade)

`Spatial<TConvention>`と同型の`static class`。`where TBackend : struct, IAudioBackend<TEngine, TVoice>`
制約により、`AudioBackend<XAudio2Backend, XAudio2EngineHandle, XAudio2VoiceHandle>.SetVolume(...)`は
コンパイル時に`XAudio2Backend.SetVolume(...)`への直接静的呼び出しへ解決される。

### 4. ネイティブハンドルは`nint`で表現する

`XAudio2EngineHandle`/`XAudio2VoiceHandle`は内部に`nint`(生ポインタではなく`System.IntPtr`)だけを
持つ。生ポインタ型フィールド(`IXAudio2*`等)を含む構造体はそれ自体が`unsafe`修飾を要求し、それを
フィールドとして持つ`Component`(`AudioSource`/`Level`/`FramePresent`)や、それを扱う
`UserEcsProject/Components.cs`/`Systems/RuntimeSystem.cs`/`Scenes/MainScene.cs`もunsafeコンテキストを
要求されてしまう。`nint`は通常の値型なので、ネイティブポインタへのキャストは`XAudio2Backend`内部
(`unsafe`修飾済み)だけに閉じ込められ、ECS側は完全にsafeコードのまま保てる。

### 5. プラットフォーム選択の集中点(`UserEcsProject/AudioBackend.cs`)

```csharp
global using AudioEngineHandle = CSharpGameEngine.Audio.XAudio2.XAudio2EngineHandle;
global using AudioVoiceHandle = CSharpGameEngine.Audio.XAudio2.XAudio2VoiceHandle;

internal static class GameAudio
{
    public static AudioEngineHandle CreateEngine() =>
        AudioBackend<XAudio2Backend, AudioEngineHandle, AudioVoiceHandle>.CreateEngine();
    // ...残り7メンバーも同型
}
```

`Spatial.cs`のXMLコメントが推奨する「1プリセットを選んだプロジェクトは自分の非ジェネリックFacadeを
公開する」という規約(`Spatial<TConvention>`に対する`ProjectSpatial`相当)に倣った。将来別バックエンド
へ切り替える場合、変更箇所は`AudioBackend.cs`の3行(2つのalias + `GameAudio`が閉じる型引数)だけで、
`Components.cs`/`Systems/RuntimeSystem.cs`/`Scenes/MainScene.cs`は一切変更不要。

### 6. Component/System設計(既存パターンへの写像)

`AppHost.Window`(`Win32Window?`)/`Level.Renderer`(`StageRenderer?`)と同じ三段構成:

| | 役割 |
|---|---|
| Component | `AudioSource { PositionX/Y/Z, Volume, Loop, AudioVoiceHandle? Voice }`、`AudioListener { PositionX/Y/Z }` |
| 遅延生成 | `LevelLoad`が`StageRenderer`生成と同じタイミングで`GameAudio.CreateEngine()`を呼び、`Level.Listener`/`Source`をコピーして実Entityを作る |
| 毎フレーム同期・終了時破棄 | 新規`AudioPlayback : ISystem`が`Volume`を毎フレーム反映し、`!state.Enabled`で`Stop`+`DestroyVoice`。`Present`が`FramePresent.AudioEngine`を`DestroyEngine`する |

`Voice`フィールドは`AudioVoiceHandle?`(`Nullable<struct>`)。`Win32Window?`等の参照型null許容フィールド
と意味は同じ(未生成はnull)だが、ハンドル自体はネイティブポインタを持たない安全な値型。

`AudioListener`は`{AudioListener}`Archetype形状の宣言だけを行う`AudioListenerSpawn.Spawn`(ADR-0033の
`Spawn`用途)で登録され、ディスパッチ対象を持たない。

### 7. 音源データはファイルI/O無しで生成する

リポジトリに音声アセット(.wav等)が1件も存在しないため、`ProceduralAudioClip.GenerateSineWave`が
440Hz・1秒・振幅0.3倍のモノラル16bit PCMを実行時に生成する。アセットインポートパイプラインは
本ADRのスコープ外。

## 既知の制約

- **距離減衰・3D定位は未実装**。`AudioSource`と`AudioListener`は別Entityであり、閉集合Archetypeの
  制約上、同一`Update`呼び出し内で両者を横断参照できない(ADR-0033)。`AudioListener`は将来拡張の
  器として位置データのみ持つ。距離減衰を実装する場合、両者を同一Entityにまとめるか、値を毎フレーム
  コピーする別の仕組みが要る。
- 実オーディオ出力デバイスに依存するテスト(`XAudio2BackendTests`)は、デバイスが無い実行環境では
  失敗しうる(未検証、既存のGPU依存テストと同種のリスクとして許容)。
- 「実際に耳で聞こえるか」は自動検証の対象外。ビルド成功・例外なし・HRESULT成功・プロセス生存確認
  までが自動検証の限界。

## 代替案として検討したもの

- **XAudio2への直接依存**(当初案): ユーザーから明示的に差し戻された。プラットフォーム依存が
  `UserEcsProject`全体に染み出し、将来の別バックエンド追加が事実上不可能になる。
- **`IAudioBackend`をinstanceメソッドのインターフェースにし、ジェネリック型引数へstruct制約を課す
  だけで済ませる**: JITによるinstance interfaceメソッドのdevirtualizationは条件付きの最適化であり
  `static abstract`ほど確実ではない。RUFが`static abstract`を選んだのと同じ理由で採用しなかった。
- **ネイティブポインタをComponentへ直接持たせる**: `Component`/`UserEcsProject`側がunsafeコンテキスト
  を要求されるようになり、既存の「エンジンオブジェクトはComponentのフィールドとして持つが呼び出し側は
  安全なAPIしか見ない」という設計原則(ADR-0035)に反する。`nint`ハンドルで解決した。
- **`SubmitBuffer`を`void`にしてハンドルを`ref`で書き換える**: `TVoice`を不変値として扱う設計原則
  (`in`パラメータ)と矛盾する。戻り値で新しいハンドルを返す形に統一した。

## 帰結

### 得られたもの

- `Components.cs`/`Systems/RuntimeSystem.cs`/`Scenes/MainScene.cs`はいずれも`CSharpGameEngine.Windows`
  や`TerraFX.Interop.Windows`を一切参照しない(`AudioEngineHandle`/`AudioVoiceHandle`/`GameAudio`という
  名前だけを見る)。
- `AudioBackendFacadeTests`(フェイクバックエンド)が、この一般化されたジェネリック機構が実際に
  機能することをハードウェア非依存で証明している。
- RUFと同じ「1つの確立されたパターン」をAudioにも適用したことで、将来別のプラットフォーム依存機能
  (例: DirectX型の抽象化)にも同じ設計を再利用できる土台ができた。

### 失ったもの

- 抽象化層(`IAudioBackend`/`AudioBackend<TBackend,...>`/2つのハンドル型/`AudioException`/
  `AudioDiagnostics`)というコード量が、直接XAudio2を呼ぶ場合より増える。現状バックエンドは
  XAudio2の1つだけなので、この抽象化の恩恵(バックエンド差し替え)は未実証(RUFも同様に、現状
  `UnityCoordinateConvention`固定でしか使われていない)。

## 検証結果(実際に実行して確認)

```
dotnet build src/CSharpGameEngine.Core/CSharpGameEngine.Core.csproj -c Debug
  → 成功、警告0/エラー0

dotnet test tests/CSharpGameEngine.Core.Tests/... -c Debug
  → 119/119 成功(既存111件 + 新規8件: ProceduralAudioClipTests 6件、AudioBackendFacadeTests 2件)

dotnet build src/CSharpGameEngine.Windows/CSharpGameEngine.Windows.csproj -c Debug
  → 成功、警告0/エラー0

dotnet test tests/CSharpGameEngine.Windows.Tests/... -c Debug
  → 69/69 成功(既存67件 + 新規2件: XAudio2BackendTests、実デバイスでのCreateEngine/CreateVoice/
    SubmitBuffer/Start/Stop/DestroyVoice/DestroyEngineが例外を投げないことを実機で確認)

dotnet build UserEcsProject/UserEcsProject.csproj -c Debug
  → 成功、警告0/エラー0
```

`UserEcsProject/Generated/.../GameSystem.Types.g.cs`を目視確認し、`Archetype0`〜`7`が変更前と
完全に同一、`Archetype8 {AudioListener}`/`Archetype9 {AudioSource}`が新規追加されていることを
確認した(既存Systemが登録済みのComponent集合を再利用する設計により、既存の出現順は一切動かなかった)。

実機: `UserEcsProject.exe`を`Start-Process`で起動し、4秒後もプロセスが生存(`AudioEngine.Create()`が
実アプリ内で成功していることの証拠)、ウィンドウタイトルが期待値と一致することを確認した。

## 関連機能

- F-L1-007 — `CSharpGameEngine.Core`(`IAudioBackend`/`AudioBackend<TBackend,...>`/`ProceduralAudioClip`)
- F-L4-001 — `CSharpGameEngine.Windows`(`XAudio2Backend`、このプロジェクト初のL4 Domain Serversエントリ)

関連: [[ADR-0025-RUF-Semantic-Coordinates]]、[[ADR-0033-Static-Archetype-Ecs-Migration]]、
[[ADR-0035-Generated-Entry-Point-And-Engine-Objects-As-Components]]、[[ADR-0038-Startup-Scene-Declarative-Api]]


## Windows ownership update (ADR-0048)

XAudio2 is selected by the shared Windows Runtime facade. UserEcsProject no longer defines a global audio alias or backend facade.