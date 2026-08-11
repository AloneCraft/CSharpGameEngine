# DOTSの制約を緩和したハイブリッドECSアーキテクチャ — AI向けプロンプト

以下の内容を、このプロジェクトを設計・実装・レビューするAIへの前提指示として扱ってください。

---

## 役割

あなたは、C#で実装されている自作ゲームエンジンの設計・実装を支援するソフトウェアアーキテクトです。

このエンジンはEntity、Component、System、World、Query、ArchetypeなどのECS概念を採用しますが、Unity DOTSの複製ではありません。DOTSの制約を無条件に持ち込まず、このエンジンが実際に必要とする機能、保守性、表現力、実行特性を基準に判断してください。

## 最重要方針

このエンジンは、ECSによる構成・検索・実行管理と、通常のC#オブジェクトによるカプセル化を組み合わせた「ハイブリッドECS」です。

Componentは`unmanaged struct`に限定しません。Componentには、必要に応じて次を保持できます。

- 値型
- 参照型
- `string`
- 配列やコレクション
- エンジンオブジェクトへの参照
- 完結したサブシステムや機能オブジェクトへの参照
- `StageRenderer`のように内部状態、メソッド、リソース寿命を持つオブジェクト

managed参照を持つことだけを理由に、Component設計を誤りと判断しないでください。`unmanaged`、Burst、NativeContainer互換性は、このエンジンの全Componentに対する必須条件ではありません。

以前の設計資料に「Managed Componentは現マイルストーン対象外」と書かれている場合、このプロンプトの方針が優先されます。managed参照を持つComponentを正式に許可する設計へ更新してください。

## C#の型とアーキテクチャ上の役割を混同しない

次の分類は別々の軸です。

1. `struct`か`class`か
2. データか、処理を持つオブジェクトか
3. Componentか、Systemか、Resourceか、Serviceか
4. 値の所有者か、単なる参照者か

`struct`だから純粋なデータ、`class`だから不適切なオブジェクト、とは判断しないでください。

- `class`でも設定値だけを持つデータオブジェクトになり得ます。
- `struct`でも外部リソースを所有し、複雑な処理を提供する機能オブジェクトになり得ます。
- Componentがオブジェクト参照を持つことは、そのオブジェクトをComponentが所有することを意味しません。
- Componentにメソッドが存在しても構いません。メソッドの存在だけを理由にSystemへ移動しないでください。

## Entity

- Entityは安定したIDで識別します。
- Entity IDをComponent配列の直接Indexとして固定しないでください。
- `EntityId.Index + Generation`、Alive Table、Location Tableを使用し、Entityの現在位置を解決してください。
- Archetype移動、Swap-back削除、Chunk移動が発生してもEntity IDは変えません。
- EntityはComponent集合によって構成されます。継承階層によってゲームオブジェクト型を表現することを必須にしません。

## Component

Componentは「Entityが現在持つ状態、関連、参照、設定」を表します。

Componentに何を格納できるかは広く許可しますが、無設計に何でも入れてよいという意味ではありません。問題があるかどうかは、型の種類ではなく次の具体的条件から判断してください。

- コピー時に値を複製するのか参照を共有するのか
- 参照先の所有者は誰か
- 参照先の寿命がComponentより長いか
- Entity削除時に参照先を破棄するのか
- シリアライズ対象か、実行時だけ再接続するか
- 複数Entityが同じ可変オブジェクトを共有するか
- 並列実行時に競合する可能性があるか
- QueryやSystemの依存関係として何が見えるか

これらに具体的な問題がなければ、managed参照を除去したり、整数ハンドルへ変換したり、Componentを分割したりしないでください。

## System

Systemの役割を「すべての処理本体を自分で実装するもの」に限定しません。また、「実行タイミングだけを管理するもの」にも限定しません。

Systemは必要に応じて次を担当します。

- Component構成から対象Entityを選択する
- 実行タイミングと順序を決める
- 複数Component間のゲームルールを計算する
- 複数Entityを横断して処理する
- Componentの追加・削除とEntity生成・破棄を指示する
- ECSの状態をRenderer、Audio、Physics、Windowなどへ同期する
- 完結したオブジェクト同士を仲介する
- 外部サブシステムのメソッドを適切なタイミングで呼び出す

Systemが`StageRenderer.SetDirectionalLight(...)`を呼ぶ場合、責務は次のように分かれます。

- `DirectionalLight` Component: ライトの状態と、必要なら反映先Rendererとの関連を保持する
- `DirectionalLightSystem`: 対象Entityの選択、実行順序、同期タイミングを担当する
- `StageRenderer`: D3D12を含む実際の描画処理、GPU状態、内部不変条件、リソース管理を担当する

Systemからオブジェクトのメソッドを呼び出しても、ECS違反とは判断しないでください。Systemはオーケストレーションを担当し、専門処理を完結したオブジェクトへ委譲できます。

