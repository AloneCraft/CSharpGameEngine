# ADR-0035: エントリポイントの生成と、エンジンオブジェクトのComponent化

## ステータス

承認済み。[[ADR-0033-Static-Archetype-Ecs-Migration]]決定事項10の「メインループ本体は生成しない /
Program.csには手書きのループが残る」を更新し、**条件を満たすプロジェクトでは`Program.cs`そのものを
不要にする**。ADR-0033の他の決定事項(構造的推論・閉集合Archetype・記述順の実行順序)はすべて有効。

## 背景

ユーザーから「`Program.cs`を作らなくてもいいようにSystem/Componentに実装してほしい。
プラットフォームのウィンドウシステムや依存も含めて。fbxはレベルとして扱う」という依頼があった。

ADR-0033決定事項10は、起動時のエンティティ生成をECSへ持ち込むところまでは達成していたが、
`Program.cs`は残ると結論していた。その理由は**Context引数**である:

- Contextは`GeneratedGameLoop.Run`/`RunFrame`の引数として渡され、**ループ開始前に構築されて
  ループ全体で固定される**。
- `Win32Window`/`StageRenderer`はまさに「起動後に、しかも順番に依存して作られるもの」だった
  (レンダラーはウィンドウハンドルとメッシュ集合が両方揃ってからでないと生成できない)。
- したがってこれらをContextにする限り、構築コードはループの外 = `Program.cs`にしか置けない。

つまり残っていた`Program.cs`は「エンジン固有の処理だから」ではなく、**Contextという受け渡し方の
帰結**だった。ここを変えれば`Program.cs`は消える。

## 決定事項

### 1. エンジンオブジェクトはComponentのフィールドとして持つ

`Win32Window`/`StageRenderer`のような参照型は、Context引数ではなくComponentのフィールドとして
Entityに載せる。Componentの要件は「非nullableのstructであること」だけで(`GameSystemAnalyzer`)、
参照型フィールドを持つことは制約に触れない。`ChunkList<T>`も`IsReferenceOrContainsReferences<T>`で
参照を含む型を扱えるようになっている。`UserGameProject`の
`SceneSpawnRequest { LoadedScene? Scene }`が既にこの形であり、新機構ではない。

これにより「まだ作られていない」状態をnullで表現でき、**Systemの中で遅延生成できる**。
結果としてContext引数がゼロになり、生成されるループのシグネチャが`Run(ref State)`に確定する。

**代償**: 閉集合Archetype方式には「別Entityのコンポーネントを読む」手段が無いため、複数のEntityから
参照したいオブジェクトは生成時にコピーして持たせる(`Renderable.Renderer`のように)。これは回避策
ではなく、この方式で依存を明示する唯一の書き方である。

### 2. エントリポイントは4条件を満たすときだけ生成する

`GameSystemEntryPointEmitter`が`GameSystem.EntryPoint.g.cs`を出力する。中身は3行:
`World`を作り、シードEntityを1つ作り、`GeneratedGameLoop.Run(ref State)`を呼ぶ。

出力するのは次を**すべて**満たす場合に限る:

1. `Compilation.Options.OutputKind`が`ConsoleApplication`/`WindowsApplication`
2. `Compilation.GetEntryPoint(ct)`がnull(ユーザーのMainもトップレベル文も無い)
3. ディスパッチ対象Systemが1つ以上ある(=`GeneratedGameLoop`が出力される)
4. どの`Update`にもContext引数が無い(`Run`のシグネチャが`(ref State)`に確定する条件)

条件2があるため、手書き`Program.cs`を持つ`UserGameProject`はCS0017(エントリポイントの重複)を
起こさない。条件1により全テストプロジェクト(Library)と`CSharpGameEngine.Entities`単体ビルドも
無関係でいられる。Generatorは自分の出力を含まないコンパイルを見るので、この判定が自分自身の
`Main`に反応することもない。

条件1・2が真で条件4だけ落ちる場合(Contextを使っていて`Program.cs`が無い)は、**SYSGEN021**で理由を
名指しする。何もしないとCS5001「エントリポイントが見つかりません」だけが出て原因が分からないため。

### 3. シードEntityは`CSharpGameEngine.Entities.Bootstrap`

