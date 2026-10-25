# ADR-0080 — Universal model import on the content pipeline

- Status: Proposed
- Date: 2026-10-25

## Context

M0114 introduces the content-backed model import pipeline. Source: the user-specified implementation plan (912 lines). The complete design follows verbatim.

## Decision

Implementation scope is M0114 S0–S5, using local Windows verification and temporary Assimp FBX. The handoff decisions override tentative alternatives in the design below: preserve facing in v1; native shims begin in M0115. M0115 and later are excluded from this implementation.

## Alternatives considered

See the complete decision tables below.

## Consequences

Runtime consumes engine-owned artifacts; source decoding moves to Cook workers. Cutover requires the S0 parity oracle before removing source readers.

## Verification

S0 parity, chirality, worker contract tests, layer checks, Debug solution tests, FeatureRunner stages, four templates on WARP/hardware, and verify-template-package. Unexecuted gates remain unverified.

## Related features

Provisional IDs and milestone scope are listed in section 11 below; availability is checked against the current feature ledger before implementation.

---

# 設計全文(ADR-0080 の元)

## 指摘への対応

検証の方法は次のとおり。`Content.csproj`、`Navigation.plugin.items`、`.gitignore`、`Implementation-Status.md`、`FeatureStageContracts.cs`、`ContentAddressedStore.cs` はリポジトリで直接確かめた。それ以外はレビューの引用と研究ファイルを根拠にした。

| 指摘 | 判定 | 変更内容 / 却下理由 |
|---|---|---|
| C1 `AssetAuthoringService` は L6 の Content にあり、L7 の登録表を引けない | 採用 | `Content.csproj` の ProjectReference は Contracts のみで、指摘は事実。Contracts に `IImporterCatalog` を置き、`AssetAuthoringService.Execute` に注入する。実装は ContentCompiler `serve` が registry から作る。importer の照合は「拡張子由来の固定値との一致」から「カタログの候補かどうか」へ変える。§2 に行を足す。 |
| C2 `CgeCompileContent` が `BeforeCompile` だと Navigation の Cook に間に合わない | 採用 | `Navigation.plugin.items:65` は `BeforeTargets="AssignTargetPaths"` で、指摘どおり。`CgeCompileContent` を `BeforeTargets="AssignTargetPaths"` にし、Navigation には `DependsOnTargets="CgeCompileContent"` を付ける。`_CgeRunNavigationCook` の Inputs に content-index を足す。実行順を §4.6 に図示する。 |
| C3 SDK パッケージ、VSIX 同梱経路の欠落 | 採用(F15 と統合) | §2 に `packaging/` の行を足す。M0114 の完了条件に pack 対象の移動、Cooking/Contracts/Core の除外、`verify-template-package` の必須化、VSIX の `CgeVsixBridge` の置換を入れる。 |
| C4 テクスチャ 16384² が CAS 上限 512 MiB を超える | 採用 | `DefaultMaximumArtifactBytes=512 MiB` を確認した。16384² の RGBA8 は 1 GiB になる。最大辺は 8192 とし、超過は `texture.maxSize` で縮小するか IMP803 にする。IR 内ではデコードせず、デコードは artifact 切り出し時に行う。IR の上限は ir-cache 用に別に決める。 |
| C5 切り出しや envelope 化の失敗が全体を中断する | 採用 | 切り出し、envelope 化、サイズと依存数の検査、P18 は前処理に移す。失敗した資産はまとめてノードから外す。ノードの `CompileAsync` は例外を出さず、確定済みバイト列を返すだけにする。 |
| C6 Feature ID の衝突 | 採用 | `F-L5-046` と `F-L9-010` が既にあることを確認した。L5 は 047/048 に変え、ADR-0080 の着手時に各層の最大番号を再 grep して確定する。 |
| C7 envelope 依存数、診断数、フレーム上限 | 採用 | §10.6 に追記した。診断は同一コードを集約して 256 件以内にする。サブアセット一覧は件数で切り詰め、全体は一時ファイル経由にする。依存数 65,535 を超える Model は中間 Model に分ける。 |
| C8 `.gitignore` の否定パターン | 採用 | `.gitignore:59` が `**/Assets/Fab/` というディレクトリ除外であることを確認した。この形では `.meta` を再包含できない。`**/Assets/Fab/**`、`!**/Assets/Fab/**/`、`!**/Assets/Fab/**/*.meta` の 3 行に置き換える。 |
| C9 `ToolRequired` モードは存在しない | 採用 | `FeatureStageMode` は `Headless` と `Visual` の 2 値だけで、`FeatureStageRunStatus.Blocked` は既にある。モードは `Headless` のままにし、Stage 属性 `RequiredTools` で判定して、ツールが無ければ `Blocked` を返す。 |
| C10 `StableId.FromName` の署名 | 一部採用 | 引用された `src/CSharpGameEngine.Core/StableId.cs` はその場所に見つからず、署名は未検証。ただし第 1 引数が `StableId` なら文字列では呼べないので、名前空間用の定数 `StableId` を 1 つ定義する形に直す。実際のファイルと署名は実装時に確認する。 |
| C11 AssimpNetter の CVE 修正の断定 | 採用 | 断定を外す。ネイティブの assimp の版を実行時に取得し、6.0.2 以上であることを契約テストで確かめる。 |
| C12 bpy スクリプトの GPL とスナップショット | 採用 | `cge_blender_export.py` は GPL-2.0-or-later のヘッダーを付けた別ファイルとし、DLL には埋め込まずデータとして同梱する。THIRD_PARTY_NOTICES に記載する。`NoRawRedistribution`、UE-Only、不明のスナップショットは、doctor でコミットを拒否する。50 MB 超はテクスチャを分けるか、コミットしない運用にする。 |
| F01 1 ファイル複数スキン(blocker) | 採用 | 既定は P07 で「スキン統合」を行う。各メッシュの JOINTS を骨格の和集合の番号に付け替え、IBM が 1e-4 以内で一致することを検証する。不一致は IMP エラーで、`skin.merge=error\|keep` で選ばせる。`keep` のときは、スキンごとにパレットを持つ実行時の拡張(`SkeletalMeshRuntime.Palettes[]`、`SkinOfMesh[]`)を M0116 で入れる。ChiralityRig に 2 スキンと VRoid 形式を足す。 |
| F02 UV の V 軸の向き(blocker) | 採用 | `InterchangeSpace.UvOrigin` を足す。P04b で `v'=1-v` にし、`UvTransform` の offset.y と rotation を直し、tangent.w を反転する。P10 の tangent 生成は P04b の後に置く。 |
| F03 Blender の `export_apply` とモーフ、カメラ、ライト | 採用 | `export_apply=False` を既定にする。shape key の無いオブジェクトだけ、スクリプトが評価済みメッシュで modifier を適用する。`export_cameras`、`export_lights`、`export_import_convert_lighting_mode='SPEC'`、`export_vertex_color='ACTIVE'` も指定する。オプション名は `hasattr` で確かめ、無ければ IMP502 にする。 |
| F04 骨格の関節集合の閉包、名前の安定性 | 採用 | P07 に閉包規則を明文化する。骨格の関節集合は (スキンの joints ∪ アニメーション対象ノード) の祖先を最小共通祖先まで含めたもので、足した関節は `Helper` で IBM は単位行列にする。共通の親が無ければ合成ルートを足す。名前の無い関節は構造名 `<parent>/_<ordinal>` にする。 |
| F05 RestToReference の算出方法 | 採用 | 3 段の優先順位を置く。(1) プロファイルの基準姿勢、(2) 四肢の方向を揃え、ねじれは膝と肘の曲がる平面から決めるヒューリスティック、(3) どちらも使えなければ補正せず IMP121 の警告を出して `anim.space=humanoid` を無効にする。`n_b` は対応のある骨の間の局所回転の積で定義し直す。 |
| F06 カメラ、ライト、Events などの受け口 | 採用 | 項目ごとに行き先を決めた(§7)。カメラとライトは `ContentCompiler convert model-scene` でシーン JSON の子ノードにする。Events と Curves は clip の API を足す。`PropertyTracks` は v1 では捨てて IMP140 にする。Sockets は API を足す。Additive は受け口が無ければエラー。`OriginOffset` は VSIX が配置時に Transform へ書く。 |
| F07 レンダラーの alpha、doubleSided、occlusion | 採用 | `grep` で `AlphaMode`/`DoubleSided` が src に 0 件であることを確かめた。描画契約 `stage-v14` に、モーフと alphaMode/alphaCutoff/doubleSided/occlusion を入れ、M0116 に置く。BeforeWrite に `TextureRepack` を足す。 |
| F08 サブメッシュと頂点数上限 | 採用 | `StageObjectCreateInfo` に `FirstIndex`/`IndexCount` を足し、頂点、スキン、モーフを共有する。P12b で頂点数が上限を超えるメッシュを分割する(CAD は部品ごと)。 |
| F09 CAD の巨大座標と float32 | 採用 | `cge-occt-convert` がメッシュ化の前に、倍精度の平行移動をアセンブリの根に掛ける。移動量はサイドカーに書き、エンジン側は `OriginOffset` にする。`BRepMesh` は `InParallel=false` にし、ラベルの走査順を固定する。入れ子の取り込み結果の Space は、サイドカーで上書きする。 |
| F10 Draco、meshopt、WebP と `KHR_animation_pointer` | 一部採用 | `cge.gltf` に `GltfNormalizer` を足す。GLB を解析して Draco/meshopt をデコードし、通常の bufferView に直し、拡張を外してから SharpGLTF に渡す。`KHR_animation_pointer` の API 対応(1.0.6 に有り、1.0.7 は未確認)は spike で確かめる。WebP は libwebp を shim に足すか IMP202 にする。圧縮の fixture は数 KB の既製バイナリをコミットする。 |
| F11 負スケールの符号が P06 より前に消える | 採用 | `Scale` は並べ替えだけで符号を保つ(`Abs` をやめる)。P06 は行列式の符号で負スケールを検出する。 |
| F12 USD の経路が未確定 | 採用 | [Q]10 に案 (d) を足した。`usd-core` の PyPI wheel を PerImport の変換スクリプトとして使う案で、spike を先に行う。それまで USD は「未確定」とする。 |
| F13 Unity の「プロジェクトに書かない」との矛盾 | 採用 | ブリッジはキャッシュ内に作る最小の Unity プロジェクトで動かす。GUID の閉包の資産だけをコピーする。`cge.unity-anim` の仕様と、Project Converter の対応範囲を ADR-0082 に書く。 |
| F14 検証コーパスの現実性 | 採用 | Maya/Max の形は、ASCII FBX を書くテストコードで T0 に合成する。T1 は無償ツールに限る。ChiralityRig の端ケースを増やす。T2 のうち 8 本をハッシュ固定の必須サブセットにする。 |
| F15 パッケージングと配布が構成図に無い | 採用(C3 と統合) | C3 と同じ変更で §2 に行を足す。 |
| F16 ビルド順序と Navigation がシーン単位であること | 採用(C2 と統合) | 汎用ターゲットで「資産単位の importer」と「シーン単位の cooker」を区別する。後者は `CookRequest.ContentIndexPath` で Mesh artifact を引く。 |
| F17 失敗したソースの前回状態 | 採用 | 開発用マニフェストと content-index に、失敗した資産の前回の参照を書き足す。GC の Quarantine は失敗した資産の State を保つ。失敗ソースを指す Roots は `GetClosure` の前に外す。 |
| F18 Feature ID の衝突と基準値 | 採用(C6 と統合) | S0 の等価性の神託は M0112 のコミット後に取る。テンプレートの `.glb` は 94 本から増える(未追跡の 3 本)ので件数を固定せず、実行時に数える。 |
| F19 ufbx を float でビルドする判断 | 採用 | ufbx は既定の倍精度でビルドし、shim の ABI で float に落とす。oracle は ChiralityRig の解析値を主とし、`ufbx_evaluate_scene` は補助とする。 |
| F20 Assimp 経由のボーンとマテリアル | 採用 | `fbx.phong` を `legacy.phong` に一般化する。`cge.assimp` の節に、ボーン→骨格、`offsetMatrix`→IBM、`aiAnimation`、`aiAnimMesh` の対応を書く。 |
| F21 ライト強度の単位 | 採用 | 形式ごとの換算表と `light.intensityScale` を仕様に書く。 |
| F22 MikkTSpace の入手 | 採用 | `mikktspace.c`(zlib)を自前で移植する作業を M0115 に明記する。NormalTangentMirrorTest で参照出力と照合する。 |
| F23 BVH、PLY、STL、3MF の細部 | 採用 | `bvh.upAxis` を足す。3DGS の PLY は Sniff で拒否する。STL は頂点を溶接する。3MF の components は共有メッシュにする。BVH は M0116 まで束縛できないとリリースノートに書く。 |
| F24 DCC ブリッジの細部 | 採用 | 結果 JSON に `textures[]` を足し、`.cgelink` に `clips[]` を持たせる。`FBXExportSplitAnimationIntoTakes` を使う。Godot の `.import` 用に Variant の最小パーサーを実装する。 |
| F25 暫定 `cge.fbx` が捨て作業になる、層の記述 | 一部採用 | ufbx の M0114 への前倒しは [Q]2 の結果に依存する。(a)/(b) なら前倒しし、(c) なら暫定のままにする。L6 の素データ型を L4/L5 が参照してよい、という規則は ADR-0080 に明記する。 |

