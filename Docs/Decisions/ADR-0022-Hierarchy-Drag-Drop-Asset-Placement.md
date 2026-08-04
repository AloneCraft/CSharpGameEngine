# ADR-0022: ヒエラルキーへのドラッグ&ドロップ配置(EditorSpec Phase 2 前半)

## ステータス
承認済み

## 背景

ADR-0021で実装した「シェイプ」パレットは、`CSharpGameEngine.Geometry`が生成する手続き型プリミティブ
限定だった。`Docs/EditorSpec.md` Phase 2のゴールは「ソリューションエクスプローラーから任意の
`.glb`/`.gltf`/`.fbx`をヒエラルキーへドラッグすると、シーンにノードが追加される」こと。ユーザーに
Phase 2の2項目(ドラッグ&ドロップ配置 / Inspectorパネル)のどちらから進めるか確認したところ、
依存関係が無い中でドラッグ&ドロップ配置を先に実装することを選んだ。

## 決定事項

### 1. `SceneNodeAdder`(新規、共通ロジックの抽出)

ADR-0021の`PrimitiveSpawner.SpawnAsync`が持っていた「現在開いているシーンにノードを追加して保存し、
`SceneEditorHub.RequestReload`で再読み込みさせる」処理を`SceneNodeAdder.TryAddAssetNode(string
absoluteMeshPath, string nameHint, out string? errorMessage)`として切り出した。
`PrimitiveSpawner`(プリミティブ生成後のノード追加)と、本ADRで追加する
`HierarchyToolWindowControl`のドラッグ&ドロップハンドラ(既存アセットのノード追加)の**両方**が
これを呼ぶ - 「メッシュファイルへの絶対パスが手元にある状態から先」の処理は完全に同一のため。
名前の重複回避(`Name`→`Name_1`→...)・相対パス変換(`Uri.MakeRelativeUri`、net472に
`Path.GetRelativePath`が無いためのADR-0021由来のワークアラウンド)もここに集約した。

### 2. `HierarchyToolWindowControl`にWPF標準のドラッグ&ドロップイベントを追加

`TreeView`に`AllowDrop="True"`+`DragEnter`/`DragOver`/`Drop`ハンドラを追加。VSのソリューション
エクスプローラーからのドラッグは`System.Windows.DataFormats.FileDrop`(標準の`CF_HDROP`)で
実ファイルパスを受け取れることを前提にした実装(多くのVS拡張がこの経路に依存している一般的な
パターン)。`DragEnter`/`DragOver`では拡張子が`.glb`/`.gltf`/`.fbx`のいずれかであるものが1つでも
含まれていれば`DragDropEffects.Copy`を返し、そうでなければ`DragDropEffects.None`(ドロップ不可の
カーソル表示)。`Drop`では対象拡張子のファイルそれぞれについて`SceneNodeAdder.TryAddAssetNode`を
呼ぶ(ファイル名(拡張子なし)をノード名のヒントにする)。シーンが開かれていない場合は
`TryAddAssetNode`が`false`を返すので、案内メッセージを出して終了する。

VS固有のドラッグ&ドロップ形式(`CF_VSSTGPROJECTITEMS`等、`IVsSolution`/`IVsUIHierarchyWindow`
経由での解決が必要なもの)は使っていない - `FileDrop`だけで十分実用に足ると判断したが、実機で
機能しない場合の代替手段として記録しておく。

### 3. シーンJSONスキーマは無変更

ドラッグ&ドロップで追加されたノードも、シェイプパレットで追加されたノードと同じく通常の
`meshAssetPath`付きノードとして扱われる。位置は原点固定・親はシーンのルートノード
(`SceneFile.FindFirstRootNodeId()`)- Transform数値編集はADR-0022のスコープ外(Inspectorパネル、
Phase 2後半)。

## 検証結果(実際に実行して確認)

- `dotnet build tools/CSharpGameEngine.VSExtension/...csproj`: 警告・エラーなし(VS threading
  analyzerのVSTHRD010指摘は`ThreadHelper.ThrowIfNotOnUIThread()`追加で解消)。
- `dotnet build CSharpGameEngine.slnx` / `dotnet test CSharpGameEngine.slnx`: 全11アセンブリ・
  218件成功。
- テスト修正の副産物: `tests/CSharpGameEngine.Scene.Tests/DefaultSceneFileTests.cs`が
  `UserGameProject/DefaultScene.scene`のノード総数を厳密に3件と仮定していたため、ユーザーが
  実機でシェイプパレットを実際に使用した結果(Root/Cube_A/Cube_B以外に複数ノードが追加された
  状態)でテストが落ちた。これはシェイプパレット機能が実際に動作している証拠でもあるため、
  ファイルを元に戻すのではなく**テスト側を「Root配下にCube_A/Cube_Bが存在する」という部分一致
  検証に修正**した - このファイルはツール自体によって書き換えられることが設計上の前提であり、
  厳密な総数一致は最初から誤ったテストだった。

## 未解決・保留事項

- VS実験用インスタンスでの実機確認(ソリューションエクスプローラーからの実際のドラッグ操作で
  `FileDrop`形式が本当に得られるか)は本ADR執筆時点で未実施。`FileDrop`が機能しない場合は
  VS固有のドラッグ形式(`CF_VSSTGPROJECTITEMS`)への切り替えが必要になる。
- Inspectorパネル(Phase 2後半、Transform数値編集)は引き続き未着手。
