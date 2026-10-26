# 仮想シャドウの左右比較

リポジトリのルートから次を実行する。通常の showcase のシーンは保持され、ビルドオプションで比較用の起動シーンだけを選ぶ。

```powershell
dotnet build UserEcsProject/UserEcsProject.csproj -c Release -p:CgeShadowComparison=true -m:1
dotnet run --project UserEcsProject/UserEcsProject.csproj -c Release --no-build
```

左が通常の 4 段カスケード、右が仮想シャドウ。カメラ、時刻、照明、材質、露出は共通。柵の細い影、斜面、段差、移動する柱、遠くの遮蔽物を見る。

| 操作 | 動作 |
|---|---|
| P | シミュレーションの停止・再生。停止すると更新済みの影ページは再描画されない |
| 矢印 | 両側のカメラを同時に 1 m 移動 |
| R | カメラを初期位置へ戻す |
| 1 / 2 / 3 | 通常単独 / 仮想単独 / 左右比較 |
| S | 両側を同時に角直径 0 度の硬い影 / 2 度のソフトシャドウへ切替 |
| 4 / 5 | 両側共通の太陽の角直径を 0.5 度ずつ縮小 / 拡大 |

左右とも同じ PCSS、32+中心の遮蔽物探索、64 のフィルタサンプル、メートル単位の深度バイアスと双線形の影比較を使う。画面に太陽の角直径と半影半径の上限を表示する。見本は違いを見やすくするため角直径 2 度、半影半径の上限 2 m。通常の設定値の角直径は 0.5357 度。半影は遮蔽物と受光面の光源方向の距離 × tan(角直径/2) で広がる。半径の上限は探索範囲も制限するため、広い半影では制限が現れる。

床の近く (下面まで 0.3 m) と高い位置 (4.9 m) に板を置いている。同じ角直径でも高い板の影ほど柔らかい。4/5 で角直径を変えると、双方の影が同時に変わる。

`DirectionalShadowRendering` の `SoftShadows=true` で有効にし、`SourceAngularDiameterDegrees` と `MaximumSoftShadowRadiusMeters` を指定する。従来シーンは未指定なら従来の PCF を維持する。`SoftShadows=true` かつ角直径 0 では、左右とも共通の双線形ハードシャドウ比較を使う。CSM の低解像度によるぼけは残るが、意図的なフィルタ幅やサンプル数の差は設けない。

ページの requested・resident・rendered・reused・missing requests と GPU 時間を画面で確認できる。仮想モードにも CSM のフォールバック用描画があるので、総シャドウ時間は CSM と virtual pages の合計。missing requests は不足したページ要求数を示し、画素数ではない。

自動撮影は既存の automation を利用する。ビルド後、実行ファイルのフォルダーで起動する。出力先は絶対パスにする。

```powershell
$env:CGE_GRAPHICS_ADAPTER='warp' # 実機測定時は hardware
$env:CGE_D3D12_DEBUG_LAYER='1'
$env:CGE_AUTOMATION_FRAMES='36'
$env:CGE_AUTOMATION_CAPTURE='G:/GitHub/CSharpGameEngine/.artifacts/virtual-shadows/comparison.png'
$env:CGE_AUTOMATION_REPORT='G:/GitHub/CSharpGameEngine/.artifacts/virtual-shadows/comparison.json'
# 静止したキャッシュを確認するときだけ指定する
$env:CGE_AUTOMATION_INPUT='P@8-9'
./UserEcsProject.exe
```

単独モードを測定するときは `CGE_AUTOMATION_INPUT='1@4-5'` または `'2@4-5'`。撮影画像と JSON の `virtualShadows` を一緒に保存する。既定起動へ戻すには `-p:CgeShadowComparison=true` を付けずに再ビルドする。

通常のシーンでは `DirectionalShadowRendering` を環境ノードに追加し、`Mode="Virtual"`、`PageBudget=1024` とする。未指定なら既存のカスケードを使う。比較用の `Compare=true` は太陽・環境光・固定露出の見本用であり、空・GI・AO・自動露出は無効な組み合わせとして拒否する。
