# ADR-0029: 動的Archetype SoA ECSへの全面移行(`CSharpGameEngine.Entities` + `GameSystemGenerator`)

## ステータス
Superseded by [[ADR-0033-Static-Archetype-Ecs-Migration]](静的(閉集合)Archetype ECSへの全面移行、M0013)。
本ADRが導入した動的Archetype方式(`World`の`Dictionary<ComponentTypeSet, Archetype>`、`EntityQuery`の
差分スキャン、`EntityCommandBuffer`、`[GameSystem]`/`[Update]`等の属性)は削除済み。本ADRの中核だった
「引数からQuery・Read/Writeアクセスを推論する」という発想はADR-0033にも引き継がれているが、
Component引数がすべて`ref`になったことでRead/Writeの区別が消え、本ADRが実装したWrite→Readの自動
実行順序決定(`GameSystemScheduler`)は原理的に成立しなくなり失われた(ADR-0033決定事項3)。

## 背景

既存のECSは2系統併存していた: [[ADR-0015-Archetype-Ecs-Static-Runtime]]の`CSharpGameEngine.Ecs`
(コンパイル時に`[EcsArchetype]`型を宣言する静的アーキタイプ)と、[[ADR-0024-Generated-Entity-AoS-SoA]]
の`CSharpGameEngine.Generation.Abstractions`/`GenerateEntityGenerator`(`[GenerateEntity]`で
AoS/SoAを選択生成)。いずれも「ユーザーがEntity型ごとにArchetype/Store型を宣言し、Query・
foreach・ゲームループを手書きする」という設計で、実行時のComponent追加・削除(構造変更)は
ADR-0015で明示的に非目標と記録されたまま未実装だった。

ユーザーから、次の新方式への全面移行の依頼があった:

- 内部データレイアウトはSoAのみ
- ユーザーはComponent型と処理メソッドだけを書く(Query・foreach・Chunk走査・ゲームループは書かない)
- 処理メソッドの引数からQuery条件とRead/Writeアクセスを推論する
- 各処理メソッドを独立した実行単位として扱う
- SGがQuery・SoAループ・依存関係・ゲームループ登録を生成する
- Entityの Component 追加・削除、Entity生成・破棄を実行時に許可する
- 構造変更はCommand Buffer経由で安全な地点にまとめて適用する
- Archetypeは実行時のComponent集合によって決まる
- 新しいArchetypeが生成されたときだけQuery Cacheを更新する
- ホットループ内ではComponent有無の判定を行わない
- ReflectionやType Dictionaryによる検索をホットループに入れない

依頼には実行可能なイラストレーション用サンプル(`MovementSystems.Move`/`HealthSystems.Regenerate`/
`DisableDeadEntity`、`in MovableTag _`というタグ引数パターン)が添付されていたが、このサンプルは
`MovableCharacterChunk`/`DisabledCharacterChunk`という2つのArchetypeを直接ハードコードしており、
「Archetypeは実行時のComponent集合によって決まる」という本文の要件と矛盾していた。この矛盾を
ユーザーへ明示的に確認した(後述「決定事項」参照)。

## 決定事項

### 1. Archetypeは実行時に動的生成する(Unity DOTS方式)

コンパイル時の閉じた列挙、またはハイブリッド方式ではなく、実行時に実際のComponent集合から
動的にArchetypeを生成する方式を採用した。添付サンプルの`MovableCharacterChunk`/
`DisabledCharacterChunk`は「イラストレーション目的の説明であり、実装対象そのものではない」
という位置づけに明示的に読み替えた。

「動的生成」と「ホットループにReflection/Type Dictionaryを入れない」を両立させる鍵は、
`ComponentType<T>.Id`の静的初期化時に列ファクトリデリゲート(`() => new ComponentColumn<T>()`)を
1回だけ登録すること: 実行時に「ID 42の列を作れ」と言われても`MakeGenericType`は使わず、
焼き込み済みデリゲート配列の添字呼び出しで解決する。詳細は[[Ecs-Soa-Systems]]参照。

### 2. 旧実装は完全削除しSoA一本化する

旧`CSharpGameEngine.Ecs`(AoSではないが、Chunk1つ=1Archetype全体という別方式)と
`CSharpGameEngine.Generation.Abstractions`/`GenerateEntityGenerator`(AoS/SoA選択生成)は、
新実装への移行完了と同じ変更の中で完全に削除した。新旧を並行稼働させる案、AoSだけを残す案は
不採用。

### 3. 依存関係は実行順序の決定のみに使う(単一スレッド)

Write→Read依存エッジは並列実行の有効化には使わず、単一スレッドでの決定的な実行順序を
決めるためだけに使う。並列実行の有効化、または「依存関係解析なし・宣言順のみ」は不採用。

### 4. 今回のパスで全て完了させる(段階分割しない)

設計案の初期段階では「新サンプル先行、UserGameProjectは後続マイルストーン」という2フェーズ案も
検討したが、これは「旧実装を完全削除する」という決定②と両立しない
(`UserGameProject/Archetypes.cs`・`Systems/*.cs`・`Program.cs`が旧APIに直接依存しているため、
削除するなら同じパスで移行するしかない)。ユーザーはこの矛盾を指摘された上で「今回すべて
完了させる」ことを選び、UserGameProject移行と旧実装の一括削除を単一のパス(本ADR・M0009)で
行った。

