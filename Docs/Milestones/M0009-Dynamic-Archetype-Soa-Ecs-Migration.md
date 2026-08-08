# M0009: 動的Archetype SoA ECSへの全面移行

## 目的

旧2系統のECS(`CSharpGameEngine.Ecs`のコンパイル時アーキタイプ、`GenerateEntityGenerator`の
AoS/SoA選択生成)を廃止し、ユーザーがComponent型と処理メソッドだけを書き、SGが引数から
Query・Read/Writeアクセス・SoAループ・依存順序・ゲームループを推論・生成する新方式へ全面移行する。
実行時のComponent追加・削除(構造変更)を初めて実装する。設計判断は
[[ADR-0029-Dynamic-Archetype-Soa-Ecs]]に記録した。

## 実装したこと

### 新ランタイム `src/CSharpGameEngine.Entities/`
- `Entity`/`ComponentType<T>`/`ComponentTypeRegistry`/`ComponentMask`/`ComponentTypeSet`/
  `ComponentColumn<T>`/`Archetype`/`EntityArchetype`/`World`: 動的Archetype生成、
  `CreateArchetype<T1..T8>`、`GetRef<T>`。
- 構造変更: `World`の`AddComponentDefault`/`AddComponentValue`/`RemoveComponent`
  (`internal`)、Archetypeのadd/removeエッジキャッシュ、`CopySharedColumns`によるO(k)列コピー。
- `EntityQuery`/`QueryMatches`: `World.ArchetypeCount`ベースの差分スキャン。
- `EntityCommandBuffer`/`ComponentValueStore<T>`: FIFO記録+`Playback`、一時Entityハンドル
  (`Entity.Temporary`)。
- `[GameSystem]`/`[Update]`/`[Context]`/`[CurrentEntity]`/`[Commands]`/`[Without]`属性。
- `tests/CSharpGameEngine.Entities.Tests/`: 51件。

### `GameSystemGenerator`(`src/CSharpGameEngine.CodeAnalysis/`)
- `GameSystemAnalyzer`/`GameSystemModel`/`GameSystemExecutorEmitter`: 引数からのQuery/
  Read-Write推論、メソッドごとの独立したExecutor型生成。
- `GameSystemScheduler`/`GameSystemLoopEmitter`: Write→Read依存順序のKahn法トポロジカル
  ソート、`.Collect()`ベースの単一`GeneratedGameLoop`生成(このリポジトリ初の結合出力
  パターン)。
- `GameSystemDiagnostics`: SYSGEN001〜020。
- `tests/CSharpGameEngine.CodeAnalysis.Tests/`: `GameSystemGeneratorDiagnosticsTests.cs`
  (20診断)、`GameSystemGeneratorEmissionTests.cs`(7件、テキスト形状+コンパイル可能性+
  決定性+依存順序)、`GameSystemGeneratorEndToEndTests.cs`(4件、実World/Entity実行)。

### `samples/SoaSystemsDemo/`(新規)
ユーザー提示サンプルをほぼそのまま再現。`DemoScenario.Run()`が1シナリオでQuery一致・
SoAループ・`[Context]`/`[CurrentEntity]`/`[Commands]`・構造変更・Archetype動的生成・
Queryキャッシュの差分更新を同時に検証し、"OK"を返す。

### UserGameProjectの移行
- `Archetypes.cs`削除。`Components.cs`に`MovableTag`(新規、MovingPropSystemのQuery
  disambiguation用)、`FrameContext`(新規、`[Context]`用)、`InputState.WasJumpKeyDown`
  (新規フィールド、フレームまたぎ状態の持ち場所)を追加。
- `Systems/InputSystem.cs`/`PlayerControlSystem.cs`/`CameraSystem.cs`/
  `MovingPropSystem.cs`/`RenderSyncSystem.cs`を全て`[GameSystem]`/`[Update]`形式へ
  書き換え。`Camera`(`CSharpGameEngine.Graphics.D3D12`の素の`struct`)をプレイヤーEntity
  自身のComponentにした(別のシングルトンEntityにすると2Entityにまたがる読み書きになり、
  1メソッド=1Archetype族というモデルに合わないため)。`RenderSyncSystem.SyncProp`は
  Query推論により`StageProp`/`MovableStageProp`の両方に自然に一致するため、旧実装が
  2メソッド必要だった処理が1メソッドに統合された。
- `Player`のSpawnするComponent数が`CreateArchetype`の型引数上限8に達したため、どの
  システムからも読み書きされていなかった`Velocity`をPlayerの構成から外した。
- `Program.cs`を`GeneratedGameLoop.Update(world, stageRenderer:, win32Window:,
  frameContext:)`(名前付き引数)の1呼び出しへ書き換え。スポーンコードは
  `world.CreateEntity(archetype)` + `world.GetRef<T>(entity) = value`へ変更。
