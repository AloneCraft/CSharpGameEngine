# M0101 — ゲームコードの書き方の簡素化
- 日付: 2026-10-20
- 状態: 進行中（段階 1 まで完了）

## 目的
ユーザーの依頼（2026-10-20）「ユーザーが『こう書けたらいいな』と思えるコードを書いて。UE や unity のレガシーを清算したようなものを。ECS と Roslyn だからこそできるものを探って。逆算的にランタイムもそれを実現できるように書き換える」。決定は [ADR-0077](../Decisions/ADR-0077-Gameplay-Code-Simplification.md)、理想コードは [Templates/README.md](../../Templates/README.md) と `Templates/ThirdPerson`、`FirstPerson`、`TopDown`、`Vehicle`。

## 進捗
| 段階 | 内容 | 状態 |
|---|---|---|
| 0 | 理想コードを書く（4 テンプレート）。実現性を実コードに照らしてレビューする | 完了 |
| 1 | 引数の役割表の見直し: `ref State` を任意にする、`in T` を読み取りの Component にする | 完了 |
| 2 | UE 由来の名前の清算（キャラクター・カメラ・アニメーションの型とフィールド）。意味が変わる部分（`GameWindow` の統合、`ViewAngles` の turns 化と `Turn()`/`Planar()`）は後の段階へ | 完了（改名の範囲） |
| 3 | `in Parent<T>`（親の Component を型で読む） | 完了 |
| 4 | `[InputActions]`（入力を構造体で宣言）、`ViewAngles.Turn()`/`Planar()`、`AppHost.CaptureMouse` | 完了 |
| 5 | 音（`Audio` サービス、WAV の読み込み、XAudio2 のワンショット再生）。EngineHost のプロパティの型で渡すサービス | 完了 |
| 6 | イベント（`in EventWriter<T>` で送り、`in T` で1件ずつ受ける）。キャラクターの `Jumped`/`Landed`/`Footstep` | 完了 |
| 7 | 型付きアセット `Assets.*`。シーンが名指しするアセットの存在検査（SCENE208） | 完了 |
| 8 | UE5.7 の FirstPerson・TopDown テンプレートの移植（`Pointer`・`Picking`・`MoveIntent.MoveTo` を含む） | 完了 |
| 9 | Vehicle テンプレートの移植（簡易の車両モデル、三角形メッシュへの正確な光線、`Hud` サービス）と、4 テンプレートの VS 新規プロジェクトへの登録 | 完了 |
| 10 | フェーズの推論（入力とイベントは済み）、`GameWindow` の統合 | 未着手（ADR-0077 §2） |

## 段階 0. 理想コード
- 書いたもの: 4 つのテンプレートの `Code/*.cs`、`Scenes/*.json`、README。`.csproj` は置かない。置き場所は `Templates/`（ユーザー指示 2026-10-20: 先頭大文字）。
- レビューの結論: 全項目が実装可能。ただし `Start`、全面的なフェーズ推論、型付き `Assets` は縮める・見送る。理由は ADR-0077 §2。
- 見つかった問題（実装前の事実）:
  - 実行時に出したメッシュは描画されない（`LevelLoad` の描画物の一覧が初回で固定）。→ M0105 で、描画物が Entity の増減に追従するようになった。
  - `FixedTransform.cs` が Entity を Generation=1 固定で引き直す（スロット再利用後に位置解決が失敗する疑い）。未再現。→ M0105 で再現し、解決器のキーを `Entity` 全体にして直した。
  - ECS から音が鳴らせない（WAV のデコーダー、ミキサーの出力経路、`AudioSource` のクリップが無い）。

## 段階 1. 引数の役割表
- 実装: `GameSystemAnalyzer.AnalyzeMethod`。末尾の `ref State` を任意にした（SYSGEN013 を削除。記述子とリリース追跡の行も削除）。`in` の非 nullable struct（プリミティブを除く）を読み取りの Component として扱う。`[ReadOnlyComponent]` は従来どおり受け付ける。
- 結果: `GeneratedGameLoop` は `State` の引数を渡さない呼び出しを出せる。`in T` の Component は変更バージョンを刻まない。構造体の Context は書けなくなった。
- テスト: 新規 3 件（State 省略、`in` の読み取り、`in`+`ref` の重複）と、構造体の Context を使っていた 3 件を参照型へ移した。SYSGEN013 を期待していた 2 件を差し替えた。CodeAnalysis.Tests 220/220。
- 影響範囲の確認: エンジンの System、UserEcsProject、`Templates/CSharpGameEngine.Templates.ThirdPerson` に `in` の struct の Context は無く、ビルドは通る。

