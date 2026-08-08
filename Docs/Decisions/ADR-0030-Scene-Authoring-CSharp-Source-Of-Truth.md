# ADR-0030: C#コードをSceneの唯一の正本とするScene Authoring(Phase A)

## ステータス
承認済み

## 背景

既存のScene実装は完全にJSON方式で、`Docs/EditorSpec.md` §3がADR-0002(「`.cs`ファイルとオリジナル
アセットが正規のSource」)の原則に倣い「シーンJSONもオーサリングされたSourceとして扱う」と明記して
いる。実装は2系統に分裂していた:
- `src/CSharpGameEngine.Scene`(net10.0、`SceneNode`/`Transform`/`SceneGraph`)。Docs/EditorSpec.mdの
  正本という位置づけだが、自分自身のテスト以外どこからも参照されていない。
- `tools/CSharpGameEngine.VSExtension/SceneFile.cs`(net472、TFMの壁による独立複製、`Guid`直書き、
  `Components`フィールド自体が存在せず往復で静かに失われる)。実際の`UserGameProject/DefaultScene.scene`
  はこちら側のみが書き込んでいる。

ユーザーから、次の仕様見本(`samples/SoaSystemsDemo/シーン構築見本.cs`)が添付され、これを正本とする
Scene Hierarchy編集機能の実装依頼があった:

- `[SceneDefinition]`が付いた`static partial class`を1つのScene定義として扱う。
- `private static partial SceneNodeSymbol X { get; }`をノード宣言として扱う(SGが実装本体を生成)。
- `Scene.Create(X, X => { ... })`のネストで親子関係を表現する。第1引数のノード名とラムダ引数名は
  必ず一致させる(IDEとAnalyzerが検証する規約)。
- ラムダ内の`Set(new Component{...})`呼び出しをそのノードのComponentとして扱う。
- Hierarchy Docking WindowはRoslynでユーザー領域を解析し、Add/Delete/Rename/Reparentを実際の
  C#構文木への編集として反映する(SceneデータとC#コードを別々に保存して同期するのではない)。
- `Define`内では条件分岐・ループ・動的ノード名・任意の外部メソッド呼び出しを禁止する(動的スポーンは
  通常のRuntime Systemへ分離する)。
- SceneNodeIdは完全修飾シンボル名からSGが決定論的に生成する。Rename後もIDを維持する必要が生じた
  場合は、将来的にIDE管理のhidden annotationか`.scene.meta`を検討する(見本自身が明示的に未解決と
  記載)。

ユーザーは「既存実装を最初から捨てて作り直さず、まずリポジトリ全体を調査すること」「最初の回答では
コードを変更せず10項目の分析を提示し、承認を待つこと」を明示的に指示した。3並列のExplore Agentに
よる調査(Scene/Entity/Hierarchy現状、VSIX Docking Window+Roslyn Workspace能力監査、SG規約+ECS統合
面)と1つのPlan Agentによる設計統合を経て、10項目の計画を提示し承認を得た。

## 決定事項

### 1. スコープをPhase Aに限定する(VSIX側のライブ編集は別タスク)

新Attribute/Component + Source Generator/Analyzer + 動作するサンプル(`dotnet run`でOK確認)までを
今回のスコープとする。VSIX側のRoslyn Workspaceによるライブ編集(Hierarchy Docking Windowでの
Add/Delete/Rename/Reparent)は、このリポジトリに前例が無い新規領域
(`DocumentEditor`/`Renamer.RenameSymbolAsync`/`VisualStudioWorkspace.TryApplyChanges`のいずれも
未使用、`CSharpGameEngine.VSExtension.csproj`にWorkspace系PackageReferenceが無い)かつ実機VS
デバッグが必須な部分が多いため、次回以降の別タスクとする。ADR-0029(ECS移行)は「削除と移行が
相互に強制し合う」ため一括実装が必然だったが、今回はJSON版が無改修のまま並行して動き続けられる
ため分割する理由がある、とユーザーへ説明の上で確定した。

### 2. JSON版は「将来的に置き換える前提」で設計するが、今回のパスでは一切変更しない

