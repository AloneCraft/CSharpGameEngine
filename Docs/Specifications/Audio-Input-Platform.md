# Audio, Input, and Platform specification

- Status: Implemented for portable input, Win32 event adaptation, deterministic Input Actions, audio device lifecycle, offline mixing, listener-relative spatial audio, artifact-backed voice recovery, and read-only Platform/Input/Audio diagnostics
- Parent index: [Roadmap](../Roadmap.md), [Implementation Status](../Implementation-Status.md)
- Decisions: [ADR-0056](../Decisions/ADR-0056-Platform-Input-And-Audio-Server-Boundaries.md)
- Target layers: L0, L1, L4, L8

## Scope and observed baseline

Windows/Win32 input, XAudio2 abstractions, and the current Windows runtime are baseline evidence. M0064 implements the portable value contracts, QPC-stamped Win32 event adaptation, and deterministic action processing. The EngineHost-owned audio device server implements native creation, operational silent fallback, explicit recreation, and disposal. Its portable mixer owns immutable PCM clips and decoded stream artifacts, buses, voice cursors, spatial gain/pan, looping, limits, typed underrun policy, and typed recovery decisions. Protocol v5/debug snapshot v2 copies bounded Platform/Input/Audio diagnostics into the existing Preview Host request without transferring service or native ownership.

Windows is the first backend: Win32 and XAudio2 live in L0. The corresponding L1/L4 contracts are platform-neutral so another backend can be added without pulling native types above L0.

## L1 value contracts

```csharp
public readonly record struct MonotonicTimestamp(long Ticks);
public readonly record struct WindowId(StableId Value);
public readonly record struct InputDeviceId(StableId Value);
public readonly record struct InputActionId(StableId Value);
public readonly record struct AudioBusId(StableId Value);
public readonly record struct AudioVoiceId(StableId Value);
public sealed record InputEvent;
public sealed record InputActionValue;
```

The contracts carry values and explicit ownership only; they expose neither `HWND`, Win32 message types, COM pointers, XAudio2 interfaces, nor codec provider types. `MonotonicTimestamp` is sourced from QPC on Windows and is converted into a process-monotonic timeline with a documented frequency/provenance.

## Server data flow

```text
Win32 message / device notification -> L0 event adapter -> timestamped L1 InputEvent
  -> L4 Input Server -> contexts/chords/dead zones/rebinds -> deterministic InputAction sequence

asset stream artifact -> L4 Audio Server -> buses/mixer/voice allocator -> L0 XAudio2 adapter
  -> hardware output or mandatory silent/offline PCM reference backend
```

Focus loss, pointer capture release, window destruction, and hotplug force release actions for every held key/button/axis state. The action server processes events in monotonic timestamp plus stable device/sequence order, so the same raw trace produces the same action sequence.

## Input contract

Action maps declare context priority, chord predicates, normalization, dead zones, and rebind policy. Rebind validates collision and reserved actions before it becomes active. A context transition emits releases for controls no longer valid; it cannot leave stale pressed state in gameplay. Diagnostics include timestamp, window/device/action IDs, context, raw value, transformed value, and release reason.
### Value reads and modifiers (ADR-0067)

Gameplay reads an action's current value with `InputServer.ReadValue`. The highest-priority active context that binds the action answers, and the value is the sum of all its bindings. This is how Unreal's Enhanced Input combines W/A/S/D into one 2D move.

A binding transforms its raw value in a fixed order. The dead zone and scale apply first. The ordered `InputModifier` list follows: `InputNegateModifier` per axis, and `InputSwizzleModifier` for Unreal's YXZ swizzle.

Relative controls are `RelativeAxis1D` and `RelativeAxis2D`: raw mouse movement and the wheel. They hold no state. The events of one `ProcessEvents` call are accumulated, and the sum is readable until the next call.

Keyboard, mouse, and gamepad controls use Unreal key names (for example `SpaceBar`, `Gamepad_Left2D`, and `Gamepad_FaceButton_Bottom`), so imported mappings bind without translation. Raw mouse movement is `Delta`, which corresponds to Unreal's `Mouse2D`.

XInput pad 0 is polled once per `DrainInputEvents()`. It emits button, stick, and trigger changes, plus Connected and Disconnected events; a disconnect releases every held control.

Mouse-look capture hides and clips the cursor only while the window owns focus. `ClipCursor` is system-wide, so a background or hidden window applies the capture when it receives focus.

The game window takes no text input, so it has no input method context: an open IME (a Japanese one in hiragana mode) would turn the keys it processes into `VK_PROCESSKEY`, and movement keys would not reach the game. The window removes its input context association when it is created.

Automation input (`CGE_AUTOMATION_INPUT`, e.g. `W@20-40;SpaceBar@50-51`) presses keys by presented frame through the same adapter path as the window procedure.

## Audio contract and recovery