---

## 最終設計

# ユニバーサル 3D インポート基盤 設計書(ADR-0080 案)

凡例: **[F]** はリポジトリで確かめた事実、**[P]** は提案、**[Q]** は §12.2 でユーザーに決めてもらう点。

### 0. 中核の決定と衝突の解決

**決定**
- **D1** 新しいプラグインの種類は作らない。importer は ADR-0079 の Cook モジュールに置く兄弟の契約で、動かすのは Cook.Cli のワーカーだけ。
- **D2** IR と artifact の payload は分ける。
  - IR(名前空間 `CSharpGameEngine.Interchange`)は既存の `CSharpGameEngine.Cooking` に置く。
  - 実行時の codec は `CSharpGameEngine.Content/Models` に置く。
  - 新しい csproj は `src/` に作らない(ADR-0034)。
- **D3** `IncrementalContentCompiler` は改造しない。取り込みはコンパイラの手前で行い、ソース 1 つをサブアセットごとのノードに展開する。
- **D4** 正規化はエンジンが持つ。1 つの実装をワーカー内で動かし、オーケストレーターは最後の不変条件(P18)を再検証する。
- **D5** プロセスの規則は 1 つ。ソースのデコード(SDK、ネイティブ shim、DCC)は、すべて Cook.Cli のワーカーで行う。devenv、MSBuild ノード、ゲーム、Preview Host、他の CLI はプロセス内で取り込まない。
- **D6** SubAssetId は名前から決定的に作る。シーン JSON はパスの文字列のままで、後ろに `:選択子` を付けられる。
- **D7** ライセンスは IR から License artifact へ運ぶ。UE-Only と Unity Companion は取り込みの時点で止め、不明なものは `package` で止める。
- **D8** 即時移行を守る。各マイルストーンが、置き換えた旧経路を同じ変更で消す。番号は **M0114 から**([Q]1)。
- **D9(追加)** 失敗の分離。取り込み、切り出し、envelope 化、検証はノード作成の前に終える。ノードの `CompileAsync` は例外を出さない。
- **D10(追加)** `.meta` の importer 検証は、Contracts の `IImporterCatalog` を通す。L6 から L7 の登録表は直接引かない。

**衝突の解決**

| # | 衝突 | 採用 | 理由 |
|---|---|---|---|
| C1 | マイルストーン番号 | M0114〜M0122 | [F] ADR-0078 が M0112/M0113 を予約済み |
| C2 | IR と artifact | 分ける | 版を独立させる。新 csproj は不要 |
| C3 | コンパイラ統合 | 改造しない | 検証済みの部品を壊さない |
| C4 | L7 CLI のプロセス内例外、facade | どちらも無し | 方式を 1 つにする。facade は互換ブリッジで禁止 |
| C5 | パスの置き場 | 純粋計算は Cooking、第三者コードが要るものは Import の processor | Cooking にパッケージ依存を入れない |
| C6 | 座標 | 閉じた `SourceConvention` と `ConventionConversion<TS,TT>` | static abstract の規則に合う |
| C7 | 拡張点 | `IAssetImporter`、`IModelProcessor`(3 段階)、データプロファイル 2 種 | 概念を 2 つに抑える |
| C8 | VRM | `plugins/CSharpGameEngine.Vrm` | エンジン自身がプラグイン機構を使う |
| C9 | 選択子 | `:` | `#` は Windows のファイル名に使える |
| C10 | リターゲット | 実行時に結び付けるとき | 資産をまたぐ取り込み依存を作らない |
| C11 | 向き | `process.facing=auto` | VRM0 だけ +F になる問題を消す |
| C12 | `.cgeplugin` の `Importers` 欄 | 作らない | 記述子の二重管理を避ける |
| C13 | 第三者の隔離 | 最低 Dedicated | 最も安全な側に倒す |
| C14(追加) | 複数スキン | 取り込み時に統合(既定) | ランタイムを変えずに済む |

---

### 1. 目標と非目標

**目標**
1. Unity、UE5、VRoid、Maya、3ds Max、CAD、Blender、Fab、Godot と、交換形式(glTF/GLB、FBX、USD/USDZ、OBJ、BVH、Collada、PLY/STL/3MF、VRM 0.x/1.0、VRMA、Mixamo FBX)を取り込む。
2. 対象は、階層、メッシュ、PBR マテリアル、骨格とスキン、アニメーション、ヒューマノイド、VRM の揺れものと拘束と表情、カメラ、ライト、ライセンス。
3. すべてを 1 つの IR に集め、エンジン空間(`UnityCoordinateConvention`)への正規化は 1 か所で 1 回だけ行う。
4. 実行時は artifact だけを読む。ゲームから SharpGLTF、Assimp、Cooking を外す。
5. 第三者が Cook モジュールだけで、形式、ブリッジ、processor、プロファイルを足せる。
6. 増分で再取り込みでき、決定的で、`--verify` で確かめられる。
7. 出典とライセンスを追跡し、配布の前に検査する。

**非目標**
- 次の独自形式の直接パース: `.uasset`/`.umap`、`.blend`、`.max`、`.ma`/`.mb`、`.vroid`、Unity の `Library/`、Humanoid マッスルの `.anim`、Godot のバイナリ `.scn`/`.res`、ネイティブ CAD。CUE4Parse と UAssetAPI は使わない。
- DCC への書き戻し(テスト用 glTF writer を除く)。
- 出荷ゲームの実行中の取り込み。
- Fab と Asset Store の自動取得、ログイン、スクレイピング(ADR-0011)。
- DCC を必須にすること。
- M0107 の鏡像を importer で直すこと。
- Mecanim、ABP、AnimationTree の挙動変換。
- Unity のマッスル空間を Unity なしで評価すること。
- Alembic、DQS の描画、IFC/DWG/3DM。

---

### 2. 層ごとの構成

```text
devenv.exe(VSIX, net472, L8) ─ named pipe(Contracts の DTO) ─► ContentCompiler serve(net10, L7)
MSBuild ─ Exec ─► ContentCompiler compile
   オーケストレーター: registry / import-catalog / ir-cache / IncrementalContentCompiler(改造なし)/ CAS / content-index
   (サードパーティ DLL は読まない)
     └ pipe + 一時ファイル(.cgex)─► Cook.Cli import-worker ×N
          共有 ALC: Cooking, Contracts, Core
          収集可能 ALC: 組み込み Import モジュール、プラグイン Cook DLL
          └ Job Object つき spawn ─► blender / UnrealEditor-Cmd / Unity / godot / mayapy / 3dsmaxbatch / cge-occt-convert / usd 変換
Game.exe / EditorPreview.Host ─► RuntimeAssetStore + RuntimeAssetCodec<T>(Content/Models)。ソースは読まない
```

**MSBuild の実行順**(C2、F16)
1. Cook モジュールのビルド(`_CgeBuildCookModules`)
2. `CgeCompileContent`(`BeforeTargets="AssignTargetPaths"`。生成器用の AdditionalFiles と出力の None 項目は同じターゲット内で足す)
3. 各プラグインの Cook(`CgeCookNavigation` は `DependsOnTargets="CgeCompileContent"`、Inputs に content-index を含める)
4. `AssignTargetPaths`
5. `CoreCompile`

| コンポーネント | 置き場所 | 層 | TFM | プロセス | 区分 |
|---|---|---|---|---|---|
| `SubAssetIdentity`、`AssetSelector`、`HumanoidBone`、`IImporterCatalog`、VSIX の DTO | `src/CSharpGameEngine.Contracts` | L2 | netstandard2.0 | 全て | 追加 |
| `AssetAuthoringService`(カタログ注入) | `src/CSharpGameEngine.Content` | L6 | net10 | ContentCompiler serve | 変更(固定 switch を削除) |
| `ConventionConversion<TS,TT>` | `src/CSharpGameEngine.Core/Spatial` | L1 | net10 | 全て | 追加。`GltfAxisConversion`/`FbxAxisConversion` は削除 |
| IR、codec、検証器、契約、`ImportDiagnostic`、エンジンのパス、`PluginModuleLoadContext` | `src/CSharpGameEngine.Cooking` | L7 | net10 | ワーカー、オーケストレーター(codec と検証器のみ) | 変更 |
| 組み込み reader と processor(glTF/VRM 土台、FBX/ufbx、OBJ/STL/PLY/BVH/3MF、Unity YAML、Godot `.tres`、Assimp 予備、TextureCook/Repack、MeshOptimize) | `src/CSharpGameEngine.Import`(Cook モジュール化) | L7 | net10 | ワーカー ALC | 変更 |
| ネイティブ shim(ufbx を倍精度で、meshopt、Draco、basisu、必要なら libwebp) | `native/CSharpGameEngine.Import.Native`(CMake) | L7 | win-x64 | ワーカー | 追加 |
| payload codec(Mesh/Model/Skeleton/AnimationClip/License)、kind 8〜12 | `src/CSharpGameEngine.Content/Models`、`ContentArtifact.cs` | L6 | net10 | ゲーム、Preview、オーケストレーター | 追加 |
| Material v2 | `Rendering/MaterialArtifacts.cs` | L4 | net10 | 同上 | 変更 |
| ワーカー | `tools/CSharpGameEngine.Cook.Cli` | L7 | net10 | 別プロセス | 変更 |
| オーケストレーター | `tools/CSharpGameEngine.ContentCompiler` | L7 | net10 | MSBuild、VSIX 常駐 | 変更。Import.Gltf.Cli、Import.Fbx.Cli、Import.AssetAuthoring.Cli を置換して削除 |
| VRM | `plugins/CSharpGameEngine.Vrm/{Cook,Runtime}` | L7/L5 | net10 / shproj | ワーカー、ゲーム | 追加 |
| DCC ブリッジ | `plugins/CSharpGameEngine.DccBridges/Cook`(`Scripts/<tool>/`) | L7 | net10 | ワーカーから外部ツール | 追加 |
| USD、CAD | `plugins/CSharpGameEngine.Usd/Cook`、`plugins/CSharpGameEngine.Cad/Cook`、`native/CSharpGameEngine.Occt.Convert` | L7 | net10 / C++ | 変換プロセス | 追加 |
| 実行時 | `Runtime.Animation`、`Runtime.Windows`、`Runtime.Physics`、Rendering | L4/L5 | — | ゲーム | 変更 |
| 生成器 | `CSharpGameEngine.CodeAnalysis` | L7 | netstandard2.0 | csc | 変更 |
| SDK パッケージ(追加) | `packaging/CSharpGameEngine.Sdk` | — | — | NuGet | 変更。下記参照 |
| MSBuild | `build/CSharpGameEngine.Content.targets`(新規)、`Game.props`、`Game.targets` | — | — | MSBuild | 追加と変更 |
| VSIX | `tools/CSharpGameEngine.VSExtension` | L8 | net472 | devenv | 変更 |

