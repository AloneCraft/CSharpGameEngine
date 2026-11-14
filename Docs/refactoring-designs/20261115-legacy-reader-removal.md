# 旧版リーダーの削除と互換の残りの整理

## 背景
[ADR-0098](../Decisions/ADR-0098-Product-Goal-Phases-And-Compatibility.md) §5 の規則 2 は「変換は CLI が行う 1 方向の処理とし、旧版を読む経路をランタイムと生成器に残さない」と決めた。所有者の依頼(2026-11-15):「旧版リーダーを変換ツールへ移して削除して。それも含めて全般的にリファクタリングして」。

## 現状の観察
旧版を読む経路(2026-11-15、作業ツリーで確認):

| 対象 | 場所 | 旧版の扱い |
|---|---|---|
| シーン JSON v1・v2 | `src/CSharpGameEngine.SceneModel/Json/SceneJsonSceneAnalyzer.cs:157`(`AngleReading(DocumentVersion>=TurnsSchemaVersion)`)、`SceneJsonAngles.cs`、`SceneJsonAngleUpgrade.cs` | 共有ソースの解析器が v1〜v3 を読む。SceneModel は生成器・VSIX・CLI の全部に入るので、ビルドとエディターも旧版を読める。`SceneJson.Cli upgrade` はこの解析器で旧版を読み、書き換え、読み直して同じ意味かを確かめる |
| Snapshot v1 | `src/CSharpGameEngine.Entities/Snapshot.cs:69-85,250-283`、`src/CSharpGameEngine.CodeAnalysis/GameSystemSnapshotEmitter.cs:43-90` | `TryRead` が版 1 と旧スキーマ指紋を受け付け、生成コードが旧構造の記述子(`SnapshotLegacy*`、`SnapshotDefaultLegacy*`)で読む |
| WorldCell v1 | `src/CSharpGameEngine.World/WorldCellArtifact.cs:86,177` | 版 1 の 16 バイト固定の座標を読む |
| `.cgephyscache` スキーマ 2・3 | `src/CSharpGameEngine.Runtime.Physics/PhysicsCache.cs:39,58-62,175-209,304,362-387` | 読み手が 2〜4、書き手も 2〜4 を扱う |
| glTF の旧メタデータ | `tools/CSharpGameEngine.ContentCompiler/ModelImportCommands.cs:72`(IMP008) | 読むときに黙って移行する。詳細は未調査 |

すでに規則に合っているもの(変えない):
- `guid:` の旧サイドカー: 明示の「Convert legacy metadata」だけが変換する(`AssetAuthoringService.TryMigrateLegacy`)。
- `LightApiMigrationAnalyzer`: 消した書き方を診断で知らせる(ADR-0098 §5 の B)。
- `EnvironmentLightingSettings.Legacy`、`ExposureMode.Legacy`: ファイル形式ではなく描画の設定の名前。

進め方に関わる事実:
1. **作業ツリーに未コミットの変更が 306 ファイルある。** 上の表の全ファイル(`Snapshot.cs`、`GameSystemSnapshotEmitter.cs`、`WorldCellArtifact.cs`、`PhysicsCache.cs`、`SceneJson*.cs`)がその中に入っている。最後のコミットは 2026-10-29 で、Snapshot v1 と WorldCell v1 を読む処理そのものが、未コミットの M0128 の作業で入った。
2. **Snapshot v1 の読み込みは、2 週間前に承認された設計の要件である**(下の節)。

## 過去設計から引き継ぐ仕様
- [20261031-static-production-flow.md](20261031-static-production-flow.md)「Snapshot移行の具体化」:「旧形式は既存の構造識別と読み方を維持し、新形式への明示的な移行を行う」。**この文書はこの要件を置き換える**(ADR-0098 §5 の D「1.0 まで保証しない」と規則 2 による)。同じ設計の「旧データと新形式を混同しない」「異なる数値設定間でビット一致は要求しない」は引き継ぐ。
- ADR-0070 §7: C# シーンの切り替えと同じ形にする(変換ツール + 残っていればエラーで変換コマンドを示す)。
- ADR-0077 M2: 型名の変更でスキーマ指紋が変われば、Snapshot と物理キャッシュは拒否される。今回も同じ扱い。

## 目的
1. ランタイム・生成器・VSIX が読む形式を最新版だけにする。
2. 旧版のファイルは、直し方を書いたエラーで止める。
3. 変換が要る対象(シーン JSON)は、旧版を読むコードを `SceneJson.Cli` の中だけに置く。

