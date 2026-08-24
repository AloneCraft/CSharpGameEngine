# ADR-0044: 実行時状態をComponent型ごと分離する

## 状態

Accepted(2026-08-20)。CodeVerified — 実機確認は未実施。

## 文脈

Componentパレット(M0052)から追加したComponentが、Inspectorで「解析待ち…」のまま値を1つも
編集できないという実機報告から調査を始めた。原因を追うと、より根本的な設計上の混在が見つかった。

**Component構造体が3種類のフィールドを混ぜている:**

| 種別 | 例 |
|---|---|
| ① 既定値0で意味が通る作者データ | `PositionX`, `ColorR`, `Intensity`, `Range` |
| ② 非ゼロ既定値が要る作者データ | `Transform.RotationW=1`, `ScaleX/Y/Z=1` |
| ③ **エンジンが実行時に埋めるハンドル** | `Renderer`, `Window`, `Voice`, `AudioEngine`, `Loaded`, `RenderCamera`, `ObjectIndex` |

③をInspectorへ「編集可能な値」として並べるのは積極的に誤りである。ユーザーが`Renderer`欄に
入力できてしまうし、パレットが撒く既定値の対象にも入ってしまう。

## 検討した選択肢

### 案A: 全フィールドを`required`にする

C#コンパイラが明示指定を強制するので、どんなインスタンス化も必ず全フィールドを書くことになり、
「解析待ち」問題が構造的に消える。**却下した。**

- `required`と**意味のある既定値は排他**である。`Transform`は明示コンストラクタで
  `RotationW=1f,ScaleX/Y/Z=1f`を設定し、コメントに「Scene DSLの`new Transform()`がUnityと同じ
  identity transformになるための既定値」と意図を明記している。全required化すると`new Transform()`が
  違法になり、この設計意図が到達不能になる。`FirstPersonStageScene`の約55ノードすべてが
  `RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f`を書く羽目になる — それはまさにこのコンストラクタが
  避けるために存在しているものである。
- ③のフィールドでrequiredは有害。`Renderer=null`を全呼び出し箇所で明示させることになり、
  「エンジンが後で埋める」という設計意図と真逆の記述を強制する。

### 案B: 属性で印を付ける(`[SceneRuntimeState]`)

変更は小さく正確だが、ADR-0033の「属性を使わない構造的推論」と方向が違う。
作者が書く型の定義に編集ツール都合のノイズが混ざる。

### 案C: 構造的ルール(nullableフィールドを実行時ハンドルとみなす)

属性不要でADR-0033に沿うが、`Level.Loaded`(bool)と`Renderable.ObjectIndex`(int)が例外になり、
規則が「ほぼ当たる」止まりになる。規則が例外を持つと、判定を書く場所が増えてズレの温床になる。

## 決定

**③を別のComponent構造体へ分離し、マーカーインターフェースで随伴関係を宣言する。**

```csharp
public interface IRuntimeComponent { }
public interface IRuntimeComponent<TAuthored>:IRuntimeComponent where TAuthored:struct { }
```

```csharp
public struct DirectionalLight {                       // 作者が書く面。これがInspectorに並ぶ
    public float DirectionTowardLightX;
    public float ColorR;
    public float Intensity;
}
public struct DirectionalLightRuntime:IRuntimeComponent<DirectionalLight> {
    public StageRenderer?Renderer;                     // エンジンが埋める。編集面に出ない
}
```

**既定値はコンストラクタで表現する**(`required`は使わない)。これは`Transform`が既に確立している
パターンで、意味のある非ゼロ既定値を表現できる唯一の手段である。

### 随伴付与

`IRuntimeComponent<TAuthored>`を実装する型は、作者が`TAuthored`を宣言したNodeへ
Source Generatorが自動的に付与する。仕組みは`Parent`/`SceneNodeTag`と**完全に同じ**で、
`SceneInstantiateEmitter.BuildEffectiveComponents`という1箇所を通る。