- **SDK パッケージ**(C3、F15)
  - Import を `lib/` から `tools/Cook/`(モジュールフォルダ。shim と `runtimes/win-x64/native` を含む)へ移す。
  - AssimpNet/SharpGLTF の PackageReference をそこへ移す。
  - ContentCompiler(`tools/ContentCompiler/`)と `Content.targets` を pack 対象に加える。
  - Cook モジュールの pack 時の除外を Cooking/Contracts/Core の 3 つにする。
  - VSIX の `CgeVsixBridge` は、ContentCompiler、Cook.Cli、組み込みモジュールの publish に置き換える。
  - `verify-template-package` を M0114 の必須ゲートにする。
- 新しい csproj/shproj/projitems は、同じ変更で `CSharpGameEngine.slnx` に登録する。対象は Vrm.Cook、Vrm.Runtime、DccBridges.Cook、Usd.Cook、Cad.Cook、各テスト、ネイティブ配布用の csproj。

---

### 3. 正規の IR(Interchange model)

#### 3.1 原則
- **空間は状態として持つ。** reader は `Space.State=Source` で出す。P04〜P05 が `Engine` にし、artifact の writer は `Engine` だけを受け付ける。
- **階層は平坦にしない。** `ParentIndex` を持ち、親が子より先に並ぶ(P00 で検証)。関節はノードで、レストはノードの Local。
- **数値**は float(System.Numerics)と `Int128`(Q64.64 生値。CAD の原点のみ)。double は `[ExternalNumericAdapter]` を付けたメソッドの中だけで使う(NUM001)。
- **未知の拡張も捨てない。** `InterchangeRawExtension` に生 JSON を入れ、`SourceMap` で番号を対応させる。
- **IR は自己完結にする。** ただし画像は圧縮のまま(`EncodedBytes`)保ち、IR 内でデコードしない(C4)。
- **正準エンコード**: LE、NaN/Inf は拒否、-0 は +0、レストの四元数は w≥0、キーの半球は連続にそろえる。

#### 3.2 型のスケッチ
`CSharpGameEngine.Cooking` / 名前空間 `CSharpGameEngine.Interchange`。**【変更】** の印は、レビュー対応で変えた箇所。

```csharp
public enum InterchangeSpaceState:byte { Source,Engine }
public enum SourceConvention:byte { Unity,Gltf,Fbx,Unreal,BlenderWorld }
public enum AssetFacing:byte { Unknown,PlusForward,MinusForward,PlusRight,MinusRight }
public enum ReferencePoseKind:byte { Unknown,TPose,APose,Other }
public enum UvOrigin:byte { TopLeft,BottomLeft }                       // 【変更】F02
public readonly record struct OriginOffset(Int128 X,Int128 Y,Int128 Z);
public sealed record InterchangeSpace(InterchangeSpaceState State,SourceConvention Convention,float MetersPerUnit,AssetFacing Facing,ReferencePoseKind Pose,UvOrigin UvOrigin,OriginOffset Origin,string Evidence);
public sealed record InterchangeDocument(int SchemaVersion,InterchangeSpace Space,
  ImmutableArray<InterchangeNode> Nodes,ImmutableArray<InterchangeMesh> Meshes,ImmutableArray<InterchangeMaterial> Materials,
  ImmutableArray<InterchangeTexture> Textures,ImmutableArray<InterchangeImage> Images,ImmutableArray<InterchangeSkeleton> Skeletons,
  ImmutableArray<InterchangeSkin> Skins,ImmutableArray<InterchangeAnimationClip> Clips,ImmutableArray<InterchangeCamera> Cameras,
  ImmutableArray<InterchangeLight> Lights,InterchangeRig Rig,InterchangeProvenance Provenance,
  ImmutableArray<InterchangeBlock> Blocks,ImmutableArray<InterchangeRawExtension> RawExtensions,
  InterchangeSourceMap SourceMap,ImmutableArray<InterchangeRuntimeBlock> RuntimeBlocks);

// 値と拡張
public enum InterchangeValueKind:byte { Bool,Int,Float,Float2,Float3,Float4,Quaternion,Text,Texture }
public readonly record struct InterchangeValue(InterchangeValueKind Kind,Vector4 Number,string? Text,InterchangeTextureRef? Texture);
public readonly record struct InterchangeProperty(string Key,InterchangeValue Value);
public sealed record InterchangeBlock(string Schema,ImmutableSortedDictionary<string,InterchangeValue> Values); // "khr.materials_clearcoat@1","vrm.mtoon@1","cge.world_grid@1"
public enum InterchangeScope:byte { Document,Node,Mesh,Primitive,Material,Texture,Image,Skin,Animation,Camera,Light }
public sealed record InterchangeRawExtension(string Name,InterchangeScope Scope,int SourceIndex,ImmutableArray<byte> JsonUtf8);
public sealed record InterchangeSourceMap(ImmutableArray<int> NodeOfSourceNode,ImmutableArray<int> MeshOfSourceMesh,ImmutableArray<int> MaterialOfSourceMaterial,ImmutableArray<int> ImageOfSourceImage);
public sealed record InterchangeRuntimeBlock(string Schema,ImmutableArray<byte> Payload);

// シーングラフ
[Flags] public enum InterchangeNodeFlags:ushort { None=0,Joint=1,Hidden=2,Helper=4,Socket=8,LodGroup=16,Collision=32 }
public enum InheritMode:byte { Normal,SegmentScaleCompensate,IgnoreParentScale }
public readonly record struct InterchangeTransform(Vector3 Translation,Quaternion Rotation,Vector3 Scale,InheritMode Inherit);
public sealed record InterchangeNode(string Name,int ParentIndex,InterchangeTransform Local,int MeshIndex,
  ImmutableArray<int> MaterialSlots,int SkinIndex,int CameraIndex,int LightIndex,InterchangeNodeFlags Flags,
  ImmutableArray<InterchangeProperty> Properties,ImmutableArray<InterchangeBlock> Blocks);

// メッシュ(glTF の primitive 群は連結してサブメッシュにする)
public readonly record struct InterchangeSubmesh(int FirstIndex,int IndexCount,int MaterialSlot);
public sealed record InterchangeInfluences(int PerVertex,ImmutableArray<ushort> Joints,ImmutableArray<float> Weights); // Source 1..32、Engine は 8
public sealed record InterchangeVertexStreams(int Count,ImmutableArray<Vector3> Positions,ImmutableArray<Vector3> Normals,
  ImmutableArray<Vector4> Tangents,ImmutableArray<ImmutableArray<Vector2>> TexCoords,ImmutableArray<ImmutableArray<Vector4>> Colors,
  InterchangeInfluences? Influences,ImmutableArray<InterchangeCustomStream> Custom);
public sealed record InterchangeMorphTarget(string Name,ImmutableArray<int> SparseVertices,ImmutableArray<Vector3> PositionDeltas,
  ImmutableArray<Vector3> NormalDeltas,ImmutableArray<Vector3> TangentDeltas,float DefaultWeight,ImmutableArray<InterchangeMorphInBetween> InBetweens);
public sealed record InterchangeMesh(string Name,InterchangeVertexStreams Vertices,ImmutableArray<uint> Indices,
  ImmutableArray<InterchangeSubmesh> Submeshes,ImmutableArray<int> DefaultMaterials,ImmutableArray<InterchangeMorphTarget> MorphTargets,InterchangeBounds Bounds);

// マテリアル(Pbr、UvTransform、Texture、Image、Sampler は旧設計どおり。ここでは差分のみ)
public sealed record InterchangeSourceMaterial(string ShaderName,ImmutableArray<InterchangeProperty> Parameters); // legacy.phong / unity.urp-lit / godot.standard が PBR に写す
// InterchangeImage は EncodedBytes を圧縮のまま保つ。デコードは artifact 切り出し時(texture.maxSize 以下)

// 骨格とスキン【変更】F04, F01
[Flags] public enum InterchangeJointFlags:byte { None=0,Deform=1,Leaf=2,Helper=4,IkTarget=8,Twist=16,Synthetic=32 }
public sealed record InterchangeSkeleton(string Name,ImmutableArray<int> JointNodes,ImmutableArray<InterchangeJointFlags> JointFlags,int RootParentNode);
// JointNodes は (スキンの joints ∪ アニメ対象ノード) の祖先閉包(最小共通祖先まで)。共通の親が無ければ Synthetic ルート。親が先
public sealed record InterchangeSkin(string Name,int SkeletonIndex,ImmutableArray<int> JointMap,ImmutableArray<Matrix4x4> InverseBindMatrices,Matrix4x4 BindShape,InterchangeSkinningMethod Method);
// 既定(skin.merge=error)では P07 後に 1 モデル 1 Skin。keep のときだけ複数

// アニメーション(Keys、Transform/MorphWeight/Property/Curve/Event/RootMotion は旧設計どおり)
public sealed record InterchangeAnimationClip(string Name,float StartSeconds,float EndSeconds,float SourceFramesPerSecond,InterchangeClipFlags Flags,
  ImmutableArray<InterchangeTransformTrack> Transforms,ImmutableArray<InterchangeMorphWeightTrack> MorphWeights,
  ImmutableArray<InterchangePropertyTrack> Properties,ImmutableArray<InterchangeCurve> Curves,
  ImmutableArray<InterchangeAnimationEvent> Events,InterchangeRootMotion? RootMotion);
// Properties は v1 では artifact に出さず IMP140(F06)

// アバターと Rig
public sealed record InterchangeAvatar(int SkeletonIndex,int RootNode,ImmutableArray<InterchangeHumanBone> Bones,ReferencePoseKind SourcePose,
  ImmutableArray<Quaternion> RestToReference,float HipsHeightMeters,string MappingSource,ImmutableArray<InterchangeProperty> SourceLimits);
// InterchangeRig(Avatars/Expressions/LookAt/FirstPerson/SecondaryMotion)、Camera、Light、License、Provenance は旧設計どおり
```

Contracts(netstandard2.0)に置くもの:
- `HumanoidBone`(VRM 1.0 の 55 本)と `HumanoidBones.Required`(必須 15 本)。
- `SubAssetKind { Mesh,Material,Texture,Skeleton,Clip,DistanceField,License }`。
- `SubAssetIdentity.Derive(Kind, CanonicalName)`(`"cge.subasset/1\n<kind>\n<NFC 名>"` を SubAssetId の正準バイトにする)。
- `AssetSelector(Path,Kind,Name)`。
- `IImporterCatalog { IReadOnlyList<ImporterCandidateDto> Candidates(string Extension); bool IsValid(string Identity,string Extension); }`【追加】D10。

#### 3.3 artifact との対応

