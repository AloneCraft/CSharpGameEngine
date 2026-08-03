# ADR-0013: ソリューションエクスプローラーでの3Dアセットプレビュー(`IVsEditorFactory`)

## ステータス
承認済み

## 背景

ユーザーから、Unity/UEのアセットブラウザのように、ソリューションエクスプローラーで
`.glb`/`.gltf`/`.fbx`ファイルを開くと3Dプレビューが表示される機能の要望があった(ADR-0012の
`.meta`生成と同時に依頼)。VSでこれを実現する標準的な仕組みは、VS標準搭載のImage Editor等と
同じ`IVsEditorFactory`(クラシックCOMベースのエディター登録機構)である。

## 決定事項

### アーキテクチャ選定
新しいout-of-process「VisualStudio.Extensibility」モデル(`Microsoft.VisualStudio.
Extensibility.Editor.Contracts`)はテキストエディター拡張(マージン・コマンド・補完)向けで、
ネイティブHWNDをドキュメントエディターとしてホストする経路が無いことを確認した。既存の
`AsyncPackage`(`Package`から派生)は`RegisterEditorFactory`を既に継承しており、基底クラス
変更なしで`IVsEditorFactory`実装を登録できる。

### 実装
- 既存の`ViewerHwndHost`(ADR-0007/0008)の`.fbx`/`.gltf`/`.glb`拡張子ディスパッチロジックを
  `MeshLoader.cs`(新規、`internal static class MeshLoader`)に切り出し、`ViewerHwndHost`と
  新規プレビュー機構の両方から共有した。`ViewerHwndHost`の外部から見た挙動は無変更。
- **`AssetPreviewHwndHost.cs`**: `ViewerHwndHost`と同じ構造(`RenderSurfaceWindow`+
  `VorticeRenderer`+`CompositionTarget.Rendering`フック)だが、モデルパスをハードコードせず
  コンストラクタ/`SetModelPath`で受け取る。
- **`AssetPreviewControl.xaml`/`.xaml.cs`**: `AssetPreviewHwndHost`をホストするだけのWPF
  コンテンツ(`ViewerToolWindowControl`と同型)。
- **`AssetPreviewWindowPane.cs`**: `WindowPane`を継承し`IVsPersistDocData`/
  `IPersistFileFormat`(および基底の`IPersist`)を実装する、エディターのDocView兼DocData
  (同一オブジェクト)。読み取り専用(`IsDirty`系は常にfalse、`Save`/`InitNew`は`E_NOTIMPL`)。
  `LoadFromPath`をコンストラクタ・`IPersistFileFormat.Load`・`IVsPersistDocData.LoadDocData`
  の3箇所から冪等に呼べるようにした。
- **`GlbFbxPreviewEditorFactory.cs`**: `IVsEditorFactory`実装。`CreateEditorInstance`で
  `AssetPreviewWindowPane`を構築し、`Marshal.GetIUnknownForObject`で`ppunkDocView`/
  `ppunkDocData`両方に同一インスタンスを渡す。`MapLogicalView`は`LOGVIEWID_Primary`/
  `LOGVIEWID_Designer`のみ`S_OK`を返す。
- `CSharpGameEngineViewerPackage`に`[ProvideEditorFactory(typeof(GlbFbxPreviewEditorFactory),
  110, TrustLevel = __VSEDITORTRUSTLEVEL.ETL_AlwaysTrusted)]`、
  `[ProvideEditorLogicalView(typeof(GlbFbxPreviewEditorFactory),
  VSConstants.LOGVIEWID.Designer_string)]`、`[ProvideEditorExtension(...)]`(`.glb`/`.gltf`/
  `.fbx`向けに3つ、いずれも優先度50)を追加し、`InitializeAsync`で
  `RegisterEditorFactory(new GlbFbxPreviewEditorFactory())`を呼ぶ。`ProjectGuid`は指定せず、
  どのプロジェクト種別でも機能するようにした。新規`VSPackage.resx`(名前`"110"`に表示名を登録)
  を追加した(`[PackageRegistration(UseManagedResourcesOnly = true)]`のため必要)。
