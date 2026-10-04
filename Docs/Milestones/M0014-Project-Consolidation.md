# M0014: src/ のプロジェクト統合(16 → 5)

## 目的

`src/`配下の16プロジェクトを、実際のビルド形状の違いに一致する5プロジェクトへ畳む。
利用者のいないプロジェクトと、通常ビルドで一度も実行されていなかった機構を削除する。
設計判断は[[ADR-0034-Project-Consolidation]]に記録した。

**名前空間は一切変更しない**という判断が本マイルストーンの中核であり、その結果として
統合作業を通じて`using`の編集は1行も発生していない。

## 実装したこと

作業は4つのチェックポイントに分け、各段階でDebug/Releaseの両ビルドと全テストを回し、
赤なら先へ進まない方針で実施した。

### CP-A: 削除のみ

削除に先立ち、`Scene.Tests`の`DefaultSceneFileTests`を`VSExtension.Tests`へ移植した
(**出荷される`UserGameProject/DefaultScene.scene`を実際に読む唯一のテスト**だったため。
VSIX同梱の`SceneFile.Load`と既存の`FindRepoRoot()`を使い約20行)。その上で削除:

- `src/CSharpGameEngine.Scene/` + `tests/CSharpGameEngine.Scene.Tests/`
- `src/CSharpGameEngine.Spatial.Abstractions/SpatialPresetSelection.props`
- `samples/SpatialPresetDemo/`および`samples/`ディレクトリごと
- `src/CSharpGameEngine.StaticArchetype/`(`obj/`のみの残骸)
- `UserGameProject.slnx`(M0009で消えた`CSharpGameEngine.Ecs`等を参照しており読み込み不能だった)

→ **368件 / 13プロジェクト**。`VSExtension.Tests`が39→40。

### CP-B: `CSharpGameEngine.Core`統合

被参照数が最大なので最初に安定させた。ディレクトリは`src/CSharpGameEngine.Core/`を**再利用**。

- 22ファイルを`git mv`: `Spatial/`(15) + `Spatial/Conventions/`(5) + `Geometry/`(2)。既存5ファイルは根に据置。
- 7つのcsprojを削除(`Spatial.Abstractions`/`.Unity`/`.Unreal`/`.Blender`/`.Gltf`/`.Fbx`/`Geometry`)。
- 消費者6箇所を張り替え: `Graphics.D3D12`, `Import.Gltf`, `Import.Fbx`, `Primitive.Cli`, `UserGameProject`, `.slnx`。
- `Graphics.D3D12.Tests`の`Geometry`へのテスト専用辺を削除(`ProceduralMeshFactory`が`Core`内に来たため)。
- `Spatial.Tests` + `Geometry.Tests`を`Core.Tests`へ吸収。`RufVector.RawCross`の`InternalsVisibleTo`を
  `CSharpGameEngine.Core.Tests`へ張り替え。
- csprojに「**PackageReferenceを一切持たない**のがこのアセンブリの境界の理由」というコメントを明記。

→ **368件 / 11プロジェクト**、`Core.Tests` = **111件**(13 + 30 + 68)。**`using`の編集は0**。

### CP-C: `CSharpGameEngine.Import`統合

- 19ファイルを`git mv`: `Gltf/`(13) + `Fbx/`(6)。
- `Gltf/BoundingBox.cs`と`Fbx/BoundingBox.cs`、および両者の`ImportedMesh.cs`は**ファイル名も型名も同一**
  だが名前空間が異なるため1アセンブリ内に共存できる(名前空間維持判断の直接的な帰結)。
- csprojは`SharpGLTF.Core` 1.0.6 と `AssimpNet` 4.1.0 の和集合。
  [[ADR-0009-Fbx-Model-Import]]の「純C#のFBXライブラリが存在しない」根拠コメントは**逐語で保持**。
- `InternalsVisibleTo`を`CSharpGameEngine.Import.Tests`へ張り替え
  (`FbxAxisConversion`は`internal`で`FbxAxisConversionTests`が直接呼ぶ)。
- 消費者6箇所を張り替え: `Extension`, `Import.Gltf.Cli`, `Import.Fbx.Cli`, `Graphics.D3D12.Tests`,
  `UserGameProject`, `.slnx`。
- テストを`tests/CSharpGameEngine.Import.Tests/{Gltf,Fbx}/`へ集約。`RuntimeIdentifier=win-x64`(assimpの
  ネイティブDLL用)・`SharpGLTF.Toolkit`・フィクスチャ6件を和集合にした。

→ **368件 / 10プロジェクト**、`Import.Tests` = **30件**(20 + 10)。

### CP-D: `CSharpGameEngine.Windows`統合

- 24ファイルを`git mv`: `Graphics/`(17) + `Platform/`(2) + `Hlsl/`(5)。
- 3プロジェクトのPropertyGroupはもともと完全に同一で、`TerraFX.Interop.Windows`も共通、
  かつ`Graphics.D3D12`は`Compiler.Hlsl`を参照していたため、分割の利得は無かった。
- **DXCネイティブコピー**(`$(PkgMicrosoft_Direct3D_DXC)`から`dxcompiler.dll`/`dxil.dll`、
  [[ADR-0003-Windows-D3D12-Vertical-Slice]])の`<None>`ブロックを**逐語で移設**。
  これはユニットテストでは検出できず実行時にのみ壊れる箇所。
- `Windows.Tests`では`Triangle.hlsl`/`Model.hlsl`が旧2テストプロジェクトから**同一Linkで二重宣言**
  されるため、各1回のみに集約した(重複は警告 → `TreatWarningsAsErrors`でビルド失敗になる)。