| IR の部分 | Kind | AssetKey | 依存 | payload |
|---|---|---|---|---|
| ノード木、スキン、ソケット、Rig、RuntimeBlocks、原点 | **Model=8** | `(AssetId,null)` | Mesh*、Material*、Skeleton*、License | `cge-model-v1` |
| メッシュ(ストリーム、サブメッシュ、影響 8×u16 と unorm 重み、疎モーフ) | Mesh=1 | `mesh:<name>` | License | `cge-mesh-v1` |
| マテリアル(alpha、doubleSided、occlusion、UV セット、Block) | Material=3 | `material:<name>` | Texture*、License | `render-material-v2`(v1 の読み手は削除) |
| テクスチャ | Texture=2 | `texture:<name>` | License | RGBA8(最大辺 8192 以下) |
| 骨格とアバター | **Skeleton=9** | `skeleton:<root>` | License | `cge-skeleton-v1` |
| クリップ(Events と Curves を含む) | **AnimationClip=10** | `clip:<name>` | License | `cge-animation-clip-v1` |
| 距離場 | **DistanceField=11** | `sdf:<mesh>` | Mesh | 既存バイト列 |
| ライセンスとクレジット | **License=12** | `license:` | — | `cge-license-v1` |

- カメラとライトは Model に入れない。`ContentCompiler convert model-scene` がシーン JSON の子ノードに展開する(F06)。
- `IsKnownKind` に 8〜12 を足す。

**SubAssetId の導出規則**
1. 正規名はソース名の NFC で、大文字と小文字は区別する。
2. 名前が無いもの:
   - mesh は `@node:<ルートからのパス>`。
   - texture は `@sha256:<先頭 16 桁>`。
   - 関節は `<parent>/_<ordinal>`(F04)。
   - それ以外は `<kind>[n]`。
   - いずれも IMP150 を出す。
3. 重複名は、深さ優先のソース順に `~2`、`~3` を付ける(IMP151)。
4. 分割したメッシュは `name.0`、`name.1`。
5. `.meta` の `subasset.alias.<kind>:<旧名>=<新名>` を、ハッシュを取る前に適用する。
6. provider の GUID や配列番号は使わない。
7. 選択子の中の `/`、`:`、`%` は `%XX` にする。ハッシュはエスケープ前の名前で取る。

---

### 4. importer プラグインの契約

#### 4.1 インターフェース(`CSharpGameEngine.Cooking`)

```csharp
public interface IAssetImporter {
  ImporterDescriptor Descriptor{get;}
  ImportSniffResult Sniff(ImportSniffRequest Request);
  ValueTask<ImportResult> ImportAsync(ImportRequest Request,IImportContext Context,CancellationToken CancellationToken);
}
public interface IModelProcessor {
  ModelProcessorDescriptor Descriptor{get;}
  bool AppliesTo(InterchangeDocument Document,IProcessContext Context);
  ValueTask<InterchangeDocument> ProcessAsync(InterchangeDocument Document,IProcessContext Context,CancellationToken CancellationToken);
}
public enum ImporterIsolation:byte { Shared,Serialized,Dedicated,PerImport }
public enum ImporterDeterminism:byte { Deterministic,ExternalToolCached }
public sealed record ImporterDescriptor(string Identity,int Version,string DisplayName,ImmutableArray<string> Extensions,int Priority,
  string Provider,ImporterIsolation Isolation,ImporterDeterminism Determinism,ImportCapabilities Capabilities,
  ImmutableArray<ImportOptionDescriptor> Options,ImmutableArray<string> NestedImporters,ImmutableArray<ExternalToolRequirement> Tools);
public sealed record ImportRequest(AssetId AssetId,string SourceUri,string SourcePath,string ProjectDirectory,IReadOnlyDictionary<string,string> Options,string ScratchDirectory);
public sealed record ImportResult(InterchangeDocument? Document);
public interface IImportContext {
  Stream OpenRead(string ProjectRelativePath);
  bool TryOpenRead(string ProjectRelativePath,out Stream? Stream);
  void RecordDependency(string ProjectRelativePath);
  string StageArchive(string ProjectRelativePath,ArchiveLimits Limits);
  void Report(ImportDiagnostic Diagnostic);
  IProgress<ImportProgress> Progress{get;}
  ValueTask<InterchangeDocument> ImportNestedAsync(string ScratchRelativePath,string ImporterIdentity,IReadOnlyDictionary<string,string> Options,CancellationToken CancellationToken);
  // ブリッジが入れ子の結果の Space を上書きする(CAD の Z-up/メートル、原点など)
  ValueTask<InterchangeDocument> ImportNestedAsync(string ScratchRelativePath,string ImporterIdentity,IReadOnlyDictionary<string,string> Options,InterchangeSpace? SpaceOverride,CancellationToken CancellationToken);
  ExternalTool? ResolveTool(string ToolId,string VersionRange);
  ValueTask<ExternalProcessResult> RunToolAsync(ExternalProcessRequest Request,CancellationToken CancellationToken);
  IReadOnlyList<HumanoidProfile> HumanoidProfiles{get;}
  IReadOnlyList<ExporterProfile> ExporterProfiles{get;}
}
public enum ModelProcessorStage:byte { SourceSpace,AfterNormalize,BeforeWrite }
public sealed record ModelProcessorDescriptor(string Identity,int Version,ModelProcessorStage Stage,int Order,ImporterIsolation Isolation,ImmutableArray<ImportOptionDescriptor> Options);
public sealed record ImportDiagnostic(ImportSeverity Severity,string Code,string Component,ImportPhase Phase,string Message,ImportSourceLocation Location);
```

- 現在の 4 系統の診断型(`GltfImportDiagnostic`、`FbxImportDiagnostic`、`FbxImportDiagnosticSeverity`、`ContentImportDiagnostic`)を `ImportDiagnostic` に統一する(即時移行)。
- MSBuild への出力形式は `Assets/Hero.fbx(12,0): error IMP303: … [component=cge.fbx@2 provider=ufbx 0.23.1 phase=Normalize options=1a2b3c4d]`。
- 同一コードの診断は集約し、1 資産あたり 256 件以内にする。

**診断コード範囲**

| 範囲 | 意味 | 代表例 |
|---|---|---|
| IMP0xx | 発見、`.meta`、選択、ワーカー | 001 `.meta` 無し、002 未登録、003 候補が決まらない、004 未知のオプション、005 ソース無し、006 許されない場所の読み取り、010 SubAssetId 衝突 |
| IMP1xx | 検証とエンジンのパス | 101 範囲外、102 非有限、103 循環、104 関節名重複、105 レスト/バインド不一致、106 影響を 8 本に、110 法線/tangent 生成、120/121 ヒューマノイド、130 再サンプル、140 描画しない拡張、150/151 名前、160 スキン統合不可 |
| IMP2xx | glTF と VRM | 201 `extensionsRequired` 未対応、202 圧縮デコーダー無し、203 VRM プラグイン無効、210 meta 不足、211 VRM0 向き補正 |
| IMP3xx | FBX | 301 プロファイル適用、302 leaf 除去、303 SSC 焼き込み、304 DQ→LBS |
| IMP4xx | 他の交換形式 | 401 単位推定、402 Assimp 予備 |
| IMP5xx | ブリッジ | 501 ツール未設定、502 版不足またはオプション不明、503 終了コード≠0、504 スナップショット古い、505 ツールライセンス、506 ロック、507 ログに ERROR |
| IMP6xx | CAD | 601 テッセレーション空、602 原点移動 |
| IMP7xx | ライセンス | 701 UE-Only、702 Unity Companion、703 未確認、704 VRM 商用不可、705 改変禁止、706 NoAI、707 クレジット必須 |
| IMP8xx | 資源とプロセス | 801 クラッシュ、802 期限切れ/キャンセル、803 上限超過 |
| IMP9xx | 決定性 | 901 reader、902 パス/writer |

#### 4.2 発見と登録
- モジュールは、Cook DLL の公開で abstract でなく引数なしの ctor を持つ `IAssetImporter`/`IModelProcessor`/`IAssetCooker` を FullName 順に並べたもの。
- データプロファイルは `Profiles/*.cgehumanoid.json` と `Profiles/*.cgeexporter.json`。
- `Cook.Cli describe <dll>` が記述子を JSON で返す。オーケストレーターは DLL の SHA-256 をキーに `obj/cge-content/registry.json` へキャッシュする。devenv とオーケストレーターは第三者 DLL を読まない。
- 各プラグインの `.plugin.items` が `<CgeCookModule …/>` を足す。Navigation の手書きターゲットは M0115 で載せ替える。組み込み Import は Game.targets が常に足す。
- `PluginModuleLoadContext` の共有集合は {Cooking, Contracts, Core}(現状は 1 つだけを比較している)。
- 互換は `.cgeplugin` の `EngineVersion`(PLUGIN005)。IR の minor 版は追加のみ。
- `IImporterCatalog` の実装は registry.json から作る。`AssetAuthoringService` は注入されたカタログで importer を検証する(D10)。

#### 4.3 選択
- 選ぶのは `.meta` を新しく作るときだけ(VSIX または `meta init`)。作成後は `.meta` の `importer` が正本。
- 順序は、Sniff が Definite の候補、Priority の大きい順、プロジェクトのプラグイン優先、Identity の ordinal 順。
- 同点なら IMP003 にしてユーザーに選んでもらう。
- `.cgelink` は中身の `tool` で決める。
- `ContentCompiler explain <file>` が候補と連鎖を表示する。

#### 4.4 オプション(`.meta`)
- 形式は schema 1 の平らな文字列辞書のまま。
- 名前空間:
  - エンジンのパス: `process.*`、`skin.*`、`skeleton.*`、`mesh.*`、`anim.*`、`humanoid.*`、`license.*`、`subasset.alias.*`、`limits.*`、`bridge.*`、`texture.*`、`light.*`。
  - 各 importer/processor: `<identity>.*`。
- 角度は turns、長さはメートル。未知のキーは IMP004 のエラー。
- 列は添字キーで書く: `clips.0.name`、`humanoid.map.LeftUpperArm=…`。
- `MaximumOptions` は `Contracts/AssetAuthoringContracts.cs:5` で 64 から 1024 に上げる。
- `OptionsHash` は、`chain` と既定値を埋めた全オプションを正準 JSON にした SHA-256。
- `sourceUri` はリポジトリ基準からプロジェクト相対に直す(移行時)。

#### 4.5 依存、キャンセル、決定性、隔離
- **SourceHash** は主ファイルと前回の読み取り集合(パスとハッシュ、または absent)から作る。新しく読んだファイルは import-catalog に記録する。
- **キャンセル**は、トークン、ワーカーへの `Cancel`、2 秒待ってプロセスツリー kill の順。`BridgeServerHost.RunAsync` がトークンを捨てている問題は同じ変更で直す。
- **決定性**: `compile --verify` は別々の 2 ワーカーで取り込み、IR(IMP901)と envelope(IMP902)を比較する。`ExternalToolCached` は tool-cache に置く。
- **隔離**:

  | 種類 | 動かし方 | 例 |
  |---|---|---|
  | Shared | 共有ワーカーでスレッド並列 | SharpGLTF、マネージド reader |
  | Serialized | 専用ワーカーで直列 | Assimp |
  | Dedicated | importer ごとに専用 | ufbx |
  | PerImport | 毎回新プロセス | OCCT、USD、DCC |

  第三者は最低 Dedicated。クラッシュは IMP801 とし、並列度 1 で 1 回だけ再試行する。上限はメモリ 8 GiB、期限 300 s(CAD は 1800 s)。ネットワークは禁止。
- **並行性**: ワーカー数は論理コアの半分。DCC はツール ID ごとに `MaximumConcurrent` で制限し、プロジェクトごとにロックする。

#### 4.6 オーケストレーターの手順
`IAssetCooker` は残す。`CookRequest` に `ContentIndexPath` を足す。汎用ターゲットは「資産単位の importer」と「シーン単位の cooker」を区別し、Navigation(シーン単位)は Mesh artifact を引く。

