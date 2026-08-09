# ADR-0034: src/ のプロジェクト統合(16 → 5)とアセンブリ境界の再定義

## ステータス

承認済み。[[ADR-0025-RUF-Semantic-Coordinates]]のSpatialPreset機構と6分割構成、および
[[ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth]]で役目を終えていた`CSharpGameEngine.Scene`を
廃止し、アセンブリ境界を「実際のビルド形状の違い」に一致させ直した。

## 背景

ユーザーから「`src`の下に大量のプロジェクトがある。SGとECSランタイムだけでいいと思うのだが
なぜここまで多いのか。統合化案を考えて」という指摘があった。実態を計測した結果、指摘は正しかった:

- **16プロジェクト・11,128行**。うち**6プロジェクトはソースファイル1個以下**、
  **5プロジェクトは35行未満**。
- **Spatial系5プロジェクトは合計132行**(1つあたり平均26行、大半がXMLドキュメントコメント)。
  `Spatial.Fbx`と`Spatial.Gltf`は数学的に同一で、ファイル内コメントに
  「`Import.Fbx`が`Import.Gltf`を参照しなくて済むように分けただけ」と明記されていた。
- **`Scene`(252行)は生きた利用者ゼロ**。参照は自分のテストのみ。
  [[ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth]]/[[ADR-0031-Scene-Definition-3D-Preview]]で
  Scene Authoringが`Entities`へ移った際、JSON方式の旧実装が残置されていた。
- **`Spatial.Unreal`/`Spatial.Blender`は本番利用ゼロ**。分割の唯一の根拠だった
  `SpatialPresetSelection.props`(コンパイル時に1つだけConventionを参照する機構)の利用者は
  `samples/SpatialPresetDemo`のみで、しかも`.slnx`未登録のため**通常ビルドで一度も実行されて
  いなかった**。エンジン本体は[[ADR-0026-Camera-Geometry-Ruf-Integration]]で
  `UnityCoordinateConvention`に固定済みだった。
- `Graphics.D3D12`・`Platform.Windows`・`Compiler.Hlsl`の3つは**PropertyGroupが完全に同一**で
  (`net10.0-windows10.0.19041.0` / `x64` / `AllowUnsafeBlocks` / `TerraFX.Interop.Windows`)、
  かつ`Graphics.D3D12`は`Compiler.Hlsl`を参照していた。

つまり**アセンブリ境界がフォルダ名の代用として使われている**状態だった。

## 決定事項

### 1. 境界の理屈は「分類学」ではなく「ビルド形状」

統合後の5プロジェクトは、分野で切ったのではなく**ビルドの実際の違い**で切ってある:

| # | プロジェクト | 吸収したもの | TFM | 境界の理由 |
|---|---|---|---|---|
| 1 | `CSharpGameEngine.CodeAnalysis` | (無変更) | netstandard2.0 | Roslyn Analyzer。`IsRoslynComponent`で`analyzers/dotnet/cs`としてpackされる |
| 2 | `CSharpGameEngine.Entities` | (無変更) | net10.0 | **共有ソース**。`partial struct World`を消費側コンパイル内でSGが完成させる([[ADR-0033-Static-Archetype-Ecs-Migration]]) |
| 3 | `CSharpGameEngine.Core` | Core + Spatial.Abstractions + Convention 5つ + Geometry | net10.0 | **外部パッケージ依存がゼロ** |
| 4 | `CSharpGameEngine.Import` | Import.Gltf + Import.Fbx | net10.0 | サードパーティのアセットSDK(SharpGLTF / AssimpNet)を抱える |
| 5 | `CSharpGameEngine.Windows` | Graphics.D3D12 + Platform.Windows + Compiler.Hlsl | net10.0-windows10.0.19041.0 / x64 / unsafe | windows TFM・x64・unsafe・TerraFX/DXCのネイティブ依存が要る |

ネイティブ依存を選ばずに使える資産はすべて`Core`に集まる。依存辺は3本だけになった:
`Entities → CodeAnalysis`(Analyzer参照)、`Import → Core`、`Windows → Core`。

上の1と2が統合できないのは技術的制約であって選好ではない:

- `CodeAnalysis`はnetstandard2.0でなければRoslynにロードされない。
- `Entities`の`World`/`State`は`partial struct`で、**C#のpartial型は同一コンパイル内でしか
  合成できない**。コンパイル済みDLLを`<ProjectReference>`する方式は原理的に成立しない
  ([[ADR-0033-Static-Archetype-Ecs-Migration]]決定事項1)。

`Core`はディレクトリを**再利用**した(`src/CSharpGameEngine.Core/`のまま)。README/
[[ADR-0003-Windows-D3D12-Vertical-Slice]]の「Coreはwindows/D3D/DXCに一切依存しない」という
記述はそのまま真であり続ける。

### 2. 名前空間は一切変更しない(本ADRで最も重要な判断)

