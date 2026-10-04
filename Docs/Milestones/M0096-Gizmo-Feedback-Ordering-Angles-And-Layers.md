# M0096 — ギズモの反映・上下反転の既定・RunsAfter 順序・角度の turns・プレビュー統合・層の修正
- 日付: 2026-10-01
- 状態: 完了

## 目的
M0095 の後にユーザーから受けた不具合報告と設計判断を、M0095 と同じ手順（修正前に失敗するテスト → 最小修正 → 全テスト → 文書の整合）で実装する。

## 進捗
| 項目 | 内容 | 状態 |
|---|---|---|
| A | ランタイムビューで、選択したギズモを操作しても確定まで画面のオブジェクトが動かない | 完了（82bf9b0、SDK 0.1.5 は d3b6c4e） |
| B | Options の上下反転（Look / Orbit）: チェックを入れたときの今の挙動を、チェックなしの既定にする | 完了（8589960） |
| C | 実行時に使われない `VsixStrings.en-US.resx`、obj だけ残った旧ディレクトリ、`UserEcsProject/Generated/` の削除 | 完了（dbb9fc1） |
| D | 整数 `[SystemOrder]` を廃止し `[RunsAfter]` の相対順序に一本化（同着は完全修飾名順） | 完了（122e316、ADR-0073、SDK 0.1.6 は 24bd2d3）。`[RunsBefore]` を追加した（下記） |
| E | シーン JSON の角度を turns（1 = 1 回転）で保存し、Inspector は度で表示 | 完了（d63ef02、ADR-0074、SDK 0.1.7 は 21e9b6f） |
| F | Scene ビューの 2 プロセス（ランタイムビューと編集ワールド）の統合 | 完了（9b17de6、ADR-0047 に追記、SDK 0.1.8 は 1688528） |
| G | Runtime.Physics / Character の Windows 依存の解消と、`Physics.Cli` の UserEcsProject 依存の除去 | 完了（6f8b056、SDK 0.1.9 は b22dcdd） |
| H | 角度の JSON のキーから C# の単位の語を外す（ユーザー判断「JSON のキーだけ別名」） | 完了（b7e6b1b、ADR-0074 決定 5、SDK 0.1.10 は e14259d） |

## A. ギズモ操作をランタイムビューに反映
- 原因: ドラッグ中、VSIX は動かしたオブジェクトの行列を `RenderPreviewFrame.Objects[].World` で送るが、ランタイムビュー（`RenderPreviewRuntimeSession.UpdateFrame`）は `Objects` を選択表示にしか使わない。ゲームのワールドは文書の値で描くので、テキストに書き込まれる確定までオブジェクトは動かず、ギズモだけが動いていた。
- 修正:
  - `RenderPreviewFrame.TransformOverrides`（ノードパスと親相対の Position / Rotation / Scale、RUF）を追加した。省略可能なので、プロトコルの版は 6 のまま。
  - VSIX はドラッグ中、ドラッグの根ノードの値を確定と同じ計算（`TryCalculateLocal(PreviewWorld, ParentWorld)`）で求めて毎フレーム送る。子孫は親相対なので送らない。
  - ホストは対象ノードの `Transform` の Position / Rotation / Scale だけを差し替え、Frame など他のメンバーは文書の値のまま、文書の編集と同じ反射の経路で設定する。
  - キャンセルではもとの文書を送り直す。
- 保険: ランタイムビューへ送る JSON を、Roslyn の解ではなくテキストのコンテナの現在値（開いている文書ではエディタのバッファ）から読むようにした。差分が空の解析結果でも、最後に送った JSON と本文が違えば送り直す。モデルの差分は Designer が扱う値しか見ないため。
- テスト:
  - `RenderPreviewRuntimeSessionTests.DragTransformOverrides_MoveTheObjectUntilTheDocumentIsResent`: 上書きで画像が変わり、キャンセルで送り直す元の文書で戻り、同じ値を確定した文書の画像と一致する。ホストの適用を外すと失敗する。
  - `RenderPreviewSceneBuilderTests.DragOverrides_AreParentRelativeAndKeyedByNodePath`: 回転した親の下でも、上書きは親相対の値になる。
  - `LogicalQuaternionConventionTests`: Designer の `System.Numerics` の回転（ヨー = U、ピッチ = R、ロール = F）と、エンジンの `EulerRuf` が同じ回転になる。
