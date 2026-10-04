# ADR-0008: VSIXツールウィンドウへのインプロセスD3D12埋め込み(Vortice.Windows)

## ステータス
Superseded by ADR-0052(共有の外部プロセス Preview Host、F-L8-018、2026-09-01)。本ADRのVSIX内レンダラー
(`VorticeRenderer.cs`/`SceneRenderer.cs`)とVortice/SharpGen依存は削除済みで、以下の未解決事項も現行の制約ではない。

## 背景

ADR-0007では、VSツールウィンドウ内にglTFビューアを表示する方式として、「別プロセス
(`CSharpGameEngine.Extension.exe`)を起動し、そのウィンドウだけをHWND再親化でVSツールウィンドウ
に埋め込む」方式を実装した。ユーザーはこれを見て「別プロセスであること」自体を問題視し、
「それはやめてvsixインプロセスにして」と明確に指示した。

**技術調査で判明した事実(推測ではなく実際に確認済み)**:
- `TerraFX.Interop.Windows`(既存の`Graphics.D3D12`/`Compiler.Hlsl`が使うD3D12/DXGI/WICバイン
  ディング)は、過去の全バージョン(`10.0.19041-beta1` 〜 `10.0.26100.6`)を通じて一貫して
  `net5.0`以降のみをターゲットにしており、`net472`(VSIXが動作するランタイム)からは一切参照
  できない。これがADR-0007で別プロセスに分離せざるを得なかった根本原因である。
- 代替のD3D12バインディングライブラリ`Vortice.Windows`には、`.NETStandard2.0`(net472から参照
  可能)をターゲットにしていたバージョン系列が存在する。NuGetの実際のバージョン履歴を確認した
  ところ、`2.4.2`が netstandard2.0 に対応した最後(最新)のバージョンだった(`2.4.0`/`2.4.1`は
  欠番、`3.0.0`以降はnet6.0+専用)。`Vortice.Direct3D12`、`Vortice.DXGI`、`Vortice.D3DCompiler`
  いずれも`2.4.2`でnetstandard2.0を対象に含むことを確認済み。
- `SharpGLTF.Core` 1.0.6 も元々 `.NETStandard2.0`/`.NETStandard2.1` に対応しており、net472から
  直接参照できることを確認済み。

**`2.4.2`固定のトレードオフ(ユーザー指摘、正直に記録)**: それ以降(3.x系、現在の最新3.8.3まで)に
Vortice.Windows側で行われたバインディング層のバグフィックスや新D3D12機能への対応を受け取れない。
ただし、これは「D3D12ランタイム自体(OS側のd3d12.dll)のバグフィックス」とは別物 - ランタイムの
バグフィックスはOS/ドライバー側の話でありVorticeのバージョンとは無関係に適用される。3.x系で追加
された機能は主にDirectX Raytracing拡張・Work Graphs・Mesh Shaderなど、このプロジェクトが使わない
先端機能が中心であり、実害は小さいと判断している。

ユーザーからは追加で「疎結合にする必要がある」との指摘があった。新しいnet472/Vortice版の
レンダリングコードは、既存のnet10.0コードベース(`Graphics.D3D12`、`Import.Gltf`、
`Platform.Windows`、`Compiler.Hlsl`)から完全に独立させ、`ProjectReference`等で結合しない。

## 決定事項

### 1. VSIX専用の独立した軽量レンダラーを新規実装(既存コードは無変更)
`tools/CSharpGameEngine.VSExtension` 内に、`Vortice.Direct3D12`/`Vortice.DXGI`/
`Vortice.D3DCompiler`(いずれも`2.4.2`)と`SharpGLTF.Core`(1.0.6、netstandard2.0グループ)を
直接参照する、完全に独立したD3D12レンダラー(`VorticeRenderer`)とglTFローダー
(`SimpleGltfLoader`)を新規に書いた。既存の`src/CSharpGameEngine.Graphics.D3D12`等への
`ProjectReference`は一切追加していない - ユーザーの「疎結合」指示通り、net10.0側のビルド・CLI
(`CSharpGameEngine.Extension`)・全テストはこの変更の影響を一切受けない(実際に
`dotnet build`/`dotnet test`をリポジトリ全体に対して実行し、無変更であることを確認済み)。

スコープは意図的に最小限: テクスチャなし、単純なLambertianシェーディングのみ、glTFの
NORMAL属性欠落時のフォールバック合成なし(バンドルされている`Box.glb`は常にNORMALを持つため)。
ADR-0005のテクスチャ表示機能は今回は移植していない。

### 2. ADR-0007の外部プロセス方式に伴うコードを削除
`ViewerContainerWindow.cs`(Job Object/ウィンドウ再親化用のP/Invoke)、`ViewerHwndHost.cs`内の
`Process.Start`/ポーリング/Job Objectロジック一式を削除した。`src/CSharpGameEngine.
Platform.Windows/Win32Window.cs`の`ParentHwnd`対応と親ウィンドウ生存ポーリング、
`ViewerRunOptions`/`ViewerOptionsParser`の`--parent-hwnd`も、`OpenViewerCommand`(別ウィンドウ
方式、ADR-0006)からは使われていない機能だったため削除した。既存の`OpenViewerCommand`自体は
変更せずそのまま残している。