1. ソースを列挙する(`Assets/**`(`Assets/Editor` を除く)と有効プラグインの `Assets/**`)。
2. `.meta` を読む。無ければ [Q]4 の方針で扱う。ソースツリーには書かない。
3. 記述子から、オプション、OptionsHash、ImporterIdentity、ToolVersion を決める。
4. **前処理**(D9、C5): 取り込みの fingerprint を再計算する。一致すれば前回の結果を使い、不一致ならワーカーで取り込む。続けて次を終える。
   - P18 の再検証。
   - 切り出し。
   - texture のデコード。
   - envelope 化。
   - サイズ(512 MiB 以下)と依存数(65,535 以下)の検査。
5. 失敗した資産はまとめてノードから外し、診断を集める。開発用マニフェストと content-index には、失敗資産の前回の参照を書き足す(F17)。Roots のうち失敗した資産を指すものは `GetClosure` の前に外す。GC の Quarantine は失敗資産の State を保つ。ビルド結果は 1。
6. 確定したバイト列を返すだけのノードを作る。ノードは例外を出さない。
7. 改造しない `IncrementalContentCompiler.CompileAsync` を呼ぶ。
8. 成功したら、import-catalog と content-index を原子的に置き換える。
9. 開発中は `<game>.content.json` を書く。publish では根の集合からの閉包で `ShippingContentPackage.PublishAsync` を呼ぶ。

---

### 5. アダプターの分類と取り込み元ごとの経路

#### 5.1 分類(すべて `IAssetImporter`)

| 区分 | 中身 | 該当 |
|---|---|---|
| A マネージド直接 | ワーカー内で C# がパース | glTF/GLB/VRM/VRMA、OBJ、STL、PLY、BVH、3MF、Unity YAML、Godot `.tres`、DXF |
| B ネイティブ直接 | shim を `LibraryImport` | FBX(ufbx)、Draco、meshopt、KTX2、Assimp(優先度 10) |
| C 変換プロセス | C++ exe を PerImport で起動し、glTF とサイドカーを入れ子で取り込む | STEP/IGES(OCCT)、USD([Q]10) |
| D DCC ブリッジ | ユーザーのライセンスのツールをヘッドレス実行 | Blender、UE5、Godot(バイナリ)、Unity(Humanoid 焼き込み)、Maya、3ds Max |
| processor | 段階つきで意味を足す | `vrm.processor`、`vrm.mtoon`、`unity.urp-lit`、`godot.standard`、`legacy.phong`、TextureCook/Repack、MeshOptimize |
| データプロファイル | JSON | ヒューマノイド(`mixamo`、`ue5-manny`、`ue4`、`max-biped`、`rigify`、`godot-humanoid`、`vrm0`、`bvh-cmu`)と書き出し元の癖 |
| Project Converter | `ContentCompiler convert <unity\|godot>` が一度だけ `.prefab`/`.unity`/`.tscn` をシーン JSON に変える(ADR-0070) | Unity、Godot |

**D の共通形**
- 起動は `<tool> <headless フラグ> <script>`、環境変数は `CGE_BRIDGE_REQUEST` のみ。
- 要求は `cge-bridge-request/1`、結果は `cge-bridge-result/1`。結果は `tool`、`script`、`source`、`outputs[]`(role: Model/ClipsOnly/Texture)、`space`、`readSet[]`、`textures[]`(F24)、`humanoid?`、`clips[]`、`events[]`、`curves[]`、`sockets[]`、`license?`、`diagnostics[]` を持つ。
- 成功の条件は、終了コード 0、結果ファイルがある、全出力がある、の 3 つ。
- ログはファイルから回収する。ユーザーのプロジェクトには書かない。
- ツールの場所は `%LOCALAPPDATA%\CSharpGameEngine\ExternalTools.json`、`CGE_TOOL_<ID>`、`--tool` の順に引く。固定パスと固定件数は設定とテストの期待値に移す。
- `bridge.mode=auto|live|snapshot`([Q]3)。スナップショットは `ContentCompiler bridge snapshot` だけが作る。
- `.cgelink` は `{tool,project,asset,kind,clips[]}`。

#### 5.2 取り込み元ごとの経路

| 取り込み元 | 主経路 | 予備 | 正規化する癖 |
|---|---|---|---|
| **Unity** | (A) `.unitypackage` またはフォルダを展開する。ModelImporter の YAML(`globalScale`、`useFileScale`、`clipAnimations`、`humanDescription`)を `.meta` の `clips.*`、`humanoid.map.*`、`process.scale` に一度だけ翻訳する。FBX は `cge.fbx`、Generic の `.anim` は `cge.unity-anim`(`m_PositionCurves`、`m_RotationCurves`、`m_ScaleCurves`、`m_EulerCurves`、`m_FloatCurves`、serializedVersion ごとの接線モード)。`.prefab`/`.unity` は Project Converter(対応コンポーネントの一覧、未対応は error、variant は基底+override の順で適用) | (D) キャッシュ内の最小 Unity プロジェクトに、UnityGLTF とブリッジを版固定で入れる。GUID の閉包の資産だけをコピーし、`-batchmode -executeMethod` で Humanoid のマッスルを焼き込む | LH、Y-up、m。Companion License は IMP702。Asset Store の資産は生ファイルを再配布しない。Personal の batchmode は Hub 認証が前提で CI に向かない |
| **UE5** | (D) `.cgelink` から `UnrealEditor-Cmd -ExecutePythonScript … -unattended`。`export_uniform_scale=0.01`、スキンの重みとモーフを出す。サイドカーで Notify、カーブ、ソケット、virtual bone を運ぶ。`tools/UnrealExport` を DccBridges に移す | FBX(`AssetExportTask`)、USD | `-nullrhi` はアニメーションでは付け、メッシュでは付けない。マテリアルが全部空なら IMP502 相当。クリップだけのファイルは ClipsOnly で読む。`ik_*` は IkTarget。Fab の Epic スケルトンは A-pose。UE-Only は IMP701。AnimSequence ごとに 1 回書き出す |
| **VRoid** | (A) VRoid Studio が書いた VRM 0.x/1.0 を `cge.gltf` で読み、Vrm プラグインの processor が解釈する | — | VRM0 は glTF の -Z を向く。`auto` なら P09 で -F にそろえる(IMP211)。親指の読み替え、`stiffiness` の綴り、表情重み 0〜100 を 0〜1 にする。複数スキンは P07 で統合する(F01)。meta の原文は保つ。MToon は Block として持つ |
| **Maya** | (D) `mayapy`、`fbxmaya` で FBX を書く。`FBXExportEmbeddedTextures true`(または textures[] で報告)、`FBXExportSplitAnimationIntoTakes` を指定する。ファイルごとに 1 回呼ぶ | `mayaUSDExport` | RH、Y-up、cm。ufbx の `target_unit_meters=1`、`space_conversion=MODIFY_GEOMETRY`、`inherit_mode_handling=COMPENSATE`。HIK はサイドカーでアバターにする。`kLicenseFailure` は IMP505 |
| **3ds Max** | (D) `3dsmaxbatch` と pymxs(`FBXExporterSetParam`) | glTF プラグイン | RH、Z-up、inch/cm。Biped は `max-biped` のプロファイル。IK は焼き込む。`.max` は直接読まない(EULA) |
| **CAD** | (C) `cge-occt-convert`。メッシュ化の前に倍精度の平行移動で原点を移し、量をサイドカーに書く。`BRepMesh` は `InParallel=false` で、ラベルの走査順を固定する。`RWGltf_CafWriter` で書き、`cge.gltf` で入れ子に読む。サイドカーで Space を上書きする(Z-up、メートル)。DXF は netDxf | ネイティブ CAD は STEP AP242 または glTF に書き出す | mm→m。`cad.upAxis=Z`。同じ形状の配置はメッシュを共有する。512 MiB 超の部品や頂点数の上限超過は分割する(P12b)。骨格は無い |
| **Blender** | (D) `blender -b --factory-startup … --python-exit-code 1 --python cge_blender_export.py`。`export_yup`、`export_apply=False`(F03)、`export_def_bones`、`export_rest_position_armature`、`export_animation_mode='ACTIONS'`、`export_force_sampling`、`export_all_influences`、`export_morph`、`export_morph_animation`、`export_cameras`、`export_lights`、`export_import_convert_lighting_mode='SPEC'`、`export_vertex_color='ACTIVE'`。shape key の無いオブジェクトだけ評価済みメッシュで modifier を適用し、shape key があるオブジェクトの modifier は警告にする | FBX、USD | Blender の FBX は `*_end` を除く。スクリプトは GPL ヘッダーつきのデータファイルとして同梱する。CI で live にできる |
| **Fab** | ドロップゾーン(§8)で zip を展開し、主形式を選ぶ(GLB/glTF > FBX > USD > OBJ) | UE 形式のみの出品は、ユーザーの UE プロジェクトに入れてから `.cgelink` | 自動取得はしない。Converted GLB は Draco/meshopt のデコードが要る。ライセンスはユーザーが入力する |
| **Godot** | (A) `.glb` は `cge.gltf`。`.tres` の Animation/AnimationLibrary は `cge.godot-anim`。`.import` は Variant の最小パーサーで翻訳する。`.tscn` は Project Converter | (D) `godot --headless -s cge_export.gd`(バイナリ `.scn`/`.res` 用) | RH、Y-up、m。`godot-humanoid` のプロファイル。`res://` はプロジェクト相対に解決する |

#### 5.3 交換形式

| 形式 | importer | ライブラリ | 要点 |
|---|---|---|---|
| glTF/GLB/.vrm/.vrma | `cge.gltf` | SharpGLTF 1.0.7(M0115 で更新。**事前に spike で挙動を確認**) | 先に `GltfNormalizer`(GLB の JSON/BIN を解析し、Draco/meshopt を shim でデコードして通常の bufferView に直し、拡張を外す)。KTX2 は basisu、WebP は libwebp または IMP202。COLOR_n/UV_n/JOINTS_n を全部読む。CUBICSPLINE の接線を保つ。`KHR_animation_pointer` の API 対応(1.0.6 に有り、1.0.7 は spike で確認)。VRMC_* などは生 JSON で運ぶ |
| FBX(Mixamo 含む) | `cge.fbx` | ufbx(倍精度ビルド、shim の ABI で float) | `target_axes`=glTF、`target_unit_meters=1`。UV は BottomLeft と宣言する。`ufbx_bake_anim`、blend deformer(in-between)、`geometry_to_bone` で IBM を作る。Mixamo は `mixamorig:` を除き、`*_End` を Leaf にする |
| OBJ/STL/PLY/3MF | `cge.obj` ほか | マネージド | OBJ は UV BottomLeft。STL は mm を仮定(IMP401)して頂点を溶接する。3DGS の PLY は Sniff で拒否する。3MF の components は共有メッシュにする |
| BVH | `cge.bvh` | マネージド | 回転順はチャネル順。`bvh.upAxis` と `bvh.metersPerUnit`(既定 0.01)。End Site は Leaf。M0116 まで束縛できない |
| USD/USDZ | `cge.usd` | [Q]10 | UsdSkel の joints 順、bind/rest、疎アニメ、blendShapes。UV は BottomLeft。**未確定(spike 後)** |
| Collada、3DS、X、SMD、PMX | `cge.assimp`(優先度 10) | AssimpNetter 6.0.5 | Serialized。`aiBone.offsetMatrix` を IBM(BindShape は分離)に、`aiAnimation` をトラックに、`aiAnimMesh` をモーフに写す。Phong は `legacy.phong` で PBR にする。ネイティブの assimp の版を実行時に取得し、6.0.2 以上を契約テストで確かめる |
| VRMA | `cge.gltf` と `vrm.processor` | — | 回転のみ(移動は hips だけ)。表情は Curve にする |