- 実機（CgeAutomation ハイブ、`PostMessage` によるマウス操作でフォーカスは奪わない、`PrimitiveShowcaseBoxScene`）:
  - U 軸の矢印を 70 px 上へドラッグした。ボタンを押したままの画像で箱・影・ギズモが一緒に上へ動いた（選択直後との差 129,698 px）。離した後の画像との差は HUD とハンドルのハイライトの 8,590 px だった。バッファは `Position.U` 0.5 → 0.6725。
  - R 軸のスケールハンドルのドラッグでも、ドラッグ中に箱が伸び、確定後の画像との差は 630 px。
  - 文書は「保存しない」で閉じ、ファイルは変わっていないことを確認した。
- 当時の観測: R 軸の矢印の先端（画面上で赤く描かれた画素）を押しても掴めなかった。→ [M0098](M0098-ThirdPerson-World-Grid.md) B で解決した。当時の画像を調べ直すと、ヒットテストは空ではなく、ドラッグは始まっていた。その後、スクリプトが送っていない位置の移動で大きく動き、取り消されていた（自動化の副作用と推測）。同じ判定には別の欠陥（レイと線分の最近点の計算、コーンの底の縁）があり、M0098 B で直した。

## B. 上下反転の既定
- 変更: Look（RMB）と Orbit（Alt+LMB）の垂直の基準を反転した。チェックなしで、下ドラッグで `Pitch` が下がる（下を向く）。それまでチェックを入れたときの向き。
- ピクセル完全一致（クリックした点がカーソルに追従する）は「右ドラッグで Yaw 増・下ドラッグで Pitch 増」でしか成り立たない。基準の垂直が逆になったので、条件を `InvertLookX && InvertLookY` に変えた。
- 設定の移行は版 3。値は基準に対する反転のまま変換しない。チェックの無い利用者は新しい既定の向きに、チェックを入れていた利用者はそれまでの既定の向きになる。版 1→2 の変換は版 1 のときだけ行う。
- テスト: 基準の向き（Look と Orbit）とピクセル一致の条件は修正前に失敗、修正後に成功する。版 2 の設定は変換されないことを固定した。
- Options の文言は「上下回転を反転する」で、向きを書いていないので変更しない。

## C. 削除
- `VsixStrings.en-US.resx`: 中身は日本語で、実行時には使われていなかった。アセンブリの `NeutralLanguage` が en-US なので、`ResourceManager` は en-US を要求されても本体の既定リソース（`VsixStrings.resx`、英語）を返し、en-US のサテライトを参照しない。参照箇所もテストも無い。
- obj だけ残った旧ディレクトリ: 承認時は 16 個と数えていたが、同じ基準（追跡ファイルなし、中身は 2026-08-21 の NuGet 復元の中間ファイル 5 個だけ、gitignore 対象）で 24 個あった。数え違いと判断し、24 個とも削除した。報告の後、ユーザーが 24 個の削除を承認した（2026-10-01「削除していい」）。
  - `src/` の `Compiler.Hlsl`、`Geometry`、`Graphics.D3D12`、`Import.Fbx`、`Import.Gltf`、`Platform.Windows`、`Scene`、`Spatial.Abstractions`、`Spatial.Blender`、`Spatial.Fbx`、`Spatial.Gltf`、`Spatial.Unity`、`Spatial.Unreal`
  - `tests/` の `Compiler.Hlsl.Tests`、`Geometry.Tests`、`Graphics.D3D12.Tests`、`Import.Fbx.Tests`、`Import.Gltf.Tests`、`Scene.Tests`、`SceneAuthoring.Cli.Tests`、`Spatial.Tests`、`UserGameProject.Tests`
  - `tools/CSharpGameEngine.SceneAuthoring.Cli`、`UserGameProject/`
- `UserEcsProject/Generated/`: 2026-08-21 の生成物（存在しない `TransformPrecisionGenerator` の出力を含む）。プロジェクトはここへ出力していない。
- 文書: `Architecture.md` が `UserGameProject/` を実在の `.csproj`、`SceneAuthoring.Cli` を現役のブリッジとして書いていたので、現在のもの（`UserEcsProject/`、ThirdPerson テンプレート、現在の CLI ブリッジ）に直した。`UserEcsProject/README.md` の `Generated/` への参照も直した。README の他の部分は以前から古い（別タスクとして提案）。
- 全テスト成功。ビルド後も削除したディレクトリは再作成されない。

