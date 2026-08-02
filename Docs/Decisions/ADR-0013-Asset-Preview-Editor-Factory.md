# ADR-0013: ソリューションエクスプローラーでの3Dアセットプレビュー(`IVsEditorFactory`)

## ステータス
承認済み

## 背景

ユーザーから、Unity/UEのアセットブラウザのように、ソリューションエクスプローラーで
`.glb`/`.gltf`/`.fbx`ファイルを選択すると3Dプレビューが表示される機能の要望があった(ADR-0012の
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
  の3箇所から冪等に呼べるようにし、シェルが実際にどちらを呼ぶか不明な点を吸収した。
- **`GlbFbxPreviewEditorFactory.cs`**: `IVsEditorFactory`実装。`CreateEditorInstance`で
  `AssetPreviewWindowPane`を構築し、`Marshal.GetIUnknownForObject`で`ppunkDocView`/
  `ppunkDocData`両方に同一インスタンスを渡す(読み取り専用エディターでの一般的なパターン)。
  `MapLogicalView`は`LOGVIEWID_Primary`/`LOGVIEWID_Designer`のみ`S_OK`を返す。
- `CSharpGameEngineViewerPackage`に`[ProvideEditorFactory(typeof(GlbFbxPreviewEditorFactory),
  110, TrustLevel = __VSEDITORTRUSTLEVEL.ETL_AlwaysTrusted)]`、
  `[ProvideEditorLogicalView(typeof(GlbFbxPreviewEditorFactory),
  VSConstants.LOGVIEWID.Designer_string)]`、`[ProvideEditorExtension(...)]`(`.glb`/`.gltf`/
  `.fbx`向けに3つ、いずれも優先度50)を追加し、`InitializeAsync`で
  `RegisterEditorFactory(new GlbFbxPreviewEditorFactory())`を呼ぶ。`ProjectGuid`は指定せず、
  どのプロジェクト種別でも(Solution Itemsとして置かれたファイルでも)機能するようにした。
  リソース文字列解決のため新規`VSPackage.resx`(名前`"110"`に表示名を登録)を追加した
  (`[PackageRegistration(UseManagedResourcesOnly = true)]`のため必要)。
- `.vsct`/`.vsixmanifest`の変更は不要だった - エディターファクトリ登録はpkgdef経由で自動反映
  され、「プログラムから開く」ダイアログはVSが登録済みエディターを自動的に列挙する。

### 実装時に実際のコンパイラで確認・修正した点(推測が外れた箇所)
リフレクションによる事前のAPI検証は、`Microsoft.VisualStudio.Shell.Interop.dll`が型フォワーディング
方式の参照アセンブリであることに起因する制約で完全には機能しなかった(バイナリ内に
`IVsEditorFactory`等の文字列は存在することを`grep`で確認できたが、`Assembly.GetTypes()`は
0件を返した)。そのため実装は既知のAPI知識に基づいて書き、実際の`dotnet build`のエラーで
以下を修正した:
- `IPersistFileFormat : IPersist`であり、`IPersist.GetClassID`と`IPersistFileFormat.
  GetClassID`は(シグネチャが同一でも)別々の明示的実装が必要だった。
- `IPersistFileFormat.GetFormatList(out string)`の実装が必要だった(未実装だとインター
  フェイス未実装エラー)。
- `[ProvideEditorFactory]`の`CommonPhysicalViewAttributes`は`bool`ではなく別の型で、
  今回のスコープでは指定を省略した(既定値で問題ない)。
- `IPersist`は`Microsoft.VisualStudio.OLE.Interop`名前空間にある(`Microsoft.VisualStudio.
  Shell.Interop`ではない)。

## 検証結果(実際に実行して確認)
- `dotnet build`(VSExtensionプロジェクト単体、およびリポジトリ全体17プロジェクト)が警告・
  エラーなく成功することを確認した。

## 未解決・保留事項(実機検証が必要、優先順位順)
1. ソリューションエクスプローラーで連続してファイルを切り替えた際のパフォーマンス(毎回
   `BuildWindowCore`でD3D12デバイス/スワップチェーンを新規作成すると、素早い連続クリックで
   引っかかりが出る可能性がある)。
2. VSの標準「プレビュータブ」(1回クリックで一時プレビュー、ダブルクリックで正式に開く)機能が
   カスタムエディターファクトリ登録だけで自動的に機能するか。
3. `.gltf`拡張子について、新規登録したエディターがVS標準のテキスト/JSONエディターより
   優先されるか(テスターが過去に「既定のエディターとして設定」していた場合は挙動が変わる
   可能性がある)。
4. `[ProvideEditorFactory]`のリソースID解決(`VSPackage.resx`との対応)- ビルドは通ったが
   「プログラムから開く」ダイアログでの実際の表示名は未確認。
5. 本セッションで判明した通り、VS実験用インスタンスのスクリーンショット取得は
   `Process.MainWindowHandle`ではなくUI Automationの`NativeWindowHandle`経由で行うこと
   (ADR-0010参照)。