---

### 6. 取り込み後の処理(順序は固定。パスの版は `ToolVersion` に入る)

| # | パス | 担当 | v1(M0114 の parity) | v2(M0115) |
|---|---|---|---|---|
| P00 | 構造検証 | エンジン | ○ | ○ |
| P01 | ライセンスの早期関門 | エンジン | ○ | ○ |
| P02 | 上限(IMP803。texture 最大辺 8192、頂点、関節、モーフ、サブアセット数) | エンジン | ○ | ○ |
| — | processor: SourceSpace(VRM の解釈など) | プラグイン | | |
| P03 | 書き出し元プロファイル(leaf と helper の判定、名前空間の除去) | エンジンとデータ | — | ○ |
| P04 | 軸: `ConventionConversion<TS,Unity>` | エンジン | ○ | ○ |
| P04b | **UV 原点**(BottomLeft なら `v'=1-v`、UvTransform と tangent.w を補正) | エンジン | ○ | ○ |
| P05 | 単位(位置、移動キー、IBM の移動、モーフ差分、カメラ、ライト、コライダー、hips 高、ルートモーション)。スケールのキーには掛けない | エンジン | ○ | ○ |
| P06 | 階層整理(分解できない行列は helper に焼く。**負スケールは行列式の符号で検出**し、巻き順を反転して複製する。SSC は焼き込む) | エンジン | 検証のみ | ○ |
| P07 | 骨格正規化(**関節集合の閉包**、構造名の割り当て、leaf 除去(`ik_*` は残す)、剛体付属物を 1 関節のスキンに、**スキン統合**(IBM が 1e-4 で一致するか検証し、不一致は IMP160)) | エンジン | 名前規則と検証 | ○ |
| P08 | バインドポーズ(`IBM×Model_rest≈I`、BindShape の焼き込み) | エンジン | 検証のみ | ○ |
| P09 | 向き(`process.facing=auto\|preserve\|forward`) | エンジン | preserve | auto |
| P10 | メッシュ(法線、tangent、頂点色を線形に、KHR_texture_transform を UV0 に焼く、STL の頂点溶接) | エンジン | Lengyel | **MikkTSpace**(zlib の `mikktspace.c` を自前で移植する) |
| P11 | 影響を 8 本に | エンジン | セット単位切り捨て | 上位 8 本、再正規化 |
| P12 | モーフの疎化 | エンジン | — | ○ |
| P12b | **頂点数上限での分割**(スキンとモーフの付け替え込み) | エンジン | — | ○ |
| — | processor: AfterNormalize(マテリアル写像、ヒューマノイド手がかり) | プラグイン | | |
| P13 | ヒューマノイド(ソース情報、プロファイル、`humanoid.map.*`)。`RestToReference` は 3 段(プロファイル、方向+ねじれのヒューリスティック、不可なら IMP121 で無効化) | エンジンとデータ | — | M0116 |
| P14 | アニメーション(分割、ClipsOnly 統合、0 起点、半球、Hermite、キー削減) | エンジン | LINEAR/STEP | Hermite を保つ |
| P15 | ルートモーション抽出 | エンジン | — | M0116 |
| P16 | マテリアル(SpecGloss→MR、alpha、色空間) | エンジン | ○ | ○ |
| — | processor: BeforeWrite(**TextureRepack**(DX 法線、ORM/Unity Metal-Smooth の再パック)、MeshOptimize/LOD) | Import とプラグイン | | |
| P17 | ライセンス確定 | エンジン | ○ | ○ |
| P18 | 最後の不変条件(8 本で Σw=1、番号範囲、レスト≈I、名前の一意、上限、正準化)。**オーケストレーターが前処理でもう一度実行** | エンジン | ○ | ○ |
| P19 | SubAssetId を作り、artifact に分割する | オーケストレーター | ○ | ○ |

- **プラグインが入れない場所**: P00〜P02 の前、P04〜P08、P18、P19。
- **v1/v2 分割**: M0114 は今の数値と厳密に同じにし、数値が変わる改善は M0115 に集約してゴールデンの差分を 1 回でレビューする。
- **P04 の数学**(行ベクトルの規約 `p′=p·B`)
  - 点、ベクトル、法線、モーフ差分、重力の向きは `p·B`。
  - 行列は **Bᵀ·M·B**。コードは `Transpose(Basis)*Value*Basis`。glTF→エンジンの diag(1,1,-1) は ADR-0027 の C·M·C と一致する。
  - 四元数のベクトル部は `det(B)·(v·B)`、w はそのまま。
  - **スケールは成分の並べ替えのみ**(符号は保つ。`|B|` で並べ替える)。
  - tangent.w と三角形の巻き順は `det(B)=-1` で反転する。
  - 実装は `AxisNormalizer` の閉じた switch から `Apply<TConvention>()` に振り分ける。
- **P09 の不変条件**: 向き補正 Q は、`RootParentNode` のワールド、骨格に属さない最上位ノードの Local、そのトラックに掛ける。スキン頂点は `v·Q`、IBM は `Q⁻¹·IBM` にする。レストのパレットは単位行列のまま保たれる。

```csharp
public static class ConventionConversion<TSource,TTarget> where TSource:struct,ICoordinateConvention where TTarget:struct,ICoordinateConvention {
  public static Matrix4x4 Basis{get;}=BuildBasis();
  public static bool IsMirror{get;}=TSource.Handedness!=TTarget.Handedness;
  public static Vector3 Vector(Vector3 V)=>Vector3.TransformNormal(V,Basis);
  public static Matrix4x4 Matrix(Matrix4x4 M)=>Matrix4x4.Transpose(Basis)*M*Basis;
  public static Quaternion Rotation(Quaternion Q){var A=Vector(new(Q.X,Q.Y,Q.Z))*(IsMirror?-1f:1f);return new(A,Q.W);}
  public static Vector3 Scale(Vector3 S)=>PermuteAbsBasis(S);   // 符号は保つ
  public static Vector4 Tangent(Vector4 T){var X=Vector(new(T.X,T.Y,T.Z));return new(X,IsMirror?-T.W:T.W);}
}
```

---

### 7. 実行時の変更

1. **入口**: EngineHost の起動時に `RuntimeContent` を作る。Debug は `<game>.content.json`、Release は `ShippingContentPackage.OpenAsync` を使い、どちらも `RuntimeAssetStore` を通す。Preview Host は再取り込みの後に `TryUpdateManifestAsync` で差し替える。
2. **codec**(Runtime.Windows が登録): Mesh、Model、Skeleton、AnimationClip、License、DistanceField。Material と Texture は既存の登録関数を包む。
3. **`StageSceneDataBuilder`**
   - 参照される AssetKey を集めて `RetainAsync` をまとめて呼ぶ。
   - 削除するもの: `ImportScene`、WIC デコード、ソースのハッシュから作るキー、`.cgesdf`。
   - **サブメッシュ**(F08): `StageObjectCreateInfo` に `FirstIndex`/`IndexCount` を足し、頂点、スキン、モーフを共有する。
4. **`Runtime.Animation`**
   - `SkeletalAssetCache` を AssetKey キーの `SkeletalContent` に置き換え、`Import.Gltf` の using を削除する。
   - **複数スキン**
     - 既定は取り込み時の統合(P07)で、ランタイムは変えない。
     - `skin.merge=keep` のときだけ M0116 で入れる: `SkeletalMeshRuntime.Palettes[]`、`Skeleton.SkinIbm[s][]`、`SkinOfMesh[]`、`StageSkinCreateInfo.JointCount` のスキンごとの値。`SkinnedMeshSync` はメッシュ番号からスキンを引く。
   - Hermite のサンプリングを足す。
   - **リターゲット**
     - クリップは基準姿勢に対する正規化ローカル `n_b` で持つ。`n_b` は、対応のある骨の間にある局所回転の積で定義する。
     - 結び付けるとき、対象骨格で `D_b=n_b·D_p(b)` を親から計算し、クリップの fps でサンプルし直してキャッシュする。
     - 対応の無い関節は基準姿勢のままにする。hips の移動には `HipsHeight′/HipsHeight` を掛ける。
   - `FootIk` と `FirstPersonRig` の骨名は、アバターがあれば `HumanoidBone` から引く。
   - **受け口**(F06)
     - Events: `AnimationClip.SampleEvents` と通知コンポーネント(M0116)。
     - Curves: `SampleCurves`(VRMA の表情。M0117)。
     - PropertyTracks: v1 では捨てて IMP140。
     - Sockets: API を足す。
     - Additive: 受け口が無ければエラー。
     - ルートモーション: `Locomotion.RootMotion`(既定 off)。表示のみ([Q]7)。
5. **描画契約 `stage-v14`**(M0116)
   - モーフ(`SkeletalMeshRuntime.MorphWeights`)と、`alphaMode`/`alphaCutoff`/`doubleSided`/`occlusion`(F07)を入れる。
   - 現状 `src` にこれらは存在しない。
   - UV1 と頂点色は [Q]9 の判断まで artifact に保持し、IMP140 で警告する。
6. **VRM の実行時**(M0117): 表示のみ。LookAt、Expression、NodeConstraint、SpringBone の順で動かす。データは `ModelData` の Rig から読み、第三者のデータは `RuntimeBlocks` を Schema で引く。
7. **物理と L7 の利用者**: `StaticMeshCollision` は `IContentMeshSource` を通して読む。Physics.Cli、Navigation、距離場、`Unreal.SceneImporter` は Mesh artifact を読む。これにより `.glb` と `.fbx` の座標系の不一致(G3)と鏡像衝突(H6)は構造的に直る。
8. **シーン JSON の参照**
   - 値は文字列のまま(`"Assets/Anims/Locomotion.fbx:Walk_Fwd"`)。
   - 選択子が無ければ、その欄の種類のサブアセットがちょうど 1 つのときにそれを使い、0 または 2 つ以上なら SCENE210。
   - 生成器は content-index を AdditionalFiles として読み、SCENE209/210 を検査する。型つき参照(`Assets.Anims.…`)は M0116。

**層の規則**(F25): L6 の素データ型を L4/L5 が参照してよいことを ADR-0080 に書く。`HumanoidBone` は Contracts に置く。

---

### 8. VSIX とエディタの UX(net472 の境界を保つ)

- **ホストを 1 つにする**: `ContentCompiler serve` に一本化する。M0114 で旧 3 CLI を削除する。VSIX が受け取るのは Contracts の DTO だけ(`ImporterDescriptorDto`、`ImportOptionDescriptorDto`、`ImportSummaryDto`、`PreviewMeshDocument`)。診断は集約して 256 件以内にし、サブアセット一覧は件数で切り詰め、全体は一時ファイル経由にする。
- **`.meta` の書き手を 1 つにする**: `AssetAuthoringService`(net10、`IImporterCatalog` 注入)だけが書く。`AssetMetaWatcher` の旧形式 `guid:` の書き手は削除する。
- **取り込みダイアログ**: ホストの `Inspect` が、候補、`explain`、オプションスキーマ、要約を返す。ユーザーが選んだら `SaveMetadata`、続いて `dotnet msbuild -t:CgeCompileContent` の順に進める。
- **インスペクター**
  - 型つき UI(角度は度で表示して turns で保存)。
  - クリップ分割表。
  - ヒューマノイド対応表(必須ボーンの欠けを赤で示す)。
  - 診断一覧、ライセンス欄、サブアセットの木。
  - ヒエラルキーへのドラッグ配置。
  - Reimport(偽の完了を出さず、キャンセルで本当にワーカーを止める)。