- `Entities/`(Particle 6ファイル)+`benchmark`サブコマンド削除。
  `ユーザーが定義するコードとSGが生成するコード例.cs`は内容を`samples/SoaSystemsDemo/`へ
  昇格した上で削除。
- `tests/UserGameProject.Tests/PlayerControlSystemTests.cs`を新API向けに書き換え
  (`PlayerControlSystem.Control`を`ref`/`in`のローカル変数で直接呼ぶだけになり、World/
  Entityが不要になった)。`ParticleStorageTests.cs`/`BenchmarkHarnessTests.cs`削除。

### 旧実装の一括削除
`src/CSharpGameEngine.Ecs/`(12ファイル)、`src/CSharpGameEngine.Generation.Abstractions/`
(2ファイル)、`CSharpGameEngine.CodeAnalysis`の`EcsArchetype*.cs`(5)+`GenerateEntity*.cs`(8)、
`tests/CSharpGameEngine.Ecs.Tests/`(9ファイル)、`CodeAnalysis.Tests`の`EcsArchetype*`・
`GenerateEntity*`(12ファイル)を削除。`AnalyzerReleases.Unshipped.md`からECSGEN・ENTGENを
削除。`CSharpGameEngine.slnx`・`UserGameProject.csproj`・`CodeAnalysis.Tests.csproj`から
該当プロジェクト参照を除去。`samples/AosSoaGeneratedExpansion/`はエンジン非参照の
自己完結ファイルのため保持し、歴史資料である旨をREADME(新規)に明記した。

## 意図的に除外したこと

- `Win32Window`のマウスキャプチャがフォーカス再取得後に急回転する既存バグの修正
  (本移行とは無関係、実機確認中に新たに発見。「残課題」参照)。
- `World.CreateArchetype`の9型引数以上への拡張(現状8で全ての実利用箇所を満たせるため)。
- AoS-vs-SoAベンチマークの代替手段の提供(SoA一本化の直接の代償として失われることを
  ユーザーに明示し受け入れられた)。

## 変更したFeature ID

- `F-L3-001`〜`006`、`F-L7-003`・`004`・`006`〜`008`(既存ID、Removedへ更新) — 旧
  `CSharpGameEngine.Ecs`/`CSharpGameEngine.Generation.Abstractions`関連
- `F-L3-007`〜`010`(新規) — `CSharpGameEngine.Entities`(Entity/World/動的Archetype、
  構造変更、EntityQuery、EntityCommandBuffer)
- `F-L7-009`〜`011`(新規) — `GameSystemGenerator`(Analyzer/ExecutorEmitter、
  Scheduler/LoopEmitter、診断SYSGEN001〜020)
- `F-L9-005`(新規) — `samples/SoaSystemsDemo`
- `F-L9-003`・`F-L9-004`(既存ID更新) — `SceneLoader`とステージスポーン、ECS入力抽象化と
  ゲームプレイSystems群

## 実行したコマンドと結果

```
dotnet build src/CSharpGameEngine.CodeAnalysis/...csproj -c Debug  → 成功、警告0/エラー0
dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/...          → 成功、121件
  (Step7時点、GameSystemGenerator関連33件新規+既存88件)
dotnet run --project samples/SoaSystemsDemo                        → "OK"
dotnet build UserGameProject/UserGameProject.csproj -c Debug       → 成功、警告0/エラー0
dotnet run --project UserGameProject                                → 実機描画・移動確認
  (下記「リバースエンジニアリングした挙動」参照)

旧実装削除後:
dotnet build CSharpGameEngine.slnx -c Debug / -c Release           → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx                                   → 成功、13プロジェクト・354件全成功
  (CSharpGameEngine.CodeAnalysis.Tests: 121件→36件(旧Ecs/GenerateEntity関連85件削除)、
   他12プロジェクトは無改修のまま全件成功)
```

## リバースエンジニアリングした挙動

- `samples/SoaSystemsDemo`の`DemoScenario.Run()`を実際に実行し、以下を数値レベルで確認した:
  Archetypeを跨いだSoAループの正しい読み書き、`[Without]`による除外、`[Context]`/
  `[CurrentEntity]`/`[Commands]`の正しい転送、構造変更が行ループ中には反映されず
  `GeneratedGameLoop`末尾の`Playback`でのみ反映されること、そしてフレーム間に新規生成した
  Archetypeを既存のキャッシュ済みQueryが次フレームで正しく検出すること
  (「新しいArchetypeが生成されたときだけQuery Cacheを更新する」という要件の直接証拠)。