## D. 実行順序を相対指定に（ADR-0073）
- 移行前の実効順序を、生成されたループ（UserEcsProject、ThirdPerson テンプレート、stages 10 個）から Phase ごとに抽出して記録し、移行後と比べた。
- 規則: Phase の順は固定。Phase の中は `[RunsAfter]` / `[RunsBefore]` の辺を守るトポロジカル順、辺の無いもの同士は完全修飾名順。起動シーンは FrameInput の先頭に `startupOrder` → 完全修飾名の順。
- **`[RunsBefore]` を追加した（指示は `[RunsAfter]` だったが、それだけでは書けない順序があった）**:
  - ThirdPerson テンプレートの `ThirdPersonAnimation` は `LocomotionAnimator` を書き、エンジンの `LocomotionAnimation` がそれを読む。エンジンはゲームの型を参照できないので、ゲーム側から「前」と書くしかない。名前順では `CSharpGameEngine.…` が `CgeThirdPerson.…` より前になり、1 フレーム遅れる。
  - Runtime.Physics / Character / Animation から Runtime.Windows の型を参照しないため（G）。
  - 代わりの案（`ThirdPersonAnimation` を PostPhysics に移す）は、この例では同じ結果になるが一般的な手段にならない。
  - 報告の後、ユーザーは損得を確認して残すと決めた（2026-10-01「残す」）。
- 移行:
  - エンジンは移行前の順序を鎖として書いた。物理: `PhysicsSyncBegin` → `PhysicsBodySync` → `StaticMeshColliderSync` → `PhysicsStep` → `PhysicsTransformWriteback` → `CharacterMovementUpdate`。Presentation: `SpringArmUpdate` → `RuntimeSystems.cs` の宣言順 → `SkeletalMeshSetup` → `LocomotionAnimation` → `SkinnedMeshSync` → 音 → `Present`。
  - ゲームは実際の依存を書いた: `ShowcaseControls` / `ThirdPersonControls` は `Window` の後、`ThirdPersonAnimation` は `LocomotionAnimation` の前、`ShowcaseDashboard` は `LevelLoad` の後。
- 実効順序の変化（意図したもの）:
  - FrameInput: 起動シーンがすべて先頭に来た。以前は `startupOrder` と整数の順序値の比較で、`Window` や `ShowcaseControls` が起動シーンの間に挟まっていた。
  - Presentation: `ShowcaseDashboard` は `Present` の後（描画先は Win32 の別パネルで、D3D のフレームとは無関係）。`ThirdPersonAnimation` は `SkeletalMeshSetup` の後（`LocomotionAnimation` の前は保たれる）。
  - 物理と、他のエンジンの順序は変わらない。
- 診断: SYSGEN033（対象が System でない）、SYSGEN034（循環）、SYSGEN035（Phase の順と矛盾）。`[SystemOrder]` は `[Obsolete(error: true)]`（CS0619）。SystemOrder 専用の SYSGEN022〜025 と、スケジュール専用の SYSGEN030 / 031 は削除した。
- `[RunsAfter]` は `[ScheduledSystem]` を暗黙に付けない。書き込み競合の検査（SYSGEN032）は、スケジュール外の System を経由する経路も順序として数える。
- テスト:
  - `GameSystemOrderTests` を書き直した（辺が名前より優先、辺の無いものは名前順、起動シーンが先頭、SYSGEN033〜035、`[SystemOrder]` が CS0619）。`GameSystemScheduleTests` に 3 件追加・2 件更新。修正前の生成器では 16 件中 11 件が失敗する。
  - `GeneratedSystemOrderTests`（Windows.Tests）: UserEcsProject の生成された System 集合から、Phase ごとの実行順序を固定した。
  - 生成された `RunFrame` の引数順や Archetype の番号を直接書いていた既存テスト 3 件を、新しい順序に合わせた。
- 調査中の誤り: 移行前の順序の抽出で、テンプレートと stages の生成物が「無い」と出た。`-p:Platform=x64` の違いを疑ったが、実際は抽出スクリプトが Windows のパス長制限（260 文字）でファイルを読めていなかっただけだった。

