# FirstPerson(理想コード・提案)

UE5.8 の First Person テンプレート相当。**まだビルドできない**(仕組みは [`../README.md`](../README.md))。

ThirdPerson との差が「コードではなくシーン」に出るのが見どころ。`PlayerActions` と `PlayerControls` は ThirdPerson と同一で、違いは次の2点だけ。

- `Scenes/Character.json`: カメラが体の目の位置(`Eyes`)にあり、視点の上下角は ±0.24 turns(約±86°)に制限される。体は移動方向を向かない(`FaceMovement: false`)。
- `Code/ArmsFollowView.cs`: 親(体)の視点の上下角を、腕(子)へ渡す。`Parent<T>` で親の Component を読む例。

足音は `Audio.PlayLocal`(音源が自分の耳元)で鳴らす。

論点: `PlayerActions` / `PlayerControls` が ThirdPerson と完全に同じなので、エンジンが標準の「歩くキャラクター操作」を提供し、ユーザーは差分だけを書く形にできる。標準を提供するか、サンプルごとに明示するかは確認したい。