## 段階 2. 名前の清算
- 事実: 型名とフィールド名の使用箇所は、`src/Runtime.Character`・`Runtime.Animation`・`Runtime.Windows`、テスト、FeatureRunner、UE インポーター、ThirdPerson テンプレートの JSON に限られていた。UserEcsProject のシーンは使っていない。
- 実装: 追跡済みの 1089 ファイルへ型名を単語境界つきで一括置換し、フィールド名はファイルを限定して置換した。テンプレートのシーン JSON も同じ置換で書き換えた。`schemaVersion` は 3 のまま（利用者は同梱のテンプレートだけで、並行リーダーは作らない: ADR-0070 §7）。
- 型: `CharacterMovement`→`CharacterMotion`、`CharacterMovementState`→`CharacterMotionState`（`OnGround`/`Falling` を追加）、`CharacterMovementMode`→`CharacterMotionMode`、`CharacterInput`→`MoveIntent`（`Move`→`Direction`、`Jump`→`JumpRequested` と `Jump()`）、`PlayerController`→`ViewAngles`、`SpringArm`→`OrbitCamera`、`LocomotionAnimator`→`Locomotion`、`LocomotionAnimatorRuntime`→`LocomotionRuntime`。System は `CharacterMotionUpdate`、`OrbitCameraUpdate`。
- フィールド: `MaxWalkSpeed`→`WalkSpeed`、`MaxAcceleration`→`Acceleration`、`BrakingDecelerationWalking`→`Braking`、`BrakingDecelerationFalling`→`AirBraking`、`JumpZVelocity`→`JumpSpeed`、`MaxStepHeight`→`StepHeight`、`WalkableFloorAngleDegrees`→`MaxSlopeDegrees`（JSON は `MaxSlope`）、`RotationRateDegrees`→`TurnRateDegrees`（`TurnRate`）、`OrientRotationToMovement`→`FaceMovement`、`ControlYawDegrees`→`YawDegrees`（`Yaw`）、`ControlPitchDegrees`→`PitchDegrees`、`PivotOffset`→`Pivot`、`TargetArmLength`→`Distance`、`ProbeRadius`→`Radius`、`DoCollisionTest`→`Collide`、`IdleClip`/`WalkClip`/`JogClip`/`JumpClip`/`FallClip`/`LandClip`→`Idle`/`Walk`/`Jog`/`Jump`/`Fall`/`Land`。
- 残した名前: UE のステージ名と資産ファイル名、`PlayerControl`（静的ヘルパー。`[InputActions]` の段階で `ViewAngles` のメソッドへ寄せる）、`CapsuleRadius`/`CapsuleHalfHeight`（理想コードの入れ子の `Capsule` は未実装）、履歴文書（ADR・マイルストーン・Conformance・逆解析）の本文。
- 結果:
  - ソリューション全体がビルドできる（警告 0、エラー 0）。
  - CodeAnalysis.Tests 220/220、VSExtension.Tests 1175/1175、SceneModel.Tests 203/203、ほか全プロジェクトが合格。
  - `Windows.Tests` は、GPU を使わない `Runtime.Windows.Tests`（CharacterMotor・OrbitCamera・Animation を含む）が 185/185。
  - `verify-thirdperson-character`（WARP）で、改名後のテンプレートが W と Space で 15.7 m 歩き、1.228 m 跳ねて、D3D12 のエラー 0 で着地した。
- 環境の問題（改名とは無関係。2026-10-21 に対処）:
  - `Windows.Tests` の全件実行は、WARP を使う描画テストの途中でテストホストが終了コード 127 で落ちた。変更前の `HEAD` をビルドした別の作業ツリーでも落ちたので、今回の変更が原因ではない。落ちるテストクラスは実行ごとに違った。2026-10-21 には、通常実行 2 回と `--blame-crash` 付き 1 回のすべてが 443/445 で通り、再現しなくなった。原因は特定できていない（この機械の GPU や他のプロセスとの競合が疑われるが、未確認）。
  - `tools/CSharpGameEngine.SceneJson.Cli` の復元が `NU1902`/`NU1903`（`System.Security.Cryptography.Pkcs`/`Xml` 10.0.12 の既知の脆弱性）をエラーにした（`TreatWarningsAsErrors`）。`System.Security.Cryptography.Xml` の固定を 10.0.13 に上げて解消した（`Pkcs` は推移的に 10.0.13 になる）。ソリューション全体の復元は、監査を有効にしたまま警告なしで通る。