## E. シーン JSON の角度を turns で保存（ADR-0074）
- 角度のメンバーとコンストラクタ引数に `[Angle(AngleUnit)]`（Contracts）を付けた。Numerics が Contracts を参照するようにした。
- `schemaVersion` 2 = turns。読み込み（生成器・プレビューホスト）は版 1 と 2 を受け付け、書き出し（変換器）は常に版 2。生成されるコードとゲームの値は、今までどおりメンバーの単位。
- 数値は、読み戻して同じ float になる最短の表記で書く。
- 調査中の誤りと修正:
  - 最初は float だけで変換したが、テンプレートの移行で 2 ファイルが「値が変わる」として止まった。float の turns に 2π や 360 を掛けても、元の単位の float に届かない値がある（`RotationRateDegrees` の 500、`1.5707965` ラジアン）。変換の 2 メソッドだけを double にし、NUM001 の例外（`[ExternalNumericAdapter]`）とした。
  - 共有ソースは Contracts を参照しないコンパイル（生成器、CLI）にも入る。そこで属性は、`CGE_NUMERIC_ADAPTER` を定義したコンパイル（SceneModel 本体、VSIX、プレビューホスト）だけで付ける。
  - 度の表示で `90` が `9E+01` になる不具合をテストで見つけて直した。
- 移行: `SceneJson.Cli upgrade <game .csproj> [--write]` を追加した。角度の数値と版だけを置き換え、読み直して全 Component の値が元と同じでなければ書かない。
  - リポジトリの全シーン（UserEcsProject 16、ThirdPerson テンプレート 3、stages 10）と新規シーンの項目テンプレートを版 2 にした。
  - 角度を含むのは 6 ファイルで、`EulerRuf` 123 か所とほかの角度 5 か所。値は 1 ビットも変わっていない。
- Inspector: 角度の数値は度で表示・入力し、行の名前に ` (°)` を付ける。読み取り専用の角度（`EulerRuf` の R / U / F など）も度で表示する。ギズモの HUD は以前から度。
- Designer で版 1 の文書を編集すると、その編集で版 2 になる（版番号と角度の数値が変わった値として書き戻される）。
- テスト:
  - `SceneJsonAngleTests`（CodeAnalysis）: 版 1 と版 2 の同じ値、変換器の版 2・turns、移行が数値と版だけを書き換えること、float だけでは届かない値も移行で変わらないこと、スキーマの説明。
  - `SceneJsonAnglesTests`（SceneModel）: 表記の最短性と、2 万個の float の往復。
  - `SceneInspectorModelTests.Build_AngleMembers_ShowAndTakeDegrees`（VSExtension）。
  - 既存の `SceneJsonRuntimeValuesTests` は、プレビューホストが turns の文書から生成器と同じ値を作ることを確かめる（文書の版を渡すように直した）。
- ユーザーの判断事項だったもの: JSON のキーが C# のメンバー名のままで、`FovYRadians` や `SunDiskAngleDegrees` のキーに turns の値が入っていた。ユーザーは「JSON のキーだけ別名」を選んだ（H で実施）。

## F. プレビューの 2 プロセスを 1 つに（ADR-0047 に追記）
- ランタイムビューのレンダーサーバが、同じ ALC・同じブリッジで編集ワールドも動かす。`Initialize(scene)` をビューの後に呼び、15Hz の `TickRealtime` はセッションのタイマーで回し、`Dispose` で `Shutdown` する。2 つの World は分けたまま。tick と全ての要求は同じロックで直列化した。
- 編集ワールドが起動できない（ビルドにシーンが無い等）ときも、ビューはそのまま動く。tick 中の例外は編集ワールドだけを止める。
- Spatial / Physics のデバッグの問い合わせ先を、VSIX が保持するレンダーサーバのプロセス ID（`RenderPreviewHostClient.ProcessId`）にした。
- 同じプロセスでビューの World も Spatial のスナップショットを公開するので、World を指定しない問い合わせには編集ワールドを返す（ブリッジが `PreferForSpatialDebug` で既定にする）。Open の応答に編集ワールドの ID（`EditorSpatialWorldId`）を載せた（省略可能な項目なので、プロトコルの版は据え置き）。
- 削除: ホストの `--assembly` モード、VSIX の `EnsureEditorPreviewHost`・`EditorWorldProcess`・別プロセスの停止処理、`EditorWorldShutdownTests`（対象のプロセスが無くなった）。
- テスト:
  - `Open_RunsTheEditorWorldBesideTheViewUntilDisposed`: 編集ワールドが 15Hz で tick し、Dispose で止まる。
  - `RuntimeViewHost_AnswersSpatialDebugWithTheEditorWorld`: 実際のホストプロセスの Spatial のパイプが、編集ワールドの ID で答える。