The Audio Server owns mixer graph, buses, voice limits, gain/pan/spatial parameters, loop points, decoded stream artifacts, and source lifetime. The silent/offline backend is required and renders deterministic PCM buffers for test oracles. Hardware/XAudio2 is optional additional evidence and cannot be a required headless test result. Spatialization accepts only a listener-local `Vector3` produced after the Q64.64 reference-frame transform; it derives linear range attenuation and stereo pan and never creates a global float coordinate.

Runtime codec dependency is avoided by converting source into stream artifacts in L7. An optional codec or provider is behind an L4 adapter and cannot leak types into artifacts/public APIs. An underrun increments a typed counter and follows an explicit silence/stop policy.

Audio device state is `Unavailable`, `Creating`, `Ready`, `Recovering`, `Faulted`, `Disposed`; backend mode is independently `None`, `XAudio2`, or `Silent`. On device loss the server stops submission, releases the old native generation exactly once, and recreates the backend. A successful hardware or silent generation restores only `RestoreFromArtifact` voices at the mixer-owned logical frame; all other voices emit `StoppedNotEligible`. A failed recreation without fallback leaves logical voice state unchanged for an explicit retry. Restoration uses `AssetArtifactReference` and never a live native pointer.

## Immediate migration and failure policy

When a target feature lands, it replaces the affected native facade, input abstraction, codec reader, or audio path in the same change. It removes the old public API and old artifact reader and regenerates test artifacts. Failure reports identify server state, device/window/action/voice IDs, timestamp, native HRESULT/provider identity where applicable, and recovery decision.

## Performance and compatibility

Headless contract tests use the silent backend and WARP where graphics diagnostics are involved. Windows x64 Debug/Release are mandatory; actual audio hardware, GPU hardware, and Experimental Instance are supplemental evidence. Performance stores five warm-ups plus thirty samples, median/p95/MAD, backend mode, buffer configuration, and machine fingerprint.


## Stage oracle matrix

| Feature | Level | Stage | Mode | Timeout | Oracle |
|---|---:|---|---|---:|---|
| F-L1-008 | 1 | `platform.boundaries` | Headless / Foundation | 900s | Portable values contain no native types. |
| F-L0-010 | 2 | `platform.window-input` | Headless / Foundation | 900s | QPC monotonicity and forced releases. |
| F-L4-009 | 2 | `audio.lifecycle` | Headless / Foundation | 900s | Device/server ownership transitions. |
| F-L4-010 | 3 | `input.actions` | Headless / Foundation | 900s | Deterministic actions for contexts/chords/rebind. |
| F-L4-011 | 3 | `audio.mixer` | Headless / Foundation | 900s | Offline PCM mixer/voice/underrun oracle. |
| F-L4-012 | 4 | `audio.spatial` | Headless / Foundation | 900s | Spatial/stream/device recovery policy. |
| F-L7-034 | 6 | `platform.compatibility` | Headless / Foundation | 3600s | Required Windows cells separate optional hardware. |
| F-L7-035 | 7 | `platform.release` | Headless / Foundation | 7200s | Scoped platform provenance and closure. |

All rows are Implemented/TestVerified with executable stages. `Platform-Compatibility-Matrix.json` references the required Debug/Release solution, Windows-TFM, and WARP CI cells and the executable prerequisite Stage closure; hardware Audio/GPU are optional Manual Evidence references and never substitute for required correctness. `platform.release` re-executes that prerequisite and binds its recent configuration/Git-matched result, the exact Platform source/verification closure, required adapter modes, optional hardware snapshot, and machine/timestamp provenance into one canonical SHA report. Product packaging, signing, SBOM, and the immutable product release manifest remain owned by F-L7-045/F-L7-048.

## M0064 deterministic input vertical slice

`Win32Window.DrainInputEvents()` is the only public raw-input boundary. Keyboard, mouse buttons/wheel, raw relative mouse, focus/capture loss, device removal, and destruction become ordered `InputEvent` values; the removed polling APIs are not retained. `AppHostRuntime` owns one `InputServer`, and the FrameInput phase finalizes exactly one action frame before simulation.

Showcase actions use stable IDs and a dedicated context. Context priority, chords, one/two-dimensional dead zones, reserved actions, rebind collision checks, forced cancellation/release, and canonical action hashing are covered by Debug/Release contract stages.

## 入力の宣言(`[InputActions]`、ADR-0077)

ゲームは、操作を `[InputActions] partial struct` で宣言する。メンバーは `Stick`(2 軸の状態値)・`Look`(このフレームに視点を回す量 turns)・`Button`(`Held`/`Pressed`/`Released`)・`Pointer`(画面上のカーソル)で、割り当ては `[Keys]`・`[Gamepad(Pad.*)]`・`[Mouse(MouseAxis.Delta)]` で書く。