- 最終確認（2026-10-21）: `dotnet test CSharpGameEngine.slnx -c Debug`（復元と監査を有効のまま）が全プロジェクト合格。VSExtension.Tests 1175/1175、Windows.Tests 443/445（2 件スキップ）。

## 段階 3. `in Parent<T>`
- 実装: `Parent<T>`（`Entity` と `Value`。親の値の読み取り専用のコピー）を `Entities` に追加。`GameSystemAnalyzer` が `in Parent<T>` を関係として分類し（先に判定する。`Parent<T>` 自体が非 nullable struct のため）、子の Archetype に `Parent` を暗黙に加える。`GameSystemIndexAssigner` は `T` を Archetype に属さなくても Component として登録する（`TryGetComponent<T>` が生成される）。`GameSystemLoopEmitter` が、呼び出しの前に親を引き、親が死んでいる・`T` を持たない子をスキップする（変更済みの刻印より前に判定する）。
- 診断: SYSGEN036（`in` 以外、`T` が struct でない、`Spawn` で使用）、SYSGEN037（同じ `T` を自分の引数としても受ける、または `Parent<T>` を 2 つ書く。親と子が同じ Archetype に属し得て、読み書きが順序に依存するため）。
- 利用: `OrbitCameraUpdate` が `in Parent<ViewAngles>` を使うようになり、登録だけの `ViewAnglesQueryRegistration` を削除した。ThirdPerson テンプレートの `ThirdPersonAnimation` は `Entity` と `TryGetComponent` の 2 回の呼び出しが `in Parent<CharacterMotionState>` になった。`HierarchyQueryRegistration` は、`Parent<T>` を使う System が 1 つも無い構成でも `Parent` を登録するため残した。
- 残した制約: `OrbitCameraUpdate` は自分の `Transform` と親の `Transform` の両方が要るので、`Parent<Transform>` ではなく `Parent<ViewAngles>` の `Entity` から `TryGetComponent` で読む。親の `T` は変更検出と `[ScheduledSystem]` の競合検査に現れない。
- テスト: `GameSystemParentRelationTests` 7 件（親の値を読む、親が死んだ子と親の無い子のスキップ、生成コードの形、SYSGEN036 ×2、SYSGEN037 ×2、SYSGEN008）。CodeAnalysis.Tests 227/227。ソリューション全体のテストが合格（VSExtension 1175、Windows 443/445）。`verify-thirdperson-character`（WARP）は改名前と同じ値（歩行 15.700 m、跳躍 1.228 m、D3D12 のエラー 0）。

