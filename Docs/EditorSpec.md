# エディタ目標仕様書(Godot対比・UI逆算)

## 0. 目的とスコープ

`Docs/Architecture.md` は現在のレイヤー構造を、`Docs/Decisions/ADR-0001`〜`ADR-0013` は個々の実装決定を時系列で記録している。どちらも「何が実装済みか」を知るには適しているが、「次に何を実装すべきか」を知るには13本のADRを横断的に読む必要がある。本書はそれとは別に、**目標状態(to-be)を1箇所にまとめた仕様書**として新規に書く。実装が進んだ範囲は、これまで通り個別のADRとして決定ログに残す運用は変えない - 本書はその前段にある「実装すべきUIの見た目」から「そのために必要な機能」を逆算した設計図である。

参照モデルには [Godot Engine](https://godotengine.org/) を使う。理由は次の3点:
- オープンソースでUIパラダイム(Scene dock / FileSystem dock / Inspector / Viewport)が公開・観察可能である。
- 3Dゲームエンジンとして最低限成立するための要素を、業界標準的な形で体現している。
- Godot自体がフルECS(アーキタイプ型)ではなくノードベースの retained scene graph を採用しており、「必要最小限」を志向する本エンジンの現段階と設計思想が近い。

Godotとの対比から、**ゲームエンジンとして成立するために必要な最小限の要素**を次の4点と定義する:

1. シーン内オブジェクトを木構造で見る(Hierarchy)
2. アセット(3Dモデル)をシーンに配置する(モデル配置)
3. 配置したオブジェクトに振る舞いを付与する(C#アタッチ)
4. エディタで組んだ内容がそのまま実行時にも再現される(Play / WYSIWYG)

本書はこの4点の実現に厳密にスコープする。物理演算・オーディオ・アニメーション・フルPBR・複数シーン遷移・Undo/Redo・ギズモ操作・配布パッケージングは明示的にスコープ外とする(詳細は §5)。

## 1. 現状(本書執筆時点の事実)

本書のフェーズはすべて「現状」からの差分として定義する。現状は以下の通り(いずれもコードを実際に確認済み):

- `src/CSharpGameEngine.Core` には ECS/Scene/Entity/Component/Node/Transform の概念が一切存在しない。中身は `Diagnostic`/`DiagnosticBag`/`DiagnosticSeverity`/`SourceLocation`(コンパイラ的診断情報)と `StableId`(安定GUID型)のみで、シーングラフはゼロから設計する必要がある。
- `src/CSharpGameEngine.Graphics.D3D12` の `ModelRenderer.Create(...)` は単一メッシュ(position/normal/uv/indexの1セット)・単一固定カメラのみを受け付ける。トランスフォーム引数がなく、複数メッシュには非対応。
- `tools/CSharpGameEngine.VSExtension`(net472)は既に次を持つ: `OpenViewerCommand`(別ウィンドウでビューアを起動、[[ADR-0006-VSIX-Extension-Skeleton]])、`GlbFbxPreviewEditorFactory` + `AssetPreviewHwndHost`(ソリューションエクスプローラーでの単一ファイルプレビュー、[[ADR-0013-Asset-Preview-Editor-Factory]])、`FabDownloadWatcher`/`AssetMetaWatcher`(アセット取り込みと`.meta`生成、[[ADR-0011-Fab-DropZone-And-Toolbar]] / [[ADR-0012-UserGameProject-AutoOpen-And-AssetMeta]])。**Phase 1(ヒエラルキー)は実装済み**(`HierarchyToolWindowPane`)だが、当初の「パッケージロード時にシーンファイルを走査する」設計ではなく「`*.scene`ファイルをダブルクリックで開くと表示される」設計に変わっている([[ADR-0020-Scene-Preview-And-Hierarchy-Selection-Sync]]/[[ADR-0021-Editor-Layout-And-Shape-Palette]]参照、§2.1で詳述)。単一メッシュ固定描画の`ViewerToolWindowPane`/`ViewerHwndHost`(埋め込みビューア、[[ADR-0008-VSIX-InProcess-Vortice-Renderer]])は、シーン描画が「シーン」ドキュメントタブ(`SceneWindowPane`/`SceneHwndHost`/`SceneRenderer`)に一本化されたことに伴いADR-0021で削除された。**インスペクターパネルはまだ存在しない。**
- `VorticeRenderer`(VSIX専用、net472 / `Vortice.Windows` 2.4.2)はコンストラクタで `SimpleMesh` を1個だけ受け取る。[[ADR-0008-VSIX-InProcess-Vortice-Renderer]] により net10.0側(`Graphics.D3D12`)とは意図的に非結合(`ProjectReference`なし、「疎結合にする必要がある」というユーザー指示に基づく)。そのため**複数メッシュ対応は net10.0側・net472側それぞれに別々に実装する必要がある**([[ADR-0004-Gltf-Model-Viewer-Vertical-Slice]] §3 の "duplicate now, consolidate later" 方針を踏襲)。
- `UserGameProject/UserGameProject.csproj` は net10.0 だが `ProjectReference` はゼロ、`Program.cs` も存在しない([[ADR-0012-UserGameProject-AutoOpen-And-AssetMeta]] 時点で「エンジンとの統合は将来のマイルストーン」と明記済み)。**現状は `dotnet run` しても実行可能なエントリポイントがなく、何も表示されない。**

## 2. Godot対比によるUIターゲット定義

Godotエディタの基本レイアウト(簡略化):

```
+-------------------+-------------------------------+-------------------+
|  Scene             |                                 |  Inspector        |
|  (Hierarchy tree)  |        3D Viewport              |  Transform        |
|  - Root             |     (grid, camera nav,          |  Mesh             |
|    - MeshInstance3D |      selected-object gizmo)     |  Script  [Add]    |
|    - MeshInstance3D |                                 |                    |
|                      |                                 |                    |
+-------------------+                                 +-------------------+
|  FileSystem         |                                 |                    |
|  (res:// assets:     |                                 |                    |
|   models/ scripts/   |                                 |                    |
|   scenes/)            |                                 |                    |
+-------------------+-------------------------------+-------------------+
                       [Select][Move][Rotate][Scale]   [Play] [Pause] [Stop]
```

本プロジェクトはスタンドアロンエディタではなく **Visual Studio 拡張機能** である([[ADR-0001-Layered-Architecture]] L8の制約: 「エンジン/コンパイラ/CLIの契約に対する薄いフロントエンド」)。したがって上記の各ドックをゼロから作るのではなく、VSの既存機能に対応させられるものは対応させる:

| Godotのドック | 本エンジンでの対応 | 状態 |
|---|---|---|
| Scene dock(Hierarchy) | **Hierarchy ツールウィンドウ**(`HierarchyToolWindowPane`) | 実装済み(Phase 1、ADR-0020/0021。設計はダブルクリック起点+選択ハイライトに変更) |
| 3D Viewport | **「シーン」ドキュメントタブ**(`SceneWindowPane`/`SceneHwndHost`/`SceneRenderer`) - `*.scene`をダブルクリックすると開く | 複数オブジェクト描画+選択ハイライトまで実装済み(ADR-0020/0021)。当初想定の「ツールウィンドウ拡張」ではなくドキュメントタブ方式に変更(§2.1参照) |
| アセット配置 | **シェイプ パレット**(`ShapePaletteToolWindowPane`)- 基本プリミティブ8種をクリックでシーンに追加 | 手続き型プリミティブのみ実装済み(ADR-0021)。ドラッグ&ドロップによる任意アセット配置はPhase 2として未着手 |
| Inspector | 新規 **Inspector ツールウィンドウ** | 新規開発(Phase 2〜3、未着手) |
| FileSystem dock | 既存のソリューションエクスプローラー + `.meta`生成([[ADR-0012-UserGameProject-AutoOpen-And-AssetMeta]]) + アセットプレビュー([[ADR-0013-Asset-Preview-Editor-Factory]]) | **流用、新規開発不要** |
| Play ボタン | VS標準のデバッグ開始(F5)、または新規Playツールバーボタン | Phase 4(ボタンは任意の追加、未着手) |
| 移動/回転/スケールギズモ | (スコープ外、Inspectorの数値入力で代替) | §6参照 |

## 2.1 既定ウィンドウレイアウト(GameEngineレイアウト)

ADR-0021で、VSを起動しただけでUnity風の3ペイン構成が出来上がっている状態を実装した。用語対応を明確にしておく:

- **「シーン」**(ドキュメントタブ) = UnityのScene view相当。**編集する場所**。`*.scene`をダブルクリックすると開く。
- **「ヒエラルキー」**(常時ドッキング表示のツールウィンドウ) = UnityのHierarchy相当。開いているシーンのノード木を表示し、クリックで「シーン」側の対応オブジェクトをハイライトする(一方向、ADR-0020)。
- **「シェイプ」**(常時ドッキング表示のツールウィンドウ) = Unityの GameObject作成ショートカット相当。基本プリミティブをクリックでシーンに追加する(ADR-0021)。
- **Unityの「Game view」に相当するものはツールウィンドウとして存在しない** - 本システムでは`UserGameProject`をビルドして実行すること(既存の`OpenGameCommand`、別プロセスとして起動)がGame viewの役割を果たす。「シーン」パネルを「ゲーム」パネルと呼ぶのは誤りなので注意(過去に誤った命名をした経緯がある)。

既定配置(初回起動時のみ適用、ASCII図):

```
+------------------+-------------------------------+------------------+
|                    |                                 |                    |
|  ヒエラルキー        |         シーン                    |    シェイプ          |
|  Root              |     (ドキュメントタブ)              |  ボックス 球形       |
|   Cube_A            |                                 |  シリンダー カプセル |
|   Cube_B            |                                 |  コーン   トーラス   |
|                    |                                 |  矩形    ディスク    |
+--------------------+---------------------------------+--------------------+
|  ソリューションエクスプローラー(既存、リネームなし)                              |
+---------------------------------------------------------------------------+
```

「C# Game Engine」ツールバーも既定で表示される。

**VS SDKの制約と設計判断**(実装前に調査済み):
- `[ProvideToolWindow]`属性の`Style`/`Window`/`Orientation`/`DockedWidth`は、そのVSハイブでツールウィンドウが**初めて生成される時だけ**効く既定値。以後はVSが`.winprf`ウィンドウプロファイルに保存した実レイアウトが優先されるため、2回目以降の起動では属性の指定は無視される。
- `.vsct`にツールバーを既定で表示させるフラグは存在しない(`DefaultDocked`はドッキング/フローティングの区別であって表示/非表示ではない、[[ADR-0011-Fab-DropZone-And-Toolbar]]で実地確認済み)。`EnvDTE.DTE.CommandBars`をキャストして`CommandBar.Visible = true`を呼ぶプログラム的な方法のみが機能する。
- VS 2015+の「ウィンドウ > ウィンドウ レイアウトの適用」(名前付きレイアウト、Ctrl+Alt+1〜9)を**プログラムから登録する公開APIはVS SDKに存在しない**。実際の実装(`ILayoutManagementServiceInternal`)は非公開の内部アセンブリ(`Microsoft.VisualStudio.Platform.WindowManagement.dll`)にのみ存在する。
- 上記3点により、「初回起動時のみ`IVsWindowFrame.SetFramePos`(公開API)で強制配置し、以後はVS標準の『前回開いていた状態を復元』に任せる」という設計を採用した。手動でいつでも配置をやり直せる「GameEngine レイアウトを適用」コマンドも用意した(Tools メニュー/ツールバー)。

## 3. データモデル: シーンファイル形式

`Scene` は `SceneNode` の木構造として表現する。各ノードが持つ情報:

| フィールド | 型 | 説明 |
|---|---|---|
| `Id` | 安定GUID(`StableId`と同形式) | ノードの永続識別子。Unity/UEの`.meta`と同じ考え方。 |
| `Name` | string | ヒエラルキーに表示される名前。 |
| `ParentId` | 安定GUID? | 親ノードのId。ルートノードは`null`。 |
| `Transform` | 位置(Vector3)/回転(Quaternion)/スケール(Vector3) | ローカル変換。 |
| `MeshAssetPath` | string? | `.glb`/`.gltf`/`.fbx`への相対パス(未設定ならメッシュを持たない空ノード)。 |
| `Components` | string[] | アタッチされたC#スクリプトの完全修飾型名のリスト。 |

保存形式は人間が差分を追える JSON、拡張子は**単一の`*.scene`**(中身はJSON)。[[ADR-0002-Source-And-Artifact]] の原則(「`.cs`ファイルとオリジナルアセットが正規のSource」)に倣い、**シーンJSONも生成物ではなくオーサリングされたSourceとして扱う** - `UserGameProject/`配下に配置し、`.csproj`の`<None>`項目としてソリューションエクスプローラーに表示・Git管理する。

**拡張子についての注記(ADR-0021)**: 当初`*.scene.json`(ドット2つの複合拡張子)として実装したが、VSの`ProvideEditorExtension`はこれを単一の拡張子として認識せず、ダブルクリックしても何も開かないという不具合が実機検証で判明した。単一拡張子`*.scene`に変更して解決した(Unityの`.unity`/UEの`.umap`と同じ発想)。中身のJSONスキーマ自体(下記)は変更していない。

スキーマ例(ルート+子ノード1つ):

```json
{
  "schemaVersion": 1,
  "nodes": [
    {
      "id": "5b1c8e2a-0000-4000-8000-000000000001",
      "name": "Root",
      "parentId": null,
      "transform": { "position": [0, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1] },
      "meshAssetPath": null,
      "components": []
    },
    {
      "id": "5b1c8e2a-0000-4000-8000-000000000002",
      "name": "Box",
      "parentId": "5b1c8e2a-0000-4000-8000-000000000001",
      "transform": { "position": [2, 0, 0], "rotation": [0, 0, 0, 1], "scale": [1, 1, 1] },
      "meshAssetPath": "Assets/Fab/SomeAsset/model.glb",
      "components": ["UserGameProject.Scripts.SpinBox"]
    }
  ]
}
```

**追記(ADR-0030、Scene Authoring Phase A)**: 上記のJSON方式は本書執筆時点でも変更していない
(このセクションの内容自体は現行のまま有効)。ただし、将来的にはC#コードそのものをSceneの正本と
する方式へ置き換える前提で、`[SceneDefinition] static partial class`+`SceneDefinitionGenerator`
という並行方式をPhase Aとして追加した。Phase Aは新しいAttribute/Component/Source Generatorと
動作するサンプル(`samples/SceneAuthoringDemo`)のみで、本書が定めるJSON方式Hierarchy Docking
Windowの編集フロー(本セクションおよび§5)にはまだ一切統合していない。詳細は
[[ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth]]および[[M0010-Scene-Authoring-Phase-A]]を参照。

**追記2(ADR-0031)**: `[SceneDefinition]`クラスをコードエディタで開いた状態での右クリック
「シーンプレビュー」が、実際に`CreateRuntime`を実行して得たEntity階層を、本セクションが定める
`.scene`プレビュー基盤(§2の「シーン」ドキュメントタブ)へ一時的な合成`.scene`ファイルとして
流し込む形で追加された。これもHierarchy Docking Windowの編集フローへの統合ではなく読み取り専用の
プレビューであり、本セクションのJSONスキーマ自体への変更は無い。詳細は
[[ADR-0031-Scene-Definition-3D-Preview]]および[[M0011-Scene-Definition-Preview]]を参照。

## 4. レイヤー対応(`Docs/Architecture.md` のL0〜L9との対応)

- **新規プロジェクト `src/CSharpGameEngine.Scene`**(net10.0、L2/L3相当)を新設する。`Core`はピュアカーネルのまま変更しない。ここに `SceneNode` / `Transform` / `Scene`(木構造・JSONシリアライズ) / `GameScript` 基底クラス(`OnStart()` / `OnUpdate(float deltaTime)` の仮想メソッドを持つ)を置く。
- **なぜフルECSではなくGodot式のノード木を選ぶか**: 「必要最小限」というスコープと、参照モデルであるGodot自体がアーキタイプ型ECSではなくノードベースの retained scene graph を採用している事実に整合させるため。将来アーキタイプECSへの移行が必要になった場合は、その時点で改めてADRとして決定する。
- **L8(`CSharpGameEngine.VSExtension`)に増える新規/変更コンポーネント**: Hierarchyツールウィンドウ(実装済み)、Inspectorツールウィンドウ(未着手)、複数メッシュ対応レンダラー(実装済み - ただし当初想定した`VorticeRenderer`の拡張ではなく、新規`SceneRenderer`として追加し`VorticeRenderer`は無改修のまま温存する形になった、ADR-0021参照)、ソリューションエクスプローラー→Hierarchyのドラッグ&ドロップ配置(未着手)、「スクリプトを追加」コマンド(未着手)。
- **L9(`UserGameProject`)に増える新規コンポーネント**: `Core`/`CSharpGameEngine.Scene`/`Graphics.D3D12`/`Platform.Windows`/`Import.Gltf`/`Import.Fbx`への`ProjectReference`、`Program.cs`エントリポイント、`Graphics.D3D12`側の複数メッシュ対応レンダラー。
- **2レンダラー非結合の帰結**: net10.0側(`Graphics.D3D12`)とVSIX専用net472側(`VorticeRenderer`)の複数メッシュ対応は、[[ADR-0008-VSIX-InProcess-Vortice-Renderer]] の疎結合方針により**別々に実装する必要がある**。本書もこれを前提に、Phase 2(VSIX/Vortice側)とPhase 4(UserGameProject/Graphics.D3D12側)で別々の変更として記述する。

## 5. フェーズ分けロードマップ

### Phase 1: ヒエラルキー表示のみ(読み取り専用)

**ゴール**: `UserGameProject`を開くと、新規Hierarchyツールウィンドウにシーンノードの木(初期状態はルートのみ)が表示される。編集・配置はまだ行わない。

**新規**:
- `src/CSharpGameEngine.Scene` プロジェクトの最小型(`Id`/`Name`/`ParentId`/`Transform`。`MeshAssetPath`/`Components`はスキーマに含めてよいが未使用でよい)。
- シーンJSON v1スキーマ(§3)。
- `UserGameProject/`配下のデフォルト空シーン(ルートノードのみ)。
- VSExtension側 `HierarchyToolWindowPane` + WPF `TreeView`(`System.Text.Json`で読み込み・表示のみ、`AssetMetaWatcher`と同様パッケージロード時にシーンファイルを走査する構成)。

**検証**: `CSharpGameEngine.Scene`の単体テスト(JSON往復・親子解決)。VS実験用インスタンスでHierarchyパネルにテスト用シーンのノード木が表示されるスクリーンショット([[ADR-0006-VSIX-Extension-Skeleton]]以降で確立済みの実機検証パターンを踏襲)。

### Phase 2: アセットのモデル配置

**進捗(ADR-0021時点)**: 「シーン全ノードを描画する」部分と「シーンにノードを追加保存する」部分は、想定と異なる経路(ドラッグ&ドロップではなくシェイプパレットのクリック、対象は手続き型プリミティブのみ)で**先行実装済み**。`SceneRenderer`(新規、`VorticeRenderer`は無改修のまま温存)が`IReadOnlyList<SceneRenderObject>`(mesh+world transform+nodeId)を受け取り複数オブジェクトを描画する。`PrimitiveSpawner`がシーンJSONにノードを追加して保存し、`SceneEditorHub.RequestReload`で再描画をトリガーする一連の流れは、Inspectorの数値編集を除けば下記ゴールと同じ形。**残っているのは**、任意の`.glb`/`.gltf`/`.fbx`アセット(手続き型プリミティブに限らない)をソリューションエクスプローラーからドラッグ配置する導線と、Inspectorツールウィンドウそのもの。

**ゴール**: ソリューションエクスプローラーから `.glb`/`.gltf`/`.fbx` をHierarchyへドラッグ(またはコンテキストメニュー「シーンに配置」)すると、そのアセットを参照する新規ノードがシーンJSONに追加され保存される。Inspectorで選択ノードのTransform(位置/回転/スケール)を数値編集できる。

**新規/変更**:
- ソリューションエクスプローラー→Hierarchyへのドラッグ&ドロップ処理(選択アイテムのファイルパス解決) - `PrimitiveSpawner`(ADR-0021)のノード追加・保存・再読み込みロジックを、ドラッグされた任意アセットのパスに対しても使えるよう一般化する。
- 新規 `InspectorToolWindowPane`(選択ノードのTransformを数値編集、変更をシーンJSONへ書き戻し・再描画をトリガー)。

**検証**: 2種類以上のモデルをドラッグ配置し、シーンに両方が別々の位置に描画され、Hierarchyにも両ノードが表示されるスクリーンショット。

### Phase 3: C#スクリプトのアタッチ

**ゴール**: Inspectorでノード選択中に「スクリプトを追加」を押すと、`UserGameProject/Scripts/<NodeName>.cs`が新規`GameScript`派生テンプレートとして生成され、`UserGameProject.csproj`に追加され、シーンJSONの当該ノードにコンポーネント参照(完全修飾型名)が追加され、生成ファイルがVSエディタで開かれる。

**新規**:
- `CSharpGameEngine.Scene`に`GameScript`基底クラス(net10.0、`UserGameProject`から直接参照可能)。
- C#スクリプトテンプレート文字列(`OnStart`/`OnUpdate`の空実装を含む雛形)。
- Inspectorの「スクリプトを追加」コマンド(`EnvDTE`/`IVsProject.AddItem`でプロジェクトに追加 + `DTE.ItemOperations.OpenFile`で開く)。

**このフェーズで明示的にやらないこと**: エディタのViewport内でスクリプトを実際に実行すること。Godot自身もエディタ編集時と実行時を区別しており、加えて[[ADR-0010-VSIX-Fbx-OutOfProcess-Bridge]] が「`devenv.exe`プロセス内で信頼できないコードを実行するリスクを避ける」という理由でFBXのネイティブパースをブリッジプロセスへ隔離した判断と同種の理由により、ユーザースクリプトの実行はPhase 4のPlay(`UserGameProject`という別プロセス)でのみ行う。

**検証**: テンプレート生成+シーンJSON参照の単体テスト。VS実験用インスタンスでスクリプト生成→Inspector表示のスクリーンショット。

### Phase 4: `UserGameProject`起動でモデル表示(Play)

**ゴール**: `dotnet run --project UserGameProject`(またはVSのF5)を実行すると、現在のデフォルトシーンJSONを読み込み、全ノードをインスタンス化して描画し(Phase 1〜3でHierarchy/Viewportに組んだ内容と一致するWYSIWYG)、各ノードにアタッチされたスクリプトの`OnStart`/`OnUpdate`を実行するウィンドウが開く。

**新規**:
- `UserGameProject.csproj`の`ProjectReference`は既に`CSharpGameEngine.Core`/`CSharpGameEngine.Windows`/`CSharpGameEngine.Import`が揃っている(ADR-0034で5プロジェクト構成になり、旧`Scene`/`Graphics.D3D12`/`Platform.Windows`/`Import.Gltf`/`Import.Fbx`はこの3つに吸収された)。`UserGameProject`側にnet472境界の問題はない。なおシーンの正本はJSONではなくC#(`[SceneDefinition]`、ADR-0030/0031/0033)であり、`CSharpGameEngine.Scene`はADR-0034で削除済み。
- `Program.cs`(`CSharpGameEngine.Extension`/`TestHost`と同様のWindow+Device+SwapChain構成だが、`--model <path>`ではなくシーンJSON駆動)。
- `Graphics.D3D12`側にPhase 2のVortice版と対になる複数メッシュ対応レンダラー(§4「2レンダラー非結合の帰結」参照)。
- 固定タイムステップの更新ループ(毎フレーム各ノードの`Components`を解決し`OnUpdate`を呼ぶ)。

**任意の追加(必須ではないが推奨・低コスト)**: VSIXに「Play」ツールバーボタンを追加し、`Process.Start`で`dotnet run --project UserGameProject`を実行するだけ(`OpenViewerCommand`と同型のパターン、[[ADR-0006-VSIX-Extension-Skeleton]])。Godotの▶ボタンに相当し、既存パターンの再利用のみで実装コストが低いため推奨するが、Phase 4のゴール自体の必須要件ではない。

**検証**: 2ノード以上・スクリプト付きのシーンJSONに対し`dotnet run --project UserGameProject`を実行し、Viewportツールウィンドウのスクリーンショットと同じ配置でモデルが表示されることを実コマンド実行+スクリーンショットで証明する(このプロジェクトで一貫している「実際に確認した」原則、[[ADR-0004-Gltf-Model-Viewer-Vertical-Slice]]以降踏襲)。

## 6. 明示的スコープ外

物理演算、オーディオ、アニメーション/スキニング、フルPBRマテリアル(metallic/roughness/normal/emissive/occlusion - [[ADR-0005-Gltf-BaseColor-Texture-Display]]時点でも保留)、複数シーン間の遷移・読み込み、Undo/Redo、マルチセレクト、ビューポート内ギズモ(移動/回転/スケールハンドル)による直接操作(Phase 2はInspectorの数値入力のみ)、スタンドアロン配布用パッケージング。これらが必要になった時点で、本書とは別に個別のADRとしてスコープする。

## 7. 検証方法の総括

- 新規`CSharpGameEngine.Scene`にテストプロジェクトを追加し、シーンJSON往復・ノード木操作を単体テストで担保する。
- 各フェーズのVSIX変更は、[[ADR-0006-VSIX-Extension-Skeleton]]以降で確立済みの「VS実験用インスタンス + UI Automation(`NativeWindowHandle`経由 - `Process.MainWindowHandle`は複数トップレベルウィンドウがある場合に誤ったハンドルを返すことが[[ADR-0010-VSIX-Fbx-OutOfProcess-Bridge]]で判明済み) + スクリーンショット」パターンを踏襲して実機検証する。
- Phase 4は`dotnet run --project UserGameProject`の実コマンド出力とスクリーンショットで、Hierarchy/Viewportで組んだ内容と実行時表示が一致することを証明する。