- **プレビュー**(M0118): `serve` がその資産のノードを CAS へ compile し、EditorPreview.Host がゲームと同じ codec で描く。スケルトン重ね描きとクリップのスクラブに対応する。このとき `PreviewMeshDocument`、`MeshLoader`、`ExternalGltfLoader`、`ExternalFbxLoader` を削除する。VS のエディタ登録は静的なので、組み込み拡張子を固定で登録し、プラグインの拡張子は「Preview Asset / Import Settings」コマンドから開く。
- **Fab のドロップゾーン**: 展開(`StageArchive` の上限)、主形式の選択、ライセンス入力ダイアログ(種類、NoAI、出品 URL)の順に進む。UE-Only を選ぶと、その場で IMP701 を説明する。
- **外部ツールのページ**: `ExternalTools.json` の検出版とライセンス要否を表示し、`--version` で確かめる。

---

### 9. ライセンスと出典の関門

| 入力 | 取り方 | 規則(既定。[Q]11) |
|---|---|---|
| Fab の UE-Only | ダイアログまたは `license.restrictions` | IMP701。常にエラー |
| Unity Companion License | 展開時に検出または申告 | IMP702。常にエラー |
| Unity Asset Store の EULA | 申告 | `NoRawRedistribution`。出荷は artifact のみ |
| VRM の meta | 自動(`Confirmed=true`) | 非商用を `CgeProductCommercial=true` で使うと IMP704。`modification=prohibited` は形を変えるパス(統合、LOD、向きの焼き込み)を止め、形式変換と正規化は続ける(IMP705) |
| CC-BY、creditNotation 必須 | meta またはダイアログ | IMP707。出荷の閉包の License から `ThirdPartyAssetNotices` を生成する |
| NoAI | ダイアログ | IMP706(情報)。記録して表示する |
| 不明または未確認 | — | IMP703。開発中は警告、`package` と `content.release` ではエラー |

- 法的判断はしない。ゲートはユーザーの方針を実行する仕組みとして提供する。
- **Git の衛生**(C8、C12)
  - `.gitignore:59` を 3 行に置き換える: `**/Assets/Fab/**`、`!**/Assets/Fab/**/`、`!**/Assets/Fab/**/*.meta`。
  - doctor は `git check-ignore` で、`NoRawRedistribution`、UE-Only、不明の生ファイルやスナップショットが追跡されていないか確かめる。
  - 50 MB 超は、テクスチャを分けるかコミットしない運用にする。40 MB で警告する。
  - `cge_blender_export.py` は GPL-2.0-or-later ヘッダーを付けた別ファイルとし、THIRD_PARTY_NOTICES に記載する。

---

### 10. 検証の戦略

#### 10.1 コーパス
- **T0 合成**(コミット。1 つ 200 KB 未満、合計 5 MB 未満)
  - glTF/VRM(SharpGLTF.Toolkit)。
  - ASCII FBX 7.4。Maya/Max の形も、Properties70 に `Lcl`、`PreRotation`、`InheritType`、`UnitScaleFactor`、`UpAxis` を持つ形でテストコードから書いて合成する。
  - OBJ、PLY、STL、BVH、USDA、STEP AP214、3MF。
  - 壊れたもの。
  - Draco/meshopt/KTX2 は C# で合成できないので、数 KB の既製バイナリを T1 にコミットする。
- **T1 無償ツールの書き出し記録**(Blender、Godot、UE のみ。自作 CC0)。作り直しスクリプトは `tools/ImportCorpus/<Tool>/`。
- **T2 取得物**(コミットしない): 次の 8 本はハッシュ固定の必須サブセット。
  - SimpleSkin
  - CesiumMan
  - MorphStressTest
  - CubicInterpolation
  - TextureCoordinateTest
  - NormalTangentMirrorTest
  - MultiUVTest
  - VertexColorTest
- **T3 ユーザー機のみ**: Fab、Maya、Max、Asset Store。環境変数で opt-in する。

#### 10.2 ChiralityRig
- 基本形:
  - 色つき四面体の腕(長さ 1、2、3 cm)。
  - 1 m の基準棒。
  - 「F」の字の UV テクスチャ。
  - 非対称な 3 関節の鎖と、9 本以上の影響。
  - 名前の無い関節、SSC、レストとバインドのずれ。
  - +R へ動くモーフと in-between。
  - CUBICSPLINE のクリップとイベント。
  - +F を見るカメラ、spot light。
- 追加する端ケース(F14):
  - 2 スキン(同一骨格で関節の部分集合が違う)。
  - 関節でない中間ノードがアニメーションする。
  - 非ゼロ開始のクリップ。
  - 重複名。
  - 負スケール(静的、スキン付き)。
  - A-pose のレストと T-pose のクリップ。
  - mm と inch。
  - 1e6 の原点。
  - 66,000 関節(エラーの確認)。
  - UV 原点違い。
- 期待値はエンジン空間の解析値のみ。画面の見た目(M0107)とは混ぜない。

#### 10.3 ゴールデンの 3 層(`Golden-Artifacts.json`)
1. Reader のテキストダンプ(厳密一致)。
2. パス後の IR(構造は厳密、float は 1e-6)。
3. artifact の ContentHash。
- 更新は `CGE_UPDATE_GOLDEN=1` のときだけ。

#### 10.4 移行前の等価性の神託(M0114 の最初のコミット。M0112 のコミット後に取る)
- 現行経路で、テンプレートの `.glb` を実行時に数え上げて次を記録する(94 本から増える)。
  - 頂点ハッシュ。
  - マテリアル値。
  - レストのパレット誤差。
  - Quinn の 20 クリップの t=0/0.25/0.5 の量子化パレット。
  - 束縛できたトラック数。
- 新経路は同一値を出す(JPEG のみ ±1 LSB)。`StandardModeGoldenFrameTests` のハッシュは不変。

#### 10.5 その他
- **契約テスト** `ImporterContractTests<T>`
  - 別プロセスで 2 回取り込み、バイト列が一致する。
  - キャンセルが 2 秒以内に効く。
  - 切り詰め入力、0 バイト、拡張子偽装を IMP エラーで返す。
  - パス文字(Unicode、ANSI 外、`#`、`;`、長いパス)。
  - 読み取り集合だけをコピーしても同じ結果になる。
  - 未知オプションを拒否する。
  - IR の型グラフが BCL、Interchange、Contracts のみ。
- **ブリッジ**: 偽 DCC exe を使う。実ツールのテストは Stage 属性 `RequiredTools` で判定し、ツールが無ければ `Blocked`(合格に数えない)。`FeatureStageMode` は増やさない。
- **往復**: IR→glTF writer→`cge.gltf`、正規化の冪等性。
- **oracle**: 主は ChiralityRig の解析値。`ufbx_evaluate_scene` は補助(同じ ufbx なので解釈誤りは見つからない)。
- **リターゲット**: A-pose の Manny と T-pose の VRM の手先と足先の向きの差が 5° 以内。中間の twist 骨が動くクリップを含める。
- **層の検査**(`EngineLayerSpec`): Runtime.*、Rendering、Content、Game.props が Import と Cooking を参照しない。Cooking の公開 API に SharpGLTF/Assimp/ufbx の型が無い。VSIX が Content と Cooking を参照しない。

#### 10.6 上限と性能

| 上限 | 既定 |
|---|---|
| ソース | 2 GiB 以下 |
| IR(ir-cache 用) | 別途設定(CAS 上限とは無関係) |
| artifact 1 つ | 512 MiB 以下(CAS の上限) |
| テクスチャ最大辺 | 8192 以下(`W*H*4+16 ≤ 512 MiB`)。超過は `texture.maxSize` で縮小、でなければ IMP803。上限引き上げは ADR に書く |
| メッシュ頂点 | 4,194,240 以下(超過は P12b で分割) |
| 関節 | 65,535 以下 |
| モーフ | メッシュあたり 256 以下 |
| Model の依存数 | 65,535 以下(超過は中間 Model に分ける) |
| 診断数 | 256 以下(集約) |
| ブリッジのフレーム | 16 MiB 以下(超過は一時ファイル経由) |

- 性能目標: ThirdPerson の cold compile は 30 s 未満、warm compile は 2 s 未満、Quinn の取り込みは 10 s 未満。

#### 10.7 FeatureRunner の Stage

| Stage | Feature | 判定 |
|---|---|---|
| `content.model-schema` | F-L2-007、F-L6-010 | SubAssetId、payload の往復 |
| `import.interchange` | F-L7-054 | IR の codec の正準性 |
| `import.registry-isolation` | F-L7-055 | 選択順序、ALC、クラッシュ、`--verify` |
| `content.import-pipeline` | F-L7-056 | cold/warm、葉の変更、失敗の分離(ノード前の失敗) |
| `import.pipeline-golden` | F-L7-057 | v1/v2 のゴールデンと神託 |
| `import.chirality` | 横断 | 全経路が同じ期待値 |
| `content.importer-conformance`(既存) | F-L7-058〜060 | 契約テストとゴールデン |
| `import.humanoid-retarget` | F-L7-061、F-L5-047 | 対応網羅度、リターゲット誤差、ルートモーション |
| `import.vrm` | F-L7-062、F-L5-048 | VRM0/1 の向き、表情、揺れもの |
| `import.tool-bridges` | F-L7-063、064 | 偽ツール必須、実ツールは Blocked/Verified |
| `import.usd`、`import.cad` | F-L7-065、066 | — |
| `import.license-gate` | F-L7-067 | IMP701〜707、Release の拒否 |
| `content.model-runtime-load` | F-L4-020 | 実行時 codec と ContentIndex |
| `rendering.skeletal-animation`(変更) | F-L4-015、F-L4-021 | Quinn が artifact から動く、レスト≈I、alpha/doubleSided |
| `vsix.asset-import` | F-L8-033、034 | ダイアログ、プレビュー、Fab |

---

### 11. 移行計画とロードマップ

**文書**
- ADR-0080「Universal model import on the content pipeline」(M0114)。
- ADR-0081「Native import shims」(M0115)。
- ADR-0082「DCC tool bridges and third-party content licensing」(M0119)。Unity の Project Converter 範囲と `cge.unity-anim` 仕様を含む。
- ルートモーションをシミュレーションに使う場合は ADR-0083([Q]7)。
- 更新する文書: Asset-Content-Pipeline.md、Plugins-And-Assets.md、Spatial-RUF.md、Rendering.md、Editor-VSIX.md、Architecture.md、Implementation-Status.md、THIRD_PARTY_NOTICES.md、ADR-0078(予約の追記)。

**Feature ID**: 各層の最大番号は、ADR-0080 の着手時に再 grep して確定する。M0113 と衝突する場合は、マージ順で振り直す。

| 層 | 新しい Feature(暫定) |
|---|---|
| L1 | F-L1-010 `ConventionConversion` |
| L2 | F-L2-007 SubAssetIdentity、AssetSelector、HumanoidBone、IImporterCatalog |
| L4 | F-L4-020 実行時の codec と ContentIndex、F-L4-021 モーフ・alpha・doubleSided・occlusion の描画契約 |
| L5 | **F-L5-047** artifact からの SkeletalMesh と Animation(複数スキン、Hermite、リターゲット、ルートモーション)、**F-L5-048** VRM の実行時(`F-L5-046` は M0112 が使用済み) |
| L6 | F-L6-010 モデル payload と kind 8〜12 |
| L7 | F-L7-054 IR、055 契約と登録表とワーカー、056 オーケストレーター、057 エンジンのパス、058 glTF、059 FBX と Assimp、060 基本形式、061 ヒューマノイドとプロファイル、062 VRM、063 ブリッジ枠組みと Blender/Godot/UE5、064 Unity/Maya/Max、065 USD、066 CAD、067 ライセンス、068 テクスチャとメッシュ最適化 |
| L8 | F-L8-033 取り込み UX とプレビュー、F-L8-034 Fab と外部ツール |