## 段階 4. `[InputActions]`
- 宣言: `Core/InputActionDeclarations.cs`（`CSharpGameEngine.Input`）に、属性 `[InputActions]`・`[Keys]`・`[Gamepad]`・`[Mouse]`、列挙 `Key`（値は Windows の仮想キーコード）・`Pad`・`MouseAxis`、値 `Stick`・`Look`・`Button` を置いた。理想コードの `Stick Look` は、レビューの指摘（視点の dt の扱いを `Stick` に暗黙に持たせない）に従って `Look` 型に分けた。ゲームパッドの列挙は、値の型 `Stick`/`Button` と名前が衝突するので `Pad.LeftStick`/`Pad.South` とした（理想コードの `Stick.Left`/`Button.South` から変更。`Templates/` の理想コードも合わせた）。
- 生成: `InputActionsEmitter`（`ForAttributeWithMetadataName`）が、`partial struct` ごとに `ContextId`、メンバー名から決める Action の ID（`StableId.FromName`。GUID は書かない）、`CreateContext()`、毎フレームの `Read(InputServer, DeltaSeconds)` を出す。全構造体は `GeneratedInputActions.CreateServer()` に集約される。診断は INPUT001〜004。
- System: `in PlayerActions` は Resource（Component ではない）。`GeneratedSystemSet` の `InputN` に保持し、System の呼び出しの前に `Read(EngineHost.Input, min(FrameDeltaSeconds, 0.1))` で更新する。入力の据え付けは最初の `RunFrame` で 1 回だけで、最初のフレームの入力を捨てない。入力を読む System は、`[SystemPhase]` を書かなければ FrameInput に入り、エンジンの `Window` System の後に動く（暗黙の `RunsAfter`）。明示した `[SystemPhase]` は優先される。`in` 以外と `Spawn` での使用は SYSGEN038。
- 視点: `Look` は、マウスの移動量（上へ動かすと Y が正）と右スティック（倒した量 × フレーム時間）を turns/フレームへそろえる（`Look.MouseTurnsPerCount`、`Look.StickTurnsPerSecond`）。`ViewAngles.Turn(Look)`、`ViewAngles.Planar(Stick)` を追加した。
- マウスの捕捉: `AppHost.CaptureMouse`（既定 false）。`Window` System が、自動実行でないときに捕捉する。
- 利用: ThirdPerson テンプレートの `ThirdPersonInput.cs`（GUID 手書きの `IA_*`/`IMC_*`）と `ThirdPersonControls.cs`（初回フラグ、`SetInput`、`CurrentFrame` の走査、マウスの捕捉）を、`PlayerActions` と `PlayerControls`（`Update` が 1 つ）に置き換えた。UE インポーターは入力を生成しなくなった。
- テスト: `InputActionsGeneratorTests` 10 件（生成物の形、集約、Resource の呼び出しと FrameInput、`Window` の後への並び、明示フェーズの優先、SYSGEN038、INPUT001〜004）。CodeAnalysis.Tests 237/237。WARP の `verify-thirdperson-character` は改名前と同じ値（歩行 15.700 m、跳躍 1.228 m、D3D12 のエラー 0）。マウスの視点は自動実行で再現できないので、`Look` の読み取りは生成コードの形のテストと手動確認に限る。
- 残したもの: `Axis`・`Pointer`・`Touch`・トリガー（TopDown と Vehicle の入力）、`GameWindow` への統合、`ViewAngles` の turns 化、キーの再割り当てとコンテキスト切り替え（ユーザー判断: 短さを優先）。

## 段階 5. 音
- 事実（実装前）: ECS から音は鳴らせなかった。WAV のデコーダー、ミキサーから XAudio2 への送出、`AudioSource` のクリップがどれも無かった。
- サービス: System の引数が、`EngineHost` のプロパティの型と同じ参照型なら、生成されたループが `Host.<プロパティ>` を渡す（`IPhysicsHost` の一般化）。`Audio` を引数に書くだけで使える。サービスを使う System があれば、`EngineHost` を名指ししなくてもループは `EngineHost` を受ける。
- `Audio`（`CSharpGameEngine.Runtime.Windows`）: `Play(Sound, Entity Source, Volume)`、`PlayLocal(Sound, Volume)`。要求は貯めておき、`Present` の直前（`AudioListener` の姿勢が確定した後）にまとめて処理する。距離で音量を下げ、左右に振る（モノラルのクリップ）。リスナーが無ければ距離なしで鳴らす。1 フレームに 32 件まで（超過は捨てて数える）。壊れた・無いファイルは理由を標準エラーに 1 回出して、以後は無視する。
- 再生: `AudioDeviceServer.PlayOneShot(PcmClip, Gain, Pan)` が、クリップごとに XAudio2 の source voice を作って再生する。終わった voice は毎フレーム `ReapOneShots` で破棄する。無音の代替（Silent）では何も鳴らさず、要求数だけ数える。デバイスの作り直しでは、鳴っている voice を先に破棄する。
- WAV: `WavReader`（RIFF/WAVE、PCM 8/16/24 ビットと 32 ビット float、モノラル/ステレオ）が `PcmClip`（16 ビット PCM）にする。これは容器の読み取りで、圧縮形式は読まない。ADR-0056 の「コーデックはビルド時に `.cgeaudio` へ変換し、実行時には持たない」とは別に、変換が入るまで `.wav` をそのまま読む（文書化した拡張）。
- 素材（2026-10-22 に差し替え）: UE のテンプレートには音声が無い。最初は `Assets/Sounds/{Jump,Land,Footstep}.wav` を合成した仮の音（`tools/SoundPlaceholders/Generate-PlaceholderSounds.ps1`。決定的）にしていたが、ユーザーの指示「UE5 から取り込む」に従い、ユーザーの UE5 プロジェクト（`G:\UE\5.0`、Epic の教材 Northwood）の SoundWave を、`tools/UnrealExport/import_template_sounds.ps1`（`export_sound_waves.py`）で UE5.8 から WAV に書き出して取り込んだ。足音は `A_Footstep_Dirt01`（ステレオ 44.1 kHz）、ジャンプは `sw_Foley_Jump_01`、着地は `sw_Foley_JumpLand_Tile_01`（モノラル 48 kHz）。ThirdPerson と FirstPerson は 3 つ、TopDown は足音と着地。同じ名前（`Footstep`・`Jump`・`Land`）なので `Assets.Sounds.*` は変わらない。足音は 1 つの素材を繰り返す（元には 8 種類の変化があるが、`Audio.Play` は 1 つの `SoundAsset` を受ける）。耳での確認は未実施。
- テスト: `WavReaderTests` 6 件（Audio.Tests 19/19）、`AudioServiceTests` 3 件（要求は flush まで待つ、壊れたファイルは無視、上限）、`TemplateSoundTests` 3 件、`GameSystemServiceTests` 3 件。

