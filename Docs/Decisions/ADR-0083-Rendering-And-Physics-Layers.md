# ADR-0083 — 描画と物理を適切な層へ置く: L0 は D3D12 の実行だけ、パス構成・光源・深度の方針は L4/L5、物理は Entity を使わない L4 契約と L5 ソルバー
- 状態: Accepted(2026-10-25。ユーザーの依頼:「これらは最も低いレイヤーではないと思う。適切なレイヤーに実装して」。計画はユーザーが承認し、「今回触る描画と物理だけ」を選んだ)
- 関連: ADR-0001(層モデル)、ADR-0034(プロジェクト構成)、ADR-0052(Render Graph と Preview の境界)、ADR-0056(Audio・Input・Platform)、ADR-0063、ADR-0079(プラグイン)、ADR-0080、ADR-0081、ADR-0082、M0115、M0118

## 背景
- 目標の層は、Win32/D3D12/XAudio2 の binding が L0、移植可能な値が L1〜L5、エンジンのサーバーが L4、成果物が L6、静的コンパイラが L7、VS が L8(`Docs/Architecture.md:214,224-227`、`Docs/Specifications/Rendering.md:42-48,65`、ADR-0052)。層はアセンブリでは強制されず、Feature ID 単位で追跡する(`Architecture.md:118-120`)。
- 現状(調べた事実、2026-10-25):
  - `src/CSharpGameEngine.Windows`(文書上は L0)の `Graphics/` は 39 ファイル 14,595 行で、D3D12/DXGI/Win32 の binding は約 866 行(約 6 %)。残りはパス構成・光源・影・GI・空・Lumen・自動露出・ポスト・マテリアル・対数深度などの描画機能である。`StageRenderer.RenderFrame` がパスを固定の順に記録し、Forward+ のタイル振り分けも `StageRenderer` の中にある。
  - 移植可能な CPU アルゴリズムと値型が L0 のアセンブリ・名前空間 `CSharpGameEngine.Graphics.D3D12` にある(`PhysicalLighting`、`DirectionalShadowCascades`、`EmissivePatchBuilder`、`EnvironmentLighting`、`StageRenderInputs`、`TypedLightBackend`、`Camera`)。L5 の `Runtime.Windows` が 8 ファイルでその名前空間を `using` している。
  - `src/CSharpGameEngine.Rendering`(net10.0、TerraFX なし)に `RenderGraph`(宣言のみ、実行器なし)・`RenderQualityProfile`・`RenderWorldSnapshot`(可視性。実行時の経路に無い)・`LightSubmissions`・`RadialDepth` がある。文書上の層は L1・L4・L5 で揺れている。
  - `RenderServer`(`src/CSharpGameEngine.Windows/Runtime/RenderServer.cs`)は具象の `StageRenderer` を持つ facade で、`D3D12Exception` と DXGI の HRESULT(`0x887A0005/06/07`)を直接扱う。公開 API に L0 の型(`Graphics.D3D12.Camera`、`D3D12DebugMessage`、`DXGI_FORMAT` 付きの `PixelReadbackResult`、`nint Hwnd`、`Stage*CreateInfo`)が出ている。`Rendering.md:44` の「D3D12 types do not cross L0」に反する。
  - シーンの Component `Camera`・`PrimitiveMesh` は L0 のアセンブリにあるが、シーン JSON は Component の完全修飾名(`CSharpGameEngine.Runtime.Windows.Camera`)を正本にする。
  - 物理: L4 の契約(`PhysicsContracts`・`PhysicsDomainRegistry`・`IPhysicsHost`)と L5 のソルバーが同じ共有プロジェクト `Runtime.Physics` にあり、契約は `Entity`(L3)を使う(`PhysicsContracts.cs` で 24 回、`DeterministicPhysicsWorld.cs` で 34 回)。ECS なしでコンパイルできるのは 4 ファイルだけ(`tools/CSharpGameEngine.Physics.Cli/CSharpGameEngine.Physics.Cli.csproj:15-19`)。
  - 層の試験 `EngineLayerSpec` は `Runtime.Physics` と `Runtime.Character` 直下の `.cs` だけを正規表現で検査する(`TopDirectoryOnly`)。

## 決定

