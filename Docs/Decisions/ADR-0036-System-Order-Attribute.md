# ADR-0036: `[SystemOrder]`属性によるソースコード間の実行順序宣言

## ステータス

承認済み。[[ADR-0033-Static-Archetype-Ecs-Migration]]決定事項2(「属性を一切使わない構造的推論」)
からの**唯一かつ局所的な例外**を導入する。決定事項1(実行順序=記述順)自体は変わらない — 変わるのは
「複数ファイル・複数Systemにまたがったときのタイブレーク」の決め方だけである。

## 背景

ADR-0033決定事項1は、Systemの実行順序を「ソース上の記述順」と定義した。同一ファイル内は
`SourceSpan.Start`昇順で確定するが、複数ファイルにまたがる場合は`SyntaxTree.FilePath`のOrdinal順
(=絶対パス、つまりマシン依存)にフォールバックする。ADR-0033はこれを「未解決事項」として明記し、
「順序が意味を持つSystemは1ファイルへ集約する運用で担保する」とした。

`UserEcsProject`を`Systems/GameSystems.cs`(ゲーム固有)と`Systems/RuntimeSystem.cs`(エンジン側、
いずれ共有ソースへ移設予定)へ分割した際、まさにこの制約に触れた: 前者が後者より先に走る必要が
あるが、それを担保していたのは「たまたまファイル名が`G`と`R`でアルファベット順に一致していた」
という偶然だった。

ユーザーから直接の依頼があった: 「ランタイムに属性を作り、`file`スコープの空クラスへ付けることで
ファイル全体の実行順序を明示的に宣言できるようにしてほしい」(第一次要望)。実装後、さらに
「file orderではなくsystem orderにしてほしい。Systemに付けてもいいしマーカーに付けてもいい。
ソースコード間の実行順序を決められるようにしてほしい」(第二次要望)という追加依頼があり、
「ファイル単位」から「System単位/ファイル単位のどちらも選べる」設計へ拡張した。

## 決定事項

### 1. `CSharpGameEngine.Entities.SystemOrderAttribute`

```csharp
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class SystemOrderAttribute : Attribute
{
    public SystemOrderAttribute(int order) => this.Order = order;
    public int Order { get; }
}
```

`Bootstrap.cs`/`Parent.cs`と同じ「エンジンが持つ小さな型」の一つとして
`src/CSharpGameEngine.Entities/SystemOrderAttribute.cs`に置く。ワイルドカード(`*.cs`)で消費側へ
自動的に伝播するので、消費プロジェクトのcsproj変更は不要。

### 2. 2通りの付け方

**①`ISystem`実装struct自身に直接付ける** — そのSystem**だけ**の順序(最も細かい粒度):

```csharp
[SystemOrder(0)]
internal struct FooSystem : ISystem { ... }
```

**②`file`スコープのマーカークラスに付ける** — そのファイル内で、自分自身の順序を持たない
**全Systemの既定値**になる(ファイル単位のまとめ指定):

```csharp
[SystemOrder(0)]
file sealed class Order
{
}
```

同じSystemに両方が影響し得る場合は①が優先される(`GameSystemExecutionOrder.OrderOf`参照)。
どちらも無ければ既定値0で、既存のファイルパスOrdinal順というタイブレークにフォールバックする
(後方互換)。

クラス名は任意(`file`スコープなのでファイルをまたいだ名前衝突が起きない)。マーカーとして使う
場合は**`file`修飾子でなければSYSGEN022**を報告する(通常のクラスに付けると、そのクラス名が
ファイルの外へ漏れる=マーカーのつもりが公開APIの一部になるため)。struct側に付けたが`ISystem`を
実装していない場合は**SYSGEN025**(値がどこからも参照されず静かに無視されるのを防ぐ)。

### 3. 意図的な例外である理由

Component/Systemの役割はComponent/System自身の構造(ref-kind、末尾`ref State`の有無など)から
判別できる — ADR-0033決定事項2はこれを根拠に「属性ゼロ」を選んだ。しかし**ソースコード間の
相対順序**はどの構造からも導出できない情報であり、宣言的に指定する以外に手段が無い。したがって
「構造的に導出できない情報にだけ、最小限の属性を許す」という一貫した基準の下でこの例外を設ける。

①(Systemへ直接)を許したことで、Component/System自体は完全に無属性という原則には一点だけ
穴が開くが、これはユーザーの明示的な要望であり、かつ「使わなければ何も変わらない」opt-inの
性質は保たれている。

### 4. 比較キーの優先順位

`GameSystemExecutionOrder.Compare`(新規、`GameSystemIndexAssigner`と`GameSystemEntryPointEmitter`が
共有)が単一の比較関数になる。1つのSystemの実効的な順序値は`OrderOf`が決める:

1. `[SystemOrder]`をSystem自身に直接付けた明示値(`GameSystemOrderContext.SystemOrderByFqn`)
2. `[SystemOrder]`をファイルマーカーへ付けたファイル既定値(`GameSystemOrderContext.FileOrderByPath`、
   いずれも宣言が無ければ0)

その値を使って:

3. `FilePath`のOrdinal順(ADR-0033からの既存の決定的タイブレーク、後方互換のため残す)
4. ファイル内の`DeclarationPosition`(ADR-0033決定事項1そのもの、常に記述順)

### 5. 診断: SYSGEN022〜025

| ID | 内容 |
|---|---|
| SYSGEN022 | `[SystemOrder]`をファイル既定値として使ったが、その付け先が`file`スコープでない |
| SYSGEN023 | 1つのファイルに`[SystemOrder]`付きマーカーが2つ以上ある(ファイル既定値が定まらない) |
| SYSGEN024 | 2つ以上の**異なるファイル**が同じ**明示的なファイル既定値**を宣言している |
| SYSGEN025 | `[SystemOrder]`をstructに付けたが`ISystem`を実装していない(値がどこにも効かない) |