## 段階 6. イベント
- 型: `IEvent`（struct の印）、`EventWriter<T>`（`Send`）。`Update(in EventWriter<T> Events)` で送り、`Update(in T Event)` で受ける。受け手は Entity を回さず、送られたイベント 1 件ごとに呼ばれる。
- 寿命: イベントはそのフレームの間だけ存在し、フレームの頭（`RunFrame`、`RunBakeTick`、エディターの各フレーム）で破棄する。`GeneratedSystemSet` に型ごとのキューと、受け手のメソッドごとの読み取り位置を持つ。固定 tick を何度回しても、同じイベントを 2 回読まない。
- 配線（`GameSystemEventWiring`）: `[SystemPhase]` を書かない受け手は、送り手の最も遅いフェーズに入り、同じフェーズの送り手の後に並ぶ。受け手が送り手より早いフェーズを明示したら SYSGEN041（イベントが翌フレームの頭で消えるため）。送り手のいない受け手は SYSGEN042（警告。呼ばれない）。受け手が Component・Entity・`Parent<T>` を同時に取るのは SYSGEN040、イベント引数の形の誤りは SYSGEN039。
- キャラクター: `CharacterMotionUpdate` が `Jumped`（地面で `Jump()` を受けて浮いた）、`Landed`（降りた。`Impact` は落下の強さ 0〜1）、`Footstep`（地面を `CharacterMotion.StepLength` 歩くごとに 1 回。距離は `CharacterMotionState.StrideDistance`）を送る。GLB のクリップにアニメーションの通知は無いので、足音は歩幅から作った。`Event.Position` ではなく `Event.Source`（Entity）にした（階層の座標系では位置が意味を持たないため）。
- 利用: ThirdPerson テンプレートの `CharacterSounds`（イベントごとに `Audio.Play`）。カメラに `AudioListener` を加えた。
- テスト: `GameSystemEventTests` 6 件（フェーズと順序、イベントを 1 件ずつ読み次のフレームで破棄するコンパイルして実行するテスト、SYSGEN039〜042）。CodeAnalysis.Tests 246/246。
- 検証: WARP の `verify-thirdperson-character` が、改名前と同じ値（歩行 15.700 m、跳躍 1.228 m、D3D12 のエラー 0）に加えて、`audioRequests=15`（ジャンプ、着地、足音）、実機の XAudio2 で 15 件すべて再生（`audioStarted=15`）を確認した。検証は、レポートに音のフィールドがあれば 2 件以上を要求する。実際に耳で聞いた確認は未実施。
- 残したもの: 音量の距離減衰の係数（`AudioSpatialParameters.Default` 固定）、`AudioSource` のクリップ（常時鳴る音）、3D の定位（モノラルの左右のみ）。