### 5. 新ランタイムのプロジェクト名は`CSharpGameEngine.Entities`

将来的な`CSharpGameEngine.Ecs`への改称は行わない。

### 6. ユーザー可視の型名は`Entity`/`EntityCommandBuffer`

添付サンプルの`EntityId`/`EntityCommands`という名前ではなく、既存の`Entity.cs`(旧`Ecs`から
引き継いだ構造)とDOTS流の`EntityCommandBuffer`という名前を採用した。

### 7. `[Without(typeof(X))]`を「引数から推論する」原則の唯一の例外として追加する

除外条件は引数の書き方では表現できないため、メソッド属性として明示的に用意した。

## 実装

新ランタイム`src/CSharpGameEngine.Entities/`は、旧`CSharpGameEngine.Ecs`の
`Entity`/`ComponentColumn<T>`/`ArchetypeStorage`のswap-back+2倍成長`EnsureCapacity`/
`World`のfree-list ID割当+Generationという実証済みの部分を流用しつつ、動的Archetype生成・
構造変更・Query・Command Bufferを新規実装した。詳細な仕様は[[Ecs-Soa-Systems]]を参照。

`GameSystemGenerator`(`CSharpGameEngine.CodeAnalysis`)は既存2ジェネレータ
(`EcsArchetypeGenerator`/`GenerateEntityGenerator`)と同じAnalyzer→Model→Emitter3層・
診断ゲート・`List<string>`→`string.Join("\n",…)`(改行コード非依存)規約を踏襲した上で、
このリポジトリ初の`.Collect()`ベースの結合出力(`GameSystemLoopEmitter`、コンパイル内の
全`[GameSystem]`クラスを1つの`GeneratedGameLoop`へ集約)を追加した。

## 帰結

### 得られたもの
- 実行時のComponent追加・削除(構造変更)が初めて可能になった(ADR-0015の既知の制約を解消)。
- ユーザーはQuery・foreach・ゲームループを一切書かなくなった。`RenderSyncSystem.SyncProp`の
  ように、旧実装で2メソッド必要だった処理(`StageProp`/`MovableStageProp`それぞれの専用
  コレクション)が、Query推論により1メソッドへ自然に統合される例も出た。
- システム間の実行順序を手動管理する必要がなくなった(`Program.cs`の呼び出し順序が
  そのまま更新順序、という旧設計から解放された)。

### 失われたもの
- AoS-vs-SoAの再実行可能なベンチマーク([[M0006-Generated-Entity-Benchmark]])。実測値は
  ドキュメントに記録として残るが、SoA一本化によりAoSバックエンドの選択肢自体が無くなったため
  再測定は不可能になった。ユーザーに明示した上で受け入れられた、決定②の直接の代償。
  `samples/AosSoaGeneratedExpansion`はエンジン非参照の自己完結ファイルなので歴史資料として
  残す(README追加)。

### 新たに発見した既存バグ(本ADRの対象外、記録のみ)
- UserGameProject移行後の実機描画確認中に、`CSharpGameEngine.Platform.Windows.Win32Window`の
  マウスキャプチャ(`SetMouseCaptured`)が、ウィンドウがフォーカスを失って再取得した際に
  大きな一回限りの回転デルタを生む問題を発見した(`ClipCursor`はフォーカス喪失時にOSにより
  解除されるが、`_capturedCenterScreenPoint`は再計算されないため)。実際のプレイでもAlt+Tab後に
  カメラが急回転する形で再現しうる。本ADRのスコープ外のため修正はせず、[[M0009-Dynamic-Archetype-Soa-Ecs-Migration]]
  に記録した。

## 検証結果(実際に実行して確認)

```
dotnet build src/CSharpGameEngine.CodeAnalysis/... -c Debug     → 成功、警告0/エラー0
dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/...       → 成功、121件(旧削除前)→36件(旧削除後)+新規テスト
dotnet run --project samples/SoaSystemsDemo                     → "OK"
dotnet build UserGameProject/UserGameProject.csproj -c Debug    → 成功、警告0/エラー0
dotnet run --project UserGameProject                            → 実機描画確認(スクリーンショット、M0009参照)
dotnet build CSharpGameEngine.slnx -c Debug / -c Release        → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx                                → 成功、13プロジェクト・354件全成功
```

## 関連機能

- F-L3-007〜010 — `CSharpGameEngine.Entities`(Entity/World/動的Archetype、構造変更、
  EntityQuery、EntityCommandBuffer、新規)
- F-L7-009〜011 — `GameSystemGenerator`(Analyzer/ExecutorEmitter、Scheduler/LoopEmitter、
  診断SYSGEN001〜020、新規)
- F-L9-005 — `samples/SoaSystemsDemo`(新規)
- F-L9-003、F-L9-004 — `SceneLoader`とステージスポーン、ECS入力抽象化とゲームプレイSystems群
  (UserGameProjectの移行、既存ID更新)
- F-L3-001〜006、F-L7-003・004・006〜008 — 旧`CSharpGameEngine.Ecs`/
  `CSharpGameEngine.Generation.Abstractions`関連(Removedへ更新)