SYSGEN024はユーザーへ確認の上で「ビルドエラーにする」を選んだ。**ただしファイル既定値同士の
重複だけが対象**で、System単体への明示値同士が重複しても許容する(意図的な「同着」でありうる
ため — 例えば複数の独立したSystemを同じ優先度グループとして扱いたい場合)。属性を付けていない
ファイル同士が暗黙の0で並ぶのも対象外。`ForAttributeWithMetadataName`で収集した「明示的に属性を
使った対象」だけを見るため、「使わなければ何も変わらない」というopt-inの性質が保たれる。

### 6. `UserEcsProject`への適用(両方の付け方を実演)

`Systems/GameSystems.cs`の`GameStartupSystem`に`[SystemOrder(0)]`を**直接**付けた(①の実例)。
`Systems/RuntimeSystem.cs`は複数Systemをまとめて後ろへ回したいので、`file`スコープの
マーカーへ`[SystemOrder(1)]`を付けた(②の実例)。両ファイルの冒頭コメントから
「`G < R`だから」という偶然頼みの説明を削除し、明示的な宣言に置き換えた。生成される
`GameSystem.Loop.g.cs`のArchetype出現順は変更前と**完全に同一**である(偶然にたどり着いていた
結論を、今度は宣言で再現している)。

## 代替案として検討したもの

- **ファイルにしか付けられない設計(第一次実装)**: ユーザーから「Systemに直接付けてもよくしたい」
  という明示の追加要望があり拡張した。ファイル単位のまとめ指定は依然として便利なため両立させた。
- **マーカーinterface**(`IFrameBeginSystem`/`IFrameEndSystem`など、ADR-0035の未解決事項が示唆して
  いた案): 「先頭/末尾」の2値しか表現できず、任意個のSystem・ファイルへの全順序を表現できない。
- **System単体の明示値の重複もエラーにする**: ファイル既定値の重複とは性質が異なる(複数の
  Systemを意図的に同じ優先度として扱いたい場合が正当にありうる)ため、System側では重複チェック
  をしないと決めた。
- **属性をISystem実装struct自身にしか付けられなくする(ファイルマーカーを廃止)**: ファイル単位で
  複数のSystemをまとめて動かしたいケース(`RuntimeSystem.cs`のような、4つのSystemを一括りにしたい
  場面)で、全Systemに同じ値を付けて回るのは冗長。両方を残すことで粒度を選べるようにした。

## 帰結

### 得られたもの
- ファイル横断・System横断のSystem実行順序が、ファイル名やパスという偶然の要素から独立した。
- 細かい制御(System単体)と粗い制御(ファイル単位)の両方を、同じ属性・同じ優先順位ルールで
  選べるようになった。
- 単一ファイル構成(`UserGameProject/Systems/Systems.cs`)や属性を使わない既存プロジェクトは
  一切影響を受けない(完全な後方互換)。

### 失ったもの
- Component/Systemの「属性ゼロ」という原則に、Systemへの直接付与を許した分だけ穴が開いた。
  ただし完全にopt-inであり、使わない限り何も変わらない。

## 検証結果(実際に実行して確認)

```
dotnet build src/CSharpGameEngine.CodeAnalysis/CSharpGameEngine.CodeAnalysis.csproj -c Debug
  → 成功、警告0/エラー0

dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/...
  → 73/73 成功(新規9件: 逆順E2E1件、後方互換回帰1件、System直接指定の優先1件、
    SYSGEN022〜025各1件、System単体の重複が許容されることの回帰1件、暗黙0の非衝突1件)

dotnet build UserEcsProject/UserEcsProject.csproj -c Debug
  → 成功、警告0/エラー0

dotnet build CSharpGameEngine.slnx -c Debug   → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Release → 成功、警告0/エラー0
dotnet test  CSharpGameEngine.slnx            → 全9プロジェクト成功、382件・失敗0
```

`GameSystemOrderTests.CrossSourceExecutionOrder_FollowsSystemOrderValue_NotAlphabeticalFileName`は、
ファイル名をわざとアルファベット順に**逆らわせて**配置し(`AFirst.cs`=System自身に`[SystemOrder(1)]`、
`Startup.cs`=`[SystemOrder(-1)]`、`ZFirst.cs`=fileマーカー経由`[SystemOrder(0)]`)、生成された`Main`を
実際に実行して実行順が`Startup → ZFirst → AFirst`(=`[SystemOrder]`の値どおり)になることを証明した。
`SystemOrderOnTheSystemItself_OverridesTheFileMarkerDefault`は、ファイル既定値0に対しSystem単体の
明示値-1が優先されることを同様に実行して確認した。

`UserEcsProject/Generated/.../GameSystem.Loop.g.cs`を目視確認し、Archetype出現順が本変更の前後で
一致することを確認した。実機では`UserEcsProject.exe`を起動し、`PrintWindow`でウィンドウタイトルと
クリア色(`#598CD9`)を確認、例外出力なし。

## 関連機能

- F-L3-012 — `CSharpGameEngine.Entities`(`SystemOrderAttribute.cs`の追加)
- F-L7-015 — `GameSystemGenerator`(`GameSystemOrderAnalyzer`/`GameSystemExecutionOrder`の追加)
- F-L7-016 — `GameSystemGenerator`診断(SYSGEN022〜025の追加)

関連: [[M0016-File-Order-Attribute]]