## 非目的
- 名前空間 `CSharpGameEngine.Runtime.Windows` の改名(ADR-0098 §5 規則 5)。全シーン JSON の型名とスキーマ指紋が変わるので、別の設計にする。
- `Roadmap.md` と `Architecture.md` の経過報告の整理(段階 A の出口条件。文書だけの別作業)。
- 未コミットの M0128 の作業の検証やコミット。
- `Templates/ThirdPerson` などの理想コードのフォルダー(ビルドしない見本。`schemaVersion` が 3 でないが、解析器を通らない)。

## 提案設計

### 1. シーン JSON
- `SceneJsonSceneAnalyzer` は `schemaVersion` が 3 のときだけ読む。それ以外は診断(既存の版不一致の診断があれば使い、無ければ新しい SCENE 番号)で止め、メッセージに `SceneJson.Cli upgrade <project>` を書く。
- v1・v2 の角度の読み方と旧キー名の対応(`AngleReading` の旧分岐、`SceneJsonAngleUpgrade`)を、`tools/CSharpGameEngine.SceneJson.Cli` の中へ移す。SceneModel の共有ソースからは消す。
- `upgrade` の「読み直して同じ意味か」の確認は、旧版側を CLI の中の旧リーダーで、新版側を共有の解析器で読んで比べる。

### 2. Snapshot
- `EcsSnapshotArtifactCodec.TryRead` は版 2 だけを受け付ける。`LegacySchemas` の引数、`ReadSingle`/`ReadDouble` の `Legacy` 分岐、`FormatVersion` による分岐を消す。
- 生成器の `SnapshotLegacy*`、`SnapshotDefaultLegacy*`、`SnapshotSchemaMap.Legacy*` を消す。
- 変換ツールは作らない。旧構造の読み方はゲームごとの生成コードで、単体のツールにできない。1.0 までセーブは保証しない(ADR-0098 §5 の D)。版 1 は `UnsupportedVersion` で、メッセージに「この版のエンジンで保存し直す」と書く。

### 3. WorldCell
- 版 2 だけを読む。`TryReadCoordinates` の `Version==1` の分岐を消す。
- 前提(実装の最初に確かめる): WorldCell の成果物はベイクで作り直せる。作り直せないものがリポジトリにあれば、ここで止めて報告する。

### 4. 物理キャッシュ
- 読み手も書き手もスキーマ 4 だけ。`OldestReadableSchemaVersion`、`TryAccept` の旧スキーマの受け入れ、`Schema>=3`/`>=4` の分岐、書き手の旧スキーマの検査を消す。
- 決定性に縛られた記録なので変換しない(ADR-0098 §5 の E)。合わなければ今と同じエラーで、ベイクし直す。

### 5. glTF の旧メタデータ(IMP008)
- 先に調べる。読むときに書き換えているなら、`guid:` と同じ明示の変換へ寄せる。読むだけで書き換えていないなら、旧形式をエラーにして変換の操作を示す。

### 6. そのほかの互換の残り
- `GameSystemLoopEmitter.BuildLegacyFrameParameterList` の `RunFrame` の旧オーバーロード: 使っているのは `tools/CSharpGameEngine.Ecs.Benchmarks/Program.cs:112` だけ。ベンチマークを新しい形に直して消す。
- `[ReadOnlyComponent]`(ADR-0077「受け付けるが不要。廃止は後の段階」): 使用箇所を調べ、リポジトリ内を `in T` に直してから、属性を診断つきで廃止する。

### 7. 版の付け直し(ADR-0098 §5 規則 4)
- `build/CSharpGameEngine.Version.props` の `CgeEngineVersion` を `0.1.49` から `0.1.0-dev.50` にする(N は今の番号を引き継ぎ、キャッシュの古い番号と重ならないようにする)。
- VSIX の `source.extension.vsixmanifest` を `0.1.24` から `0.1.0.25` にする。番号が下がるので、CgeFixed の実験インスタンスでは一度アンインストールしてから入れ直す必要がある。所有者の F5 用なので、実行中の devenv を確かめ、配置は所有者の確認の後に行う。
- `PluginVersion.TryParse` は数字だけを受け付け、`0.1.0-dev.50` では解析に失敗して PLUGIN005 の検査が黙って飛ばされる。プレリリースを解析し、本体の番号で比べ、同じ本体ならプレリリースを正式版より前に並べる。エンジンの版が解析できないときは黙って飛ばさず診断にする。試験: `PluginManifestAnalyzerTests` にプレリリースの比較と解析失敗の行を足す。
- 番号を直書きしている試験(`SceneProjectOptions.EngineVersion="0.1.16"`、`PluginManifestAnalyzerTests` の `0.1.15`/`0.1.16`)は、範囲の検査の試験としての意味を保って書き直す。
- `GameProjectGenerator.VersionOfPackage` がパッケージのファイル名からプレリリースつきの番号を正しく取り出すことを、試験で確かめる。

