# ADR-0019: VSIXのglTF読み込みもプロセス分離ブリッジに統一する

## ステータス
承認済み

## 背景

ADR-0010でVSIXの`.fbx`読み込みは、AssimpNetのネイティブ`assimp.dll`を`devenv.exe`に直接
ロードするリスクを避けるため、`CSharpGameEngine.Import.Fbx.Cli`という別プロセスのブリッジに
隔離した。一方`.glb`/`.gltf`は`SimpleGltfLoader.cs`が`SharpGLTF.Core`(NuGetパッケージ、
netstandard2.0対応)を`CSharpGameEngine.VSExtension`から直接参照し、`devenv.exe`プロセス内で
そのままパースしていた(ADR-0008)。

同じ「モデルファイルを読み込む」という機能に対して、フォーマットによってプロセス分離の
有無が異なる、という非対称な実装になっていた。ユーザーからの指摘により、この非対称性は
好ましくないと判断し、glTF側もFBXと同じアウトオブプロセス方式に統一することにした
(SharpGLTF.Core自体はAssimpNetと異なりネイティブDLLを持たない純粋マネージドライブラリで
あり、`devenv.exe`をクラッシュさせるリスクは本来低いが、「読み込み方式を形式ごとに分けない」
という一貫性を優先した)。

この統一は、今回追加する新機能(`*.scene.json`のダブルクリックプレビュー・
ヒエラルキー・選択ハイライト、ADR-0020)がシーンノードごとに`.glb`ファイルを読み込む際にも
そのまま使われる基盤になる。

## 決定事項

### 1. 新規ブリッジプロジェクト`tools/CSharpGameEngine.Import.Gltf.Cli`
`tools/CSharpGameEngine.Import.Fbx.Cli`(ADR-0010)と同型の net10.0 コンソールアプリ。
`CSharpGameEngine.Import.Gltf`を`ProjectReference`し、`GltfImporter.Import(inputPath)`
(既存の単一メッシュ・先頭プリミティブのみのAPI、無改修)を呼び、結果を`GltfBridgeDocument`
(positions/normals/uvs/indices/baseColorFactorをフラットな`float[]`/`uint[]`で持つ、
`FbxBridgeDocument`と同じ形のJSON専用DTO)としてシリアライズして指定パスに書き出す。
引数は`<input.glb|input.gltf> <output.json>`の2つのみ、成功時は終了コード0、失敗時は1。

`SharpGLTF.Core`はAssimpNetと異なり完全マネージド(ネイティブDLLなし)なので、
`CSharpGameEngine.Import.Fbx.Cli`が必要とする`RuntimeIdentifier=win-x64`/`SelfContained=false`
は不要 - 素の`net10.0`コンソールアプリのままで`dotnet run`から実行できる。

### 2. VSIX側の2つのローダーが共通のプロセス起動ヘルパーを共有する
`tools/CSharpGameEngine.VSExtension/ExternalFbxLoader.cs`(ADR-0010)が持っていた
「`dotnet run --project ... -- <input> <output.json>`を起動し、標準出力/エラーを
`BeginOutputReadLine`/`BeginErrorReadLine`で非同期に読み、タイムアウト(60秒)で
`Process.Kill()`、非0終了コードで例外」というロジックを`ExternalBridgeProcess.cs`
(新規、internal static)に切り出した。新規`ExternalGltfLoader.cs`は同じヘルパーを
`tools\CSharpGameEngine.Import.Gltf.Cli`向けに呼ぶだけで、`ExternalFbxLoader.cs`自体も
このヘルパーを使うようリファクタリングした。

両ブリッジが出力するJSONは同一形状(`FbxBridgeDocument`/`GltfBridgeDocument`ともに
positions/normals/indices/baseColorFactorのフラット配列)なので、VSIX側の
デシリアライズ先も`BridgeDocument.cs`(新規、internal)1つに統合し、`SimpleMesh`への
変換ロジック(`ToSimpleMesh()`)もそこに一本化した。`ExternalFbxLoader`/`ExternalGltfLoader`
はどちらも「ブリッジを起動してJSONを読み、`BridgeDocument`にデシリアライズして
`ToSimpleMesh()`を呼ぶ」だけの薄いラッパーになった。