### §1 描画
1. **L0(`CSharpGameEngine.Windows`)は D3D12 の実行だけにする。** デバイス、swap chain、ヒープ、ルートシグネチャ(レジスターは L6 の ABI に従う)、`StagePipelineDesc` から PSO を作る、計画のテクスチャと遷移バリア、アップロードリング、記述子、フェンス、Present、readback、デバイス喪失の判定、機能実行器(大気・GI・AO・自動露出・GDF・Lumen・Skinning)の記録、シェーダー成果物のホットリロード。カメラ・光源・物体の状態、パスの順、深度の方針は持たない。
2. **移植可能な描画は既存の `src/CSharpGameEngine.Rendering`(net10.0)に集める。** 新しい csproj は作らない(slnx・SDK の梱包・Game.props の変更が増え、値型と Server の置き場が二つに割れるだけで得るものが無い)。参照に `CSharpGameEngine.Core` を足す。

   | 層 | 名前空間 | 中身 |
   |---|---|---|
   | L4 | `CSharpGameEngine.Rendering` | 値型(`PixelColor`、`PixelReadbackResult`、`Stage*CreateInfo`、`FogParams`、`EnvironmentLighting` の record 群、`EmissivePatch*`、光源の提出値)、`RadialDepth` |
   | L4 | `.Pipeline` | `StagePipelinePlanner`・`StageLightPlanner`・`StageShadowPlanner`・`StageFramePlanner`・`ClusterLightGrid`・`ForwardObjectLightAssignment`・`PhysicalLighting`・`DirectionalShadowCascades`・配置の構造体 `StageGpuLayouts` |
   | L4(ポート) | `.Backend` | `IStageFrameExecutor`・`IStageRenderBackend`・`IStageRenderBackendFactory`・`RenderBackendFailureException`(`RecoveryReason`)・`RenderDeviceMessage` |
   | L4 | `.Server` | `RenderServer`・`RenderFrameState` |
   | L5 | `.View` | `RenderCamera`(旧 `Graphics.D3D12.Camera`)・`RenderViewPolicy` |

3. **依存性逆転**: L0 が `Rendering.dll` を参照するのは、L4 が持つポートとポートが運ぶ値型を実装・消費するためだけで、計画の関数(Planner や Policy)は呼ばない。`Rendering.dll` は `Windows.dll` を参照しない(テストで固定)。`StageRenderer` は `IStageRenderBackend` を実装し、`Execute(StagePipelinePlan, StageFrameData, ...)` で計画を実行する。DXGI の HRESULT を回復の理由に写すのは L0。
4. **フレーム計画は二段**: `StagePipelinePlan`(キャッシュ。キーは `RenderPath`・サイズ・表示モード・選択・機能フラグ・プレビュー数。パスの並びは `StagePass` の列で、既存の `RenderGraphBuilder` で宣言して検証する)と、`StageFrameData`(毎フレーム。型付きの配列と件数で、GPU へ行く要素は unmanaged 構造体だけ。割り当て 0)。PSO のキー `StagePipelineDesc`(頂点・ピクセルのエントリー名、入力レイアウト、塗り、カリング、深度・ステンシル、ブレンド、トポロジ、色の形式、深度の形式)は値で比べ、L0 の PSO キャッシュのキーにする。`RenderQualityProfile` の宣言だけのグラフと `RenderServer.DeclaredQualityPlan` は、実際に実行する `CurrentPipeline` で置き換えて消す。
5. **定数の配置の正本は L4 の `StageGpuLayouts`**(`StageFrameConstants`・`StageObjectConstants`、`Sequential`、`[InlineArray]`)。L6 の ABI にはハッシュ文字列だけを置く。HLSL との一致は DXC の反射(`HlslCompiler` に `DXC_OUT_REFLECTION` を返す口を足す)で検査し、`Lumen.hlsl` の FrameCB の写しも同じ構造体と照合する。L0 の `Frame*Offset`/`Object*Offset` は消す。
6. **`RenderServer` と `RenderFrameState` は L4 へ移す。** L0 に `D3D12RenderServer.Create`(引数は今と同じ)を置く。`SetView(in RenderView)` が `SetCamera`・`SetViewProjection` を置き換え、`SetCamera(in RenderCamera)` は L5 の拡張メソッドとして残す。公開 API から L0 の型を無くす(`DebugMessages` は `RenderDeviceMessage`、`PixelReadbackResult.Format` は `RenderPixelFormat`)。デバイス喪失の復旧は、持続状態を L4 が持つので、新しい世代へ同じ計画を `Execute` するだけになり、`RenderFrameState.Replay(StageRenderer)` は不要になる。
7. **深度の方針(透視なら対数、正射影なら線形、観察)は L5 の `RenderViewPolicy` だけが決める。** L4 は符号化と検証(`RenderViewDepth`)、L0 は渡されたパケットを書くだけ。
8. **シーンの Component `Camera`・`PrimitiveMesh`・`PrimitiveType` は、名前空間 `CSharpGameEngine.Runtime.Windows` のまま Runtime.Windows の共有ソースへ移す。** シーン JSON の完全修飾名は変わらない(生成器はゲームのコンパイル内で名前で解決する)。
9. **今回は触らないもの**(決定 4 の「触る所だけ」): `AreaLightLtc`、`LumenSurfaceCacheLayout`、`ModelPixelVerifier`・`TrianglePixelVerifier`・`*RunOptions`(L0 のアセンブリに残す)。入力・音声サーバー、実行時の glTF 取り込みと WIC のデコード、`Runtime.Windows` の分割、`Rendering` 以外の文書上の層の不整合(F-L4-016〜019 の Layer 行「L0 — 描画」など)は範囲外で、台帳に記録する。
10. **RenderGraph の別名**(`RenderGraph.cs` の alias slot): フレームをまたいで使う Lumen の履歴と露出の状態は Import にする。L0 は別名を使わず、計画のテクスチャは Version ごとに専用の資源を持つ(別名による資源の共有は今回やらない)。

