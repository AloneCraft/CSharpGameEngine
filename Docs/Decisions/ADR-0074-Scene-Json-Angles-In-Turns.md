# ADR-0074 — シーン JSON の角度を turns（1 = 1 回転）で保存する
- 状態: Accepted（2026-10-01、ユーザー承認: 「角度は json の保存としては 1 を一回転として」、Inspector の表示は度、C# の Component のフィールドは今の単位のまま。キーの名前は「JSON のキーだけ別名」）
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

### 2. JSON は turns、版は 2（キーの別名は版 3、決定 5）
- `schemaVersion` 2 以降の文書では、角度の数値は turns（1 = 1 回転）で書く。版 1 の文書は、各メンバーの単位のまま読む（後方互換）。書き出しは常に現在の版（3）。
- 変換は JSON の境界だけで行う。
  - 読み込み: 生成器（`SceneJsonSceneAnalyzer`）とプレビューホスト（`SceneJsonRuntimeValues`）は、turns をメンバーの単位に直す。
  - 書き出し: 変換器（`SceneJsonConverter`）は、メンバーの単位から turns に直す。
  - 生成されるコード、Designer の C# 表現、ゲームの値は、今までどおりメンバーの単位。
- 数値の書き方: 読み戻して同じ float になる最短の 10 進表記。変更していない角度は編集のたびに同じ文字列のまま残る。
- 計算は double で行う。float の turns から 2π や 360 を掛けると、元の単位の float に届かない値がある（500 度、1.5707965 ラジアンなど）。そのため変換の 2 つのメソッドだけを NUM001 の例外（`[ExternalNumericAdapter]`）にした。共有ソースは Contracts を参照しないコンパイル（生成器、CLI）にも入るので、属性は `CGE_NUMERIC_ADAPTER` を定義したコンパイル（SceneModel 本体、VSIX、プレビューホスト）だけで付ける。

### 3. 移行
- `CSharpGameEngine.SceneJson.Cli upgrade <game .csproj> [--write]` は、Scenes 以下の版 1 と 2 の文書を版 3 に書き換える。版 1 の角度の数値、角度のキー（決定 5）、`schemaVersion` だけを置き換え、ほかの文字はそのまま残す。書き換えた後に読み直し、全 Component の値が元と同じでなければ書かない。
- リポジトリの全シーン（UserEcsProject 16、ThirdPerson テンプレート 3、stages 10）と、新規シーンの項目テンプレートを版 3 にした。値は 1 ビットも変わっていない（移行ツールの検証による）。
- Designer で古い版の文書を編集すると、その編集で版 3 になる。変換器が版 3 を書き、`SceneJsonTextMerge` は版番号、角度の数値、キーの変わった Component の値を、変わった値として書き戻す。

### 4. エディタは度
- Inspector は角度の数値を度で表示し、度で入力を受ける（行の名前に ` (°)`）。ソースにはメンバーの単位で書き戻す。読み取り専用の角度（`EulerRuf` の R / U / F など）も度で表示する。
- 行の名前は JSON のキー（決定 5）。度で表示する行が `FovYRadians` と読めないようにするため。ツールチップと編集の対象は C# のメンバー名のまま。
- ギズモの HUD は以前から度。
- スキーマ（`.cge/scene.schema.json`）は、角度のメンバーを決定 5 のキーで載せ、「turns であること」と C# 側のメンバー名と単位を説明として付ける。`schemaVersion` は 1、2、3 を受け付ける。

### 5. 角度のキーは単位の語を外した別名（版 3）
- 角度（`[Angle]`）のメンバーとコンストラクタ引数のキーは、C# の名前の末尾の `Radians` / `Degrees` を外したもの。数値は turns なので、C# の単位の語は JSON では誤り。C# の名前は変えない（ユーザーの判断）。
  - `FovYRadians` → `FovY`、`SunDiskAngleDegrees` → `SunDiskAngle`、`WalkableFloorAngleDegrees` → `WalkableFloorAngle`、`RotationRateDegrees` → `RotationRate`、`PlayerController` の `ControlYaw` / `ControlPitch` / `MinPitch` / `MaxPitch`、`SpotConeAngles` の `InnerFullAngle` / `OuterFullAngle`。`EulerRuf` は単位の語を含まないので変わらない。
- 外した名前が空になるとき、または同じ型の別のメンバーか public コンストラクタの引数がその名前を使っているときは、C# の名前のまま（例: `Pitch` と `PitchDegrees` を持つ型）。
- 規則は 1 か所（`SceneJsonAngles.KeyFor`）。生成器・変換器・スキーマ・Inspector は Roslyn のシンボルから（`SceneJsonSceneAnalyzer.KeyOf`）、プレビューホストはリフレクションから同じ規則を使う。
- 読み込みは別名と C# の名前のどちらも受け付ける（版を問わない）。同じメンバーを両方で書くと SCENE204 のエラー。書き出しは常に別名。
- 版を 3 に上げた理由: 別名を知らない古い生成器（SDK 0.1.9 以前）が新しい文書を読んだとき、「メンバーが無い」ではなく「版が対応外」と報告させるため。

## 結果
- JSON の角度の単位が 1 つになった。90 度は `0.25`、180 度は `0.5`。
- JSON のキーが単位を名乗らなくなった（決定 5）。C# の名前と JSON のキーが角度だけ違うので、C# から JSON を探すときは単位の語を外して探す。

## 却下した案
- float だけで変換する: 上の理由で、届かない値がある。最初の実装はこれで、テンプレートの 2 ファイルの移行が「値が変わる」として止まった。
- 単位をスキーマの説明に書くだけにする（ADR-0072 決定 5 の代案）: JSON の単位が混在したまま残る。
