# M0103 — プラグインとアセット
- 日付: 2026-10-22
- 状態: 完了(2026-10-22)

## 目的
ユーザーの依頼(2026-10-22)「こういう拡張機能はUEのプラグインやアセット開発と同じようにユーザーが作れるようにして」「プラグインとアセットの違いも明確にして」「機能追加の方法も考えて」「生成ルールをユーザーが追加するかDLLかコード追加か」。決定は [ADR-0079](../Decisions/ADR-0079-Plugins-And-Assets.md)、作り方は [Plugins-And-Assets](../Specifications/Plugins-And-Assets.md)。ADR-0078 の車両・ナビメッシュ・破壊は、この仕組みのプラグインとして作る。

## 進捗
| 段階 | 内容 | 状態 |
|---|---|---|
| 1 | `[EngineService]`(サービスを足す) | 完了 |
| 2 | Component の上限 128 → 512 | 完了 |
| 3 | 同じ単純名の Component の名前 | 完了 |
| 4 | プラグインの取り込み(3 つの置き場所、マニフェストと PLUGIN001〜005、シーンとアセット) | 完了 |
| 5 | エンジンのプラグイン Vehicles(車両の移設) | 完了 |
| 6 | `[DataAsset]` と `.cgeasset` | 完了 |
| 7 | Library の DLL をパレットとスキーマに出す | 完了 |
| 8 | Cook の契約と CLI | 完了 |
| 9 | SDK パッケージ(プラグイン、Cook、版の 1 か所化、梱包の不具合) | 完了 |
| 10 | 見本のプラグインとパッケージ経由の実走 | 完了 |
| 11 | パスの `#`・`;` | 完了 |
| 12 | 文書と全テスト | 完了 |

## 段階 1. `[EngineService]`
- `CSharpGameEngine.Authoring.EngineServiceAttribute`(Core)。生成器は型ごとに `GeneratedSystemSet` のフィールドを置き、最初の呼び出しで作る(引数なし、または `EngineHost` を 1 つ取るコンストラクター)。`IDisposable` は `Shutdown` と `ShutdownEditor` で、型名の逆順に破棄する。作れなければ SYSGEN043。
- テスト: `GameSystemEngineServiceTests`(4 件。2 つの System が同じ 1 つを共有し、Shutdown で 1 回だけ破棄されることを、コンパイルして実行して確かめる)。

## 段階 2. Component の上限
- `ComponentMask`(語の並び。語の名前は Low・High・Word2…)で、`GameSystemIndexAssigner`・`GameSystemTypesEmitter`・`GameSystemWorldEmitter`・`GameSystemLoopEmitter`・`GameSystemScheduleAnalyzer` を書き換えた。スケジュールの項目は最低 2 語(Low・High)のまま。
- 回帰の確認(事実): 変更前(fb7c951)と変更後の作業ツリーで、UserEcsProject と 4 テンプレートの生成ファイル 115 個がバイト単位で一致した(F-L7-032 の性能の証拠の前提が変わらない)。
- テスト: 既存の境界のテストに 129・200・512 種類を足した。512 種類は、コンパイルして高い番号の Component の無効化と変更の追跡まで実行する。

## 段階 3. 同じ単純名の Component
- 単純名が重なる型だけ、名前空間つきの名前(`Weather_Intensity`)にする。それでも重なるときだけ SYSGEN020。
- テスト: `ComponentTypesSharingASimpleName_AreNamedWithTheirNamespaces`、`AQualifiedNameThatIsAnotherTypesSimpleName_ReportsSysgen020`。