- `GameSystemExecutorEmitter`の実装当初、生成するExecutor型名を`{MethodName}_Executor`
  (クラス名を含めない)にしていたところ、異なる`[GameSystem]`クラスが同名メソッド
  (例: 複数クラスの`Move`)を持つと同一名前空間で型名が衝突するバグを自己レビューで発見し、
  `{ClassName}_{MethodName}_Executor`に修正した。実際に2クラス同名メソッドのケースを
  `CompileAndLoad`で最後までコンパイルさせて回帰確認した。
- `GeneratedGameLoop.Update`のContext引数名は当初`context0`/`context1`という不透明な連番
  だったが、UserGameProject移行で3つのContext型(`StageRenderer`/`Win32Window`/
  `FrameContext`)を同時に使う実例に当たった際、呼び出し側がFQNの辞書順という非直感的な
  規則を暗記しないと正しい引数順序で呼べないという実用上の欠陥に気付き、単純型名の
  先頭小文字化(衝突時連番、予約語エスケープ)へ変更した。
- UserGameProjectの実機描画確認で、リモートのスクリーンショット取得ツール
  (`SetForegroundWindow`による前面化)が`Win32Window`のマウスキャプチャに干渉し、
  フォーカス再取得のたびにカメラのPitchが極端な角度へ飛ぶ現象を観測した。原因を
  `Win32Window.cs`のコードから特定した: `ClipCursor`はOSによりフォーカス喪失時に自動解除
  されるが、`_capturedCenterScreenPoint`(前回のカーソル中心)は再計算されないため、
  フォーカス再取得時の実カーソル位置との差分が巨大な一回限りのデルタとして
  `ConsumeMouseDelta`に現れる。この問題自体はECS移行と無関係な既存バグであり、当初は
  修正をスコープ外としたため、`window.SetMouseCaptured(true)`を一時的にコメントアウトして
  検証し、確認後に元へ戻した。この状態でW前進キーを模擬入力し、プレイヤーが実際に移動した
  後のスクリーンショットで、空・タイル張りの床・PBRシェーディングされた影付きプロップが
  破綻なく描画されることを確認した。
- **(2026-08-05追記、ユーザー依頼により対応)** 上記のマウスキャプチャバグを修正した。
  `Win32Window`に`_skipNextMouseDelta`フラグを追加し、`SetMouseCaptured(true)`直後および
  `WM_SETFOCUS`での再キャプチャ直後の次の1回の`ConsumeMouseDelta`呼び出しだけは、内部の
  再センタリングは通常どおり行いつつ常に(0,0)を返すようにした(再現性の高いピンポイントな
  対策 - 根本原因の正確なタイミングの完全な特定はできていないが、症状が発生しうる箇所
  そのものを塞ぐ防御的な修正)。修正前後で同一の`SetForegroundWindow`連打シナリオを
  再現し、修正前は毎回Pitchが極端な角度まで飛んでいたのに対し、修正後は2回連続で
  水平な視点のまま安定することをスクリーンショットで確認した。`Win32Window`には
  既存の単体テストが無い(実機確認のみに委ねる既存の規約、F-L0-004参照)ため、この修正も
  同じ規約に従い実機再現テストのみで検証した。`dotnet test CSharpGameEngine.slnx`は
  修正後も354件全成功。
- 旧`CSharpGameEngine.Ecs`削除の副作用として、UserGameProject.csprojの手動編集中に
  `CSharpGameEngine.Ecs`プロジェクト参照および`AllowUnsafeBlocks`が一時的に元の値へ
  戻っている状態を検出した(VSの同時編集によるものと推測、詳細は特定できず)。実際に
  `CSharpGameEngine.Ecs.csproj`が既に削除済みだったため、この状態のままではビルドが
  壊れる。ECS移行という本タスクの目的そのものと直接矛盾する状態だったため、
  `CSharpGameEngine.Entities`参照への差し替えを再適用した上でビルド成功を確認した。

## 意図と観測の一致

Conformance: Matches。[[ADR-0029-Dynamic-Archetype-Soa-Ecs]]に記載した意図した設計と、
実際のビルド・テスト・実行結果の間に既知の乖離は無い。UserGameProjectのマウスルック自体は、
当初ECS移行と無関係な既存バグ(上記)により最初のスクリーンショット確認では無効化して
検証したが、そのバグ自体を同じマイルストーン内で修正し、修正後の再現テストでも
Matchesであることを確認した。

## 残課題

- `World.CreateArchetype<T1..T8>`の8型引数という上限。9個以上のComponentを持つEntityを
  スポーンする実利用が発生した場合、上限拡張または`EntityCommandBuffer`経由の段階的
  Component追加のいずれかを再検討する。
- AoS-vs-SoAの再実行可能な比較手段は失われたまま([[ADR-0029-Dynamic-Archetype-Soa-Ecs]]
  で明示的に受け入れ済みのトレードオフ)。
- マウス/キーボードの実機での操作感そのものは、ADR-0016から引き継いだ既存の制約により
  自動検証していない。