「全般的なリファクタリング」は、この文書では上の「互換のために残っているもの」と版の付け直しに限る。根拠のない広い書き換えはしない。

## 影響範囲
- 共有ソース: SceneModel(生成器、VSIX、CLI、SceneModel.Tests)、Entities(全ゲーム)、Runtime.Physics、World。
- 生成コード: Snapshot の生成部分の文字列が変わる。生成器のスナップショット試験の期待値を直す。
- 試験: 旧版を読むことを確かめている試験(`SnapshotGeneratorTests`、`WorldCellArtifactTests`、物理キャッシュのスキーマ 2 の再生、シーン JSON v1/v2 の読み込み)は、「旧版を拒否する」試験へ書き換えるか、CLI の変換の試験へ移す。
- SDK の版: 公開前なので製品の版は上げない(ADR-0098 §5 規則 4。2026-11-15 に所有者の指示で改めた)。この作業で版を 0.1.0 系へ付け直す(提案設計 7)。`CHANGELOG.md` には移行の手順を書く。
- 文書: ADR-0070(v1・v2 を読むという記述)、ADR-0095、`Ecs-Runtime.md` の Snapshot 契約、`Numeric-Persistence.md`、`Physics.md`、M0113(スキーマ 2 も読む)、M0128、機能台帳の F-L1-011・F-L1-012・F-L6-007、ADR-0098 の該当の段落。

## 移行手順
小さく分け、1 つずつ試験を通す。
1. 基準を取る: 触る前に、関係する試験プロジェクト(SceneModel、Entities、World、Runtime.Physics 系、CodeAnalysis、SceneJson.Cli)を Debug で実行し、結果を記録する。未コミットの作業のせいで元から落ちている試験を区別するため。
2. 物理キャッシュ(4)。
3. WorldCell(3)。
4. Snapshot(2)。
5. シーン JSON(1)。リポジトリ内の全シーンが v3 であることを先に確かめる。
6. IMP008(5)の調査と対応。
7. そのほか(6)。
8. 版の付け直し(7)。パッケージの作成と、リポジトリの外の生成ゲームの復元・ビルドで確かめる(`vsix.project-template` の Stage)。
9. CHANGELOG、文書、台帳。

## テスト計画
[段階別検証規約](G:\AI\Skills\instructions\StagedVerification.md)の工程 1 と 3 を使う。
- 工程 1: 各手順で、変えたプロジェクトの試験。旧版の入力が、指定のエラーコードとメッセージで拒否されること。最新版の往復が変わらないこと。
- 工程 3: 生成器を変える手順 4・7 で、`UserEcsProject` と ThirdPerson テンプレートのビルド。`SceneJson.Cli upgrade` は v1・v2 の見本から v3 への変換と、変換後の解析の一致。
- 最後に `dotnet test CSharpGameEngine.slnx -c Debug`(GPU を除く)。WARP の Stage と実 VS の確認は、描画とエディターの経路を変えないので実行しない(未検証として報告する)。

## リスクと対策
| リスク | 対策 |
|---|---|
| 未コミットの 306 ファイルの上に重ねるので、私の変更と M0128 の作業を後で分けにくい | 手順 1 で基準を取る。私が変えたファイルと行を最終報告に列挙する。所有者が先に M0128 をコミットするなら、それを待つ |
| 基準の時点で試験が落ちている | 落ちている試験を記録し、今回の変更で新しく落ちたものだけを直す |
| リポジトリの外に v1・v2 のシーンや版 1 の Snapshot を持つゲームがある | シーンは `upgrade` で直せる。Snapshot と物理キャッシュは作り直しになる。CHANGELOG に書く |
| `upgrade` の確認が弱くなる(旧版を読む解析器が共有でなくなる) | CLI の旧リーダーに、今の解析器の旧分岐の試験をそのまま移す |

