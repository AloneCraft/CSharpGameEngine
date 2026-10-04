# ADR-0009: FBXインポート対応(AssimpNet)

## ステータス
承認済み

## 背景

これまで`CSharpGameEngine.Import.Gltf`によりglTF(.gltf/.glb)のインポートに対応してきたが、
Fabなどのアセットストアで配布される3Dモデルの多くはFBX形式でも提供される。FBXインポート機能を
追加するにあたり、既存プロジェクトの方針(ADR-0004時点)は「依存関係ゼロの純粋C#ライブラリを
使う」だったが、これがFBXでは成立しないことが調査の結果判明した。

**調査したFBXライブラリとその結果(実際に検証済み)**:
- `FbxParser`(NuGet、依存ゼロ): FBXのバイナリ/ASCIIノードツリーを生の
  json/xml的構造として読めるだけで、メッシュ/頂点/インデックスを組み立てる高レベルAPIが
  存在しない。自前でFBXのシーングラフ・メッシュレイアウトを解釈する実装が別途必要になり、
  スコープが大きくなりすぎる。
- `FbxSharp`(純粋C#): バイナリFBXを読めず、ASCII FBXのみサポート。さらに動作確認済みなのは
  FBXフォーマットバージョン2013.3のみで、Fabなど現代のツールが書き出す新しいバイナリFBXでは
  動作しない可能性が高い。
- `Aspose.3D`: 商用ライセンスのため除外。
- `AssimpNet`(NuGet 4.1.0): ネイティブ`assimp.dll`(BSD-3-Clause)をラップするマネージドAPI。
  `.NETFramework3.5`/`.NETFramework4.0`/`.NETStandard1.3`をターゲットにしており、net10.0からも
  net472からも参照可能。バイナリ/ASCII双方のFBX、多数のバージョンに対応。

glTF向けの純粋C#実装(`SharpGLTF.Core`)に相当する「依存ゼロで実用に足るFBXパーサー」は存在
しないため、**この機能に限りネイティブ依存(AssimpNet)を許容する**という、ADR-0004からの
意図的な逸脱を行う。

## 決定事項

### 1. `src/CSharpGameEngine.Import.Fbx`を新規作成(net10.0、`CSharpGameEngine.Import.Gltf`と同じ設計方針)
`FbxImporter.Import(path)`はファイルを開いて`AssimpContext.ImportFileFromStream`(拡張子を形式のヒント)を
`Triangulate | GenerateSmoothNormals | JoinIdenticalVertices`付きで呼び出し、最初のメッシュのposition/normal/uv/インデックス/
diffuse色を抽出して、`Import.Gltf`と同形の`ImportedMesh`/`FbxImportResult`/
`FbxImportDiagnostic`(独立した型、共有はしない)に詰めて返す。Assimpのネイティブログ出力
(Error/Warning行)は`GltfImporter`と同じ契約で診断リストに集約し、コンソールへ直接書かない。

スコープは意図的に最小限: 最初のメッシュのみ、シーングラフなし、マテリアルは拡散色のみ
(テクスチャ抽出は今回の対象外、`BaseColorTextureBytes`は常に`null`)。

(2026-10-04追記)ファイル名ではなく内容を渡す。AssimpNet 4.1.0 はファイル名を ANSI 文字列(LPStr)で
ネイティブへ渡すので、システムのコードページに無い文字を含むパス(英語版 Windows の日本語フォルダーなど)は
"?" に化けて開けなかった。FBX 単体だけを読む(外部テクスチャを使わない)ので、ストリームで足りる。
テクスチャ抽出を足すときは、外部ファイルの解決に名前を使わない方法(自前の IOSystem など)が要る。

### 2. `Extension.csproj`に`RuntimeIdentifier`を追加(AssimpNetのネイティブDLL配置のため)
NuGetは、消費側プロジェクトに明示的な`RuntimeIdentifier`が設定されている場合のみ、
`runtimes/{win-x64,...}/native/`配下のRID固有ネイティブアセット(`assimp.dll`)をビルド出力に
コピーする。`RuntimeIdentifier`なしの`dotnet build`はRID非依存の出力になり、`assimp.dll`が
配置されずクラス実行時に`DllNotFoundException`になる(実際に確認済み)。これを回避するため、
`tools/CSharpGameEngine.Extension.csproj`と`tests/CSharpGameEngine.Import.Fbx.Tests.csproj`に
`<RuntimeIdentifier>win-x64</RuntimeIdentifier>` + `<SelfContained>false</SelfContained>`を
追加した。副作用として、ビルド出力パスに`win-x64`サブフォルダが追加される
(例: `bin/Debug/net10.0-windows10.0.19041.0/win-x64/`)。

### 3. CLIビューア(`CSharpGameEngine.Extension`)に拡張子ベースの分岐を追加
`Program.cs`で、`--model`のパスの拡張子が`.fbx`なら`FbxImporter.Import`、それ以外
(`.gltf`/`.glb`)なら既存の`GltfImporter.Import`を呼ぶよう分岐した。以降の
`ModelRenderer.Create`呼び出しは共通のローカル変数(positions/normals/uvs/indices/
baseColorTextureBytes/baseColorFactor/bounds)を使うよう統一し、インポーターの違いを吸収した。

### 4. テストフィクスチャ: `assets/TestFixtures/Fbx/Box.fbx`
Assimpリポジトリの`test/models/FBX/box.fbx`を最初に試したが、AssimpNet 4.1.0が同梱する
ネイティブAssimpバージョンでは`FBX-Parser did not find required element "ColorIndex"`という
エラーで空シーンになりインポートに失敗することが判明した(頂点カラーレイヤーの
`ColorIndex`要素をそのバージョンのFBXパーサーが認識できない)。代わりに同リポジトリの
`test/models/FBX/phong_cube.fbx`(問題なくインポート・描画できることを確認済み)を
`Box.fbx`として採用した。ライセンス(BSD-3-Clause)と差し替え理由は
`assets/TestFixtures/Fbx/NOTICE.md`に記録している。

## 検証結果(実際に実行して確認)

- `dotnet test tests/CSharpGameEngine.Import.Fbx.Tests`: 7件全て成功
  (正常インポート、バウンディングボックス、ファイル不在、null/空文字列、不正ファイル)。
- `dotnet run --project tools/CSharpGameEngine.Extension -- --model Samples/Box.fbx --hidden
  --debug-layer --frames 3 --verify`をハードウェアGPU(NVIDIA GeForce RTX 4060 Ti)と
  WARPソフトウェアアダプタの両方で実行し、双方で
  `Mesh: 24 vertices, 12 triangles`・`Verification: PASSED`・
  `D3D12 validation errors: 0`を確認した。
- 既存のglTFフロー(`Box.glb`/`BoxTextured.glb`)が`RuntimeIdentifier`追加後も引き続き
  `PASSED`することを確認し、回帰がないことを確認した。

## 未解決・保留事項
- マテリアルのテクスチャ抽出(FBX埋め込みテクスチャ/外部テクスチャ参照)は未対応。
- シーングラフ・複数メッシュ・スキニング/アニメーションは対象外。
- VSIX側(`CSharpGameEngine.VSExtension`)でのFBX対応は、ネイティブ`assimp.dll`を
  `devenv.exe`に直接ロードするリスクがあるため、本ADRの対象外として ADR-0010 に分離した。

## ADR-0034による追記(AssimpNetの到達範囲)

[[ADR-0034-Project-Consolidation]]が`src/CSharpGameEngine.Import.Fbx`と
`src/CSharpGameEngine.Import.Gltf`を単一の`src/CSharpGameEngine.Import`へ統合した。
**名前空間`CSharpGameEngine.Import.Fbx`/`.Import.Gltf`は無変更**で、型もAPIも本ADRの決定事項も
そのままである。本ADRのNuGetライブラリ選定根拠(純C#のFBXライブラリが存在しない件)は
新しいcsprojに**逐語でコメントとして保持**した。

副作用として、**glTFしか使わない消費者もマネージドの`AssimpNet.dll`を解決するようになった**。
ただし本ADRが問題にしていた**ネイティブの`assimp.dll`は入らない**: これはRID固有アセットであり、
NuGetは`RuntimeIdentifier`を設定した消費者にしかコピーしないためである。実測:

| 出力 | `AssimpNet.dll`(マネージド) | `assimp.dll`(ネイティブ) |
|---|---|---|
| `tests/CSharpGameEngine.Import.Tests` (RID=win-x64) | あり | あり |
| `tools/CSharpGameEngine.Import.Gltf.Cli` (RID無し) | あり | **なし** |
| `tests/CSharpGameEngine.Windows.Tests` (RID無し) | あり | **なし** |

`tools/CSharpGameEngine.Import.Gltf.Cli`は統合後も意図的に`RuntimeIdentifier`を設定していない。
本ADR決定事項2の「`RuntimeIdentifier`を足すのはネイティブDLLが必要な消費者だけ」という方針は
維持されている。テストプロジェクトは`Import.Fbx.Tests`と`Import.Gltf.Tests`が
`CSharpGameEngine.Import.Tests`へ統合され、`RuntimeIdentifier=win-x64`は和集合として引き継いだ。