### 3. `ViewerHwndHost`がネイティブ描画サーフェスを直接作成し、そこに直接Present
`ViewerHwndHost.BuildWindowCore`が、WPFの`HwndHost`契約に従い`RenderSurfaceWindow`
(`WS_CHILD`の軽量ネイティブウィンドウ、手書きP/Invoke - `TerraFX.Interop.Windows`はnet472から
使えないため)を作成し、そのHWNDに対して直接`VorticeRenderer`のDXGIスワップチェーンを
作成する。レンダリングループはWPFの`CompositionTarget.Rendering`イベント(WPFの毎フレーム
発生するレンダリングタイミング)にフックしている。プロセスは完全に1つ(`devenv.exe`)のみで、
ADR-0007のような別プロセス・HWND再親化・Job Object・生存ポーリングは一切不要になった。

### 4. 経験的に発見した実装上の問題と修正(実装時に判明、事前には予測できなかった)

- **WPFのXAML markup-compileパスが生成する一時プロジェクトに`AllowUnsafeBlocks`が引き継がれない**:
  `UseWPF=true`のプロジェクトでXAMLファイルがあると、MSBuildがビルド時に一時的な`_wpftmp.csproj`
  を生成してXAMLのマークアップコンパイルを行うが、この一時プロジェクトは元のcsprojの
  `AllowUnsafeBlocks`設定を引き継がない。このため、頂点/インデックスバッファへのデータ書き込みや
  定数バッファの更新に`unsafe`/`fixed`ポインタコードを使うことができなかった。代わりに
  `ID3D12Resource.Map<byte>(subresource, length)`(`Span<byte>`を返すジェネリックオーバーロード)
  と`System.Runtime.InteropServices.MemoryMarshal.Write<T>`を使い、`unsafe`を一切使わずに
  実装した。
- **`ID3D12Resource.Map`はジェネリックメソッドで、戻り値の型からの型推論ができない**:
  `resource.Map(0, sizeInBytes)`と書くと、代入先の`Span<byte>`から`T=byte`を推論してくれず、
  別の(`void*`を取る)非ジェネリックオーバーロードに解決されてコンパイルエラーになる。
  `resource.Map<byte>(0, sizeInBytes)`と型引数を明示する必要があった。
- **DXGIのフリップモデルスワップチェーン(D3D12で必須)は、`WS_CHILD`の子ウィンドウに対して
  そのままでは合成されない**: 初期実装では、レンダリングは(ログで確認する限り)実際に成功して
  いた(`Present`まで正常に実行されていた)にもかかわらず、ツールウィンドウには何も表示されず
  真っ白/グレーのままだった。原因は、Windowsの既定のウィンドウ合成(DWMのリダイレクション
  ビットマップ)が、子ウィンドウに対する独立したDXGIフリップモデルのPresentを正しく反映しない
  ことにあった。`CreateWindowEx`の拡張スタイルに`WS_EX_NOREDIRECTIONBITMAP`(値
  `0x00200000`)を追加することで解決した - これによりそのウィンドウのリダイレクションビットマップ
  自体が作成されなくなり、DXGIのPresentが直接合成されるようになる。これは事前のプランには
  無かった、実装中に切り分けて発見した修正である。
- **VS拡張機能のMEF/コンポーネントキャッシュが、開発中の頻繁なDLL再デプロイに追従しないことが
  ある**: DLLを変更するたびに、`devenv.exe /rootsuffix Exp /updateconfiguration`でキャッシュを
  明示的に更新しないと、古い(あるいは中間状態の)アセンブリのまま動作し続けることがあった
  (ADR-0006でも同様の現象を確認済み)。これは実験用インスタンスでの開発時のみの問題であり、
  正式にインストールされたVSIXの通常利用では発生しない。

## 検証結果(実際に実行して確認)
1. `dotnet build`(リポジトリ全体)+ `dotnet test`(GPU/非GPU問わず全73+3件)- 既存のnet10.0
   ビルド・CLI・テストに一切影響がないことを確認した(疎結合の実証)。
2. VS実験用インスタンスにインストールし、「C# Game Engine Viewer パネルを表示」を実行して、
   **`CSharpGameEngine.Extension.exe`(別プロセス版)が一切起動せず**(`Get-Process`で確認)、
   ツールウィンドウ内に`Box.glb`が実際に描画されることを実スクリーンショットで確認した。
3. ツールウィンドウのリサイズに描画面(スワップチェーン)が正しく追従することを確認した。
4. VSを閉じた際、クラッシュせず正常に終了することを確認した。別プロセスが存在しないため、
   ADR-0007で発生した「取り残されたプロセス」問題はそもそも起こり得ない。

## 未解決・保留事項
- テクスチャ/PBR表示は今回のスコープ外(ADR-0005の機能はこのVSIX専用レンダラーには移植して
  いない)。
- glTFのNORMAL属性欠落時のフォールバック合成は未実装(バンドルされている`Box.glb`は常に
  NORMALを持つため、今回のスコープでは不要)。
- `Vortice.Windows` 2.4.2 固定によるバインディング層の新機能非対応(上記参照)。
- 既存の`OpenViewerCommand`(別ウィンドウ、ADR-0006)は残しているが、内部的に使っていた
  `--parent-hwnd`関連コードは削除した - 影響はない(元々`OpenViewerCommand`はこのオプションを
  使っていなかった)。
- 実験用インスタンスでの開発時にMEFキャッシュの明示的な更新(`/updateconfiguration`)が必要な
  ケースがある点は、正式インストール時には問題にならないと考えられるが、明確に検証してはいない。