## 未決事項
- [x] Snapshot v1 は変換せずに捨てる(20261031 の承認済み設計を置き換える)。
- [x] 未コミットの M0128 の作業の上にこのまま重ねる。
- [x] `[ReadOnlyComponent]` の廃止をこの作業に含める。
- [x] SDK の版: 上げない。公開まで 0.1.0 に固定し、パッケージは `0.1.0-dev.N`、VSIX は `0.1.0.N`(2026-11-15、所有者の指示)。
- [x] CgeFixed の VSIX を一度アンインストールして `0.1.0.N` を入れ直す(番号が下がるため)。実行中の devenv が無いことを確かめてから行う。

## 合意状態
合意済み(2026-11-15。所有者:「すべて『はい』で進める」)。

## 実装の記録(2026-11-15)
基準: 変更前に関係する試験を Debug で実行した。SceneModel 217、CodeAnalysis 413、ContentCompiler 34、Entities 84、World 45 は全件合格。Windows(物理・Runtime の絞り込み)は 634 件中 3 件が変更前から失敗(`TemplateLightingSceneTests` の Lvl_ThirdPerson、`SceneJsonRuntimeValuesTests` の EngineShowcaseScene、`RenderPreviewLightingSessionTests.WARP_LegacyEquivalentLightingMatchesTheLookWithoutAnyLighting`。未コミットの M0128 の作業による既存の失敗)。証跡は `F:\CodexTemp\cge-legacy\baseline`。

設計どおりに実施したもの:
- 物理キャッシュ: 読み手・書き手・再生をスキーマ 4 だけにした(`PhysicsCache.cs`)。旧スキーマを読む試験は「書き手・読み手・再生が拒否する」試験へ置き換えた。
- WorldCell: 版 2 だけを読む。版 1 の試験は拒否の試験へ。
- Snapshot: 実行時(`Snapshot.cs`)の版 1 と旧スキーマ指紋の受け入れ、`Legacy` 引数、`FormatVersion` を削除。生成器の旧版の記述子(`SnapshotSchemaMap`、`SnapshotShapeBuilder` の Original/LegacyDefault、`SnapshotGraphCodecBuilder` の `Legacy` 引数)を削除。版 1 は `UnsupportedVersion` で「保存し直す」と案内し、World を変えない。
- `RunFrame` の旧オーバーロード: 削除。ベンチマークと試験 20 か所を `GeneratedSystemSet` へ。
- `[ReadOnlyComponent]`: `[Obsolete(error:true)]` で書き換え方を示す CS0619 にし、生成器の扱いと SYSGEN029 を削除。
- 版: `0.1.0-dev.50` と VSIX `0.1.0.25`。`PluginVersion` は SemVer のプレリリースを比べ、エンジンの版が読めないときは PLUGIN006(検査を黙って飛ばさない)。同梱・見本のプラグイン 5 つの下限を `>=0.1.0-dev.50` へ。

設計から変えたもの(理由つき):
- **シーン JSON の旧版の変換を CLI へ移した。** 変換処理 `SceneJsonAngleUpgrade.cs` は `tools/CSharpGameEngine.SceneJson.Cli/` へ移動し、生成器・VSIX が取り込む共有ソース(SceneModel)には無い。試験は SceneModel.Tests がこのファイルを CLI のフォルダーから取り込む。ただし、旧版を読む入口 `AnalyzeEarlierVersion` と、版 1 の角度の単位で読む分岐 5 か所は、解析器の値の組み立て(約 400 行)のメソッドの途中にあり、切り出すと解析器を CLI へ丸ごと複製することになる(読み手が 2 つになり、規則 2 が防ぎたい食い違いを自分で作る)。そのため、この部分だけは共有ソースに残し、`#if CGE_SCENE_UPGRADE` で囲んだ。記号を定義するのは SceneJson.Cli・SceneModel(単体のコンパイル確認用)・SceneModel.Tests だけで、生成器と VSIX のビルドには入らない。生成器の DLL に `SceneJsonAngleUpgrade` 型と `AnalyzeEarlierVersion` が無いことを `TheGeneratorAssemblyContainsNoEarlierVersionReader` が、ファイルの置き場所・記号を定義するプロジェクト・呼び出し元を `SceneJsonUpgradeBoundaryTests` が検査する。生成器・VSIX・エディターが使う `Analyze` は版 3 だけを読み、旧版と C# の名前の角度キーを `SceneJson.Cli upgrade` を示す SCENE204 にする。スキーマ定義も版 3 だけを受け付け、エディターのテキスト統合にあった版 1 の分岐は削除した。
- **IMP008 は「変換を案内して止める」にした。** ContentCompiler が読み込み時に利用者のメタデータ(`SharpGLTF` → `cge.gltf`)を黙って書き換えていた。ビルドは書き換えずに IMP008 で止め、Assets ウィンドウはこのメタデータを無効として「Convert legacy metadata」を出す。判定は `AssetMetadata.IsRetiredModelImporter` 1 か所にまとめた。
- **設計の表から漏れていた旧版の読み手 2 つを追加した**(機能台帳の再確認で発見): `.cgefracture` の版 1・2(と 1 階層のときの版 2 の書き込み)、Navigation プラグインの Cook がシーン JSON を直接読むときの版 1・2 の角度の扱い。どちらも同じ規則(生成物は互換なし、シーンは版 3 だけ)で整理した。