**アセンブリだけ統合し、名前空間は現状維持**した。`CSharpGameEngine.Core.dll`が
`CSharpGameEngine.Core` / `.Spatial.Abstractions` / `.Spatial.Unity` / … / `.Geometry`を含み、
`CSharpGameEngine.Windows.dll`が`.Graphics.D3D12` / `.Platform.Windows` / `.Compiler.Hlsl`を含む。
.NETではアセンブリ名≠名前空間は普通のことで、実害はない。

根拠は**実測した改修量**である:

| 名前空間をフラット化した場合の改修 | ファイル数 |
|---|---|
| 統合対象src内の`namespace`宣言 | 70 |
| 統合対象テスト内の`namespace`宣言 | 32 |
| 統合対象外の`using`(tools / UserGameProject等) | 14 |
| **合計** | **116** |
| 完全修飾名を含むMarkdown | 9 |

名前空間を維持すればこれらは**すべて0**になる。実際、CP-A〜CP-Dを通して
**`using`の編集は1行も発生しなかった**。

さらに`Import.Gltf`と`Import.Fbx`は`BoundingBox`と`ImportedMesh`を**両方が宣言していた**
(型名重複は全統合対象を走査してこの2つだけと確認済み)。名前空間を維持すれば
`CSharpGameEngine.Import.Gltf.BoundingBox`と`CSharpGameEngine.Import.Fbx.BoundingBox`は
別型として1アセンブリ内に共存でき、フラット化するとリネームが必要になっていた。
[[ADR-0025-RUF-Semantic-Coordinates]]のGolden Values表(完全修飾型名)が無改修で済むのも同じ理由。

### 3. ファイル配置規則

配置は「名前空間の末尾がアセンブリ名と一致するものは根、それ以外は名前空間名のサブフォルダ」:

```
src/CSharpGameEngine.Core/          {既存5ファイル} + Spatial/ + Spatial/Conventions/ + Geometry/
src/CSharpGameEngine.Import/        Gltf/ + Fbx/
src/CSharpGameEngine.Windows/       Graphics/ + Platform/ + Hlsl/
```

移動はすべて`git mv`で行った。ADRがファイルパスを引用しているため、`git log --follow`が
切れないことを優先した(65ファイルすべてがgitのrename検出に乗っていることを確認済み)。

### 4. SpatialPreset機構の廃止

`SpatialPresetSelection.props`と`samples/SpatialPresetDemo`を削除した。実測値は
[[ADR-0025-RUF-Semantic-Coordinates]]の検証結果へ転記済みなので証跡は失われない。
`Spatial.Unity`/`.Unreal`/`.Blender`/`.Gltf`/`.Fbx`のConvention型そのものは
`CSharpGameEngine.Core`の中に名前空間ごと残っており、実行時に選択して使うことは引き続きできる。
失われたのは「コンパイル時に1つだけリンクする」機構だけである。

### 5. 削除したもの

| 削除物 | 理由 |
|---|---|
| `src/CSharpGameEngine.Scene` + そのテスト | 生きた利用者ゼロ(参照は自分のテストのみ) |
| `SpatialPresetSelection.props` | 決定事項4 |
| `samples/`ディレクトリごと | `SpatialPresetDemo`は上記の実演専用、他はM0013で削除済み |
| `src/CSharpGameEngine.StaticArchetype/` | `obj/`のみの残骸(git未追跡) |
| `UserGameProject.slnx` | M0009で消えた`CSharpGameEngine.Ecs`等を参照しており**読み込み不能**だった |

`Scene.Tests`8件のうち7件は削除対象`Scene`のテストなので損失はない。残る
`DefaultSceneFileTests`だけは**出荷される`UserGameProject/DefaultScene.scene`を実際に読む
唯一のテスト**だったため、削除に先立って`VSExtension.Tests`へ移植した(VSIX同梱の
`SceneFile.Load`と既存の`FindRepoRoot()`を使用)。

## 帰結(正直に記録すべき副作用)

### (a) [[ADR-0027-Gltf-Fbx-Import-Axis-Conversion]]の分離保証が格下げになった

「`Import.Fbx`が`Import.Gltf`に依存しない」という保証は、これまで**アセンブリ境界**によって
機械的に強制されていた。統合後は同一アセンブリ内の**名前空間規約**にすぎず、
`Fbx/`から`Gltf/`の型を参照してもコンパイルは通る。レビューで守る対象になった。

### (b) `Import.Gltf.Cli`の出力に`AssimpNet.dll`が入るようになった

`Gltf`と`Fbx`が1アセンブリになったため、glTFしか使わない消費者も**マネージドの**
`AssimpNet.dll`を解決する。ただし**ネイティブの`assimp.dll`は入らない**:
これはRID固有アセットで、`RuntimeIdentifier`を設定した消費者にしかコピーされないため。
`tools/CSharpGameEngine.Import.Gltf.Cli`は意図的に`RuntimeIdentifier`を設定しないままにしてある。
実際の出力ディレクトリで次を確認済み:

