# M0019: C# Scene DSLとScene Editorの双方向同期基盤(Phase 1〜9 回顧録)

## 目的

C#の`IScene`宣言をシーンの唯一の正本とし、Source Generatorによる実行時生成と、VSIXエディタ
(ヒエラルキー・Scene デザイナー)によるライブ表示・将来の編集書き戻しを、同一の解析基盤の上に
成立させる。設計判断は[[ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor]]に記録した。

本マイルストーンは通常と異なり、専用のマイルストーン文書を書かずに
「`ADR-0040 / M0019 Phase N`」のコミット規約だけで進行した(台帳`Implementation-Status.md`の
F-L4-002 / F-L8-010が当時の一次記録)。本書は2026-08にコミット履歴・台帳・テストから再構成した
回顧録であり、当時実施していない検証を実施済みとは書かない。

## 実装したこと(Phaseとコミットの対応)

| Phase | コミット | 内容 |
|---|---|---|
| 1 | `f80a10f` | C# Scene DSL共通解析基盤: `src/CSharpGameEngine.SceneModel/`(共有ソース、netstandard2.0、`SceneDeclarationAnalyzer`/`SceneModel`/`NodePath`) |
| 2 | `2eb3e82` | Scene DSLアナライザー: `SceneDslAnalyzer`(SCENE100〜118診断。SGブリッジ側は診断を出さず二重スキグルを回避) |
| 3 | `403e2db` | `SceneInstance`ランタイムとEntity破棄: `World.InstantiateScene<TScene>()`/`UnloadScene`/`DestroyDeferred`等 |
| 4 | `a3aed37` | Scene DSLからRuntime ECSへの直接生成: `SceneModelBridge`→`SceneEmitModel`→`SceneInstantiateEmitter`/`SceneStartupSystemEmitter` |
| 5 | `904a9b4` | Scene定義を`IScene`へ一本化し旧機構(`[SceneDefinition]`/`SceneDefinitionGenerator`、ADR-0030世代)を削除 |
| 6 | `62def96` | エンジン標準Component群+`RenderServer`: `PrimitiveMesh`/`PrimitiveType`(F-L4-002) |
| 7 | (欠番) | Phase 7を名乗る単独コミットは履歴に存在しない |
| 8前半 | `9935245` | VSIXヒエラルキーをlive Roslynベースへ(`SceneWorkspaceService`+250msデバウンス解析) |
| 9 | `cfe3726` | VSIX Scene Viewを`.scene`からSceneModel(ライブRoslyn)描画へ移行(F-L8-010)。`SceneFileEditorFactory`/`SceneWindowPane`等の`.scene`ドキュメントタブ系を削除 |

Phase 9以後、`9813781`がEditor→Code方向の配管層を一括追加した(Phase 10の準備。呼び出し元は未配線):

### `tools/CSharpGameEngine.VSExtension/`(セッション契約と書き込み配管)

- `SceneEditorContracts.cs`: `SceneKey`/`EditorNodeId`/`SceneProjection`/`SceneProjectionReconciler`
  (2パス兄弟照合)/`SceneDiffEngine`。
- `SceneEditorSessionContracts.cs`: `SceneDocumentSnapshot`(Last Known Good保持)/`SceneSelection`/
  `SceneSelectionService`(`SceneSelectionOrigin.Viewport`は予約)。
- `SceneEditorSessionService.cs`: `SceneKey`でpinしたSceneごとのimmutable snapshot発行。
- `SceneSourceEditor.cs`: `SceneEditOperation`(11種のenum、実装は`SetComponentMember`/`RenameNode`の
  2種)、trivia保持の`ReplaceNode`、`.g.cs`拒否、`ApplyLatest`の1回リベース。
- `SceneTextBufferWriter.cs`: `SourceText`差分を1つの`ITextUndoTransaction`でlive bufferへ適用。
- `SceneInspectorModel.cs`/`SceneComponentCatalog.cs`: 将来のInspector/AddComponent用の純粋モデル層。

### `src/CSharpGameEngine.Windows/Runtime/`(エンジン標準Component語彙の拡充)

- `Transform.cs`: フラットfloat(`PositionX/Y/Z`,`RotationX/Y/Z/W`,`ScaleX/Y/Z`)+`TransformMath`
  (`ToMatrix`/`ToWorld`/`TryCalculateLocal`)。
- `Camera.cs`: Scene用Cameraコンポーネント(`Active`/`Projection`/`FovYRadians`/`NearZ`/`FarZ`/
  `OrthographicSize`。姿勢は同一Nodeの`Transform`から取る)。