「JSON版と永続的に共存」させる案、「将来JSON版を置き換える前提」で設計する案の2択のうち、
ユーザーは後者を選んだ。Phase Aの実装自体はJSON関連ファイル(`src/CSharpGameEngine.Scene`、
`SceneFile.cs`、`SceneEditorHub.cs`、`HierarchyToolWindowControl.xaml.cs`、`SceneNodeAdder.cs`、
`PrimitiveSpawner.cs`、`UserGameProject/DefaultScene.scene`)を一切変更しない。実際の置き換えは、
C#版がPhase B以降で実用段階に達した後の別タスクとする。

### 3. Transform表現は既存の`Position`/`Rotation`/`Scale`を再利用する(見本からの唯一のAPI形状差分)

見本は単一の合成`LocalTransform{ Position, Rotation, Scale }`を提案していたが、ユーザーは
`UserGameProject/Components.cs`で既に確立している`Position`/`Rotation`/`Scale`という3つの独立した
Componentの構成を再利用する案を選んだ。これにより`Node.Set(new LocalTransform{...})`は
`Node.Set(new Position{...}); Node.Set(new Rotation{...}); Node.Set(new Scale{...});`という
3回の`Set`呼び出しに置き換わる。

### 4. `Parent`Componentは`CSharpGameEngine.Entities`本体に追加する

このECS自体はHierarchy/親子の概念を一切持たない(全リポジトリgrepでゼロ件確認済み)。
`UserGameProject`等の消費側プロジェクトへ置く案もあったが、Unity DOTSの`Unity.Transforms.Parent`と
同じ「Hierarchy機能そのものが提供する汎用Component」という位置づけのため、ユーザーは
`CSharpGameEngine.Entities`本体への追加を選んだ(既存Componentが全て消費側プロジェクトに
置かれている慣習からの唯一の意図的な逸脱)。

### 5. `SceneDefinitionGenerator`は既存の`CSharpGameEngine.CodeAnalysis`に追加する(3個目のGenerator)

ADR-0014が「2つ目のGeneratorが出た時点で`CodeAnalysis.Abstractions`分離を再検討する」としていた
分岐点は、ADR-0029で「同一プロジェクトに追加する」として決着済みであり、その方針を3個目の
Generatorにもそのまま適用した。Analyzer→Model→Emitterの3層構成・診断ゲート・
`List<string>`+`string.Join("\n",…)`・ランタイム型への完全修飾名文字列参照(ADR-0001のレイヤー
分離維持)という、`GameSystemGenerator`で確立した規約をそのまま踏襲した。

### 6. 診断はSCENE001〜009の9件とし、見本の10件から2件減・1件増とする

見本がドキュメントコメントとして提示したSCENE001〜010のうち、SCENE007「循環した親子関係の検出」は
実装しなかった: `Create`呼び出しが構文的なネストでしか親子関係を表現できない本方式では、真の循環は
既に生成済みの祖先ノードを再度`Create`することでしか起こり得ず、これはSCENE005(同一ノードの
複数回生成)が構造的に包含するため、独立した診断としては冗長かつ到達不能に近いと判断した。
SCENE010「Hierarchyから削除しようとしたノードが他コードから参照されている」はIDE操作時のガードで
あり、ビルド時のAnalyzer診断ではなくPhase B(Roslyn Workspace編集)の対象と整理した。一方、
`World.CreateArchetype`の型引数8個という既存の上限(ADR-0029/M0009で既に実利用上の制約として
確認済み)に対応するため、見本には無いPhase A固有の診断`SCENE009`(非RootノードはComponent数+1個
分のParentを含めて8個を超えてはならない)を新設した。

### 7. `CreateRuntime`はRootの`Entity`を1つだけ返す(`SceneHandle`ラッパーは作らない)

`Define`内の最上位`Scene.Create(...)`呼び出しは1回のみ許可する(SCENE006が強制する)。見本の
仮の`SceneHandle{ EntityId Root }`という薄いラッパー型は導入せず、実在の
`CSharpGameEngine.Entities.Entity`をそのまま返す。見本の`EntityId`/`SceneWorld`という仮のRuntime型も、
ADR-0029の`Entity`/`World`踏襲と同じ方針で実在の型をそのまま使う。

### 8. 空ノードには内部タグComponent(`SceneNodeTag`)を自動付与する