### §2 物理
1. **L4 の契約は Entity を使わない**: `src/CSharpGameEngine.Physics`(`PhysicsBodyId`、Ray/Hit/Contact/Command/Event、`IReadOnlyPhysicsWorld`/`IPhysicsWorld`、`PhysicsLimits`、`PhysicsWorldSettings` など。Numerics・Contracts・Core だけを参照)。`PhysicsBodyId` は既存の `PhysicsRayHit.StableId(ulong)` を包む型。ECS の Component `PhysicsStableId` の名前は変えない。
2. **L5 のソルバー**: `src/CSharpGameEngine.Physics.Deterministic`(`DeterministicPhysicsWorld`・`PhysicsNarrowPhase`・`DeterministicAabbTree`・`PhysicsKernelScale`)。**L6 の成果物**: `src/CSharpGameEngine.Physics.Artifacts`(`PhysicsShapeArtifact`・`PhysicsCooking`・`PrefractureCooking`)。名前空間はどれも `CSharpGameEngine.Runtime.Physics` のまま(`using` とシーン JSON の型名を変えない)。
3. **ECS への接続は既存の共有プロジェクト `Runtime.Physics`(L5 アダプター)に残す**(`PhysicsStableId`・`PhysicsRotation`・`Destructible`・ドメインの登録とドメイン・System・`StaticMeshCollider` と Loader・`PhysicsBodyPlacement`・`PhysicsDebugHub`)。`PhysicsCache` は `Runtime.Simulation` の `SimulationCacheHeader` を使うのでアダプターに残し、`Runtime.Simulation` は移さない(決定 4)。アダプターは `Runtime.Windows` の `RuntimeWorldTransform.TryResolveWorld` に依存している。これを許す(L5 から L5 への依存で、`Runtime.Windows` の名前空間は `Entities` の共有ソースにある `FixedTransform` と同じ互換のために残っている)ことを `EngineLayerSpec` に明記する。禁止にする場合は `TryResolveWorld` を `Entities` 側へ移す。
4. `SceneComponentOrderAttribute`/`SceneComponentIconAttribute` を Contracts へ移す(名前空間はそのまま)。
5. `CharacterMotor`・`VehicleMotor`・`VehicleSystems`・`FootIk` は具象の `DeterministicPhysicsWorld` ではなく `IReadOnlyPhysicsWorld`(`CollectContacts`・`TryGetMaterial` を契約に足す)か `IPhysicsWorld` を取る。`Physics.Cli` はリンクしていた 4 ファイルをやめ、ProjectReference にする。物理キャッシュの `SchemaVersion` を 3 にし、古いキャッシュを拒否する。
6. 物理は L4 の Physics Server として切り出さない(`IPhysicsHost` の契約と L5 のソルバーの分離までを今回の範囲とする)。