## 段階 7. 型付きアセット
- 生成: `AssetCatalogEmitter` が、`Assets/` の下のファイルの一覧（AdditionalFiles。中身は読まない）から、`Assets.Sounds.Jump` のような入れ子の静的クラスと、型付きの参照を作る。`.wav` は `SoundAsset`、`.glb`/`.gltf`/`.fbx` は `ModelAsset`、`.png`/`.jpg` は `TextureAsset`（`CSharpGameEngine.Authoring`。文字列への暗黙の変換つき）。`Assets/Editor` は除く。識別子にならない名前（空白、数字始まり）は `_` に直し、同じ幹の別の拡張子は拡張子を付けて区別する。
- 検査: シーン JSON が `"Assets/…"` と書いたパスに対応するファイルが無ければ SCENE208（エラー。JSON の行と列に出る）。`UserEcsProject` と ThirdPerson テンプレートの既存のシーンはすべて通った。
- ビルド: `Game.props` が `Assets` の上の種類のファイルを AdditionalFiles に加える。SDK パッケージも同じ props を使う。
- 利用: `Audio.Play`/`PlayLocal` は `SoundAsset` だけを受ける（`ModelAsset` は渡せない）。ThirdPerson テンプレートの `CharacterSounds` は `Assets.Sounds.Jump` などを使い、手書きの `ThirdPersonSounds` は無くなった。
- テスト: `AssetCatalogTests` 5 件（入れ子と種類、識別子と衝突、アセットが無ければ出力なし、SCENE208 の位置、存在するアセットは通る）。CodeAnalysis.Tests 251/251。
- 検証: `verify-template-package`（SDK を pack して、生成した「My ThirdPerson」をパッケージだけでビルドして WARP で動かす）が、歩行 15.700 m、跳躍 1.228 m、D3D12 のエラー 0、`audioRequests=15` で合格。段階 1〜7 がパッケージ経由でも動く。
- 残したもの: シーン JSON のメンバーは文字列のまま（型は付かない。存在だけを検査する）。`ThirdPersonAssets`（UE インポーターが生成する `[SceneAssets]` の手書き風カタログ）は残っている。

## 段階 8. FirstPerson・TopDown の移植
- 事実: UE5.8 のインストールには Templates が無いので、UE5.7 のテンプレート（`G:\UE\5.8\<名前>` に作成した参照プロジェクト。手順は `tools/UnrealExport/README.md`）を出典にした。ステージ（`Lvl_FirstPerson`、`Lvl_TopDown`）とキャラクターの値は、`export_level.py` と `dump_blueprint.py` でマニフェストにして、C# の `LevelTemplateImporter` → `SceneJson.Cli convert` でシーン JSON にした。ThirdPerson の書き出しは触っていない。
- FirstPerson のステージ（2026-10-22 の修正）: UE の FirstPerson と ThirdPerson のレベルは、57 ノードの配置が浮動小数の誤差を除いて一致する（TopDown は別の配置）。FirstPerson から書き出したレベルは緑のマテリアルが白くなっていた（ThirdPerson の書き出しだけが緑を保っていた）ので、ユーザーの指示で、`Lvl_FirstPerson.json` と `Assets/Stage/Lvl_FirstPerson/Meshes` を ThirdPerson のレベル（名前空間・名前だけ置換）のコピーに置き換えた。`run_export_level.cmd FirstPerson` で書き出し直すと白くなる版に戻るので使わない。
- FirstPerson（`Templates/CSharpGameEngine.Templates.FirstPerson`）: 体は ThirdPerson と同じ `PlayerControls`。違いはシーンだけ（カメラは目の高さの `OrbitCamera`（`Distance` 0 = 一人称）、`FaceMovement` false、`CaptureMouse` true）。音は耳元なので `PlayLocal`。`verify-thirdperson-character --template FirstPerson`（WARP）が、歩行 16.000 m、跳躍 0.863 m、D3D12 のエラー 0、`audioRequests=15` で合格。
- TopDown（`Templates/CSharpGameEngine.Templates.TopDown`）: 入力はクリック。`[Mouse(MouseButton.Left)] Button SetDestination` と `[Pointer] Pointer Cursor` を宣言する（`Pointer` はクライアント領域の割合と `Inside`。`Read` の第 3 引数に `EngineHost.Cursor` が渡る）。`Picking` サービス（`EngineHost.Picking`）が、カメラの姿勢と画角（`Picking.TryCreateRay`）でポインターを通る光線を作り、物理の静的コリジョンに当てた最も近い点を返す。`ClickToMove` は `Picking.TryPick` の点を `MoveIntent.MoveTo` に渡すだけで、歩行は `CharacterMotionUpdate` が毎 tick `MoveIntent.Steer` で目的地へ向ける（直線、高さは無視、制動距離の手前から減速して行き過ぎずに止まる。着いたら `HasDestination` が false に戻る）。UE のナビメッシュは無く、経路探索はしない。固定のカメラは `ViewAngles`（Yaw 45°、Pitch −50°）を回さない `OrbitCamera`（`Distance` 14、衝突なし）で作った。理想コードの `Touch`・`Spawner.Spawn`（クリック位置の目印）・`Cursor.Ray` は未実装（`Cursor.Ray` は `Picking` に置き換えた）。
- 自動検証: `CGE_AUTOMATION_CURSOR=x,y`（カーソルの割合）と `MouseLeft` の入力（`Win32Window.InjectMouseButton`）を追加した。`verify-thirdperson-character --template TopDown` は `ClickToMoveVerification` が判定する（画面上部の 1 点を 5 フレームだけクリックし、離したあとも目的地まで歩いて止まること）。WARP で歩行 10.845 m、最後の 10 フレームの移動 0.000 m、D3D12 のエラー 0、`audioRequests=7` で合格。最終フレームの画像でもキャラクターが固定カメラの中央に立っている。
- テスト: `InputActionsGeneratorTests` に Mouse ボタンと Pointer の 1 件（既存の `Read` の期待値は 3 引数に更新）、`PickingTests`（光線の向きと画角、カメラの姿勢、不正な入力）、`CharacterMotorTests` に `MoveTo` の 3 件（目的地の 0.5 m 以内に止まり行き過ぎない、`Stop`、目的地が無ければ何もしない）、`ClickToMoveVerificationTests`。
- 残したもの: `Touch`、実行時に生成する目印（`Spawner`。M0105 で実装）、ナビメッシュ、`Vehicle`（車両の物理、Landscape の代わりの地面、HUD）、VS のテンプレートへの登録。

