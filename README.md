# C# Game Engine

- 製品表示名: `C# Game Engine`
- 内部名: `CSharpGameEngine`
- 現在の段階: 第3マイルストーン（glTFアセットのインポートと最小Viewer）

## Build
```bash
dotnet restore
dotnet build
```

## Test
```bash
dotnet test
```

GPU統合テスト（`CSharpGameEngine.Windows.Tests`内、`Category=GPU`）はWARPアダプタに対して実行され、
非Windows環境では明示的にSkipされます。CPU側のみ実行する場合:
```bash
dotnet test --filter "Category!=GPU"
```

## VS Code
`.vscode/launch.json`に、`UserEcsProject`・`TestHost`(self-test/triangle)・
`Extension`(Viewer)・3つのCLIブリッジ`Import.Gltf.Cli`/`Import.Fbx.Cli`/`Primitive.Cli`)分の
デバッグ構成を用意しています。VS Codeでリポジトリを開き、拡張機能`ms-dotnettools.csharp`
(初回は推奨通知が出ます)をインストールした状態で「実行とデバッグ」パネルから選択してください。
ビルドは各構成の起動前に自動実行されます(`.vscode/tasks.json`)。CLIブリッジ3つは入出力パスを
起動時に尋ねます。`tools/CSharpGameEngine.VSExtension`(net472 VSIX)はVisual Studio本体
(`devenv.exe`)のホストが必須のため対象外です。

## TestHost
```bash
dotnet run --project tools/CSharpGameEngine.TestHost -- --help
dotnet run --project tools/CSharpGameEngine.TestHost -- self-test
```

### Triangle（Windows + Direct3D 12）
通常起動（ウィンドウを表示し、閉じるまで描画を継続）:
```bash
dotnet run --project tools/CSharpGameEngine.TestHost -- triangle
```

自動検証（非表示ウィンドウ、WARPアダプタ、Debug Layer有効、3フレーム描画後にReadback検証、成功時0終了）:
```bash
dotnet run --project tools/CSharpGameEngine.TestHost -- triangle --hidden --frames 3 --warp --debug-layer --verify
```

Hardware Adapterでの検証（`--warp`を外すとHardware Adapterを使用。利用不可の場合は明確なエラーで終了し、
勝手にWARPへフォールバックしません）:
```bash
dotnet run --project tools/CSharpGameEngine.TestHost -- triangle --hidden --frames 3 --debug-layer --verify
```

triangleコマンドのオプション:
- `--frames <count>`: 指定フレーム数で自動終了する
- `--width <pixels>` / `--height <pixels>`: クライアント領域のサイズ（既定 1280x720）
- `--hidden`: ウィンドウを表示しない（自動検証向け）
- `--warp`: WARP（ソフトウェア）アダプタを使用する
- `--debug-layer`: D3D12 Debug Layerを有効化する
- `--gpu-validation`: GPU Based Validationを有効化する（`--debug-layer`と併用）
- `--verify`: 描画結果をGPUからReadbackし、Triangleが実際に描画されたかをピクセル単位で検証する

## Viewer（glTFモデル表示、Windows + Direct3D 12）
`tools/CSharpGameEngine.Extension`は、glTF/GLBファイルをインポートして固定カメラ・簡易陰影で表示する
最小Viewerです（テクスチャ・マテリアル・シーングラフ・カメラ操作は未実装。詳細:
[Docs/Decisions/ADR-0004-Gltf-Model-Viewer-Vertical-Slice.md](Docs/Decisions/ADR-0004-Gltf-Model-Viewer-Vertical-Slice.md)）。

同梱サンプル（Khronos公式のBox.glb）を表示:
```bash
dotnet run --project tools/CSharpGameEngine.Extension -- --model Samples/Box.glb
```

自動検証（非表示ウィンドウ、WARPアダプタ、Debug Layer有効、3フレーム描画後にReadback検証、成功時0終了）:
```bash
dotnet run --project tools/CSharpGameEngine.Extension -- --model Samples/Box.glb --hidden --frames 3 --warp --debug-layer --verify
```

Hardware Adapterでの検証:
```bash
dotnet run --project tools/CSharpGameEngine.Extension -- --model Samples/Box.glb --hidden --frames 3 --debug-layer --verify
```