検証(2026-11-15):
- ソリューション全体の Debug ビルド(`-m:1`): 成功、エラー 0。並列ビルドでは同じ obj の DLL への書き込み競合(CS2012)が変更前から起きるので、直列でビルドした。条件付きコンパイルの導入後に SceneJson.Cli・VSExtension・SceneModel を個別にビルドし、いずれも成功。
- 変更範囲の試験: CodeAnalysis 421、SceneModel 225(移した変換の試験を含む)、Entities 85、World 45、Core 164(シーンの変換の境界試験 2 件を含む)、Content 72、Contracts 94、Destruction 57、ContentCompiler 34(単独)が全件合格。
- 変換ツールの実行: `SceneJson.Cli upgrade UserEcsProject/UserEcsProject.csproj`(書き込みなし)が終了コード 0 で、21 シーンすべてを版 3 と判定した。ファイルの変更なし。
- GPU 以外の全体: 失敗 7 件。いずれも今回の変更が原因ではないことを確かめた。
  - Windows の 3 件: 変更前の基準と同じ試験。
  - Navigation `AMissingModelArtifact_IsAnErrorNamingIt`: 今回の版の変更だけを一時的に元へ戻した `SceneGeometry.cs` でも同じく失敗した(実験後にファイルを戻し、バイト単位で一致を確認)。モデル成果物が無いときの例外が NAV003 の経路ではなく NAV006 の経路へ入る、未コミットの M0128 の変更による失敗。
  - Bridge.Hosting `StageWallsUseTheSameCornerPivotAndForwardDirectionAsTheGame`: 試験自身のコードが `Lvl_ThirdPerson.json` を直接たどり、`Transform` に `Rotation`・`Scale` が無い「Third Person Template」ノードで `GetProperty` が例外になる。このシーンは未コミットの作業で変更中で、今回は触っておらず、失敗する経路は製品コードを通らない。
  - Import `Templates_MatchPreMigrationGeometryMaterialsAndAnimation`: テンプレートの資産ファイル数(97 が 103)の不一致。
  - ContentCompiler: 全体実行時のタイムアウト。単独では合格。
  - 変更前から失敗していた裏付け(ファイルの更新日時。この作業の最初の編集は 2026-11-15 0:27): Bridge.Hosting の入力 `Lvl_ThirdPerson.json` と試験は 2026-11-04 16:06〜16:07 から未変更。Import の比較基準 `assets/TestFixtures/Import/M0114-Source-Parity.json` は 2026-10-25、比較対象の glb は最新でも 2026-11-04 で、11-15 以降に変わった glb は 0 件(この試験はファイルを数えるだけで、今回変えたコードを通らない)。Navigation の失敗経路の `ModelAssetBundle.cs` は 2026-11-02 から未変更で、加えて今回の変更を戻した実験でも失敗する。
- 証跡: `F:\CodexTemp\cge-legacy`(baseline、step3〜8、full、nav-experiment.log、step8/cgefixed-reinstall.log)。