生成された`Main`が作る唯一のEntityで、`Parent`/`SceneNodeTag`と同じ「エンジンが持つComponent」。
これを`ref`で受け取る`Update`が「起動時に一度だけ走る処理」になり、そこから
`State.World.Create(...)`で残りのEntityをすべて組み立てる。ADR-0033決定事項10が
`SceneSpawnRequest`で示したパターンを、エンジン側の規約へ昇格させたものにあたる。

`Bootstrap`が`Done`フラグを持つのは、閉集合方式に`Destroy`が無く、Archetypeループが毎フレーム
回り続けるため。消費側は`if (bootstrap.Done) return true;`で一度きりを表現する。

`{Bootstrap}`のArchetypeは、**エントリポイントを出すと決めた場合にだけ**レジストリへ注入する
(`GameSystemIndexAssigner.Assign`の`additionalComponentSets`経由。`[SceneDefinition]`のノード形状と
同じ経路)。無条件に注入すると、生成しないコンパイルにまで誰も入らない空Archetypeが増えてしまう。

### 4. フレームの構成は「1ファイルに記述順」で決まる

生成ループは**Archetype順が外側・System順が内側**であり、Archetypeの出現順は
「Systemの並び順に引数の組を登録した順」そのものである。したがって
**1ファイルにフレーム順でSystemを並べれば、Archetypeループの順序もフレーム順になる**。

`UserEcsProject`の実例:

| 出現順 | Archetype | System | 役割 |
|---|---|---|---|
| 0 | `{Bootstrap}` | `GameStartupSystem`(GameSystems.cs) | ウィンドウ設定・レベル・シェーダー・初期カメラを宣言 |
| 1 | `{AppHost}` | (`WindowSystem`の宣言形状) | 実体の入らない空ループ |
| 2 | `{AppHost, Level}` | `WindowSystem` → `LevelLoadSystem` | ウィンドウ生成/PumpEvents、FBX→レンダラー→描画Entity |
| 3 | `{Transform, Renderable}` | `TransformSyncSystem` | 変換行列の同期 |
| 4 | `{FramePresent}` | `PresentSystem` | `RenderFrame`と後始末 |

`Archetype.Create`は即時追加で、各ループは開始時に`Count`を読む。よってArchetype0が作ったEntityは
Archetype2のループ開始前に存在し、Archetype2が作ったEntityもArchetype3/4の開始前に存在する —
**起動処理はすべてフレーム1で完了する**。

Presentを`AppHost`とは別Entity(`FramePresent`)にしているのはこのためで、`AppHost`に載せると
Archetype2のループ、すなわち描画同期(Archetype3)より前でPresentしてしまう。

### 4-2. Systemは「移設先」でファイルを分ける

`UserEcsProject`のSystemは2ファイルに分かれている:

| ファイル | 中身 | 行き先 |
|---|---|---|
| `Systems/GameSystems.cs` | `GameStartupSystem` | このプロジェクトに残る |
| `Systems/RuntimeSystem.cs` | `WindowSystem`/`LevelLoadSystem`/`TransformSyncSystem`/`PresentSystem` | エンジンの共有ソースへ |

分ける目的は移設をファイル単位の移動にすることと、**ランタイム側にゲームのリテラルが1つも残って
いないことを目視で保証する**ことにある。ウィンドウのタイトル/サイズ、FBXのパス、シェーダーのパス、
初期カメラはすべて`AppHost`/`Level`のフィールドとして`GameStartupSystem`から渡る。

ファイルを分けた以上、両者の相対順序はファイルパスのOrdinal順に落ちる(未解決事項を参照)。
現状は`GameSystems.cs` < `RuntimeSystem.cs`(G < R)で意図どおりの順序になっているが、
**仮に逆順になっても壊れない**: Runtime側のSystemはいずれも「まだ無い」状態をnullで判定して
素通りするため、起動が数フレーム遅れるだけである。逆に、順序が本当に効いている4つ
(Window → LevelLoad → TransformSync → Present)は同じファイルに置いてある。

### 5. 後始末はSystemの責務

生成された`Main`はDisposeすべき対象を知らないため、`try`/`finally`も`using`も持たない。
`State.Enabled`が落ちたフレームの最後に`PresentSystem`が`StageRenderer`/`Win32Window`を破棄する
(手書き`Program.cs`の`using`が担っていた役目)。