### §3 層の試験(`EngineLayerSpec` に足す。再帰で調べる)
- 描画: `PortableRenderingDoesNotNameNativeBinding`(`src/CSharpGameEngine.Rendering/**/*.cs` に `TerraFX|CSharpGameEngine\.Graphics\.D3D12|CSharpGameEngine\.Platform\.Windows|\bWin32\b|\bDXGI_|\bD3D12_|\bID3D12|\bIDXGI|\bHWND\b` が無い)、`PortableRenderingReferencesOnlyPortableProjects`(TFM が `net10.0` ちょうど、PackageReference と AllowUnsafeBlocks が無い、ProjectReference が Contracts・Content・Core の中、`CSharpGameEngine.Windows` への `InternalsVisibleTo` が無い)、`RenderingL4DoesNotNameL5View`、`NativeBackendDoesNotPlanFrames`(L0 のソースに `RenderViewPolicy.`・`StagePipelinePlanner.`・`StageLightPlanner.`・`RadialDepthProjection.TryCreate`・`DirectionalShadowCascades.Build`・`PhysicalLightBudget.` が無い)、`NoEngineLibraryReferencesTheNativeBackend`。
- 物理: `PhysicsContractAndSolverDoNotNameTheEcs`、`PhysicsProjectsReferenceOnlyLowerLayers`、`PhysicsSolverHasNoFloatArithmetic`。ポータブル検査の対象に新しい 3 プロジェクトと `plugins/CSharpGameEngine.Vehicles/Runtime` を加え、`TopDirectoryOnly` をやめて再帰で調べる。
- その他: `ShaderArtifactRuntimeBoundaryTests` は `typeof(StageRenderer).Assembly` を検査する形にし、`RenderServer` のアセンブリが TerraFX と `CSharpGameEngine.Windows` を参照しないことも検査する。`FeatureStageCatalogTests.EveryStageInputPathExists`(今は誰も存在を検査していない InputPaths を検査する。`FeatureStageCatalog.cs`・`RenderingReleaseEvidence.cs`・`ChangedStageSelectorTests.cs` のパスの直書きを移動に合わせて直す)。

## 採らなかった案
- 新しい csproj(例 `CSharpGameEngine.Rendering.Pipeline`)を作る: 上のとおり。
- `RenderServer` を L0 に残す: 再生・ロースター・パス順の呼び出しが L0 に残り、公開 API に D3D12 の型も残るので、「L0 は D3D12 の実行だけ」を満たさない。
- 完全な RHI(汎用のコマンドリストとバインディング)を最初から作る: 今回のパスは種類が閉じているので、パスの種類 `StagePassKind` と PSO のキー `StagePipelineDesc` で足りる。
- エンジン全体(入力・音声サーバー、実行時の glTF/WIC、`Runtime.Windows` の分割)を再配置する: ユーザーの決定で今回の範囲外。
- 物理を L4 の Physics Server として切り出す: 契約とソルバーの分離で十分で、サーバーの切り出しは範囲外。
- 契約を `Entity` のまま L4 に置く: L4 が L3 に依存する逆向きの依存が残る。

## 帰結
- L0 の描画機能の大部分が L4/L5 に移り、L0 は D3D12 の実行になる。`RenderServer` の公開 API から L0 の型が消え、層の試験が描画にも効く。
- 描画の移動は画素を変えない(対数の 7 SHA が不変であることが証拠。M0115 段階 1)。物理の分離は状態を変えない(`PhysicsGoldenStateTests` が不変。M0118)。
- `InternalsVisibleTo`、リリースの証拠のパス、`FeatureStageCatalog` の InputPaths、`GeneratedEditorPreviewBridgeEmitter` の型名が移動に合わせて変わる。
- 文書の `Layer` 行の不整合(F-L4-014〜019、F-L5-041/043 など)は、触った機能の分だけ直し、残りは Conformance に記録する。

## 検証
- §3 の試験。移動の前後で対数の 7 SHA と既存の WARP 画素の期待値を書き換えない。
- `StagePipelinePlannerTests.EveryPipelineNamesAnAbiEntry`(すべてのキーで作った計画のエントリー名が `ShaderArtifactAbi.GetEntries(Stage)` に含まれる)、`StageConstantLayoutReflectionTests`、`StageGpuLayoutTests`。
- TestHost の `frame-plan`(GPU なしでパスの一覧を表示)。
- `PhysicsGoldenStateTests` とテンプレートの実走が M0117 の終わりと同じ値。`tools/CSharpGameEngine.Physics.Cli` が ECS なしでソルバーまでコンパイルする。

## 関連する機能
- F-L0-011(D3D12 render plan executor)、F-L4-021(Entity-free physics contracts)、F-L0-002、F-L4-002、F-L4-004、F-L5-003、F-L5-038