この関数はArchetype登録(`SceneComponentSetCollector.Collect`)とコード生成(`EmitNode`)の
**両方から共有される**。両者が同じ集合を導出しないと「登録されていないArchetypeをCreateしようとする」
という診断困難なビルド破壊になるため、対応表(`SceneRuntimeComponentMap`)は引数で渡す —
渡し忘れがコンパイルエラーになる形にしてある。

対応表は`GameSystemGenerator`のインクリメンタルパイプラインで`(AuthoredFqn, RuntimeFqn)`の
**文字列ペア**として流す。`SceneEmitModel`と同じ理由でシンボルは持たない
(Compilationをルート保持するとキーストロークのたびにキャッシュが無効化される)。

### なぜマーカーインターフェースなのか

- `ISystem`が同じく構造的推論の入口である前例と整合する。
- **作者が書く側の型定義を一切汚さない。** 印が付くのは実行時Component側だけ。
- パレット除外(`IsAddable`)とSCENE119判定が、候補型自身へのO(1)述語で済む。
- 随伴先を持たない実行時Component(`Renderable`/`FramePresent` — 作者が宣言せず
  `World.Create`だけで作られる)を非generic版で同じ語彙に収められる。

命名規約(`Xxx` ↔ `XxxRuntime`)だけで暗黙に対応付ける案は却下した。無関係な`FooRuntime`を
誤って随伴させうるし、リネームで静かに壊れる。入れ子型`DirectionalLight.Runtime`も却下 —
`GameSystemIndexAssigner.ExtractSimpleTypeName`が最後のドット以降を採るため単純名が全て`Runtime`に
なり、SYSGEN020(単純名衝突)で総崩れになる。

## 帰結

### 得られるもの

- **Inspector/パレットの表示面が定義される。** 作者が設定すべき項目だけが編集可能として並ぶ。
- パレットが`new T { 全作者フィールド=default }`を撒く話が筋として通る(③が混ざらないので)。
- `Level.Sun`のような「Component値を入れ子で持つ初期値の入れ物」から、意味のない
  `Renderer=null`の入れ子が消える。
- Entity生成でコピーの変異が不要になる:
  `var Sun=Pending;Sun.Renderer=R;Create(Sun)` → `Create(Pending,new DirectionalLightRuntime{Renderer=R})`

### 得られないもの(正直に記録する)

**`Renderer is null`の判別ロジックは消えない。** 随伴付与により作者側のEntityも
`DirectionalLightRuntime`(`Renderer=null`)を持つため、`LevelLoad`の判別は
`Sun.Renderer is null` → `Runtime.Renderer is null` へ移るだけである。この分離で得られるのは
編集面の正しさであって、実行時ロジックの単純化ではない。

### コスト

- Component型が8種増える(18→20実使用)。`MaxComponentTypes=64`(SYSGEN026)に対しては十分な余裕。
- ③を読むSystemは`ref`パラメータが1つ増える。ディスパッチは部分集合一致
  (`GameSystemLoopEmitter`)なので既存のクエリは壊れない。

### 新しい診断

- **SCENE119**: 作者が実行時Componentを`Components`へ明示した。随伴先を名指しして案内する。
  `Parent`/`SceneNodeTag`専用のSCENE115とは別IDにしたのは、あちらが閉じた2件の集合で
  随伴先を名指しできないため。
- **SYSGEN027**: 同じ作者Componentを2つの実行時Componentが主張した。どちらを付与するかが
  非決定になるので、曖昧なまま片方を選ぶより明示的に止める。

## 関連

- ADR-0033(属性を使わない構造的推論)— `ISystem`の前例に倣った
- ADR-0038(Archetype番号の安定性)— 随伴Componentは`AdditionalComponentSets`より先に
  System側で索引が付くため保証は保たれる
- ADR-0040(Scene DSL / C#が正本)— `BuildEffectiveComponents`の共有原則をそのまま継承
- M0052(Componentパレット)— この設計を必要とした発端


## Shared-source ownership update (ADR-0048)

Runtime companion components and their systems are compiled from `CSharpGameEngine.Runtime.Windows` shared source with the consuming Logical precision. They are not authored scene components.