- 実機（CgeAutomation）: `PrimitiveShowcaseBoxScene` を開くと、Visual Studio の子のホストプロセスは 1 つになった（統合前は同じ操作で 2 つ）。そのプロセスの Spatial のパイプは `ok` とノードを返した。
- 別の現象として見つけたもの（未解決、別タスクに切り出した）: テストプロセスの中で `HostCrash` のテストがホストを強制終了した後、新しいホストでランタイムビューを描画すると、スワップチェーンの作成が `E_ACCESSDENIED`（0x80070005）で失敗する。
  - 編集ワールドを止めても起きるので、統合とは無関係。
  - 別の新しいウィンドウハンドルでも起きるので、ハンドルの再利用ではない。
  - このため、上のプロセスのテストは描画を含めない。描画後にビューの World と並んで公開される状況での既定の World の選択は、自動テストで確かめていない。

## G. 層の修正（Windows 依存と Physics.Cli のゲーム依存の解消）
- 物理ドメイン:
  - フレームごとのドメインの表とアクティブなフレームを `PhysicsDomainRegistry`（Runtime.Physics）に移した。`EngineHost` はそれを持って `IPhysicsHost` を実装するだけになった。
  - 物理と Character の System は Context として `IPhysicsHost` を受け取る。
  - 生成器の規則を足した: Context の型が、同じコンパイルの `EngineHost` が実装するインターフェイスなら、`EngineHost` として扱う。これでループの引数も Bake ホストの条件（Context は EngineHost だけ）も変わらない。
- `StaticMesh` と `MeshMobility` を Entities の共有ソースへ移した。名前空間 `CSharpGameEngine.Runtime.Windows` は、シーン JSON の完全修飾名を壊さないため維持した。
- `RuntimeWorldTransform` を 2 つに分けた。World の解決（`TryResolveWorld` / `TryResolveFrame`）は Entities の `FixedTransform.cs` に置いた。カメラ相対の部分は Runtime.Windows に残した。
- `PhysicsAabb` を ECS に依存しない `PhysicsAabb.cs` に分けた。
- `Physics.Cli`:
  - UserEcsProject を参照しなくなった。cook / prefracture / inspect は、ECS を使わない物理ソース（`PhysicsShapeAssets` / `PhysicsCooking` / `PrefractureCooking` / `PhysicsAabb`）を直接取り込む。
  - `bake --assembly <game.dll> <scene> <output> <ticks> <hash>` は、ゲームのビルドを収集可能な ALC に読み込む（`GameBake`）。そのゲームの `ContentRoot` をビルドのフォルダに向けてから、生成された `GeneratedSceneBakeHost.TryBake` を呼ぶ。tick 数の上限はゲームの `TryBake` が確かめる。
- VSIX（`PhysicsSceneBakeService`）:
  - ホストはエンジンだけのハッシュ（src、Physics.Cli、Bridge.Hosting、ルートのファイル）でビルドする。
  - シーンのゲームプロジェクトは、プロジェクトごとのハッシュ（エンジン、build、そのプロジェクトと Assets。Bake の出力は除く）で `.artifacts/PhysicsBakeGame/<hash>` にビルドする。このハッシュが cache の dependency hash になる。
  - ゲームのビルドは、ShaderGraph.Cli のように MSBuild タスクでビルドされるツールのために、ホストと同じくソリューションを復元してから行う。ビルド後に `-getProperty:TargetPath` でアセンブリを得る。
  - プレフラクチャの inspect はホストだけを使う。
- これまでの不具合（修正で解消）: 依存ハッシュは UserEcsProject に固定されていた。そのため、他のプロジェクトのシーンでは、そのプロジェクトの変更がハッシュに入らなかった。また、ホストは UserEcsProject の生成コードしか持たないので、他のゲームのシーンは「not part of this compilation」で失敗する構成だった。
- テスト（修正前に失敗することを確認）:
  - `CreateRequests_HashesTheScenesOwnProjectNotUserEcsProject`: 修正前は、UserEcsProject の変更が別プロジェクトのハッシュを変えて失敗した。
  - `EngineLayerSpec`: HEAD では、物理と Character の System の 6 か所が `EngineHost` を名指しし、Physics.Cli が UserEcsProject を参照していた。
  - 追加したテスト: `HostHash_CoversTheEngineButNoGame`、`Bake_BuildsEachGameOnceAndBakesWithItsBuild`。
