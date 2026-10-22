# ThirdPerson(理想コード・提案)

UE5.8 の Third Person テンプレートと同じ構成のサンプル。既定のサンプルプロジェクトになる予定。
**このフォルダはまだビルドできない。** ユーザーが「こう書けたらいい」と思えるコードを先に書いたもので、確認と書き直しの後に、ランタイムと生成器をこの形に合わせて作り替える(仕組みの一覧は [`../README.md`](../README.md))。

## 構成

| 場所 | 役割 |
|---|---|
| `Scenes/Game.json` | 起動シーン。ウィンドウ、ステージ、プレイヤーを並べるだけ |
| `Scenes/Lvl_ThirdPerson.json` | UE の `Lvl_ThirdPerson` のステージ(環境・床・ブロック。ここでは抜粋) |
| `Scenes/Character.json` | 体(CharacterMotion など)、メッシュ(骨格・アニメーション)、カメラ |
| `Code/PlayerActions.cs` | 操作の宣言(移動・視点・ジャンプ) |
| `Code/PlayerControls.cs` | 操作を「視点・移動・ジャンプ」へ翻訳する(3行) |
| `Code/CharacterAnimation.cs` | 体の動きをアニメーションへ渡す |
| `Code/CharacterSounds.cs` | ジャンプ・着地・足音のイベントに効果音を結ぶ |
| `Code/Respawn.cs` | ユーザー定義 Component と `Start` の例(落ちたら戻る) |

## 読み方

1. `Game.json` から読み始める。`Instance` が別のシーンの配置。
2. `PlayerActions` → `PlayerControls` → `Character.json` の順に、「入力 → やりたいこと → 物理」の流れを追う。
3. 音とアニメーションのファイルは `Assets/` に置く。`Assets.Sounds.Jump` は `Assets/Sounds/Jump.wav` から生成される。

## 取り込みの出典

モデル・アニメーションは UE5.8 ThirdPerson テンプレートから取り込み済み(`Templates/CSharpGameEngine.Templates.ThirdPerson`)。
効果音(Jump / Land / Footstep)の取り込み元は未確定(UE のテンプレートには音声が無い)。