| 出力 | `AssimpNet.dll`(マネージド) | `assimp.dll`(ネイティブ) |
|---|---|---|
| `Import.Tests` (RID=win-x64) | あり | あり |
| `Import.Gltf.Cli` (RID無し) | あり | **なし** |
| `Windows.Tests` (RID無し) | あり | **なし** |

[[ADR-0010-VSIX-Fbx-OutOfProcess-Bridge]]/[[ADR-0019-VSIX-Gltf-OutOfProcess-Bridge]]の
**VSIX側の隔離は不変**である。VSIX(net472, devenv.exe内)はAssimpNetを直接参照せず、
これらのCLIを短命な別プロセスとして起動するブリッジ方式のままだからである。

### (c) L1とL2をアセンブリで分離できなくなった

[[ADR-0001-Layered-Architecture]]のレイヤーモデルとの関係:

- `CSharpGameEngine.Windows`は3つともL0なので**同一レイヤー内統合**であり、影響なし。
- `Import → Core`・`Windows → Core`はいずれも**上位が下位に依存**する正しい向きで、
  逆流する辺は1本も生まれていない。
- ただし`Core`はL1(診断 / RUF)とL2(`CameraBasis` / `ProceduralMeshFactory`)を跨ぐため、
  **L1とL2の境界はアセンブリでは強制されなくなった**。レイヤーはFeature ID単位で追跡する。

### (d) `Compiler.Hlsl`のDXCネイティブコピーの所在が変わった

`dxcompiler.dll` / `dxil.dll`を`$(PkgMicrosoft_Direct3D_DXC)`からコピーする`<None>`ブロック
([[ADR-0003-Windows-D3D12-Vertical-Slice]])は`CSharpGameEngine.Windows.csproj`へ**逐語で**移した。
これが消えると**実行時にのみ**壊れ、ユニットテストでは検出できないため、実バイナリの配置と
実行スモークで確認した(下記「検証」)。

## 検証

各段階(CP-A: 削除 / CP-B: Core / CP-C: Import / CP-D: Windows)でDebug・Releaseの両ビルドと
全テストを回し、赤なら先へ進まない方針で実施した。

| | テスト総数 | テストプロジェクト | 注記 |
|---|---|---|---|
| 統合前 | 375 | 14 | |
| CP-A(削除のみ) | 368 | 13 | `Scene.Tests`8件のうち7件が消え、`DefaultSceneFileTests`が`VSExtension.Tests`へ(39→40) |
| CP-B(Core) | 368 | 11 | `Core.Tests` = 111 (13 + 30 + 68) |
| CP-C(Import) | 368 | 10 | `Import.Tests` = 30 (20 + 10) |
| CP-D(Windows) | 368 | 9 | `Windows.Tests` = 67 (60 + 7) |

- Debug / Release とも **0警告 0エラー**(`TreatWarningsAsErrors=true`)。
- CP-Cで本計画最大のリスクとしていた「まだ統合前の`Graphics.D3D12.Tests`
  (windows TFM・RID無し)がAssimpNetを初めて推移的に引き込む」件は、**`NU*`警告ゼロ**だった。
- テストでは検出できないネイティブ資産の実配置を確認:
  `TestHost`に`dxcompiler.dll`/`dxil.dll`、`Extension`と`UserGameProject`の`win-x64`に
  `dxcompiler.dll`/`dxil.dll`/`assimp.dll`。
- 実行スモーク(いずれもapphostの`.exe`経由):
  - `TestHost self-test` → SUCCESS(`Core`の吸収が効いている証明)
  - `TestHost triangle --hidden --frames 3 --warp --debug-layer --verify` → **PASSED / D3D12検証エラー0**
  - `Extension --model Samples/Box.glb ... --verify` → **PASSED / D3D12検証エラー0**
  - 両CLIブリッジの実変換 → `exit=0` / `Success:true`(FBX側はネイティブ`assimp.dll`のロードにも成功)

`dotnet <app>.dll`形式(muxer経由)で起動すると`dxcompiler`のロードに失敗するが、これは統合着手前の
コミット`f17fbd5`を別ワークツリーに展開して同一コマンドを実行し、**まったく同じ失敗が出ることを
確認済み**である。本統合による退行ではなく、muxer経由ではアプリディレクトリがネイティブDLL探索
パスに入らないという起動方法の問題。apphostの`.exe`経由なら成功する。

`UserGameProject`の実機描画(D3D12ウィンドウ)は自動検証できないが、本統合は名前空間を変えず
ロジックにも触れていないため、描画への影響は原理的に無い。上記`--verify`付きスモークが
レンダリング経路の代替検証になっている。

## 最終形

**src 5 / tests 9 / tools 8 / UserGameProject 1 = 23プロジェクト**(統合前は40)。
ソリューションファイルは`CSharpGameEngine.slnx`ただ1つ、`samples/`は無し、**368テスト**。

関連: [[M0014-Project-Consolidation]]