- 消費者を張り替え: `TestHost`(3→2), `Extension`, `UserGameProject`, `.slnx`。

→ **368件 / 9プロジェクト**、`Windows.Tests` = **67件**(60 + 7)。

### ファイル移動はすべて`git mv`

ADRがファイルパスを引用しているため、`git log --follow`が切れないことを優先した。
移動した65ファイルすべてがgitのrename検出(`R`)に乗っていることを確認済み。

## 検証結果(実際に実行して確認)

```
dotnet build CSharpGameEngine.slnx -c Debug     → 成功、警告0/エラー0
dotnet build CSharpGameEngine.slnx -c Release   → 成功、警告0/エラー0
dotnet test  CSharpGameEngine.slnx              → 368件 / 9プロジェクト、失敗0
```

テスト内訳(合計368件、失敗0):
Core 111、Windows 67、CodeAnalysis 56、VSExtension 40、Import 30、Entities 22、
UserGameProject 21、SceneAuthoring.Cli 13、Bridge.Hosting 8。

`Import.Tests`の30件は、移動したソース側の`[Fact]`/`[Theory]`+`[InlineData]`の数
(Gltf 3+7+(7+3)=20、Fbx 3+(4+3)=10)と一致することを別途検算した。

### テストでは検出できない検証

ネイティブ資産の実配置:

| 出力 | `dxcompiler.dll` | `dxil.dll` | `assimp.dll` |
|---|---|---|---|
| `TestHost` | あり | あり | (不要) |
| `Extension/win-x64` | あり | あり | あり |
| `UserGameProject/win-x64` | あり | あり | あり |

実行スモーク(いずれもapphostの`.exe`経由):

```
TestHost self-test                                    → SUCCESS
TestHost triangle --hidden --frames 3 --warp
         --debug-layer --verify                       → PASSED / D3D12検証エラー0
Extension --model Samples/Box.glb --hidden --frames 3
          --warp --debug-layer --verify               → PASSED / D3D12検証エラー0
Import.Gltf.Cli  assets/TestFixtures/Gltf/Box.glb ... → exit=0 / Success:true
Import.Fbx.Cli   assets/TestFixtures/Fbx/Box.fbx  ... → exit=0 / Success:true(ネイティブassimp.dllのロード成功)
```

`TestHost self-test`が通ることは`Core`の吸収が効いている証明、`triangle --verify`が通ることは
DXCネイティブコピーの移設が効いている証明になっている。

### 退行ではないことを確認した事象

`dotnet <app>.dll`形式(muxer経由)で`TestHost`を起動すると`dxcompiler`のロードに失敗する。
統合着手前のコミット`f17fbd5`を別ワークツリー(`git worktree add --detach`)へ展開し、
同一コマンドを実行して**まったく同じ失敗が出ることを確認**した。本統合による退行ではなく、
muxer経由ではアプリディレクトリがネイティブDLL探索パスに入らないという起動方法の問題である。
apphostの`.exe`経由なら成功する。

## 最終形

**src 5 / tests 9 / tools 8 / UserGameProject 1 = 23プロジェクト**(統合前は40)。
ソリューションファイルは`CSharpGameEngine.slnx`ただ1つ、`samples/`は無し。

```
src/CSharpGameEngine.CodeAnalysis   netstandard2.0            Roslyn Analyzer
src/CSharpGameEngine.Entities       net10.0                   共有ソース(partial struct World)
src/CSharpGameEngine.Core           net10.0                   外部パッケージ依存ゼロ
src/CSharpGameEngine.Import         net10.0                   SharpGLTF / AssimpNet
src/CSharpGameEngine.Windows        net10.0-windows / x64     TerraFX / DXC
```

依存辺は3本のみ: `Entities → CodeAnalysis`(Analyzer)、`Import → Core`、`Windows → Core`。

## 意図的に除外したこと

- **名前空間のフラット化**。実測で116ファイルの改修が必要になり、`BoundingBox`/`ImportedMesh`の
  型名衝突も生む。得られるものは「アセンブリ名と名前空間の一致」という見た目の整合性だけだった。
- **`CodeAnalysis`と`Entities`の統合**。いずれも技術的制約で不可能
  ([[ADR-0034-Project-Consolidation]]決定事項1)。
- **リファクタ自体への新しいFeature IDの付与**。ADR-0029/0033と同様、序文と状態遷移だけで記録する。

## 残課題

- [[ADR-0027-Gltf-Fbx-Import-Axis-Conversion]]の「`Import.Fbx`が`Import.Gltf`に依存しない」保証が
  アセンブリ境界による強制から**名前空間規約**へ格下げになった。レビューで守る対象になる。
- `CSharpGameEngine.Core`がL1とL2を跨ぐため、**L1とL2の境界はアセンブリでは強制されなくなった**
  ([[ADR-0001-Layered-Architecture]]、レイヤーはFeature ID単位で追跡する)。
- `UserGameProject`の実機描画は自動検証できていない(本統合は名前空間もロジックも変えていないため
  原理的に影響は無く、`--verify`付きスモークが代替検証になっている)。
- (2026-10-04 解消)`src/CSharpGameEngine.Entities/Generated/`は`.gitignore`に列挙されているのに
  git追跡下にあった。生成器が6つ書くうち3つだけが追跡され、改行をCRLFでチェックアウトする新しいcloneでは
  ビルドがLFで書き直すたびに`git status`が変更ありと表示した。追跡をやめ、ディスク上のデバッグ用出力に戻した。