## 代替案として検討したもの

- **共有ソース側(`Runtime.cs`)に手書きの`Main`を置く**: System 0件のコンパイル
  (`CSharpGameEngine.Entities`単体ビルド、各テストプロジェクト)では`World.Create(new Bootstrap())`も
  `GeneratedGameLoop`も存在せずコンパイルが壊れる。条件付きで出力できるSG生成に劣る。
- **3行の`Program.cs`を残す**: ADR-0033の範囲に収まるが、依頼(「作らなくてもよくする」)を満たさない。
- **生成された`Main`がContextをファクトリ規約で構築する**: 構築順序の依存グラフをGeneratorが
  解決する必要があり、しかも規約を書くファイルが実質`Program.cs`の代わりになる。
- **`partial struct State`にプラットフォームオブジェクトを足す**: 全Systemが`ref State`で到達でき
  参照のコピーが不要になるが、実質サービスロケータであり「宣言したComponentしか触らない」という
  性質が壊れる。ユーザーの選択でComponent方式を採った。
- **`Compilation.GetEntryPoint`ではなくMSBuildプロパティで明示的にopt-inする**: 判定は安くなるが、
  各csprojに設定行が要る(「`Program.cs`の代わりにcsprojを書く」ことになる)。自動判定を採用した。

## 帰結

### 得られたもの
- `UserEcsProject`から`Program.cs`が消えた。ユーザーが書くのは`Components.cs`と
  `Systems/RuntimeSystem.cs`だけになり、ゲーム固有の設定(タイトル/解像度/レベルのパス)も
  `GameStartupSystem`というSystemの中にある。
- ウィンドウ・レンダラー・レベル読み込みという「エンジン固有で生成不可能」とされていた処理が、
  すべて通常のSystemとして書けることが実証された。エンジン側共有ソースへ移設する道が開いた。
- Context引数を使わない構成では、ループの入口が`Run(ref State)`という固定シグネチャになる。

### 失ったもの
- 特に無し(既存プロジェクトは4条件のいずれかで対象外になり、挙動が変わらない)。
- ただしエンジンオブジェクトをComponentに載せたことで、`WorldSnapshot.Capture`のボックス化対象に
  生きた`Win32Window`/`StageRenderer`が入るようになった。消費者はVSIXプレビュー(別プロセスで
  シーンだけを実行)なので実害は無い。

## 検証結果(実際に実行して確認)

```
dotnet build src/CSharpGameEngine.CodeAnalysis/CSharpGameEngine.CodeAnalysis.csproj -c Debug
  → 成功、警告0/エラー0

dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/...  → 64/64 成功(新規8件を含む)

dotnet build src/CSharpGameEngine.Entities/CSharpGameEngine.Entities.csproj -c Debug
  → 成功(System 0件・Library なのでエントリポイントは生成されない)

dotnet build UserGameProject/UserGameProject.csproj -c Debug
  → 成功、警告0/エラー0。GameSystem.EntryPoint.g.cs は出力されない(手書きProgram.csがあるため)

dotnet build UserEcsProject/UserEcsProject.csproj -c Debug
  → 成功、警告0/エラー0。Program.cs 無しでビルドが通る

dotnet build CSharpGameEngine.slnx -c Debug   → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Release → 成功、警告0/エラー0
dotnet test  CSharpGameEngine.slnx            → 全9プロジェクト成功、373件・失敗0(内訳はM0015)
```

生成物を直接読んで確認した:

- `UserEcsProject/Generated/.../GameSystem.EntryPoint.g.cs`が出力され、
  `World.Create(new Bootstrap())` → `GeneratedGameLoop.Run(ref State)`の3行になっている。
- `GameSystem.Loop.g.cs`のArchetypeループが決定事項4の表どおり`Archetype0`→`Archetype4`の順に
  並び、`Archetype2`のループ内が`windowSystem` → `levelLoadSystem`の記述順になっている。
- `UserGameProject/Generated/`には`GameSystem.EntryPoint.g.cs`が存在しない。

