# M0049: Sceneの保存形式をJSON化する(第1段: Runtimeパイプライン)

## 目的

M0047/M0048で編集セッションの正本はメモリ内`SceneEditDocument`へ移った。残る「C#への書き戻し
(`SceneSourceEditor.ApplyForSave`)がまだ壊れやすい」という課題について、ユーザーから
「直すよりJSON保存へ進むほうがよい」との明確な方針が示された。

本リポジトリは過去に一度JSON形式(`.scene`、ADR-0020/0021/0022)からC#ソース生成方式(ADR-0030)を
経て現行のC# `IScene`/`Node`宣言的API(ADR-0038/ADR-0040)へ移行しており、ADR-0040は「JSONの並行
維持」を明示的に不採用とした(二重正本化のリスク)。ただしADR-0040がC#を選んだ最大の理由の一つ
「VSネイティブのテキストUndo」は、M0047でライブ編集が既にテキストバッファから切り離されメモリ内
Documentへ移った時点で説得力を失っている。この文脈をユーザーへ明示した上で、ユーザーは明確に
JSON化続行を選択した。

### 決定事項(ユーザー承認済み)

1. `IScene`インターフェースを**廃止**し、Sceneは`*.scene.json`ファイルの**存在だけ**で検知する
   (C#クラスの宣言は一切不要になる)
2. 既存の4ファイル(`MainScene.cs`・`FirstPersonStageScene.cs`・`FirstPersonStageAssets.cs`・
   `PrimitiveShowcaseScene.cs`)を今回まとめてJSON化する
3. **順序**: Runtimeコンパイルパイプライン(JSON→生成コード)+4ファイル移行を先に完成・検証する。
   VSIXのScene Designer(ギズモ・Hierarchy・Inspector)は、この段階では**一時的に使えなくなる**
   ことをユーザーは承認済み。Scene DesignerのJSON対応は次の別マイルストーン(M0050)。

## JSON形式の設計

```json
{
  "namespace": "UserEcsProject.Scenes",
  "className": "MainScene",
  "startupOrder": 0,
  "root": {
    "name": "Application",
    "components": [
      { "type": "UserEcsProject.AppHost", "members": { "Title": "...", "Width": 1280, "Height": 720 } }
    ],
    "children": []
  }
}
```

- `className`/`namespace`は生成される型の完全修飾名(手書きC#クラスは無いため、ここで初めて名前が決まる)
- `startupOrder`省略 = 非startup scene
- `type`はComponent構造体の完全修飾名。`Compilation.GetTypeByMetadataName`で解決する(旧C# DSLと
  異なり、`using`や囲む名前空間による解決は行わない — JSON側は常に完全修飾名を要求する)
- **値の解釈は対象フィールド/プロパティの宣言型で決める**(タグ付きJSON値は使わない):
  - 型が`enum` → JSON文字列はメンバー名。`EnumType.GetMembers(name)`で`IFieldSymbol{IsConst:true}`
    を解決する
  - 型が`string`/`bool`/`float`/`double`/`int`/`long` → JSONの値をそのまま使う
  - `[SceneAssets]`の資産パス定数は解決せず、参照箇所へ**解決済みの文字列を直書き**する
    (`FirstPersonStageAssets.cs`は廃止し、別ファイルは作らない)
  - それ以外(ネスト構造体・コンストラクタ引数・`[SceneFactory]`・readonlyフィールド) →
    今回は非対応。SCENE201(未知のメンバー)またはSCENE204(非対応の型)で拒否

## 新規実装

| # | ファイル | 内容 |
|---|---|---|
| 1 | `SceneJsonModel.cs` | `System.Text.Json`でデシリアライズするPOCO |
| 2 | `SceneJsonAnalyzer.cs` | `(AdditionalText,Compilation)→SceneEmitModel?`。型・メンバー・enum解決、SCENE2xx診断 |
| 3 | `SceneDslDiagnostics.cs` | SCENE200(不正なJSON)〜SCENE204(非対応の型)を追加 |
| 4 | `GameSystemGenerator.cs` | `Context.AdditionalTextsProvider`で`*.scene.json`を拾う新プロバイダー。C#構文由来の`SceneDeclarations`プロバイダーは削除 |
| 5 | `CSharpGameEngine.CodeAnalysis.csproj` | `System.Text.Json`参照を追加 |
| 6 | `UserEcsProject.csproj` | `<AdditionalFiles Include="Scenes/*.scene.json" />`を追加 |

`SceneInstantiateEmitter`/`SceneStartupSystemEmitter`/`SceneComponentSetCollector`/`SceneEmitModel`は
**無改造で流用**した — ADR-0040で生成層が既にシンボルから式を再構築する設計(元のC#構文Span/trivia
に一切依存しない)へ変更済みだったため、変わるのは「`SceneEmitModel`をどこから作るか」だけで済んだ。

### 撤去・整理(承認済み計画からの修正あり)

- `FirstPersonStageAssets.cs`・`MainScene.cs`・`FirstPersonStageScene.cs`・
  `PrimitiveShowcaseScene.cs`は削除し、対応する`*.scene.json`へ置き換えた。
- `src/CSharpGameEngine.Entities/IScene.cs`・`Node.cs`・`StartupSceneAttribute.cs`は
  **削除しなかった(計画からの逸脱)**。承認済み計画は「手書きC#側の権威が無くなるため削除」と
  していたが、実装中に次の2点を確認し、削除しない判断に変更した:
  1. `tests/CSharpGameEngine.{SceneModel,CodeAnalysis,VSExtension}.Tests`の`.csproj`が
     `<None Include="...\IScene.cs" Link="..." />`でこれらのファイルへ**直接リンク**しており
     (独立したコピーではない)、削除すると3つのテストプロジェクトのビルドが壊れる。
  2. 同じ承認済み計画が「VSIXはM0050までこれらの型名を前提にした
     `SceneDeclarationAnalyzer`/`SceneValueReader`/`SceneDslAnalyzer`を使い続ける」と明記しており、
     その解析器が検出する対象の型定義そのものを消すのは計画の上位意図(VSIX/解析器パイプラインを
     コンパイル可能なまま残す)と矛盾する。

  結果として3ファイルは**リポジトリに残るが、新規Sceneはどれも実装しない**(死んでいないが
  今後参照されない宣言)。
- `src/CSharpGameEngine.Entities/Runtime.cs`の`World.InstantiateScene<TScene>()`ジェネリック制約を
  `where TScene:IScene,ISceneDefinition`から`where TScene:ISceneDefinition`へ緩和した。JSON由来の
  生成Sceneクラスは手書きのpartial宣言を持たないため`IScene`を実装しようがなく、この制約緩和が
  無いと**あらゆるScene呼び出しがコンパイル不能**になっていた(メソッド本体は元から
  `ISceneDefinition.Instantiate`しか使っていなかったため、意味的な後退は無い)。
- `SceneDeclarationAnalyzer.cs`・`SceneValueReader.cs`・`SceneDslAnalyzer.cs`(C#構文からの解析)は
  計画通り削除しなかった(VSIX側がまだ使用中、M0050まで温存)。

## 4ファイルの移行と検証手順

手書きでの転記は`FirstPersonStageScene.cs`(58ノード、`-1.4810628E-08f`のような精度の高い浮動小数点
リテラルを含む)で誤りが混入するリスクが高いため、スクラッチ用のPythonスクリプト
(セッションのスクラッチパッドのみに存在、リポジトリには含めない)でC#構文を機械的にJSONへ変換した。

**発見した変換ミス**: 最初の変換で`AmbientLight`/`HeightFog`/`DirectionalLight`/`PlayerStart`
(いずれも`UserEcsProject`名前空間で定義)を誤って`CSharpGameEngine.Runtime.Windows`名前空間として
マッピングしてしまい、ビルド時にSCENE107(非Component型)で発覚した。元のC#では`using
CSharpGameEngine.Runtime.Windows;`と、ファイル自身の名前空間(`UserEcsProject.Scenes`)を包含する
外側の名前空間(`UserEcsProject`)経由の暗黙解決が併用されていたため、単純な`using`一覧からは
判別できなかった。実際の型定義を`grep`で確認して修正した。

### 生成コードの意味的同一性の実機検証

`git stash`で新パイプラインへの変更一式を退避し、旧C#構文パイプラインで`UserEcsProject`を
ビルドして生成物を保存 → stash復元 → 新JSONパイプラインで再ビルドして生成物を保存 →
両者を`diff`で比較する、という手順で「移行前後の生成`.Scene.g.cs`/`.StartupSystem.g.cs`が意味的に
同一か」を実測した(4 Scene全ての生成物、共有インフラ生成物`GameSystem.*.g.cs`を含む)。

結果:
- `MainScene`・`PrimitiveShowcaseBoxScene`・`PrimitiveShowcaseSphereScene`・共有インフラ
  (`GameSystem.EntryPoint/Loop/Types/World.g.cs`)は**完全に一致**(diff無し)。
- `FirstPersonStageScene`のみ58行中58行に差分があったが、全て以下の2種の意図した差分のみと
  トークン単位で機械検証した(構造・Archetype番号・型名・引数順序に差分は無い):
  1. **浮動小数点の書式差**(意味の変化なし): 旧パイプラインは元のC#ソーステキストに近い桁数
     (例: `0.0436f`)、新パイプラインは`SceneEmitExpressionWriter`の`G9`書式(例:
     `0.0436000004f`)。両者をfloat32へパースしたビット列は完全一致することを
     `struct.pack('f',...)`で確認した(丸め誤差の混入なし)。
  2. **資産定数のインライン化**(設計通りの意図した差分): 旧パイプラインの
     `global::UserEcsProject.Scenes.FirstPersonStageAssets.SM_Cube_...`というシンボル参照が、
     新パイプラインでは解決済みのリテラル文字列`"Assets/Stage/.../SM_Cube_....glb"`に置き換わった。
     これは本マイルストーンの設計そのもの(上記「値の解釈」参照)。

## 既存テストの追従(C#構文ベースのGenerator系テストをJSON入力へ移行)

`GameSystemGenerator`のC#構文プロバイダーを削除した結果、以下のテストがC#の`IScene`宣言を
コンパイルへ埋め込んで生成器を駆動していたため、当然ながら失敗した:

- `SceneDslGeneratorEmissionTests.cs`(12件)
- `SceneDslGeneratorEndToEndTests.cs`(6件)
- `SceneArchetypeIntegrationTests.cs`(Scene関連3件)
- `GameSystemGeneratorEndToEndTests.SceneChildNodeArchetype_IsProcessedAfterAllSystemDeclaredArchetypes`(1件)

いずれもテストの意図(生成コードの形・Archetype合流順序・実行時の振る舞い)は変えず、シーン定義部分
だけをC#の`IScene`宣言からJSON文字列へ書き換えて追従させた。このため`GameSystemGeneratorTestHelper`
に`Context.AdditionalTextsProvider`向けの入力を注入できる経路(`InMemoryAdditionalText`、
`RunGeneratorWithSceneJson`/`CompileAndLoadWithSceneJson`)を新設した。

### 移行中に発見した実装バグ(readonlyフィールドの誤受理)

テスト追従の過程で`SceneJsonAnalyzer.TryResolveMember`のバグを発見した: フィールド解決条件が
`IsConst:false`のみで`IsReadOnly`を見ておらず、`public readonly float X;`のようなreadonlyフィールドを
「代入可能なメンバー」として誤って受理していた。これは生成コードが`new T { X = ... }`という
オブジェクト初期化子を出そうとしてCS0191(readonlyフィールドへの代入不可)でコンパイル不能になる
潜在バグだった。C#構文ベースの旧パイプラインではRoslyn自身がユーザーの`.cs`ソースを解析する時点で
この種の誤りを構造的に弾けていた(そもそも`new Transform { X = ... }`という無効な初期化子は
ユーザーのソースがコンパイルエラーになる)ため、この種のバグ自体が存在し得なかった。JSON側は
メンバー解決を独自に実装しているため、同じ保証を明示的に再実装する必要がある。
`IPropertySymbol`側は元から`IsReadOnly:false`を見ていた(非対称だった)。プロパティ側と揃えて
`IsReadOnly:false`を追加し、回帰テスト(`ReadOnlyFieldIsTreatedAsUnknownMember`)を追加した。

## 新規テスト

`SceneJsonAnalyzerTests.cs`(新規11件、`tests/CSharpGameEngine.CodeAnalysis.Tests/`):
リテラル値の宣言型別解決・enumメンバー名解決・複数子ノードの再帰的走査とパスベースLocalName・
不正なJSON構文(SCENE200×3パターン)・未知のComponent型(SCENE107)・未知のメンバー(SCENE201×2、
readonlyフィールド含む)・値の型不一致(SCENE202)・未知のenumメンバー(SCENE203)。

## 変更しないもの

- `SceneInstantiateEmitter.cs`/`SceneStartupSystemEmitter.cs`/`SceneComponentSetCollector.cs`/
  `SceneEmitModel.cs`(全て無改造で流用)
- Runtime本体(`CSharpGameEngine.Entities`のArchetype/World機構)
- VSIXのScene Designer実装一式(今回は「動かなくなる」ことを受け入れるだけで、コードは触らない)

## 検証

- `dotnet build CSharpGameEngine.slnx`: 0エラー・0警告
- `dotnet test CSharpGameEngine.slnx --no-build`: 全8プロジェクト成功、1106件中失敗0
  (Core.Tests 122、Import.Tests 30、Entities.Tests 42、Bridge.Hosting.Tests 8、Windows.Tests 87、
  CodeAnalysis.Tests 112(新規11件含む)、SceneModel.Tests 104、VSExtension.Tests 601)
- `UserEcsProject`単体ビルド(クリーンビルド含む): 成功。`Generated/`配下に4Sceneぶんの
  `.Scene.g.cs`(StartupSceneの2件のみ`.StartupSystem.g.cs`も)が出力されることを確認
- 生成コードの新旧比較(上述): 差分は書式・資産インライン化のみで意味的に同一と実測確認済み
- **未実施(ユーザー確認が必要)**: 実機でのゲーム起動・描画確認(FirstPersonStageのステージ・
  カメラ・ライト等が全て揃っていること)。ビルドとdiff検証は完了しているが、実際にウィンドウを
  開いての目視確認は行っていない
- **既知の受け入れ事項**: この時点でVisual StudioのScene Designer(ツールバーからのScene選択・
  ギズモ編集・Hierarchy)は対象Sceneを一切検出できず使用不能になる。M0050で対応する