- `TransformWorldResolver.cs`: メモ化付きlocal→world解決(`Cycle`/`DeadParent`/`MissingTransform`
  ステータス)。ランタイム用で、エディタ側は未使用。

続く`bd9a8e4`/`40286e0`で、複数`IScene`クラスの選択を`ScenePickerDialog`から
`SceneDesignerToolWindowControl`内の`SceneSelector`コンボボックスへ置き換え、宣言からの表示を確認した。

### テスト

- `SceneAnalysisParityTests.cs`: SG側とVSIX側の解析同値性(net472で再検証)。
- `SceneEditorContractsTests.cs`: Reconcilerの並べ替え・一意リネームでのID維持、区別不能兄弟での
  ID非推測、SceneDiffのMember単位検出。
- `SceneSourceEditorTests.cs`: RHSのみ置換とtrivia保持、不正式・SceneKey不一致の拒否、
  `ApplyLatest`のリベース。
- `SceneTextChangePlannerTests.cs`、`SceneEditorSessionContractsTests.cs`、
  `SceneInspectorModelTests.cs`、`SceneComponentCatalogTests.cs`、
  `SceneModelRenderObjectBuilderTests.cs`、`RenderSurfaceFocusPolicyTests.cs`ほか
  (`9813781`で追加されたVSExtension.Testsの契約テスト群)。
- `TransformTests.cs`/`TransformWorldResolverTests.cs`/`SceneCameraTests.cs`(Runtime側)。

### ドキュメント

当時はADR-0040初版(41行)のみで、しかも日本語が全て`?`に文字化けした状態でコミットされていた
(コミット`9813781`のblob時点で破損、git復元不能)。2026-08にADR-0040を全面改稿し、本書(M0019)を
回顧録として追加、文字化けした12ファイルのコメント・エラーメッセージ文字列を書き直した。

## 検証結果(実際に実行して確認)

当時の一次検証はF-L8-010(TestVerified)の記録どおり: `SceneModelRenderObjectBuilderTests`7件を
含むVSExtension.Testsが全件成功。実機VS Experimental Instanceでの目視確認(コンテキストメニュー→
シーン デザイナー表示・ライブ更新・既存VS入力の回帰)は「ユーザー側で実施予定」のまま完了記録が
無いため、本書でも**実機目視は未検証**として扱う。

2026-08の本書作成時に再実行した基準値:

```
dotnet build CSharpGameEngine.slnx -c Debug → 成功、警告0/エラー0
dotnet test CSharpGameEngine.slnx -c Debug --no-build
  → 全8テストプロジェクト 610件成功・失敗0
    (Bridge.Hosting 8、Entities 40、Import 30、Core 122、CodeAnalysis 101、
     SceneModel 104、VSExtension 118、Windows 87)
```

## 意図的に除外したこと(M0020へ引き継ぎ)

- **Editor→Code配管の配線**: `SceneSourceEditor`/`SceneTextBufferWriter`は完成・テスト済みだが、
  製品コードから呼ぶ箇所が1つも無い(当時「Phase 10」と呼んでいた作業)。
- **ビューポート入力・カメラ・ピッキング・ギズモの一切**(仕様書`Docs/シーンエディタの仕様.md`の
  §4〜12全部)。
- **`SceneModelRenderObjectBuilder`のTransform認識バグ修正**: `global::UserEcsProject.Transform`
  ハードコードのため実シーン(`CSharpGameEngine.Runtime.Windows.Transform`)が全ノードIdentityで
  描画される。
- **階層Transform合成**(親子`ToWorld`)と`Level`(.glbステージ)の描画。
- **`.scene`残骸コードの削除**: `SceneFile.cs`/`SceneNodeAdder.cs`は書き込み経路を失った
  受け入れ済み退行状態のまま存続。
- **`SceneDiff`の消費**: 毎snapshotで算出しているが読む側が無く、ビューポートは全再構築。

## 残課題

- 上記引き継ぎ事項はすべて[[ADR-0041-Scene-Editor-Interaction-Architecture]] /
  [[M0020-Scene-Editor-Interaction]]のPhase 0〜7として計画済み。
- 実機VS Experimental Instanceでの目視確認は依然未実施(M0020の各Phase完了条件に含める)。

関連: [[ADR-0040-CSharp-Scene-DSL-and-Bidirectional-Editor]]、[[ADR-0038-Startup-Scene-Declarative-Api]]、
[[ADR-0030-Scene-Authoring-CSharp-Source-Of-Truth]]、[[M0020-Scene-Editor-Interaction]]