`GameSystemEntryPointTests.GeneratedMain_SeedsBootstrap_RunsFrames_AndStopsWhenStateIsDisabled`は
**生成された`Main`をリフレクションで実際に呼び出し**、「シード1個 → Systemが世界を組み立てる →
`State.Enabled`でループが終わる」を実行結果として検証している(プラットフォーム依存ゼロ)。

### 実機確認

`UserEcsProject.exe`(apphost)を起動し、`PrintWindow`(`PW_RENDERFULLCONTENT`)でクライアント領域を
キャプチャして確認した:

- ウィンドウタイトルが`GameStartupSystem`の指定どおり「C# Game Engine - ECS FBX Rendering PoC」。
- 1280x720のクライアント領域が`StageRenderer.ClearColor`(89,140,217)でクリアされ、中央に`Box.fbx`の
  立方体が描画されている。標準出力/標準エラーへの出力は無し(未処理例外なし)。

すなわち「シードEntity → ウィンドウ生成 → FBXインポート → D3D12レンダラー生成 → 変換行列の同期 →
Present」が`Program.cs`なしで通っている。立方体が暗く見えるのは、この PoC が
`SetDirectionalLight`/`SetAmbientLight`を一度も呼ばないため(旧`Program.cs`も呼んでいなかったので
本変更による退行ではない)。

## 未解決事項

- ~~**実行順序のファイル跨ぎ**: 決定事項4の順序保証は「順序が意味を持つSystemが1ファイルにある」
  ことに依存する。エンジン側システムを共有ソースへ移した瞬間、ユーザーのSystemとの相対順序は
  `SyntaxTree.FilePath`のOrdinal順(=絶対パス依存)に落ちる。移設時には
  `IFrameBeginSystem`/`IFrameEndSystem`のようなマーカーinterfaceを整列キーに足す必要がある
  (`ISystem`自体がマーカーなので、属性ゼロの方針を保ったまま解決できる)。~~
  → [[ADR-0036-System-Order-Attribute]]で解決した。実際に採用したのはマーカーinterfaceではなく
  `[SystemOrder(int)]`属性で、`ISystem`実装struct自身に直接付けるか(そのSystem1つだけの順序)、
  `file`スコープの専用マーカークラスに付ける(ファイル既定値)かを選べる。任意個のSystem・ファイルへ
  全順序を与えられる点でinterfaceの「先頭/末尾」2値より表現力が高い。`UserEcsProject`の
  `GameSystems.cs`/`RuntimeSystem.cs`にこれを適用し、偶然のファイル名依存を解消した。
  → その後[[ADR-0073-Relative-System-Order]]で`[SystemOrder]`を廃止し、`[RunsAfter]`/`[RunsBefore]`の相対指定と
  完全修飾名順に置き換えた。
- **エンジン側システムの置き場**: `CSharpGameEngine.Entities`ではあり得ない。net10.0・
  ProjectReferenceゼロのプラットフォーム非依存ソースであり、`StageRenderer`/`Win32Window`を
  参照した瞬間に単体ビルドと[[ADR-0001-Layered-Architecture]]の階層が壊れる。`Entities`と同じ理由で
  DLL参照もできない(partial型がアセンブリ境界をまたげない)ため、
  `CSharpGameEngine.Runtime.Windows`のような**新しい共有ソースプロジェクト**が要る。
- **空Archetype**: `WindowSystem`が`{AppHost}`だけを宣言し、実体は`{AppHost, Level}`にしか無いため、
  誰も入らないArchetypeのループが1つ生成される。害は無いが、宣言形状と実体の乖離として記録しておく。
- `Bootstrap`のArchetypeループも毎フレーム回り続ける(`Done`フラグの分岐1回)。汎用の`Destroy`が
  実装されれば消せる。

## 関連機能

- F-L3-012 — `CSharpGameEngine.Entities`(`Bootstrap.cs`の追加)
- F-L7-015 — `GameSystemGenerator`(`GameSystemEntryPointEmitter`の追加)
- F-L7-016 — `GameSystemGenerator`診断(SYSGEN021の追加)

関連: [[M0015-Generated-Entry-Point]]


## Shared Runtime follow-up (ADR-0048)

The Windows engine-object components and generated runtime systems now live in `CSharpGameEngine.Runtime.Windows` shared source. UserEcsProject imports the projitems rather than owning a duplicate RuntimeSystem.