## 段階 4. プラグインの取り込み
- `build/CSharpGameEngine.Game.targets`(プロジェクトファイルの後に読む)が、`Plugins/*/*.plugin.props` とエンジンの `plugins/*/*.plugin.props` を読む。エンジンのプラグインは `CgePlugins` で選ぶ。NuGet のプラグインは自分の `build/<Id>.targets` から読まれる。
- 生成器: マニフェストの検査(`PluginManifestAnalyzer`、PLUGIN001〜005)、プラグインの根(`CgePluginRoots`)の下の `Scenes/` と `Assets/`、カタログ `Assets.Plugins.<名前>.*`、SCENE208 のプラグインのパス。
- 実装中に見つけた事実:
  - MSBuild は項目をすべてのプロパティの後で評価する。プラグインごとに上書きするプロパティでは項目を条件付けできないので、`.plugin.props`(判定)と `.plugin.items`(項目)に分けた。
  - MSBuild は 1 つのプロジェクトで同じファイルを 1 回しか取り込まない(MSB4011)。共通の判定ファイルは最初のプラグインにしか効かなかった(パッケージの検証で、2 つ目のプラグインのシーンが作られず、名前を挙げていない Vehicles が取り込まれていた)。判定は各プラグインに書く。
  - 生成器に渡すプロパティは .editorconfig を通り、その読み取りは値の `;` と `#` から後ろを捨てる(`AnalyzerConfig.Parse` で確認)。`;` で区切った `CgePluginRoots` は空になり、パッケージのプラグインのシーンが作られなかった。区切りを `|` にした。
- テスト: `PluginManifestAnalyzerTests`(18 件)、`PluginSceneTests`、`AssetCatalogTests`(プラグインのアセット)、`SceneFilesTests`(プラグインの `Scenes`)。

## 段階 5. エンジンのプラグイン Vehicles
- M0101 段階 9 の簡易の車両(`Vehicle`・`VehicleState`・`VehicleIntent`・`VehicleMotor`・2 つの System)を `Runtime.Character` から `plugins/CSharpGameEngine.Vehicles/Runtime` へ移し、名前空間を `CSharpGameEngine.Vehicles` にした。Vehicle テンプレートと UserEcsProject が `CgePlugins` で有効にする。VS の新規作成は、テンプレートの csproj の `CgePlugins` を写す(`GameProjectGenerator.PluginsOf`、VS テンプレートの csproj も同じ文字列であることをテストで固定)。
- 実走(WARP): パッケージから作った Vehicle は移動 48.281 m、曲がり 38.3°、D3D12 のエラー 0。移す前と同じ値。

## 段階 6. データアセット
- `[DataAsset]`(Core)と `.cgeasset`(`{ "schemaVersion": 3, "type": …, "members": … }`)。シーンの値の解析(`SceneJsonSceneAnalyzer.AnalyzeDataAsset`)を使い、カタログの静的メンバーを値そのものにする。
- テスト: `DataAssetTests`(コンパイルして実行し、列挙・配列を含む値が読める。不正な文書は SCENE201〜204 で報告し、カタログから外す)。

## 段階 7. Library の DLL
- `[assembly: PluginLibrary]`(Core)を付けた DLL の公開 struct を、スキーマ(`SceneJsonSchemaBuilder`)とパレット(`SceneComponentCatalog`)が拾う。
- テスト: `Build_ListsThePublicStructsOfAPluginLibrary`。

## 段階 8. Cook
- `CSharpGameEngine.Cooking`(`IAssetCooker`、`CookRequest`、`CookResult`)と `CSharpGameEngine.Cook.Cli`(`list`、`cook … [--verify]`)。専用の AssemblyLoadContext で読み、契約だけを共有する。出力フォルダの外への書き込み、書くと言って書かないこと、`--verify` で 2 回の結果が違うことを拒否する。
- テスト: `CookerHostTests`(6 件)。CLI も別のプロセスで動かして確かめた(`list`、`cook --verify` が成功、決定的でない処理は終了コード 1、使い方の誤りは 2)。
- 残り(2026-10-23 更新): ビルド中の自動実行と VSIX のボタンは、最初の実際の Cook(ナビメッシュ)と一緒に [M0106](M0106-Navmesh-Bake-And-Path-Following.md) で足した。既存の `Physics.Cli bake` はまだこの上に載せ直していない。

