# ADR-0074 — シーン JSON の角度を turns（1 = 1 回転）で保存する
- 状態: Accepted（2026-10-01、ユーザー承認: 「角度は json の保存としては 1 を一回転として」、Inspector の表示は度、C# の Component のフィールドは今の単位のまま）
- 関連: ADR-0070（シーン JSON）、ADR-0072 決定 5（角度の単位、保留だったもの）、M0096

## 背景
2026-10-01 のリポジトリで観測した事実:
- 同じシーン JSON の中で、回転（`Transform.Rotation` の `EulerRuf`）はラジアン、`ControlYawDegrees` や `SunDiskAngleDegrees` は度だった。JSON の数値だけを見ても単位が分からない。
- 角度を含むシーンは 6 ファイル（`EulerRuf` 123 か所、ほかの角度 5 か所）。UE から取り込んだ回転は、π/2 からわずかにずれたラジアン（`1.5707961` など）として書かれていた。

## 決定

### 1. 角度は Component の宣言で示す
- `CSharpGameEngine.Contracts.AngleAttribute(AngleUnit)` を、角度のフィールド・プロパティ・コンストラクタ引数に付ける。付けたメンバーの値の中の数値はすべて角度として扱う（例: `LogicalQuaternion(EulerRuf)` の `EulerRuf` は `LogicalVector3` で、その R / U / F が角度）。
- 対象: `LogicalQuaternion` の `EulerRuf`、`Camera.FovYRadians`、`SpotConeAngles` の 2 つの引数、`SkyAtmosphere.SunDiskAngleDegrees`、`CharacterMovement` の `WalkableFloorAngleDegrees` と `RotationRateDegrees`（角速度。turns 毎秒になる）、`PlayerController` の 4 つ。
- C# のメンバーの単位と名前は変えない。
- Numerics（`LogicalQuaternion`）から属性を使うため、Numerics が Contracts を参照するようにした。

### 2. JSON は turns、版は 2
- `schemaVersion` 2 の文書では、角度の数値は turns（1 = 1 回転）で書く。版 1 の文書は、各メンバーの単位のまま読む（後方互換）。書き出しは常に版 2。
- 変換は JSON の境界だけで行う。
  - 読み込み: 生成器（`SceneJsonSceneAnalyzer`）とプレビューホスト（`SceneJsonRuntimeValues`）は、turns をメンバーの単位に直す。
  - 書き出し: 変換器（`SceneJsonConverter`）は、メンバーの単位から turns に直す。
  - 生成されるコード、Designer の C# 表現、ゲームの値は、今までどおりメンバーの単位。
- 数値の書き方: 読み戻して同じ float になる最短の 10 進表記。変更していない角度は編集のたびに同じ文字列のまま残る。
- 計算は double で行う。float の turns から 2π や 360 を掛けると、元の単位の float に届かない値がある（500 度、1.5707965 ラジアンなど）。そのため変換の 2 つのメソッドだけを NUM001 の例外（`[ExternalNumericAdapter]`）にした。共有ソースは Contracts を参照しないコンパイル（生成器、CLI）にも入るので、属性は `CGE_NUMERIC_ADAPTER` を定義したコンパイル（SceneModel 本体、VSIX、プレビューホスト）だけで付ける。

### 3. 移行
- `CSharpGameEngine.SceneJson.Cli upgrade <game .csproj> [--write]` は、Scenes 以下の版 1 の文書を版 2 に書き換える。角度の数値と `schemaVersion` だけを置き換え、ほかの文字はそのまま残す。書き換えた後に読み直し、全 Component の値が元と同じでなければ書かない。
- リポジトリの全シーン（UserEcsProject 16、ThirdPerson テンプレート 3、stages 10）と、新規シーンの項目テンプレートを版 2 にした。値は 1 ビットも変わっていない（移行ツールの検証による）。
- Designer で版 1 の文書を編集すると、その編集で版 2 になる。変換器が版 2 を書き、`SceneJsonTextMerge` は版番号と角度の数値を、変わった値として書き戻す。

### 4. エディタは度
- Inspector は角度の数値を度で表示し、度で入力を受ける（行の名前に ` (°)`）。ソースにはメンバーの単位で書き戻す。読み取り専用の角度（`EulerRuf` の R / U / F など）も度で表示する。
- ギズモの HUD は以前から度。
- スキーマ（`.cge/scene.schema.json`）は、角度のメンバーに「turns であること」と C# 側の単位を説明として付け、`schemaVersion` は 1 と 2 を受け付ける。

## 結果
- JSON の角度の単位が 1 つになった。90 度は `0.25`、180 度は `0.5`。
- JSON のキーは C# のメンバー名のままなので、`FovYRadians` や `SunDiskAngleDegrees` のように、単位名を含むキーに turns の値が入る。スキーマの説明で補っているが、キーの名前だけを見ると紛らわしい。メンバー名から単位を外す（C# の API の変更）か、JSON のキーだけを別名にするかは、ユーザーの判断事項として残す。

## 却下した案
- float だけで変換する: 上の理由で、届かない値がある。最初の実装はこれで、テンプレートの 2 ファイルの移行が「値が変わる」として止まった。
- 単位をスキーマの説明に書くだけにする（ADR-0072 決定 5 の代案）: JSON の単位が混在したまま残る。