全体の試験の失敗の修正(2026-11-15、所有者の指示「この作業で直す」「調べて直す」):
全体の試験には、この作業の前から(または全体実行の負荷の下だけで)失敗する試験が残っていた。原因を一つずつ特定して直した。
- Navigation `AMissingModelArtifact_IsAnErrorNamingIt`: M0128 で `ModelAssetBundle.OpenAsync` が、索引に無いモデルを `FileNotFoundException`(FileName はそのモデル)で知らせるように変わったが、Navigation の Cook は古い `KeyNotFoundException` を待っていたため、欠落が NAV003 でなく NAV006(読み込み失敗)になっていた。利用側の取り残しなので、Cook を新しい契約に合わせた(ファイル名がそのモデルを指す場合だけ欠落として扱い、他の I/O の失敗は NAV006 のまま)。
- Bridge.Hosting `StageWallsUseTheSameCornerPivotAndForwardDirectionAsTheGame`: シーン JSON は省略したメンバーと軸を既定値にする仕様だが、試験の走査が全メンバーを前提に `GetProperty` していた。試験の走査をゲームと同じ読み方(位置 0、回転なし、拡縮 1)に直した。
- Import `Templates_MatchPreMigrationGeometryMaterialsAndAnimation`: 11-04 に ThirdPerson のステージの glb が 6 つ増えたのに、移行前の比較基準(`M0114-Source-Parity.json`)が更新されていなかった。作り直した基準を元の基準と記録ごとに比べ、既存の 97 ファイルの記録が変わらず(変更 0、削除 0)、新しい 6 ファイルの記録だけが増えることを確かめてから採用した。
- ContentCompiler `CommonGltfPreservesExistingGeometryRestPaletteAndAnimationKeys`: コンパイラーの起動に 30 秒の固定の上限があった。計測すると FirstPerson の `SKM_Manny_Simple.glb` の compile --verify は単独でも 27.8 秒かかり(この作業で足した IMP008 の確認の起動は 0.1 秒で無関係)、全体実行で CPU が混むと超えていた。この上限は止まったプロセスを見つけるためのもので性能の判定ではないので、180 秒にして理由をコメントに残した。
- Windows `TemplateLightingSceneTests`(ThirdPerson): 11-04 の ThirdPerson のステージの書き換えで、ルートの `GlobalDistanceField`(中身が空の部品)だけが落ちていた。HEAD と全部品を比べ、ほかの照明・環境の部品 10 種類は残っていることを確かめ、HEAD と同じ位置・同じ形で戻した。
- Windows `RenderPreviewLightingSessionTests.WARP_LegacyEquivalentLightingMatchesTheLookWithoutAnyLighting`: 10-29(`ba0fb153`)にプレビューの既定の太陽が「真上向き 2 lux」から「上から照らす 2.5 lux」に変わったが、試験の「既定と同じ照明」が追随していなかった。試験の照明を今の既定値に合わせた。
- Windows `SceneJsonRuntimeValuesTests`(EngineShowcaseScene): 生成器は固定小数点の角度を回転数から正確に変換する(11-04 の数値設定に応じた角度)が、プレビューの実行時の読み手は float を経由して精度を落としていた(カメラの回転の四元数が約 1e-10 ずれる)。「シーン JSON の読み手は一致しなければならない」に反する。生成器の計算のうち Roslyn に依存しない部分を `SceneJsonFixedAngles.cs` に切り出し、生成器とプレビューの読み手が同じコードで変換するようにした(double の角度も double のまま変換)。あわせて、この読み手に残っていた版 1 の角度の読み分け(`UsesTurns`、`Turns`)と、C# の名前の角度キーの受け付けを削除した(生成器と同じく拒否)。
- Windows `RenderPreviewRenderSessionTests.WARP_CameraDepthIsDerivedFromProjectionWithoutASelector(Perspective: False)`: 1 回の全体実行でだけ、デバイスの世代が 1 つ多くなって失敗した(WARP が負荷時にデバイスを作り直した)。単独では 3 回とも合格し、修正後の全体実行でも合格した。製品の変更はしていない。

最終の全体の試験(ソリューション全体を `-m:1` で再ビルドし、GPU 以外を実行): 23 プロジェクト、4,464 件がすべて合格(失敗 0)。証跡は `F:\CodexTemp\cge-legacy\full3`。

CgeFixed の VSIX の入れ直し: 実行中の CgeFixed の Visual Studio が無いことを確かめ、リポジトリの `Reinstall-CgeFixedVsix.ps1` で 0.1.0.25 を入れた(Visual Studio の画面は開かない)。スクリプトが版と SHA-256 の一致を確認し、実験インスタンスには 0.1.0.25 だけが入り、0 バイトのファイルは無かった。なお、このスクリプトは BOM なしの UTF-8 に日本語を含むため、Windows PowerShell 5.1 では構文エラーで読み込めない(未コミットの作業のファイルなので書き換えず、BOM 付きの一時コピーから実行して削除した)。`Launch-VSExtension.ps1` もこのスクリプトを呼ぶので、同じ問題が起きる。