- Source Generator が `InputContext`、Action の ID(型名とメンバー名から `StableId.FromName`)、毎フレームの `Read` を生成し、全構造体を 1 つの `InputServer`(`GeneratedInputActions.CreateServer()`)にまとめる。最初の `RunFrame` で 1 回だけ据え付ける。
- System は `in PlayerActions` で受ける。呼び出しの前に、その時点の `InputServer` から読み直す。入力を読む System は、`[SystemPhase]` を書かなければ FrameInput に入り、エンジンの `Window` System の後に動く。固定フェーズに置くと、tick が 0 回のフレームで `Pressed` を逃し、複数 tick のフレームで同じ `Look` を重ねて受けるので、明示しないこと。
- `Look` はマウスの移動量(`MouseTurnsPerCount`)と、スティックの倒した量にフレーム時間を掛けたもの(`StickTurnsPerSecond`)を同じ単位にする。
- `AppHost.CaptureMouse` が true なら、ウィンドウがマウスを捕捉する(自動実行を除く)。
- 診断: INPUT001(partial struct でない)、INPUT002(メンバーの型)、INPUT003(割り当てが型に合わない)、INPUT004(割り当てなし)、SYSGEN038(`in` 以外、`Spawn`)。
- マウスのボタンは `[Mouse(MouseButton.Left)]` で `Button` に割り当てる。`[Pointer] Pointer` は画面上のカーソル(クライアント領域の左上を 0、右下を 1 とした割合と `Inside`)で、`Read` の第 3 引数 `EngineHost.Cursor` が毎フレーム埋める。3D の位置は `Picking` サービス(System の引数に `Picking` と書く)が求める。`Picking.TryPick(Pointer, out PickHit)` は、カメラの姿勢・縦の視野角・ウィンドウの縦横比でポインターを通る光線(最大 2000 m)を作り、物理の静的コリジョンの最も近い当たり(`Entity`、`Point`、`Normal`、`Distance`)を返す。カメラが物理と別の参照フレームにある、ポインターが領域の外、何にも当たらないときは false。
- 自動検証: `CGE_AUTOMATION_CURSOR=x,y`(割合)がカーソルを代わりに与え、`CGE_AUTOMATION_INPUT` の `MouseLeft@開始-終了` がマウスの左ボタンを押す。
- 未実装: `Axis`・`Touch`・トリガー、実行時の再割り当て、コンテキストの切り替え。

## 画面への出力(`Hud`、ADR-0077)

System の引数に `Hud Hud` と書くと、生成されたループが `EngineHost.Hud` を渡す(`Audio` と同じく、`EngineHost` のプロパティの型で渡すサービス)。`Hud.SetTitle(string)` はウィンドウのタイトルバーの文字を替える。同じ文字を毎フレーム渡しても、変わったときにだけウィンドウを更新する。画面上の文字の描画(UI)はまだ無いので、Vehicle テンプレートの速度計(速度とギア)がこれを使う。ウィンドウが無い実行(エディターのプレビューなど)では何もしない。

## 効果音(`Audio`、ADR-0077)

ゲームの System は `Audio Audio` を引数に取ると、`EngineHost.Sound` を受け取る(EngineHost のプロパティの型と同じ参照型の引数は、生成されたループがそのプロパティを渡す)。

- `Audio.Play(string Sound, Entity Source, float Volume = 1)`: `Source`(Entity)の位置から鳴らす。リスナー(`AudioListener`)との距離で音量が下がり、モノラルのクリップは左右に振られる。リスナーが無ければ距離なしで鳴る。
- `Audio.PlayLocal(string Sound, float Volume = 1)`: 位置を持たない音。
- `Sound` はプロジェクト相対の `.wav` のパス(出力先へコピーされる `Assets/` の下)。PCM 8/16/24 ビットと 32 ビット float、モノラルとステレオを読む。圧縮形式、壊れた・無いファイルは、理由を標準エラーに 1 回出して無視する。
- 要求は貯めておき、フレームの最後(`Present` の直前)にまとめて処理する。リスナーの姿勢が確定した後なので、System の実行順に依らない。1 フレームに 32 件を超える分は捨てる。
- 再生は `AudioDeviceServer.PlayOneShot`。XAudio2 ではクリップごとに source voice を作り、終わったものを毎フレーム破棄する。無音の代替では鳴らさず、要求数だけ数える。
- 未実装: 常時鳴る音(`AudioSource` のクリップ)、距離減衰の設定、3D の定位、ビルド時の `.cgeaudio` への変換(ADR-0056)。

## イベント(ADR-0077)

`IEvent` を実装した struct がイベントになる。送り手は `in EventWriter<T>` を引数に取って `Send` し、受け手は `in T` を引数に取る(Entity、Component、`Parent<T>` は同時に取れない)。

- 受け手は、そのフレームに送られたイベント 1 件ごとに呼ばれる。同じフレームの後で送られたものも、読み取り位置が進むので 1 回ずつ読む。イベントはフレームの頭で破棄される。
- `[SystemPhase]` を書かない受け手は、送り手の最も遅いフェーズに入り、同じフェーズの送り手の後に並ぶ。受け手が送り手より早いフェーズを明示すると SYSGEN041、送り手がいないと SYSGEN042(警告)。
- キャラクターは `Jumped`、`Landed`(`Impact` は 0〜1)、`Footstep`(`CharacterMotion.StepLength` を歩くごとに 1 回)を送る。どれも `Source`(キャラクターの Entity)を持つ。
