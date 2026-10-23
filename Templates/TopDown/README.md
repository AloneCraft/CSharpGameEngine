# TopDown(理想コード・提案)

UE5.8 の Top Down テンプレート相当。**まだビルドできない**(仕組みは [`../README.md`](../README.md))。

- `Code/ClickToMove.cs`: クリック(またはタッチ)した地面へ歩く。UE のとおり、押している間はカーソルの下の地面へ直接歩き、押していた時間が 0.5 秒未満で離したとき(短押し)に、その地面へナビメッシュの経路で歩いて目印を出す(M0108)。長押しは離すと止まるだけ。
  - 実行時スポーン: `Spawner.Spawn<CursorMarker>(Hit.Point)`(M0105 で実装した形。`CursorMarker` は `Scenes/CursorMarker.json` から生成される型で、位置を渡せる。`Scenes.CursorMarker` と値で書く提案は、Scene のクラスの名前空間が `Scenes` と重なるため採らなかった)。
  - 目印は `Scenes/CursorMarker.json` の根の `Lifetime`(秒)で自然に消える。消すコードは書かない。
- カメラは `OrbitCamera` の `FollowYaw: false`(向きを固定し、距離と角度だけで追従)。

論点:
- UE はナビメッシュで経路を探す。理想コードは `Intent.MoveTo(Point)` と書くだけにしてあり、経路探索は M0106 の Navigation プラグインが担う(キャラクターに `NavAgent`、シーンに `NavMeshBounds` を置くだけで、`MoveTo` が経路に沿って歩く。コードは変わらない)。
- `Physics` / `Spawner` は、引数の型で受け取るサービス。