`Set`呼び出しを1つも持たないノード(見本自身の`Root`がこのケース)は、`World.CreateArchetype`に
0型引数のオーバーロードが無いため、そのままでは成立しない。ユーザーに空タグを明示的に書かせる
のではなく、SceneDefinitionGeneratorが`SceneNodeTag`(新規、0フィールド)を自動的に付与する設計とした
(Runtime Emitter実装中に必要性が判明し追加した、Step1完了後の後追い判断)。

## 実装

`src/CSharpGameEngine.Entities/`に`SceneDefinitionAttribute`/`SceneNodeId`/`SceneNodeSymbol`/
`SceneBuilder`/`SceneNodeBuilder`/`Parent`/`SceneNodeTag`を追加した。`SceneBuilder`/
`SceneNodeBuilder`の`Create`/`Set`メソッド本体はDefineをコンパイル可能にするためだけの
ほぼ空実装で、SceneDefinitionGeneratorは構文木を静的解析するだけでこれらを実行しない。

`src/CSharpGameEngine.CodeAnalysis/`に`SceneDefinitionDiagnostics`(SCENE001〜009)/
`SceneDefinitionModel`(`SceneNodeModel`木構造+`ComponentSetModel`)/`SceneDefinitionAnalyzer`
(`Create`/`Set`呼び出しの構文的検証、レシーバー名はテキスト一致のみで検証しクロージャ越しの
誤参照も検出、FNV-1a 64bitによる決定論的NodeId導出)/`SceneDefinitionGenerator`(`.Collect()`
不使用、生成物はクラスごとに独立)/`SceneNodeSymbolEmitter`(各`partial`プロパティの実装本体)/
`SceneRuntimeEmitter`(`CreateRuntime`、Set引数式をテキストとしてそのまま貼り付け、元ファイルの
usingディレクティブを複製)を追加した。

`samples/SceneAuthoringDemo/`(新規、`SoaSystemsDemo`と同じプロジェクト構成)で見本のRoot/
MainCamera/Player構成を実際に動くコードとして再現し、`dotnet run`で"OK"を確認した。

## 帰結

### 得られたもの
- C#コードそのものをSceneの正本にする最初の足がかりが動作するコードとして完成した。同じ
  `Define`定義から、Runtime用コード(`CreateRuntime`)とノード識別子(`SceneNodeSymbol`実装)の
  両方をSGが単一のソースから生成する。
- `Parent`ComponentがECS本体に追加され、これまで存在しなかったHierarchy/親子関係の基盤が
  `CSharpGameEngine.Entities`全体で再利用可能になった。

### 保留したもの
- Hierarchy Docking WindowによるRoslyn Workspace経由のライブ編集(Phase B以降)。
- `SceneNodeId`のRename間安定化(見本自身が明示的に未解決とした問題を、Phase Aでもそのまま
  未解決として保留)。
- 8型引数を超えるノードへの`EntityCommandBuffer`経由の段階的Component追加(SCENE009で
  コンパイルエラーにするだけで、回避策は提供しない)。
- JSON版Scene実装の実際の置き換え(今回は無改修のまま並行して残す)。

## 検証結果(実際に実行して確認)

```
dotnet build src/CSharpGameEngine.Entities/...csproj -c Debug        → 成功、警告0/エラー0
dotnet test tests/CSharpGameEngine.Entities.Tests/...                → 成功、61件(9件新規)
dotnet build src/CSharpGameEngine.CodeAnalysis/...csproj -c Debug    → 成功、警告0/エラー0
dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/...            → 成功、52件(16件新規)
dotnet run --project samples/SceneAuthoringDemo                      → "OK"
dotnet build CSharpGameEngine.slnx -c Debug / -c Release             → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx                                     → 成功、13プロジェクト・380件全成功
```

## 関連機能

- F-L3-011(新規) — Scene Authoring基盤型(`CSharpGameEngine.Entities`の`Parent`/`SceneNodeTag`/
  `SceneNodeSymbol`系)
- F-L7-012・F-L7-013(新規) — `SceneDefinitionGenerator`(Analyzer/Model/Emitter、診断SCENE001〜009)
- F-L9-006(新規) — `samples/SceneAuthoringDemo`