自分で購入・ダウンロードしたFabアセット（glTF/GLB形式）を表示する場合は、
`UserEcsProject/Assets/Fab/`配下に配置し、`--model`でそのパスを指定してください。
```bash
dotnet run --project tools/CSharpGameEngine.Extension -- --model UserEcsProject/Assets/Fab/<アセット名>/<ファイル名>.gltf
```

Viewerのオプションは`triangle`コマンドと同様（`--frames` / `--width` / `--height` / `--hidden` /
`--warp` / `--debug-layer` / `--gpu-validation` / `--verify`）に加えて、モデルパスを指定する
`--model <path>`（必須）があります。

## 動作要件
- Windows 10 2004 (10.0.19041) 以降、x64
- .NET SDK 10.0.302 以降（`global.json`参照）
- Direct3D 12 Feature Level 11_0 に対応したHardware AdapterまたはWARP
- 追加のWindows SDKやDirectX Shader Compilerの個別インストールは不要（`TerraFX.Interop.Windows`と
  `Microsoft.Direct3D.DXC`のNuGetパッケージから必要なバイナリ・ヘッダが解決されます）
- Debug Layerを使う場合、Windowsの「グラフィックス ツール」オプション機能が無効だと自動的に
  Debug Layerなしで継続します（診断出力にその旨を表示）

## Notes
- Visual Studio拡張の本体は`tools/CSharpGameEngine.VSExtension`（net472、実際に`.vsix`を生成）です。
  `tools/CSharpGameEngine.Extension`はヘッドレスのモデルビューア実行ファイルで、
  `--verify`によるピクセル検証を伴うレンダリング経路のスモークに使います（VSIXではありません）。
- エンジン本体は Visual Studio から独立して実行できる設計方針です。
- `src/`は5プロジェクトです（詳細:
  [Docs/Decisions/ADR-0034-Project-Consolidation.md](Docs/Decisions/ADR-0034-Project-Consolidation.md)）。
  **アセンブリ名と名前空間は意図的に一致していません** — 名前空間は統合前のまま維持されています。

  | プロジェクト | TFM | 含む名前空間 |
  |---|---|---|
  | `CSharpGameEngine.CodeAnalysis` | netstandard2.0 | (Roslyn Analyzer) |
  | `CSharpGameEngine.Entities` | net10.0 | `CSharpGameEngine.Entities`（共有ソース） |
  | `CSharpGameEngine.Core` | net10.0 | `.Core` / `.Spatial.*` / `.Geometry` |
  | `CSharpGameEngine.Import` | net10.0 | `.Import.Gltf` / `.Import.Fbx` |
  | `CSharpGameEngine.Windows` | net10.0-windows / x64 | `.Platform.Windows` / `.Compiler.Hlsl` / `.Graphics.D3D12` |

- `CSharpGameEngine.Core`はWindows/Direct3D/DXCに依存せず、`PackageReference`も一切持ちません。
  Windows固有の実装は`CSharpGameEngine.Windows`に隔離されています
  （詳細: [Docs/Decisions/ADR-0003-Windows-D3D12-Vertical-Slice.md](Docs/Decisions/ADR-0003-Windows-D3D12-Vertical-Slice.md)）。
- `CSharpGameEngine.Import`（glTF/GLB・FBXインポータ）はWindows/D3D非依存の`net10.0`プロジェクトです
  （詳細: [Docs/Decisions/ADR-0004-Gltf-Model-Viewer-Vertical-Slice.md](Docs/Decisions/ADR-0004-Gltf-Model-Viewer-Vertical-Slice.md)、
  [Docs/Decisions/ADR-0009-Fbx-Model-Import.md](Docs/Decisions/ADR-0009-Fbx-Model-Import.md)）。


## Spatial partitioning

Add `SpatialBounds` beside a fixed-point `Transform` to participate in `World.GlobalSpatialIndex`; query it with `SpatialAabb`, `SpatialSphere`, or `SpatialRay`. Systems needing separate filtering or timing can own an `EntityLooseOctree` and update it explicitly. See `Docs/Specifications/Spatial-Partitioning.md`.