### 3. `SimpleGltfLoader.cs`を削除し、`SharpGLTF.Core`パッケージ参照を除去
`MeshLoader.cs`の振り分けは「`.fbx`は`ExternalFbxLoader`、それ以外(`.glb`/`.gltf`)は
`ExternalGltfLoader`」に変更した。`SimpleGltfLoader.cs`(in-process実装)は削除し、
`CSharpGameEngine.VSExtension.csproj`から`SharpGLTF.Core`パッケージ参照も除去した
(このプロジェクトはもうAssimpNetもSharpGLTF.Coreも直接参照しない)。`System.Text.Json`
パッケージ参照(バージョン10.0.1)はブリッジJSON読み込みのため引き続き必要だが、
バージョン固定理由のコメントは「SharpGLTF.Coreが要求するため」から「既に動作実績のある
バージョンとして固定」に更新した(SharpGLTF.Core自体はもう参照していないため)。

## 検証結果(実際に実行して確認)

- **ブリッジCLI単体**: `dotnet run --project tools/CSharpGameEngine.Import.Gltf.Cli -- assets/TestFixtures/Gltf/Box.glb <tmp>.json`
  を実際に実行し、終了コード0、`Positions`(24頂点分、72要素)・`Normals`・`Indices`
  (36要素、12三角形)・`BaseColorFactor`を含む正しいJSONが生成されることを確認した
  (`TEXCOORD_0`欠落による既存の警告メッセージも従来どおり標準出力に出た)。
- **ビルド**: `CSharpGameEngine.Import.Gltf.Cli`(新規)・`CSharpGameEngine.VSExtension`
  (`ExternalGltfLoader`/`ExternalBridgeProcess`/`BridgeDocument`込み、`SharpGLTF.Core`参照
  除去後)がいずれも警告・エラーなくビルドでき、VSIXパッケージ(`.vsix`)の生成にも成功した。

## 未解決・保留事項

- ADR-0010と同じ制約を継承する: ブリッジ起動は`dotnet run`経由のため初回はMSBuild評価で
  数秒かかりうる(60秒タイムアウトで吸収、体感速度の最適化は引き続きスコープ外)。
- 埋め込みツールウィンドウ(`ShowViewerToolWindowCommand`)経由での実機確認は本セッションでは
  未実施(既存の`ViewerHwndHost`はハードコードされた`Box.glb`のままで、ロード経路の変更以外に
  変わっていないため、ADR-0008以降の実績から動作継続を推定しているが、次回VS実験用インスタンス
  でのスクリーンショット確認が望ましい)。

## ADR-0034による追記

[[ADR-0034-Project-Consolidation]]により、`tools/CSharpGameEngine.Import.Gltf.Cli`の
`<ProjectReference>`先が`src/CSharpGameEngine.Import.Gltf/`から`src/CSharpGameEngine.Import/`へ
変わった。名前空間`CSharpGameEngine.Import.Gltf`は無変更で、`GltfImporter.Import(inputPath)`の
呼び出しもJSON出力形式も本ADRの決定事項もそのままである。

`Import.Gltf`と`Import.Fbx`が1アセンブリになったため、このブリッジの出力にはマネージドの
`AssimpNet.dll`が入るようになったが、**ネイティブ`assimp.dll`は入らない**(RID固有アセットであり、
このプロジェクトは意図的に`RuntimeIdentifier`を設定していない)。
[[ADR-0010-VSIX-Fbx-OutOfProcess-Bridge]]と同様、VSIX側の隔離はプロセス分離によって成立している
ため不変である。統合後に`Box.glb`の実変換を再実行し`exit=0` / `Success:true`を確認済み。