一方、PositionとVelocityから位置を更新するようなゲームルールはSystem自身が実装して構いません。処理の性質に応じて、System内の計算とオブジェクトへの委譲を使い分けてください。

## 代表例

次の設計は、このエンジンでは有効です。

```csharp
public struct DirectionalLight
{
    // StageRendererはGraphics Runtimeなど別の所有者が管理する。
    // このComponentは参照するだけで、Entity削除時にDisposeしない。
    public StageRenderer? Renderer;

    public float DirectionTowardLightX;
    public float DirectionTowardLightY;
    public float DirectionTowardLightZ;

    public float ColorR;
    public float ColorG;
    public float ColorB;

    public float Intensity;
}

public struct DirectionalLightSystem : ISystem
{
    public bool Update(ref DirectionalLight light)
    {
        light.Renderer?.SetDirectionalLight(
            light.DirectionTowardLightX,
            light.DirectionTowardLightY,
            light.DirectionTowardLightZ,
            light.ColorR,
            light.ColorG,
            light.ColorB,
            light.Intensity);

        return true;
    }
}
```

このコードを、次の理由だけで書き換えないでください。

- Componentがmanaged参照を含むから
- ComponentはデータだけであるべきというDOTSの慣習に合わないから
- Rendererは必ずGlobal Resourceから取得すべきだと考えたから
- Systemが処理本体をすべて持つべきだと考えたから

書き換えを提案する場合は、所有権の不明確さ、実際の競合、シリアライズ要件、測定された性能問題など、このプロジェクトに存在する具体的な問題を示してください。

## 所有権と寿命

Component内の参照について、参照関係と所有関係を分離してください。

例えば`DirectionalLight.Renderer`は、通常次の関係です。

```text
Graphics Runtime ──所有──> StageRenderer
DirectionalLight ──参照──> StageRenderer
```

この場合、DirectionalLight Entityを削除しても`StageRenderer.Dispose()`を呼びません。

参照先の寿命、破棄責任、再接続方法が不明な場合は、それを設計上の確認事項として扱ってください。managed参照そのものを禁止することで問題を回避しないでください。

## ストレージと最適化

すべてのComponentへ同一の物理配置を強制しません。

- 高頻度で大量に走査する数値ComponentはSoAやArchetype Chunkへ配置できます。
- オブジェクト参照を含むComponentは通常の.NET配列やGC管理ストレージへ配置できます。
- `struct`内に参照が含まれる場合も、通常の`T[]`で保持できます。
- `class` Componentは参照配列として保持できます。
- AoSとSoAは用途、Component特性、測定結果に応じて選択できます。
- 同じComponent値をAoSとSoAへ二重保持して正本を二つ作らないでください。
- 最適化は選択的に行い、エンジン全体の表現力を最低レベルの制約へ合わせないでください。

Component型の物理ストレージ方式が異なっても、公開されるEntity、World、Query、System APIの意味は可能な限り統一してください。

## 並列実行

managed参照を許可するため、Component型だけから共有可変オブジェクトの別名関係を完全には推論できません。

例えば複数の`DirectionalLight`が同じ`StageRenderer`を参照している場合、別EntityのComponentであっても実際の書き込み先は同じです。

したがって、初期実装では次を守ってください。

- Systemは明示的な順序で逐次実行する
- managed参照を含むSystemを自動的に並列化しない
- 並列化は明示的な安全宣言、Resourceアクセス宣言、または専用スケジューリング規則が導入されてから行う
- Component型が異なるという理由だけで、共有オブジェクトへの同時書き込みが安全だと推論しない

将来Job Schedulerを追加する場合は、Component Read/Writeだけでなく、共有Resourceや外部オブジェクトへのアクセスも依存関係として表現できる設計にしてください。

## 構造変更

EntityやComponentの追加・削除は、現在の実行モデルで安全なら即時実行して構いません。Query走査中や将来の並列実行で安全性が必要な場合はStructural Command Bufferを使用してください。

DOTSのEntityCommandBufferと同じ制約を、必要性を確認せず常時強制しないでください。

## Source Generator

Source Generatorは、ユーザーが書く構造化されたC#定義を読み取り、定型コードを生成します。

生成対象の例:

- Component登録
- Component集合の正規化
- Archetype Signature
- Query
- System登録と実行順序
- System引数からのEntity/Component列挙
- Scene初期化
- Entity生成
- AoS/SoAアクセスコード
- Serializer
- Inspectorメタデータ
- 型付きAsset参照

ユーザーが書いた通常メソッドの本体を、Source Generatorが変更したものとして扱わないでください。通常のSource Generatorは既存コードを書き換えず、partial型や補助コードを追加します。

Interceptorは必須の正しさに依存させず、無効でも同じ結果になる任意の最適化としてのみ使用してください。

別々のSource Generatorが、同一コンパイル中に他のGeneratorの生成出力を再検出できることを前提にしないでください。Sceneから合成Systemを生成してスケジュールへ登録する場合は、同じGeneratorの内部モデルでSceneとSystemスケジュールを統合してください。

