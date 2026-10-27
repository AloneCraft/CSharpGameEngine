# ADR-0090 — プレビューホストはランタイムと同じ glTF／スケルタル読み込みを共有する

- Status: Accepted
- Date: 2026-10-27
- Feature: F-L8-036、関連 F-L5（スケルタル）、ADR-0066
- 関連: ADR-0019/0020（VSIX の glTF 読み込みは別プロセス）、ADR-0089（アセットエディター）

## Context

Skeletal Mesh / Skeleton / Animation Sequence のビューアには、スキン付きメッシュ・マテリアル・クリップの評価・パレットが要る。
ゲームの実行時はこれを次の場所で行っている。

- `Runtime.Animation`（共有プロジェクト）: `SkeletalAssetCache`、`Skeleton`、`AnimationClip`。`Skeleton.cs` と `AnimationClip.cs` は `System.Numerics` と `Import.Gltf` の型だけに依存する。`SkeletalAssetCache` は ECS の `ContentRoot` に依存する。
- `Runtime.Windows`（共有プロジェクト）: `StageSceneDataBuilder` が glTF から `StageMeshCreateInfo`（スキン付き）と `StageMaterialCreateInfo`（テクスチャの焼き込み）を作る。`BakeTexture` は WIC（L0 の `WicImageDecoder`）でデコードする。

プレビューホスト（`EditorPreview.Host`、net10、別プロセス）は Contracts・Windows・Bridge.Hosting だけを参照し、これらを使っていない。VSIX（net472）は glTF を読まない（ADR-0019/0020）。
共有プロジェクトは ECS（`Entities`、生成コード）を前提にする「ゲームのアセンブリの一部」なので、ホストが丸ごと参照することはできない。

## Decision

1. **ホストは `Import`（L7）へのプロジェクト参照を持つ。** マネージドのみで、RuntimeIdentifier は設定しない（AssimpNet のネイティブ DLL はコピーされず、glTF の経路では読み込まれない。`Import.Gltf.Cli` と同じ扱い）。
2. **ホストは必要なソースファイルだけをリンクして同じ実装を使う。** 先例は `SceneJson` のリンク（ホスト）と `Skeleton.cs` / `AnimationClip.cs` のリンク（`Import.Tests`）。
   - `Runtime.Animation/Skeleton.cs`、`AnimationClip.cs`（そのまま）。
   - 新規の `Runtime.Windows/StageGltfAssets.cs`: `StageSceneDataBuilder` から ECS に依存しない部分（`BakeMaterial`、`BakeTexture`、キー生成、スキン付きメッシュの組み立て）を切り出した内部クラス。
   - ゲーム側の `StageSceneDataBuilder` は切り出した実装を呼ぶ。変換は 1 か所だけにし、動作を変えない。
3. **共有プロジェクト自体は参照しない。** `SkeletalAssetCache`（ECS の `ContentRoot`）と ECS 系のシステムはホストに入れない。ホストは絶対パスで直接読む。
4. **VSIX は glTF を読まない。** スキンやアニメーションの有無は glb/gltf の JSON を読む軽い確認（`GltfSkeletalProbe`）で決め、実際の読み込み・評価はホストだけが行う。
5. **ワイヤ（プレビュー protocol 11。スケルタル入力を追加したため、旧ホストとの混在を検出するよう版を更新する）:**
   - `RenderPreviewScene.Asset`（`MeshPath`・`AnimationPath`）: 絶対パス。ホストがスキン付きメッシュ・マテリアル（テクスチャ付き）を読み、明示メッシュの後ろに足す。
   - `RenderPreviewHostResponse.AssetSummary`: ジョイント名と親、クリップ名と長さ、スキン付きメッシュ数（Open と ReplaceScene の応答）。
   - `RenderPreviewFrame.Animation`（クリップ番号・時刻・ループ）、`ShowSkeleton`、`SelectedJoint`。評価はホストで行い、パレットは `RenderServer.SetSkinPalette` へ、ボーンの線はオーバーレイへ描く。

## Alternatives considered

- VSIX がパレットと姿勢を計算して毎フレームワイヤで送る: net472 に `Import.Gltf` の型を持ち込む必要があり、ADR-0019/0020 に反する。
- ホストが共有プロジェクト（`Runtime.Animation` / `Runtime.Windows`）を丸ごと取り込む: ECS・生成コードまで引き込み、ゲームのアセンブリの形に依存する。
- 変換を `Import`（L7）へ移す: WIC を使う Windows 固有の描画入力生成まで汎用インポーターに含めることになるため採用しない。上位から下位への依存そのものは禁止していない。
- `Windows`（L0）が `Import`（L7）を参照する: 同じく層に反する。
- スケルタル専用の別プロセス: プロセスが増えるだけで得るものがない。

## Consequences

- ホストの配布物に `Import` のマネージドアセンブリ（SharpGLTF.Core、AssimpNet のマネージド側、StbImageSharp）が増える。
- 姿勢評価とパレット計算はランタイムと同じソースを使う。共通の変換処理は `StageGltfAssets` に集約し、その変更をゲームとホストの両方に反映する。
- 同じメッシュ・アニメーションのパスを持つ `ReplaceScene` は、セッションが保持するメッシュ・マテリアル・テクスチャ・クリップ・姿勢を再利用する。床によって明示メッシュ数が変わるため、描画サーバーへ追加するときにメッシュとマテリアルの番号を再配置する。GPUリソースは新しいサーバーへ作り直すが、ソースの再インポートは行わない。異なるパスや、新しいセッションでの明示的な再読み込みは通常の読み込みを行う。
- 1 つのスキンを持つスケルタルアセットだけを扱う（ランタイムの `SkeletalAssetCache` と同じ制約）。2 つ以上のスキンは理由を表示する。
- 追補: 静的 glTF も protocol 12 の `Scene.StaticAssetPath` でホストに渡し、`StageGltfAssets.AppendStatic` をゲームと共有する。床切り替えでは読み込み済みCPUデータを再利用する。ホストは各ノードの変換を適用した全頂点の境界と、参照される各プリミティブを一度ずつ数えた頂点・三角形数を返す。VSIX は境界を初回のカメラ・床・照明に適用し、統計を表示する。静的 glTF は先頭プリミティブだけを読む既存ブリッジでの事前読み込みを要しない。
- 追補: protocol 13 ではスケルタルの応答にもレスト姿勢の境界と統計を含める。ホストは関節行列と頂点ウェイトを評価し、剛体ノードの変換も境界に含める。GPU のウェイト量子化を考慮した余白を加える。VSIX はこの境界を初回のカメラ・床・照明に使い、先頭プリミティブの事前読み込みを必要としない。この境界はレスト姿勢のものであり、アニメーション中の全姿勢を囲む保証はない。

## Verification

M0127 を参照。