## 段階 9. Vehicle の移植と VS テンプレートの登録
- 事実: UE の Vehicle テンプレートは Chaos Vehicle（車輪ごとの物理）で動く。エンジンには車両物理も、画面上の文字も、地形も無い。実コードを見ると、**物理の `Raycast` は静的三角形メッシュに対しても外接ボックス（AABB）としか交差を見ていなかった**（坂の手前の何も無い所にも当たる）。このため、地面に沿って走る車にも、TopDown のクリックにも不正確だった。
- 物理（`Runtime.Physics`）: `Raycast` が静的三角形メッシュのボディを、三角形そのもの（BVH で絞って Möller–Trumbore。両面。法線は光線に向いた側）で判定するようにした（`PhysicsNarrowPhase.TryRaycastMesh`）。メッシュの置き場所（位置・回転）でローカルへ移す。Box などの他の形は従来どおり外接ボックス。`Sweep` はメッシュを扱わない。
- 車（`Runtime.Character`、`Vehicle`/`VehicleState`/`VehicleIntent` と `VehicleMotor`）: UE の車輪ごとの物理ではなく、**簡易の車両モデル**。`VehicleIntent`（`Drive` -1〜1、`Steering` -1〜1、`Handbrake`）を、毎固定 tick に `VehicleMotor.Step` が `LogicalScalar` で解く。前後の速度（アクセルは最高速度に近づくほど弱まる、ブレーキ、逆向きに踏むとブレーキ→後退、自然な減速、ハンドブレーキ）、横方向の速度（`Grip` で消える。ハンドブレーキでは `HandbrakeGrip`）、旋回（前輪の角度から自転車モデルで角速度を求め、速いほど切れ角を絞り、横加速度を `MaxLateralAcceleration` で頭打ちにする）、接地（4 つの車輪の位置から車体の下へ飛ばした光線のうち 2 つ以上が当たれば接地。当たった 4 点の平均の高さと面の法線へ車体を寄せる。外れたら重力で飛び、着地で地面へ入る速度だけを消す）、壁（進む向きの前の 2 隅から水平な光線を飛ばし、急な面までの隙間で前進を打ち切る。坂は壁ではない）。右はこのエンジンの座標系で前×上の向き（+R は左）。UE の車の挙動のうち、サスペンションの沈み、トルク曲線・変速による加速の変化、スリップ角に応じたタイヤの力、車体の転倒は再現しない。`VehicleChaseView` が根の `ViewAngles` の Yaw を車の向きへなだらかに回し、子の `OrbitCamera` が後ろに付く。
- 画面への出力（`Runtime.Windows/Hud.cs`、`EngineHost.Hud`）: 画面上の文字の描画が無いので、`Hud.SetTitle` がウィンドウのタイトルバーに速度とギアを出す（`Speedometer`）。同じ文字は更新しない。
- テンプレート（`Templates/CSharpGameEngine.Templates.Vehicle`）: レベル `Lvl_VehicleBasic` は UE のレベルを `run_export_level.cmd Vehicle` で取り込んだ（壁 4 枚、コーン 20、トンネル）。Landscape は glTF に書き出せず、エンジンに地形も無いので、同じ大きさ（壁の内側 127 m 四方）の厚さ 1 m の板で代える（このテンプレートの Landscape は高さ 0 で平ら）。車は `SM_SportsCar`・`SM_SportsCar_Glass`・`SM_SportsCar_Wheel` の静的メッシュ 3 種を UE の Blueprint の配置で組んだ（UE の `SKM_SportsCar` は物理用の骨格だけで、見た目は静的メッシュ）。ホイールベース 2.642 m・トラック 1.8 m は骨格の車輪の位置、カメラの腕 6.5 m・視野 90° は Blueprint の値。シーン JSON の角度は回転数（`MaxSteering` は 34° = 0.0944444）。操作は `PlayerActions`（`Stick Drive`: Y がアクセル/ブレーキ、X がハンドル。`Button Handbrake`）と `VehicleControls`（3 行）。
- 自動検証: `verify-thirdperson-character --template Vehicle` は `VehicleDriveVerification` が判定する（W を 40 フレーム押し、途中で D を 5 フレーム押して、進んだ距離、進む向きの変化、カメラの高さを見る）。WARP で、移動 48.281 m、旋回 38.3°、カメラの高さ 2.685 m（一定）、D3D12 のエラー 0。最終フレームで、車が壁の近くで後ろ姿のまま止まっている。
- VS への登録: `GameProjectGenerator` が、テンプレート名（`ThirdPerson`/`FirstPerson`/`TopDown`/`Vehicle`）から名前空間（`Cge<名前>`）と開くレベル（`Lvl_<名前>.json`、Vehicle だけ `Lvl_VehicleBasic.json`）を決める。ウィザードはテンプレート名で `ProjectTemplateSources/<名前>` から書く。`VSExtension.csproj` が 4 つの `.vstemplate`（1033・1041）とソースをパッケージに入れ、`VSPackage*.resx` に名前と説明（ID 136〜143）を持つ。
- テスト: `PhysicsMeshRaycastTests` 9 件（坂の表面の高さ、外接ボックスの中を通り抜ける光線が当たらないこと、下からの光線、置き場所と回転、ボックスは従来どおり）、`VehicleMotorTests` 22 ケース（接地と落下、加速と最高速度、自然な減速、ブレーキと後退、右・左・停止中の旋回、横加速度の頭打ち、ハンドブレーキの横滑り、壁、坂、坂からの離陸と着地、決定性、ギア）、`VehicleDriveVerificationTests`、`GameProjectGeneratorTests` と `VsixManifestTests` の 4 テンプレート化。Windows.Tests 491 合格（2 スキップ）、VSExtension.Tests 1185、FeatureRunner.Tests 179。
- 残したもの: エンジン音（`AudioSource` の音程を回転数に結ぶ仕組みが無い）、車輪の回転・ステア・サスペンションの動き、画面上の文字（`ScreenText`）、リセットとカメラ切り替え、`Axis` 入力（トリガーのアナログ値）、地形（Landscape）。

## 完了の判定
段階 1〜9 は、意図（ADR-0077 §1、§2 の M2）と観測（生成コード、テスト、WARP での実行）が一致している（Matches）。ただし `Touch` と `Spawner` は理想コードのままで、Vehicle は簡易モデル（段階 9 に範囲を書いた）。マイルストーン全体は段階 10 が残る。

## 残り
- 段階 10 を ADR-0077 §2 の順に進める。`GameWindow` の統合と `ViewAngles` の turns 化は未実施。
- Vehicle の見た目の残り（車輪の動き）と、エンジン音。
- 各段階で、リポジトリがビルドできる状態を保つ。