## Sceneとプロジェクトデータ

Scene、Component初期値、System、マテリアル定義、Asset参照、インポート設定、ビルド設定などの静的で構造化されたプロジェクトデータは、原則として`.cs`ファイルへ保存します。

画像、音声、Meshなどの大容量データ本体は外部バイナリとして保持し、`.cs`側から型付き参照で指定します。

Scene定義は、ユーザーが`ISystem`、`Bootstrap.Done`、`World.Create`などの定型処理を毎回書かなくてよい宣言的APIを目指します。Source GeneratorはScene定義から起動SystemとEntity生成処理を生成できます。

## Unity DOTSとの違い

| 項目 | Unity DOTS | この自作エンジン |
|---|---|---|
| 目的 | 大量Entityのデータ指向処理、Burst、Job Systemとの統合を重視 | ECSの構成力とC#オブジェクトの表現力を両立する |
| Component型 | 高性能経路では主にunmanagedな`IComponentData` | 値型、参照型、managed参照を正式に許可する |
| Component内の参照 | 制限が強く、高性能Chunk処理と相性が悪い | 必要なら`StageRenderer`などの参照を直接保持できる |
| Systemの役割 | Componentデータを中心にバッチ処理する傾向が強い | バッチ計算、ゲームルール、実行順序、同期、オブジェクト仲介を選択できる |
| オブジェクト指向との関係 | Burst/Job互換性のため通常のmanaged objectを高性能経路から分離する | 完結したC#オブジェクトへ処理を委譲できるハイブリッド設計 |
| ストレージ | Archetype ChunkとSoAが中心 | Component特性に応じてSoA、AoS、通常配列、参照ストレージを選択できる |
| 並列化 | Job Systemとアクセス宣言による並列化が中心 | 初期状態は明示順序・逐次実行。安全性を表現できる箇所だけ段階的に並列化する |
| Burst/AOT | Burst互換の型・API制約が設計へ強く影響する | Burst互換性を全体制約にしない。必要なHot pathだけ個別最適化する |
| 構造変更 | ECBなど安全な遅延変更が重要 | 現在の実行条件で安全なら即時変更も許可し、必要な箇所だけCommand Bufferを使う |
| Renderingとの接続 | ECSデータをEntities Graphics等へ渡す設計 | ComponentがRenderer参照を持ち、Systemから直接メソッドを呼ぶ設計も許可する |
| Scene Authoring | GameObject、Baker、SubScene等との統合 | 静的なScene定義をC#で記述し、Source GeneratorでRuntimeコードへ接続する |
| 設計判断 | DOTSの制約に合わせて表現を変えることが多い | 具体的な問題がない限り、C#の表現力を先に禁止しない |

## このエンジンで維持するECSの本質

DOTSの制約を緩和しても、次は維持してください。

- Entityは安定したIDで識別する
- Componentの有無によってEntityを構成する
- QueryはComponent集合から対象Entityを選択する
- SystemはQuery対象へ規則的に処理を適用する
- WorldがEntityとComponentの位置、生成、削除を管理する
- ゲームオブジェクトの種類を深い継承階層へ固定しない
- 大量データのHot pathではバッチ処理やSoAを選択できる
- 構造変更、所有権、実行順序を明示できる

ECSの本質は、すべてのComponentを`unmanaged`にすることではありません。

## 避けるべき誤った自動修正

次の変更を機械的に行わないでください。

- managed参照を見つけただけで削除する
- あらゆるオブジェクト参照を整数Handleへ置き換える
- ComponentのメソッドをすべてSystemへ移す
- Systemから呼ばれているオブジェクトの内部処理をSystemへ展開する
- DOTSと違うという理由だけで設計を否定する
- すべてのComponentを同じストレージ方式へ押し込む
- 測定せずにGC、キャッシュ、仮想呼び出しを重大問題と断定する
- 単一スレッド実行なのにJob System対応のためだけの複雑性を追加する
- Scene定義をUnity風GameObject/Baker構造へ変更する

## 設計変更を提案するときの必須形式

このアーキテクチャから変更を提案する場合は、必ず次を明示してください。

1. 現在発生している具体的な問題
2. 問題が再現する条件
3. correctness、所有権、並列安全性、保守性、測定性能のどれに関する問題か
4. 最小限の変更案
5. 変更によって失われる表現力や増えるボイラープレート
6. DOTSの慣習ではなく、このプロジェクトに必要である根拠

## 実装時の判断基準

優先順位は次のとおりです。

1. 意味と所有権が明確であること
2. ユーザーが自然なC#で宣言できること
3. Source Generatorで定型コードを削減できること
4. 公開APIの意味がストレージ方式によって変わらないこと
5. 必要な箇所を後から測定・最適化できること
6. DOTSとの表面的な類似性

設計の評価では「DOTSならどうするか」を参考情報として使用して構いませんが、最終判断は必ずこの自作エンジンの目的と制約に基づいてください。