- 実行で確かめたこと:
  - CLI で UserEcsProject の `PhysicsGroundScene` を bake した（exit 0）。存在しないシーンは exit 4、存在しないアセンブリは exit 2、`--assembly` なしは exit 1 になった。
  - ThirdPerson テンプレート（`CgeThirdPerson`）の `Lvl_ThirdPerson` も bake できた。Assets ありは 1.48MB、Assets を除いたコピーは 1.9KB になった。衝突メッシュはゲームのビルドのフォルダから読まれている。
  - VSIX のサービスを既定の経路（ホストのビルド → テンプレートのゲームのビルド → 常駐ブリッジで bake）で一度実行し、成功した（37 秒、一時テストで確認し、出力とテストは削除した）。成果物のビルドと VS のビルドで bake の結果はバイト単位で同一だった。
- 残り: リポジトリの外（SDK パッケージのワークスペース）では、これまでどおり Bake にはエンジンのソースが必要。ホストがゲームに依存しなくなったので、パッケージに同梱したホストを使う形にできる（未実施）。

## H. 角度のキーの別名（ADR-0074 決定 5）
- ユーザーの判断（2026-10-01）: C# のメンバー名は変えず、JSON のキーだけを別名にする。
- 規則: 角度（`[Angle]`）のメンバーとコンストラクタ引数のキーは、C# の名前の末尾の `Radians` / `Degrees` を外したもの。外した名前が空のとき、または同じ型の別のメンバーか public コンストラクタの引数が使っているときは、C# の名前のまま。規則は `SceneJsonAngles.KeyFor` の 1 か所にある。
- 規則を各メンバーに書く属性ではなく、規則にした理由: 新しい角度のメンバーで別名の付け忘れが起きず、「JSON のキーは turns 以外の単位を名乗らない」が常に成り立つ。
- 変更:
  - 生成器（`SceneJsonSceneAnalyzer`）は、別名と C# の名前のどちらでも読む。同じメンバーを両方で書くと SCENE204 になる。生成する C# は C# のメンバー名で書く。
  - 変換器はキーを別名で書く。スキーマは別名で載せ、説明に C# のメンバー名を書く。
  - プレビューホスト（`SceneJsonRuntimeValues`）も、リフレクションで同じ規則を使う。
  - Inspector の行の名前は別名（`Fov (°)`）にした。編集の対象とツールチップは C# の名前のまま。
  - `schemaVersion` を 3 にした。別名を知らない古い生成器に「版が対応外」と報告させるため。版 1 と 2 も読む。
  - 移行コマンド（`upgrade`）は、版 1 と 2 を版 3 にする（版 1 は数値も turns にする）。キーの名前の範囲だけを書き換え、書き換えた後に読み直して値が同じことを確かめる。
- 移行: リポジトリの 29 シーン（UserEcsProject 16、ThirdPerson テンプレート 3、stages 10）を版 3 にした。全ファイルで移行の検証が通った。変わったのは版の行と、テンプレートの 5 つのキー（`SunDiskAngle`、`WalkableFloorAngle`、`RotationRate`、`ControlYaw`、`FovY`）だけ。新規シーンの項目テンプレートも版 3 にした。
- テスト（修正前に失敗することを確認）:
  - `SceneJsonAngleTests`（CodeAnalysis）の 7 件:
    - 別名と C# の名前で同じ値になる
    - コンストラクタ引数の別名と、名前が使われているときの扱い
    - 両方のキーの拒否
    - 変換器の版 3 と別名
    - 版 1 と 2 からの移行
    - スキーマ
  - `SceneJsonRuntimeValuesTests.AngleKeys_ReadWithoutTheUnitWordAndByTheCSharpName`: ホストの変更を退避すると「'Yaw' is not a settable member」で失敗した。
  - `SceneInspectorModelTests.Build_AngleMembers_ShowAndTakeDegrees`: 行の名前を `Fov (°)` に変更した。
  - 追加: 版 2 の文書への編集のマージ結果が、版 3 の正準形で同じ値になる（`EditMerge_OverAVersionTwoDocumentWritesTheCurrentForm`）。
- 既存のテストの更新: 対応しない版の例を 3 から 4 にした。コミット計画のテストのひな形を版 3 にした。版 2 の文書を編集すると版の行も 3 になるためで、上の追加テストで確かめている。