## 段階 9. SDK パッケージ
- 版を `build/CSharpGameEngine.Version.props`(`CgeEngineVersion` 0.1.16)の 1 か所にした。パッケージに `plugins/`、`build/CSharpGameEngine.Game.targets`、`lib/…/CSharpGameEngine.Cooking.dll`、`tools/Cook/` を足した。
- 見つけて直した不具合(0.1.15 から): 梱包のターゲットの中の `%(RecursiveDir)` がほかの項目と掛け合わされ、`src/<モジュール>/win-x64/…` などの余分な写しが入っていた。ワイルドカードを評価時へ移し、`PackagePath` から `%(RecursiveDir)` を外した(NuGet が自分でフォルダを保つ)。

## 段階 10. 見本と実走
- `samples/Plugins/Weather`(プロジェクトのプラグイン: System、`[EngineService]`、`[DataAsset]`、起動時のシーン)と `samples/Plugins/CSharpGameEngine.Samples.Lightning`(NuGet のプラグイン: Library の DLL と Runtime のソース)。どちらにも `Intensity` がある。
- `CSharpGameEngine.FeatureRunner verify-plugin-package`: SDK から作った ThirdPerson に 2 つを入れてビルドし、WARP で動かす。各サービスが終了時に出す合計の整合(雨量 = 更新回数 × 3、エネルギー = 落雷数 × 20)、データアセットの値、2 つの `Intensity` の名前、Vehicles が入っていないこと、プラグインのアセットのコピーを調べる。
- 結果(WARP): 歩行 15.700 m・跳躍 1.228 m(プラグインなしと同じ)、Weather の更新 888 回・雨量 2664.0、Lightning の落雷 888 回・エネルギー 17760.0、D3D12 のエラー 0。この検証が段階 4 の 2 つの不具合(MSB4011 と .editorconfig)を見つけた。

## 段階 11. パスの `#` と `;`
- 事実: 生成器へのプロパティは .editorconfig を通り、その読み取りは値の `#`・`;` から後ろを捨てる。`%TEMP%\cge#probe\My Game` に置いたゲームは ProjectDir が切れ、`Assets` が無い(CS0103)でビルドに失敗した。ADR-0070 から続いていた問題。
- 修正: `Game.targets` の `CgeEncodeGeneratorPaths` が、.editorconfig を書く直前に `CgeProjectDirectory` と `CgePluginRootsEncoded` を `#`→`?h`、`;`→`?s` で作る。生成器は先にこれを読み(`GeneratorPath.Decode`)、無ければ ProjectDir を使う。
- テスト: `AProjectAndPluginsUnderFoldersWithHashOrSemicolon_KeepTheirPaths`(Roslyn の `AnalyzerConfig.Parse` を通す)。実走: 作業フォルダを `%TEMP%\cge#hash\…` にした `verify-plugin-package` も合格。

## 段階 12. 文書と全テスト
- `dotnet test CSharpGameEngine.slnx`: 全プロジェクトで失敗 0(CodeAnalysis 298、VSExtension 1189、Windows 496 とスキップ 2、FeatureRunner 187、SceneModel 212、Cook 6 ほか)。
- 文書: ADR-0079、Plugins-And-Assets、ADR-0065 の追記、ADR-0078(番号の付け替えと Vehicles・Destruction のプラグイン)、Architecture、Ecs-Soa-Systems、Ecs-Runtime、Feature-Stages、Roadmap、Templates/README、Implementation-Status(F-L7-052、F-L3-020)、Conformance、Reverse-Engineered-Spec、CHANGELOG。

## 完了の判定
- 依頼への答え: プラグイン(コード)とアセット(データ)の違い、機能を足す 3 つの場所、足し方の表(ECS はソース、ECS を使わない処理は DLL、生成ルールはユーザーが足さずに属性で広げる)を ADR-0079 と仕様に書き、その通りに動くことを 3 つの置き場所の実走で確かめた。
- M0103 時点で未実装(データアセットの Inspector 編集・一覧は後続の M0123 の部分実装): データアセットの Designer での編集(Cook のビルド中の実行と VSIX のボタンは M0106 で実装済み)、プラグインの一覧の表示、NuGet のプラグインの Library の DLL を実行時のプレビューが確実に読むこと、`Physics.Cli bake` の Cook への載せ直し。
- 意図と観測の一致: MatchesWithDocumentedExtension。