既存の更新:
- F-L0-005 は Removed にし、F-L7-058 へ置き換える。
- F-L7-025 の Next action を満たす。
- F-L4-015、F-L7-052、F-L7-053、F-L8-019 も更新する。

**マイルストーン**

| M | 内容 | 同じ変更で消すもの | 終了条件 |
|---|---|---|---|
| **M0114** 取り込み基盤と glTF 一本化 | S0 等価性の神託。S1 Contracts と `ConventionConversion`。S2 Cooking の IR、codec、検証器、契約、パス v1(UV 原点、スキン統合の検証を含む)。S3 Cook.Cli の `describe`/`import-worker`、ContentCompiler(`compile`/`import-ir`/`inspect`/`explain`/`meta`/`serve`)、`IImporterCatalog`、content-index、Content/Models、kind 8〜12、`.meta` の一度きりの移行。S4 切り替え: `cge.gltf`、暫定の `cge.fbx`(Assimp)。ただし [Q]2 が (a) か (b) なら ufbx に前倒しする。続けて StageSceneDataBuilder、SkeletalContent、StaticMeshCollision、Physics.Cli、Navigation(`DependsOnTargets` 順序つき)、距離場、Unreal.SceneImporter。S5 SDK/VSIX の pack 移行。ビルド失敗の分離 | `GltfAxisConversion`、`FbxAxisConversion`、`GltfImporter`、`Imported*`、`ProviderNeutralImport`、`ContentMeshIr` 系、診断型 4 系統、`.cgesdf` の読み手、Game.props の Import 参照、実行時 WIC デコード、旧 `.meta` 書き手、`AssetAuthoringService` の switch、旧 3 CLI | 神託が一致し、標準フレームのハッシュが不変。4 テンプレートが WARP と実機で動く。`.fbx` の StaticMesh が実行時に使える。`verify-template-package` が緑。content.*、import.interchange、import.registry-isolation、import.chirality(glTF)が緑 |
| **M0115** 公開契約、ufbx、基本形式、パス v2 | 汎用 `CgeCookModule`(Navigation を載せ替え)、第三者の隔離、ネイティブ shim(ADR-0081)、`cge.fbx`(ufbx)、`GltfNormalizer`(Draco/meshopt/KTX2)、OBJ/STL/PLY/BVH/3MF、Assimp 予備、パス v2(MikkTSpace 移植、上位 8 本、バインド焼き直し、階層整理、P12b)、Hermite、SharpGLTF 1.0.7(spike 後) | 暫定 Assimp FBX、AssimpNet 4.1、Lengyel tangent | ufbx の補助 oracle、Blender FBX の T1、v2 のゴールデン差分レビュー、fuzz |
| **M0116** ヒューマノイド、リターゲット、描画契約 | P13〜P15、プロファイル、リターゲット、ルートモーション表示、Events と Curves の API、`stage-v14`(モーフ、alpha、doubleSided、occlusion)、TextureRepack、`skin.merge=keep` の実行時、型つき `Clips.*`、FootIk/FirstPersonRig のアバター化 | UE 名への依存(アバターがある場合) | Mixamo と BVH のクリップを Quinn で再生する Visual Stage |
| **M0117** VRM/VRoid プラグイン | `plugins/CSharpGameEngine.Vrm`(VRM0/1/VRMA の processor、MToon 近似、SpringBone/Constraint/Expression/LookAt)、meta の関門 | — | VRoid の 0.x/1.0 が表情と揺れもの込みで表示され、髪とまつ毛が alpha と両面で正しく出て、向きがそろう |
| **M0118** エディタ UX、Fab、ライセンス完成 | ダイアログ、インスペクター、対応表、artifact プレビュー、Fab、外部ツールページ、`ThirdPartyAssetNotices`、`.gitignore` 修正 | `PreviewMeshDocument` ほか | `vsix.asset-import`、`import.license-gate` |
| **M0119** DCC ブリッジ(無償ツールと UE) | 枠組み、Blender、Godot、UE5、ADR-0082 | UE の固定パス、`AnimationGlbCompactor` | 偽ツールの契約テストが緑。Blender/Godot は CI で live |
| **M0120** Unity、Maya、3ds Max | `.unitypackage`、YAML、`cge.unity-anim`、Project Converter、Unity batchmode、mayapy、3dsmaxbatch | — | 合成 Unity fixture、T1/T3 |
| **M0121** USD と CAD | USD(spike 結果で確定)、OCCT 変換器、DXF、原点移動 | — | STEP アセンブリ、USDZ、1e6 原点の精度 |
| **M0122** テクスチャとメッシュ最適化 | BC7/BC5、cook 時ミップ、meshopt、LOD | CPU ミップ生成 | `content.performance` で劣化なし |

---

### 12. リスクと判断が必要な点

#### 12.1 リスク

| # | リスク | 対策 |
|---|---|---|
| R1 | M0114 の S4 が大きい | 神託、S1〜S3 は追加のみ、段階レビュー |
| R2 | リポジトリ初のネイティブビルド | 安定した C ABI、版固定、Dedicated 隔離、fuzz |
| R3 | DCC の版変化 | 版範囲、版ごとのスクリプト、`hasattr` 検査、偽ツールのテスト |
| R4 | 浮動小数のマシン差 | ゴールデンに許容差、`--verify` は同一マシン |
| R5 | ライセンス解釈 | 申告と VRM meta のみを根拠にし、Release で止める |
| R6 | 描画範囲が IR より狭い(MToon、UV1、頂点色) | artifact に保持し IMP140 で示す。alpha、doubleSided、occlusion、モーフは M0116 で解消する |
| R7 | 生成器が content-index に依存する | 前回の index を使う。無ければ警告 1 件 |
| R8 | 信頼できない入力 | Job Object、ネットワーク禁止、上限、`StageArchive` |
| R9 | プラグインが任意コードを動かす | NuGet と同じ信頼の前提。第三者は Dedicated 以上 |
| R10 | 向き正規化と M0107 の鏡像 | 期待値はエンジン空間で書く |
| R11 | SharpGLTF 1.0.7 の API と挙動が未確認 | M0114 前に spike |
| R12 | USD の経路が未確定 | spike 後に M0121 の範囲を決める |
| R13 | 取り込み失敗でビルド全体が止まる | D9(ノード前に失敗を完結させる) |

#### 12.2 ユーザーの判断が必要な点(推奨を先頭に置く)
1. **[Q]1 マイルストーン番号**
   - (a) **M0114〜M0122**。ADR-0078 の M0112/M0113 予約を崩さない。
   - (b) M0112 から使い、ADR-0078、Conformance、UE-Template-Behavior を直す。
2. **[Q]2 ネイティブ shim の配布**
   - (a) **CI(windows-2022 MSVC)の専用ジョブで CMake ビルドし、版つき NuGet にしてローカルフィードから参照する**。開発者に C++ の環境が不要。
   - (b) ビルド済み DLL(1〜3 MB)を SHA 固定でコミット。
   - (c) ネイティブなし(AssimpNetter のみ)。FBX のスキン、SSC、in-between の品質が落ちる。
   - 影響: M0115 の開始時期と CI 構成。(a)/(b) なら ufbx を M0114 に前倒しする。
3. **[Q]3 ツールの無い環境の DCC ソース**
   - (a) **`bridge.mode=auto`**(ツールがあれば書き出し、無ければスナップショット、古ければエラー)。
   - (b) 常に live。
   - (c) 明示エクスポートのみ。
   - 影響: リポジトリサイズ、CI の再現性、Git へのスナップショットのコミットは §9 の doctor に従う。
4. **[Q]4 `.meta` が無いソース**
   - (a) **名前空間用の固定 `StableId` 定数と sourceUri から決定的な暗黙の ID を作り、IMP001 の警告を出す**。テンプレートは無改修で通る。
   - (b) エラーにして `meta init` を案内。
5. **[Q]5 gitignore した第三者資産の `.meta` をコミットするか**: コミットするとライセンス申告が共有されるが、ソースの無い人は IMP005 になる。`.gitignore` は C8 の 3 行形式に直す。
6. **[Q]6 向きの既定**
   - (a) **`auto`**(-F にそろえる)。
   - (b) `preserve`。
   - (c) +F にそろえる(テンプレートとテストを同時に直す必要があり、M0107 と絡む)。
7. **[Q]7 ルートモーション**
   - (a) **表示のみ**。
   - (b) Q64.64 シミュレーションに入れる(ADR-0083)。
   - (c) 後回し。
8. **[Q]8 リターゲット時期**
   - (a) **結び付け時**(組み合わせが自由。メモリは増える)。
   - (b) 取り込み時に焼く(資産をまたぐ順序付けが必要)。
9. **[Q]9 VRoid を忠実に描く範囲**
   - (a) **M0116 で alpha、doubleSided、occlusion、モーフを入れ、MToon はプラグインの ShaderGraph で近似する(アウトラインなし)**。
   - (b) 頂点形式(UV1、頂点色)と toon の shading model まで広げる。
   - (c) unlit で代用。
10. **[Q]10 USD の経路**
    - (a) まず Blender ブリッジで変換し、後で OpenUSD の C++ 変換器にする。
    - (b) NuGet の UniversalSceneDescription 7.3.4(USD の版が不明)。
    - (c) tinyusdz(スキン未完)。
    - **(d) `usd-core` の PyPI wheel と埋め込み CPython を PerImport で使う変換スクリプト。spike で実証してから採否を決める。推奨は (d) を spike し、結果が悪ければ (a)**。
11. **[Q]11 ライセンス関門の強さ**
    - 不明なもの: **(a) 開発中は警告、Release でエラー** / (b) 常にエラー。
    - VRM の `modification=prohibited`: **(a) 形を変えるパスを止める** / (b) 変換は改変に当たらないとみなす(法的判断はユーザー)。
    - NoAI は記録のみか警告か。
12. **[Q]12 出荷の根**
    - (a) **シーンから届くものと明示した `ContentRoots`**。
    - (b) `Assets` のすべて。
13. **[Q]13 テクスチャ**
    - デコーダー: **(a) マネージドの StbImageSharp(決定的。KTX2/WebP は shim)** / (b) ワーカーで WIC。
    - 扱い: **(a) モデルのサブアセット(テクスチャが重複する)** / (b) 独立資産。
    - 8192 超の扱い: **縮小して通す** / IMP803 で拒否。
14. **[Q]14 Unity の主経路**
    - (a) **Unity なしで `.unitypackage`、YAML、FBX を読み、Humanoid の `.anim` だけを batchmode ブリッジで扱う**。
    - (b) batchmode のみ(全員に Unity ライセンスが要る)。

### Critical Files for Implementation
- G:\GitHub\CSharpGameEngine\src\CSharpGameEngine.Cooking\AssetCooker.cs
- G:\GitHub\CSharpGameEngine\tools\CSharpGameEngine.Cook.Cli\CookerHost.cs
- G:\GitHub\CSharpGameEngine\tools\CSharpGameEngine.ContentCompiler\IncrementalContentCompiler.cs
- G:\GitHub\CSharpGameEngine\src\CSharpGameEngine.Content\ContentArtifact.cs
- G:\GitHub\CSharpGameEngine\src\CSharpGameEngine.Content\AssetAuthoringService.cs
- G:\GitHub\CSharpGameEngine\plugins\CSharpGameEngine.Navigation\Navigation.plugin.items
- G:\GitHub\CSharpGameEngine\packaging\CSharpGameEngine.Sdk\CSharpGameEngine.Sdk.csproj
- G:\GitHub\CSharpGameEngine\src\CSharpGameEngine.Runtime.Windows\StageSceneDataBuilder.cs
- G:\GitHub\CSharpGameEngine\src\CSharpGameEngine.Runtime.Animation\SkeletalAssetCache.cs