- `.vsct`/`.vsixmanifest`の変更は不要だった。

### 実装時に実際のコンパイラで確認・修正した点
リフレクションによる事前のAPI検証は、`Microsoft.VisualStudio.Shell.Interop.dll`が型フォワー
ディング方式の参照アセンブリであることに起因する制約で完全には機能しなかった(バイナリ内に
`IVsEditorFactory`等の文字列は存在することを`grep`で確認できたが、`Assembly.GetTypes()`は
0件を返した)。そのため実装は既知のAPI知識に基づいて書き、実際の`dotnet build`のエラーで
以下を修正した:
- `IPersistFileFormat : IPersist`であり、`IPersist.GetClassID`と`IPersistFileFormat.
  GetClassID`は(シグネチャが同一でも)別々の明示的実装が必要だった。
- `IPersistFileFormat.GetFormatList(out string)`の実装が必要だった。
- `[ProvideEditorFactory]`の`CommonPhysicalViewAttributes`は`bool`ではなく別の型で、
  今回のスコープでは指定を省略した。
- `IPersist`は`Microsoft.VisualStudio.OLE.Interop`名前空間にある。

## 検証結果(実際に実行して確認 - 実スクリーンショットあり)

VS実験用インスタンスで、`UserGameProject/Assets/Models/`に配置したテスト用`Box.glb`/`Box.fbx`
(検証後削除済み)に対して以下を実機確認した:

- ソリューションエクスプローラーで単に「選択」(`SelectionItemPattern.Select()`)しただけでは
  プレビューは開かない(VSの標準プレビュータブ機能は、実際のマウスダブルクリック相当の操作を
  必要とすることを確認)。`dte.ItemOperations.OpenFile(path)`でファイルを開いたところ、
  正しく`GlbFbxPreviewEditorFactory`経由のドキュメントとして開かれた。
- **`Box.glb`を開くと、実際に赤い立方体が3Dレンダリングされたタブが表示されることを
  スクリーンショットで確認した**(`SimpleGltfLoader`+`VorticeRenderer`のin-processパイプライン、
  ADR-0008と同じ描画結果)。
- **`Box.fbx`を開くと、同様に3Dレンダリングされたタブが表示されることを確認した**
  (`ExternalFbxLoader`によるADR-0010の out-of-process ブリッジ経由 - 実際にレンダリングまで
  成功しており、ブリッジの起動オーバーヘッドはあるが機能上の問題はなかった)。
- ソリューションエクスプローラー上で`Box.fbx`/`Box.glb`両方のタブが同時に開けること、
  タブ切り替えで正しく表示が切り替わることを確認した。
- `.gltf`拡張子について、VS標準のテキスト/JSONエディターとの競合は今回のテストでは発生しな
  かった(`.gltf`ファイル自体のテストは未実施 - `.glb`/`.fbx`のみ実機確認)。

## 未解決・保留事項(優先順位順)
1. `.gltf`拡張子(JSON形式)についての実機確認は未実施(`.glb`/`.fbx`のみ確認済み)。
2. ソリューションエクスプローラーで連続してファイルを切り替えた際のパフォーマンス
   (毎回`BuildWindowCore`でD3D12デバイス/スワップチェーンを新規作成する)は、今回のテストでは
   2ファイル間の切り替えのみで問題は見られなかったが、多数のアセットがあるプロジェクトでの
   検証はしていない。
3. VSの標準「プレビュータブ」(1回クリックで一時プレビュー)機能自体の自動発火は確認できて
   いない - `dte.ItemOperations.OpenFile`(プログラム的に開く)経由の動作は確認済みだが、
   実際のマウスダブルクリック相当の挙動は今回のテスト手法(UI Automation)の制約上、
   確認できなかった。
