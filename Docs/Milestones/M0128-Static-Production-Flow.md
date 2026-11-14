# M0128 — 静的構成と制作フロー（進行中）

- Date: 2026-10-31
- Updated: 2026-11-04
- Status: PartiallyImplemented
- Features: F-L1-011, F-L1-012, F-L6-007, F-L6-008, F-L8-035, F-L8-036
- Design: [承認済み計画](../refactoring-designs/20261031-static-production-flow.md)
- Decisions: [ADR-0091](../Decisions/ADR-0091-Profile-Independent-Numeric-Persistence.md)、[ADR-0092](../Decisions/ADR-0092-Static-Snapshot-Value-Graphs.md)、[ADR-0093](../Decisions/ADR-0093-Configurable-Numeric-Builds.md)、[ADR-0094](../Decisions/ADR-0094-WorldCell-Numeric-Values.md)、[ADR-0095](../Decisions/ADR-0095-Profile-Neutral-Snapshots.md)、[ADR-0096](../Decisions/ADR-0096-Project-Depth-Mapping.md)、[ADR-0097](../Decisions/ADR-0097-Model-Artifact-Production.md)

## 現在の受け入れ状況

現在の候補はSDK 0.1.46 / VSIX 0.1.17で、両方とも梱包済み。17の梱包関連23件・Release全1597件・既存Windowsプレビュー9件が成功し、実行用プレビューの停止修正は同じSDK46の別プロセス・両GPUで確認した。SDK46の数値出力はDebug/Release各3件、SDK46を含む16の梱包関連52件も成功済み。SDK46の実プロジェクトはVisual Studio用MSBuildで通常ビルド・自己完結発行・通常GPU/WARP各12フレーム・編集/実行のDouble用DLL照合が成功し、元モデル/C#ソース/制作専用DLLの配布混入は0件。壁の共通形状は、実ThirdPersonを同一カメラ・同じ配置で前後描画して確認した。旧16の実VSでシーン表示失敗を観測した。ユーザーの承認後に確認用VSを通常終了し、専用環境へ17を配置してDLLハッシュの一致を確認した。17の実VSでThirdPersonの壁表示とDLL一致を確認した。素材追加・精密値の編集保存・IDE起動・再読込・編集後の配布確認は未完了。M0128全体はPartiallyImplementedを維持する。各境界の証跡は以下と末尾の現在の残件に記録し、旧版の成功を新版の実画面成功へ読み替えない。

日本語・暗色の専用IDEで、F2と選択済みの再クリックによる名前編集、Enterによる実ファイルの改名、Ctrl+Z/Y、読み取り専用の拒否、タイルと詳細表示、更新中の回転と完了時の解除を確認した。入力後のEscがシェルの先行処理で誤確定する不一致を実ファイルで観測し、PreProcessMessageで先に破棄するよう修正。配置DLL `6829C1C8D8B80D2039222A7DD252DC3F1ECE0361ED7F994FD2498D83CA6E47B0`で再試行し、元名が存在し入力した名が存在しないことを確認した。実画像は`.artifacts/StaticProduction/ui-evidence-20261103/`の05〜15。ダブルクリックはエディターを開くが、その時点でCookサブフォルダーの発行漏れが見つかった。修正後の実VSIXからのモデル生成は上記1593件に含む。修正版で同じモデルの実画面描画が成功した（17）。メタデータ登録後の改名もIDを保持した。制作一巡の最終候補での起動・再開を残す。

| 要求 | 現在の証拠と状態 |
|---|---|
| Fixed 64/128/192bitと小数位置、実体がSingle/Doubleの設定 | Source/SDKの設定別ビルド、型・配置の実行検証、数値80件と生成器410件の両構成で検証済み。実際の日本語設定ページで64/32、128/64、192/96、Float、Doubleを選択し、csprojへの保存を確認。 |
| Roslyn・属性を使った静的な保存処理 | 生成Worldの閉じた値グラフ、Snapshot v2、証明可能なv1移行を検証済み（v1の読み取りは2026-11-15にADR-0098 §5 規則 2で削除）。配布物のモデル・一覧のJSONも生成契約を使用。 |
| 計算型に依存しないテキストとバイナリ保存 | 正確な有限10進とCGN1、丸め・範囲検査・失敗時の一括拒否を検証済み。最終配布物でFloat保存を別プロセスのDoubleへ復元。 |
| 数値型と独立したIdentity/Logarithmic | ビルド設定、CPU/GPUのビュー・リサイズ・復旧、Source/SDKのIdentity実行を検証済み。実画面でLogarithmicからIdentityへ切り替え、Doubleとは独立した設定として保存を確認。 |
| 素材生成から実行・配布までの共通モデル | ゲーム・プレビュー・衝突・ナビメッシュ・距離場を接続済み。最終外部4テンプレートと元モデルなしの自己完結配布が実行成功。 |
| 必要な回帰と重複試験の整理 | 既存の生成器・数値・モデル試験へ境界ケースを統合。最終Windows Release 148件と梱包整合17件が成功。 |
| 制作工程の実画面操作 | 新規作成、素材追加、モデル描画、登録、IDを保持した改名、Inspectorの位置編集・ソース保存、数値・深度設定の保存を実画面で確認。最終候補でのIDE起動と開き直しを残す。 |

実行結果と未確認のUI操作を区別し、M0128全体のStatusはPartiallyImplementedを維持する。

## 数値保存の共通境界

`NumericValueCodec` に符号・整数部・小数部の共通バイナリ、正確な10進出力、直接の10進→固定小数点変換を実装した。64/128/192bit固定小数点と有限float/doubleを保存境界で相互に扱う。同じ有限値は元の型によらず同一バイナリになり、小数部の2の因子を除いて正規化する。

読み込みは最近接・偶数丸めと範囲検査を完了してから固定長出力へ書く。範囲外、切り詰め、余分なバイト、不正符号・パディングを拒否し、失敗時に出力を部分変更しない。10進テキストの読み書きはdoubleを経由しない。一時的なBigIntegerは変換境界内だけで使用する。

`LogicalScalar` に共通形式への明示的な読み書きを追加した。Scalar単体の既定16バイト形式は維持する。設定別ランタイムは下記のSource・SDK統合へ接続済みで、WorldCellとSnapshotはv2の共通保存へ移行した。

## 固定小数点のライブラリ設定

`CSharpGameEngine.NumericProfile.props` / `.targets` を数値ライブラリへ接続した。幅64/128/192bitと小数位置をビルドで選び、実体をlong・Int128・Int192の8/16/24バイトとする。非既定の出力と中間出力を設定別に分離する。既定Q64.64の最適化済み実装は維持する。

`FixedArithmetic` は固定長のスタック領域で加減乗除、剰余、平方根、幅・小数位置の変更、Singleとの相互変換を処理する。失敗時は出力を変更せず、入力と出力が重なる場合も扱う。乗除・縮小・平方根は最近接・偶数丸め。管理ヒープを使わないことを実測した。

大きな分母を一度固定小数点へ拡大すると64bitで係数を作れないため、1/nを直接丸める `FromRatio` を追加した。数学関数の剰余・位相・整数倍をRaw幅に依存しない操作へ変更し、軸角の1/2も小数64bitの生定数から分数へ置き換えた。

整数オラクルで全幅の境界とランダム値、位置0/1/半分/上限付近を比較した。IEEEと設定間変換は独立した保存境界と比較する。保存境界の小数位置上限は幅−2であり、幅−1の内部演算テストとは区別した。

64/16、64/32、128/48、128/64、192/96、192/120を個別にコンパイルし、実体サイズ・演算・三角関数・ベクトル／回転・最大tickの位相・保存を実行した。実MSBuildの64/32、128/48、192/96も警告・エラー0で成功した。最終ビルドのタスクは `b709012a3b04469bb634112b65bbbbf7`、`345a3b26ad164b02832d7333cbfd6121`、`d4c52f92bf4f4924b6c916b6407ff119`。

192bitの最小刻み付近の負値がSingleへ丸まると負のゼロになる不一致を再現し、保存契約と同じ正のゼロへ正規化した（失敗タスク `9072f64e9513464d81061bec74575e4a`）。通常値だけでなく最小刻み・正負境界を既存のIEEE比較ケースへ統合した。既定型だけが前後空白を許す文字列入力の差も5設定で再現し（`b1db261683cc41d5a5974bf9c43c61f4`）、型の入口でTrimするよう修正した。保存境界自体の厳密な文法は維持する。修正後は下記の全体検証が成功した。

不正な幅・小数位置・モードが既定型へ切り替わらず拒否されることも最終MSBuildで確認した。これらは期待した拒否で、BackgroundOps結果はFailedとして保存する。タスクは `c6355737593343ff8253b08653542d2c`、`9bc7272589b8422cb95a50eafb97c2ed`、`26f249efd2a84da3b5c6329e63f22dce`。Floatでは固定専用の設定を使用しないことも確認した（`3b86b5dae04c493b8a85dad1684c8a71`、Passed）。

### 数学関数の範囲と高精度

整数のみの設定と整数部の狭い設定を加え、6設定の失敗を再現した（`e3c4c134606749168427a3a059f64778`）。数学定数・中間値を保存型の狭い範囲へ押し込むことが原因だった。固定の数学処理を閉じた静的ジェネリックへ集約し、小数32bit未満または整数部4bit未満では256bitの一時型をビルド時に選ぶ。作業精度は最低32bitとし、保存型へ戻す際に最近接・偶数丸めと範囲検査を行う。結果自体が範囲外ならTry処理はfalseと既定値を返す。

π・π/2・2πはreadonlyフィールドから個別に検査するプロパティへ変更した。返せない定数の取得だけがOverflowExceptionになり、数学関数全体を型初期化で使用不能にしない。256bit作業領域を支える演算カーネルは最大8桁へ拡張し、独立整数オラクルで追加幅も確認した。

以前の多項式・項数では高い小数精度を十分に使えていないことも、独立級数との比較で再現した（`0d3419f2992144b092a50a67b9e4b4cd`）。小数精度64bit超ではsin/cosの級数とatanの項数を精度に合わせ、既定Q64.64の経路は維持した。sin(1/2)とatan2(1/2,1/2)をガード128bitの独立整数級数と比較し、対象ケースで32刻み以内を確認した。これは全入力への誤差保証ではない。

### Float/Doubleの実体

数値ライブラリのFloat/Doubleを実際のSystem.Single/System.Doubleへビルド時の別名で接続した。C#14の拡張メンバーで既存の生成・検査・保存APIを補い、演算と数学関数はネイティブの型とMathF/Mathを使う。固定小数点への変換は挟まない。Vector3は12/24バイト、Quaternionは16/32バイトで、最小非ゼロ値・負のゼロ・Doubleの細かい差を保持する。NUM001はDouble選択時にdoubleを許し、decimal/BigIntegerの流入は拒否する。

個別の実行コンパイルはFixed12設定とFloat/Doubleの計14設定。通常の設定に加え64/0・128/0・192/0・64/62・128/126・192/190で数学処理と範囲外拒否を確認した。実MSBuildも64/0・192/190・Float/Doubleの両構成で成功した。

| 実ライブラリの設定 | Debugタスク | Releaseタスク |
|---|---|---|
| Float | `91776415ad8144ffbf86b84aee8845b4` | `b805675004394996b95f2139026bc478` |
| Double | `f596806178374e3bac321d4d8bd4ab7b` | `2d9fe60ebc944a7b8a82966ba8d56d8e` |
| Fixed 64/0 | `5658e6b8c26949e7a4f12e5334455b8f` | `d1d010eb62ca49d187bd83aafc1ad504` |
| Fixed 192/190 | `1f80d32229b94562bd46222caedddd51` | `8d701cfc6ab44318b506090fb7db2412` |

数値ライブラリ全モードとSource・SDK・数値型を持つ依存DLL・作成ツールへの伝達を接続した。Snapshot移行と共通モデルでの実行・配布は下記の検証で成功した。設定の実操作・保存は確認済み。最終候補のIDE起動・開き直しは末尾で追跡する。[数値設定仕様](../Specifications/Numeric-Configuration.md) に現在の境界を記録した。

## SourceゲームとWorldCellの数値接続

ゲーム本文のプロパティを読んだ後に数値設定を適用し、数値型を使う依存プロジェクト・作成ツールへ明示して渡す。出力・生成コード・プラグインの作成物ディレクトリーを設定別に分離する。Analyzer自身は計算型を切り替えない。DLLへ設定を記録し、NUM002で不一致と主要DLLの設定記録欠落を拒否する。ゲームだけにFloat設定を置き、コマンドラインで数値設定を与えないケースでも依存先へ届くことを確認した。

生の128bit配置を使っていた物理・車両・Navigation・格子処理を、値の比較・分数・剰余・離散座標の操作へ置換した。負の座標の商は負の無限大へ丸め、Float/DoubleはIEEEの仮数・指数から求めて、除算が整数境界へ丸まる誤差を避ける。固定幅の比率拡縮は積だけが保存幅を越えるケースを処理する。正規化と軌道計算の許容誤差は精度に応じて選び、既定の値を維持する。物理の調理データと生成キャッシュ・プレビューの数値識別も選択した設定に接続した。

WorldCell v2は6座標を長さ付きCGN1で保存する。v1はQ64.64として明示的に読み、選択した型へ一度だけ最近接・偶数丸めを行う。元の値の正規性、範囲、丸め後のBounds、閉包、依存順序を検証し、成功するまで定義を返さない。独立に組み立てたv1のバイト列、最近接・偶数丸め、破損値、範囲外での一括拒否を既存WorldCellテストへ統合した。（2026-11-15 追記: ADR-0098 §5 規則 2 により v1 の読み取りを削除し、WorldCell は v2 だけを読む。この段落の v1 の記述は当時の実装の記録。）

| Source実ゲーム | Debug | Release |
|---|---|---|
| Float | `40d8a9f051264c328de5552707a692e9`（ゲーム内設定から伝達） | `42da9701d03149319e539cb54399b45d` |
| Double | `8a0e4fa0dae24edfa3b19d085adbb9ec` | `b741a7fcdba54ef3819e2e94735e8db9` |
| Fixed 64/32 | `eceaffafeb4b4ee385e30cc92b6afd58` | — |
| Fixed 192/96 | `eff28e18c5cc420883f2195af2986c2b` | — |

すべてビルド成功、警告・エラー0。実画面での実行・保存再開・配布成功の証拠ではない。CLIのFloat/Double指定とゲーム本文のFloat指定の双方を検証した。物理・車両・描画原点・格子の関連Windowsテストは181件（`ac11403a66a243579a9e6c7fdfef4978`）、Navigation全体は57件（`06b3d62a264f4bedb2136eb97d9c483b`）がDebugで成功した。

MSBuildで完成DLLのパスを確定する前に数値設定を適用する。中間パスだけを変えると完成DLLが共有出力へ上書きされることを、実際の出力から確認して除去した。`NumericProfileOutputTests` は5設定の完成DLL・参照用DLL・中間ディレクトリー・OutDirの衝突を検出する。修正後の全構成ビルドを経てもFloat DebugのDLLハッシュは変わらなかった。

画面を作らない実ゲームの物理保存・再生も180tickで検証した。Source Float Debugは `2ab3f66516e14119a59f64f2c46039b3` / `60d1a0486e9949b7a4137b93c7a96585`、Double Debugは `ecc4ee30d4164a76aef29614c788b89b` / `44318bb73adb473fb779c050acf9cb28`、192/96は `0e3f7078359343628a9588f2f6a3967b` / `fbaa79e9c51f47b7a45df205de5767d6`。64/32、Float Release、Double Releaseも成功し、各構成内で保存と再生のSHA256が一致した。FloatのキャッシュをDoubleへ渡す負の検証（`506c922e2a73421b9b9bd75428cb839c`）は想定した終了コード4で識別不一致を返す。既存のキャッシュ識別テストにも異なる数値設定を統合し、`1d7e1e04b41a4109b6b17e1a0a1d934e` で成功した。

## SDK利用側の数値接続

SDKには不変のNumerics/Worldソースと閉じた補助プロジェクトのテンプレートを同梱する。既定は事前ビルド済みQ64.64。他の設定は利用側のobj以下でNum/Worldと有効なNavigation/Destruction Cookモジュールを構築し、コンパイル参照と実行先へのコピーを置き換える。親のDirectory.Build設定を補助プロジェクトへ再帰的に適用せず、パッケージ自体を書き換えない。SDKパック時にDoubleを指定した場合も事前ビルドDLLは既定のままであることを `6c23b6cf8ae64a4ba7134cea8cc74a98` と梱包DLLのメタデータで確認した。

ローカルSDK `0.1.9999`（初回パック `dc074d20a66a43b8b32e6105f7770a36`）から、Visual Studioと同じテンプレート生成処理でリポジトリ外に三人称ゲームを作り、実際に復元・ビルドした。利用側の入力にリポジトリ参照は0。Float DLLのRの型はSystem.Single、LogicalVector3は12バイトで、補助プロジェクトとコピー先のDLLハッシュが一致した。

| SDK三人称利用側 | Debug | Release |
|---|---|---|
| 既定128/64 | `a9778200df6340eea3bd0aea3d0e4d8e` | — |
| Float | `7227323311cd4b46b33f75a8546dc102` | `87233520b3db4497af698852007fd123` |
| Double | `2e375eb6cd7a45d9a9d290389d27ae78` | `92e77a2bf10244fca11b21293ceb8e30` |
| Fixed 64/32 | `f2b43cd45da94d6382865fdbbe3815b2` | — |
| Fixed 192/96 | `6478eeded7c941a1a988a85ba1c58e6f` | — |

すべて警告・エラー0。外部TopDownのFloatもNavigation/Destructionの両モジュールを同じ設定で構築し、Navigationデータまで生成した（`81611e98346e4d0590af72e3db88febb`）。外部三人称Floatの180tick保存・再生は `9bfcf37974ff448bace370c4dd0c488b` / `1d2c3a455cfb4dc18fe77aedbf841f38` で成功し、バイト列が一致した。画面操作、描画、Snapshot移行、開発環境なしの配布物起動の完了とは扱わない。

## Snapshotの静的生成

`SnapshotShapeBuilder` と `SnapshotSchemaMap` はRoslynの型情報から保存名、Transient、メンバー構成を抽出する。増分生成へ渡すのは文字列と値の情報で、Compilationや実行時Typeを保持しない。SystemのComponent・Relationの対象・有効なScene構成・起動用Component・自動挿入するBootstrapを収集する。Sceneの構成と起動処理は既存の収集器を再利用する。

`SnapshotValueWriterBuilder`、`SnapshotConstructedValueBuilder`、`SnapshotGraphCodecBuilder` が直接のJSON書込・読込を生成する。組み込みスカラー、文字列、Enum、Nullable、識別子、LogicalScalar、入れ子の値型を扱う。検証付きreadonlyプロパティは直接コンストラクターで復元する。readonlyフィールドも、一つの公開コンストラクターと対応づく場合は引数順で復元する。保存項目名の順序と引数の順序は独立している。グラフは異なる型256個を上限とする。

生成Worldは構造記述・値の読み書きに反射コーデックを呼ばない。未対応の参照型、独自JSON変換、条件付き項目省略等を持つ永続Componentがあり、同じソースでSnapshot操作を要求するとSYSGEN044になる。Sceneだけを利用するコードは保存対応を要求しないが、その未対応経路を後から呼ぶと明示的に失敗する。保存対象を勝手にTransientへ変えない。後から別アセンブリで呼ぶ処理はこのコンパイル時診断の対象外である。

公開の単独API `EcsSnapshotComponentCodec<T>` は残る。テストはこれを独立した旧形式の比較対象として使う。生成Worldからそこへ暗黙に切り替える経路はない。新形式は元の正規性を読み先の丸めと分離し、下記のv2へ移行した。

## Snapshot v2と設定間の読み込み

FixedのLogicalScalar、Single、Doubleの葉を共通の実数として構造識別し、値は正確な有限10進文字列にする。元の文字列・JSON・外側のチェックサムと状態ハッシュを検証してから、読み先へ最近接・偶数丸めする。コンストラクターの検証は読み先の正規化済み状態と比較し、正当な丸めを破損と誤認しない。全Componentを検証した現在形式の文書を作ってからWorld、未ロードの領域の保留、保存更新へ渡す。

実際の数値ソースと生成器をそれぞれ独立してコンパイルし、64/32・128/64・192/96・Single・DoubleのWorldで同じ値の保存バイト一致、1+2^-40の読込時の丸め、後続Entityの範囲外でも全体を変更しないことを検証した。保留する領域も適用前に範囲検査する。同じ設定間試験に領域の保留・保存の更新・後続適用を組み込み、通常のEntities試験も維持する。

形式変更前の実装で保存したv1の固定テストデータを保持し、固定型とSingle型への読み込みを検証した。旧構造と現在構造の対応は閉じたコンパイルから生成する。v1には型一覧がなく、ハッシュだけから未知の旧構造を復元しない。別DLLで消去された別名など、対応を証明できない場合は元の対応設定で開いてv2へ再保存する。詳細はADR-0095。（2026-11-15 追記: Snapshot v1 は破棄し、v1 は「保存し直す」案内付きで拒否する。この段落の v1 の読み込みは当時の実装の記録。）

使われなくなった2個の値生成器と旧生成モードを削除し、JSON属性・引数順・検証付きreadonly値の試験を、本番が使う一つのグラフ生成器へ統合した。

### SDKと実ゲームのv2確認

Snapshot v2対応のローカルパッケージ0.1.9999.1を新たに作成（`1512d3637cd74628b85214da1ab84d7d`）。リポジトリ外の三人称利用側で、保存APIを直接呼ぶ検証コードを含めて既定・Float・Double・64/32・192/96をビルドし、Float/DoubleはReleaseもビルドした。各出力を独立プロセスでロードし、保存→状態変更→復元とv2ヘッダーを確認した。実行のstdoutは全件 `OK <profile> snapshot-v2`、stderrは空。

| 設定 | Debug実行タスク | Release実行タスク |
|---|---|---|
| Fixed128/64 | `f824adfb25e54994afcc456be89089bf` | — |
| Fixed64/32 | `480b31d18a2742378a99c9c029cb7e7d` | — |
| Fixed192/96 | `74051b5a69644b329237f2e14235241f` | — |
| Float | `9ecb49bb8b78498db894db87f4cb288b` | `b3e4c024354045e48b65e7523d6020a0` |
| Double | `134964bd44894e429355f2a66811cfea` | `53b2422f5b2249b5a7235e8c4e94a344` |

TopDown Floatの新パッケージでの調理は `ff556df301d54c9aa8a2dc7d5acab085`。Sourceゲームのv2ビルドも既定・Float・Double・64/32・192/96のDebugが成功し、Releaseは既定 `3428b95498f64e2aa742c2395f860aba`、Float `cf177904350b404f92122c199c22b109`、Double `f8baadf8912a421380c25347bdefde9f` が警告・エラー0。Windowsの既存Snapshot関連9件は `d10c1dcae974403ba969724a84f9a8bb`。これらは画面入力・配布の受け入れとは別の証拠である。

## 回帰検証と修正根拠

入れ子のLogicalVector3、検証付きreadonly値、引数順がフィールド名順と異なるreadonly値、Nullable Enum、識別子、nullable文字列を一つの実行テストへまとめた。旧公開コーデックとJSONバイトを比較し、復元値、nullの維持、コンストラクターで拒否される値を確認する。テストのコンパイルではnullable警告をエラーとして扱う。

生成経路が無かった時点でこの実行テストが失敗することを確認した。追加実装でNullable Enumのnullが既定のEnum値になる不具合を再現し、生成するdefaultの型を明示して修正した。実ゲームのビルドではnullable文字列を生成関数へ渡す警告がエラーになり、書込引数をnullable対応にして同じ条件の回帰をテストへ統合した。型の収集ではRelation対象とBootstrapの漏れも修正した。別クラスの同名メソッドでも保存機能を要求すると誤判定したため、Scene単独利用テストをTheory化して失敗を再現し、Worldの受信型を確認する判定へ修正した。

## 最終検証

数値の既存演算テストと新しい保存契約テストは異なる失敗を検出する。共通契約はTheory化し、丸めを独立した整数の商・余り、float丸めをIEEEキャストと比較した。Snapshotの新しいケースは既存の生成器テストへ統合した。

| 対象 | Debug | Release | 最終BackgroundOpsタスク |
|---|---:|---:|---|
| Numerics | 80 passed | 80 passed | `74f1c891dd5046fc9b0dbd670dad53bf` / `83312e76d9514d49bbbd442b67978f0c` |
| CodeAnalysis | 410 passed | 410 passed | `335d1d0ec07b482da0bca38beadc19aa` / `c3be5e6f3afd4a4ba8f7fbb11d7b90ab` |
| Entities | 84 passed | 84 passed | `c6962262e29042ea8f60bbe9c01dc63d` / `54aa732cc92646c9a020f485a84ad86d` |
| World | 45 passed | 45 passed | `1e9b38e12ed74030aa09995599280596` / `0e8775f7623c4208a850cc2e7338205f` |
| UserEcsProject（既定設定） | Build succeeded; 0 warnings, 0 errors | Build succeeded; 0 warnings, 0 errors | `c2110c346a484ac391919173f63086c3` / `7018deb47161458296830773f569dbf6` |

ログは `D:/CodexBackgroundOps/tasks/<task-id>/stdout.log` と `result.json`。CodeAnalysis/Entities/実ゲームの最終実行は2026-11-02 JST。各テストの失敗・スキップは0。`git diff --check` は成功。テストは下記のコマンドで構成を切り替え、BackgroundOpsでworkspace共有、build-dir・CPU排他、メモリ監視への作業通知を有効にして直列実行した。GUI入力や既存Editorの停止は行っていない。

```powershell
dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/CSharpGameEngine.CodeAnalysis.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test tests/CSharpGameEngine.Entities.Tests/CSharpGameEngine.Entities.Tests.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet build UserEcsProject/UserEcsProject.csproj -c Debug --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
```

先行したソリューションDebugの全体実行は取消済みで、全体成功の証拠には含めない。この実行のFeatureRunnerではTEMPをリポジトリ内に置いたため、Git検索が親リポジトリを発見して21件が失敗した。同じソースでTEMP/TMPをリポジトリ外の `D:/CodexTemp/CgeStaticProduction` にしたFeatureRunner単独再実行は268 passed、0 failed（`14fb20c3dc8244509f17b3c01560a55d`）。期待値と製品のGit追跡は変更していない。以後の最終検証もこの外部TEMPを使う。

## 制作フローと文書の整合

モデル生成物の作成、増分コンパイル、content-index、RuntimeAssetStoreの読込は既存の `ModelArtifactPreparation` / `ModelContentCompiler` / `ModelRuntimeCodecs` と関連テストに存在する。ContentCompilerのcompileコマンドもこの経路を呼ぶ。生成物自体が存在しないという説明はArchitectureと台帳から修正した。

ゲームの `StageSceneDataBuilder`・`SkeletalAssetCache`・静的衝突、プレビューの静的・骨格読込を `ModelAssetBundle` と `StageModelAssets` へ切り替えた。旧 `StageGltfAssets` とランタイムのImported型向けアニメーションAPIを削除した。ナビメッシュと距離場も共通生成物から形状を読む。切替後のSource三人称Debugは`76216e8522fe4c489eb686e1406a6603`で成功。アニメーション・衝突・プレビュー・描画のCPU/WARP計123件は`a56bdbbc617c44299280973019bc10c4`で成功した。Navigation57件・距離場1件・旧アニメーションとの比較1件は`2492a0f4c4ce4b6693b9d8bf3799a2b3`で成功。これらは下記の静的なモデル契約への変更前の検証である。

プレビューのモデル生成はAssetAuthoringワーカーへの非同期要求へ接続した。protocol 14で生成物のフォルダー・ソースURI・モデルハッシュを渡し、表示ホストは照合して開く。要求の取り消しを生成処理へ渡し、古い要求の完了で表示を上書きしない。生成失敗時は表示ホストを入れ替えない。Contracts94件は`953f941ed637447a851af13fd03bc61b`、別プロセスでの生成・元素材削除後の読込・取り込み失敗後の前回生成物の保持・取り消しを含む制作ワーカー14件は`a56bdbbc617c44299280973019bc10c4`で合格した。VSIX全1565件と本体の警告・エラー0のビルド、SDK 0.1.9999.4の梱包は`269e1c38c01e4b75a7e334aad8cbca9a`で成功。実画面の受け入れは別に行う。

ECSの説明は、`in struct` が読み取りComponentであること、`ref State` が任意であること、共有サービスを参照型として受け取ることへ修正した。古いContext例は現在の読み取り・時間取得の例に置き換えた。Architecture、Ecs-Runtime、台帳、Reverse、Conformance、Changelogを現在の実装境界へ同期した。

## Identity描画とプロジェクト設定ページ

`CgeDepthMapping=Logarithmic|Identity` を数値型から独立させ、ゲームのカメラ提出を条件付きコンパイルで選ぶ。Identity透視は通常の投影と直接深度を使う。CPUでは正射影と別の種類として検証し、GPUは既存Stageの直接深度経路を使う。行列・復元行列は同じで、リサイズとデバイス復旧に入力を保持する。ゲームの生成ソースと出力はidentity階層へ分け、数値DLL・物理キャッシュの識別には混ぜない。対数観測者の補助表示は提出しない。影と内部GIは各パスの明示的な方式を維持する。

| 対象 | Debug | Release |
|---|---|---|
| Rendering全195件 | `ef5b7fd83a4b4fd0b855dbbf9ae3273f` | `8cbc99902a2b4d95888345ad58ebcb9e` |
| 非表示WARPのビュー・リサイズ・復旧10件 | `5a4fcbd4869b4018b241ade97cf6dd8a` | `15e4e0ff89b74f2eaa710011449c5504` |
| Sourceゲーム | Float/Identity `8d60d8988489415584ba6ec20625e26a` | Double/Identity `004e97f91c734a8abe81a99f0e47af65` |

標準CPSのPropertyPageSchemaで日本語・英語のC# Game Engineページを追加した。数値型、固定幅、小数位置、深度方式をcsprojに保存する。固定専用の項目はFixed選択時に表示する。Visual Studio付属の実MSBuild型とSystem.Xamlで両言語を読み込み、選択肢・範囲・保存先を確認した（`59505ae9787f4928b9f535a7d379ca38`）。生成器全393件は設定ごとの出力衝突と、VSLangに対応するページの評価も確認した（`313f53687899426a90f6758dd0e61e2e`）。Visual Studioの実画面での選択・保存は後段の受け入れで確認する。

SDK 0.1.9999.2は `0c87f63bfa7f48b494400b1a53e3d26d` で作成。外部の三人称はFloat/Identity Debug `c5a78489acbd485f9b7a11df6f5d1966`、Double/Identity Release `1e01f3723fba46cea0d1fa829b511709` が成功。TopDown Float/Identityの調理は `56abdf208b8142daa3ee1f0c9b5b2de5`。

## シーン入力・Inspectorと編集の整合

固定値のJSON解析に残っていたfloat経由とQ64.64固定の上限を除去した。選択した型の定数から幅・小数位置を読み、整数の係数・指数で最近接・偶数丸め後の範囲を検査する。正確な10進入力から生成し、元のJSON位置で拒否する。負の下限、正の上限付近の丸め、整数設定の半分、192bitの大きな値、最小付近の値、巨大な指数を既存生成器試験へ統合した。NUM001の設定別規則も、任意の名前のSDKゲームへ適用する。

VS側の回帰で、正確なParseInvariant式をScene DSL・保存前の予測モデルが認識せず、保存できない問題を再現した。LogicalScalar自身の静的メソッド・定数文字列・検証済み範囲という閉じた条件で数値として認識し、JSONへ正確な数値として戻す。Inspectorは固定型の範囲を使い、拒否時にfloatへ切り替えない。表示の投影も接続した。固定値の角度をJSONへ戻すときに単位を落とす不備も修正し、無関係な回転行を変更しない。Doubleは宣言側のfloat64プロファイルを確認して編集可能にし、doubleリテラルを維持する。

関連するVS側224件は `10dde893bfb948bb9dec73445a9b6caa` で全件成功。細かい値を保つ編集・範囲外拒否・Doubleの編集可否・全テンプレートの編集不変条件を含む。生成器の先行した全410件は `0225d8c686564f2a837724d1d67bcb51` / `a2979a55dc4e457a9f772a17157d2150`。編集側の最終修正を含む生成器全410件は `335d1d0ec07b482da0bca38beadc19aa` / `c3be5e6f3afd4a4ba8f7fbb11d7b90ab`、VSIX全1565件は `54811aa79d65470fa4705a243a7de57a` / `28244b9ebcf24680b0ba7af132a8f10c` でDebug/Releaseとも成功した。実ゲームは既定Debug/Release `c2110c346a484ac391919173f63086c3` / `7018deb47161458296830773f569dbf6`、64/32 Debug `f61dfec1b422442bb59117593fa5f998`、192/96 Debug `2a1a8a13b0034e74b539e9fd9af1590e` が成功。

外部SDK 0.1.9999.2のFloat Debug / Double Releaseは、Identityで12フレーム、56オブジェクトを描き、W@3-9のスクリプト入力で約4.5m移動した。デバッグレイヤー有効・D3D12エラー0。タスクは `59abbae6c4e54724813d04c8bc85a1d5` / `bbdfb28c6bd14c85aaf1aac975c8cb55`。画像は `D:/CodexTemp/CgeNumericSdk-20261102/identity-render-<float32|float64>/final.png`、報告は同じフォルダーのreport.json。Float画像を目視し、キャラクターと床・壁・空の描画を確認した。ホスト入力は無効、ウィンドウは非表示。これは実OS入力や制作全体の受け入れとは区別する。

### 最終SDKと設定評価

最終のSDK 0.1.9999.3は `ecaf78fb39de420dab05f06a062f9a41`。日本語の二重フォルダーを修正し、英語・日本語の正規パスをパッケージ内で検査した。外部三人称の日本語ページ評価は `e21806bae2654b77ad1a575627326ab4` で、既存ファイル `csharpgameengine.sdk/0.1.9999.3/build/PropertyPages/ja-JP/CgeRuntime.xaml` を解決する。Float/Identity Debugは `2d63cb5ccb304909ac5c6469e23ee0f8`、Double/Identity Releaseは `2c53d05bb389457ca1666a0a332cc84b`、TopDown Float/Identity調理は `095cf1fbff69453d8bee8cd617a2198a` で警告・エラー0。数値規則は外部ゲーム名にも適用される。

同じ最終出力のSnapshot保存・変更・復元は `e1c57ff564f64d43b3d7e196442d26c3` / `8813c0ee281b458481f2aa09eebbff5b` で成功し、それぞれ `OK float32 snapshot-v2` / `OK float64 snapshot-v2` を確認した。不正な深度指定は `5c93cf495d544e288b08eb9ce6ef7fc4` が期待したエラーメッセージ・終了1で拒否する（負の試験なのでBackgroundOpsはFailed）。

## 共通モデルへの切替後の制作・配布検証

SDK 0.1.9999.4から、ウィザードと同じ生成処理でリポジトリ外の4テンプレートを作成した。三人称のFixed 128/64・64/32・192/96、Float/Identity Debug、Double/Identity Releaseと、FirstPerson・TopDown・Vehicle Debugのビルド、およびDoubleの自己完結配布は`94db3b83e2ba403e8224ce30bb0119e0`で成功した。元モデルのコピーとImport/SharpGLTF/Assimp/Roslynの実行依存は0。テンプレート素材の管理情報未作成と利用条件未登録などの警告は残り、警告0とは扱わない。

テクスチャ付きBoxを新規追加し、実際のAssetAuthoringワーカーで正規メタデータを保存・モデル生成した。シーンへ位置R=3.25のBoxを追加して保存し、ファイルから読み直して編集値を確認した。Float Debugの再ビルドとDouble Releaseの再配布は`0aebb8f0adb246dba1226e39d499171f`で成功した。これは画面のInspector操作の証拠ではない。

JSONの実行時型検索を無効にした配布プロセスで、FloatのSnapshot v2保存は成功したが、Doubleでのモデル読込はShippingContentManifestの動的なJSON契約で失敗した（`81c9a9a0e48344cdb6948dae391418cb`）。モデル本体のバイナリ投影にも型の探索が残っていたため、列挙型の幅・参照の有無・配列要素を閉じた一覧へ明示し、項目とJSONの読み書きはソース生成器の契約へ変更した。配布用一覧とファイル編集の復旧記録も生成された契約へ変更した。Content全71件は実行時型検索を無効にして`c2ca910138fd4f9abd5614d655f61c3d`で合格し、v1の配布用一覧の固定バイト列との一致を確認した。

修正版SDK 0.1.9999.5の梱包は`5ceb34cb0a534d22a221f39a7888db72`、外部4テンプレートの再ビルドとDoubleの再配布は`fe80d48934fd4b8893822633c67358c6`で成功した。自己完結配布物の実行は`1f5c9ef91210468daae76dd5a85f2291`で成功。旧形式の30モデルを型検索無効で読み、Floatで保存したSnapshot v2を別プロセスのDoubleで位置1.25へ復元した。これは生成Worldの保存APIの検証であり、実行中のゲーム全体の保存UIの証拠ではない。

同じDouble/Identity配布物はWARPの110フレームで移動15.700m・ジャンプ上昇1.228m・下降1.228m・最低カメラ高0.985m・音要求15回を記録し、デバッグレイヤー有効・D3D12エラー0。キャプチャ画像でキャラクター・グリッドの床と壁・背景を確認した。元glTF/GLB、Import/SharpGLTF/Assimp/Roslyn依存は0で、coreclr.dllを同梱し、runtimeconfigに外部Framework要求はない。DOTNET_ROOTを存在しない場所へ向け、PATHをSystem32だけにした条件でも起動した。証跡は`D:/CodexTemp/CgeModelSdk-20261102-Cutover4/production-evidence/`の`shipping-audit.json`、`shipping-render/report.json`、`shipping-render/final.png`。これらの証跡は下記の最終SDK 0.1.42の再実行結果へ更新済みである。

## 最終コードの回帰と通常配布版

数値・保存・描画・素材・制作の回帰は次の範囲で合格した。`ead2762c2ba44918961002075bb11761`は後続のVSIX版番号2件で失敗したタスクであり、下表の先行した各試験の合格をタスク全体の成功とは扱わない。元マニフェストも0.1.11へそろえ、通常版SDKは既存NuGetキャッシュを再利用しない0.1.40へ更新した。

| 対象 | Debug | Release | 証跡 |
|---|---:|---:|---|
| 生成器全体 | 410 | 410 | `ead2762c2ba44918961002075bb11761` |
| Entities全体 | 84 | 84 | 同上 |
| Rendering全体 | 199 | 199 | 同上 |
| Content全体・型検索無効 | 71 | 71 | `c2ca910138fd4f9abd5614d655f61c3d` / `ead2762c2ba44918961002075bb11761` |
| ContentCompiler全体 | 33（先行検証） | 33 | `405ea4d698ee43db9de03b7b8def0af0` / `ead2762c2ba44918961002075bb11761` |
| Contracts全体 | 94（先行検証） | 94 | `953f941ed637447a851af13fd03bc61b` / `ead2762c2ba44918961002075bb11761` |
| Navigation全体 | 57（先行検証） | 57 | `2492a0f4c4ce4b6693b9d8bf3799a2b3` / `ead2762c2ba44918961002075bb11761` |
| VSIX全体 | 1565（先行検証） | 1565 | `269e1c38c01e4b75a7e334aad8cbca9a` / `880ae6fc1961469f975c7a7125f6b242` |
| 制作ワーカー | 14（先行検証） | 14 | `a56bdbbc617c44299280973019bc10c4` / `880ae6fc1961469f975c7a7125f6b242` |
| 元素材なしの距離場・破損時保持 | 1（先行検証） | 1 | `2492a0f4c4ce4b6693b9d8bf3799a2b3` / `880ae6fc1961469f975c7a7125f6b242` |
| アニメーション・衝突・プレビュー・描画のCPU/WARP | 123（先行検証） | 148（実行中編集等を含む最終回帰） | `6915dcdcf54e419dbc4935b15ec7fc1d` / `40a2804c331a46b6ad9420652c550269` |

先行したWindows試験はDebug123件が成功し、Releaseのビルド・試験開始後にPC全体のメモリ圧迫で自動取消になった。この取消をRelease成功とは数えない。その後、下記の追加修正を含む全148件がReleaseで成功し、未確認だった回帰を解消した。

SDK 0.1.40の梱包とVSIX 0.1.11のRelease回帰は`880ae6fc1961469f975c7a7125f6b242`、リポジトリ外の4テンプレートのビルド・三人称Float Debug・Double Releaseの自己完結配布作成は`69e15cf3734f47538226bbcd7304c218`で成功した。パッケージ内のバージョン、モデル生成targets、日本語ページ、ゲーム側Import参照なしを確認した。最終SDK 0.1.42の梱包・配布と全テンプレートの実行結果は以下に記録する。

## 実行中の数値編集と参照先へのモデル引き渡し

実行中シーンのメタデータアダプターは、選択した固定値にも旧float暗黙変換を適用していた。ビルド済みゲームが生成する`ParseRuntimeLogicalScalar`を型付きdelegateで接続し、選択した数値ライブラリの`LogicalNumeric.ParseInvariant`で直接読む。読み取り処理の欠落とCLR型の不一致を拒否し、floatへ切り替えない。最小刻み付近の差と大きな固定値、拒否条件、既存の画面更新を含む25件は`49916b7f65784289a4989b660e842567`で合格し、生成器全410件も同じタスクでDebug成功。

関連試験では、ゲームをProjectReferenceで参照する先へDLLはコピーされるが生成モデルが届かないため、モデルを使う実行中シーンの読込が失敗した（`06e25ce5efd14094a0b9702d7e59979e`、24合格・1失敗）。モデルの現在の生成物一覧を標準Content項目へ登録し、CopyToOutputDirectoryで参照先へ引き渡した。配布対象は従来どおり現在の厳密な一覧から選び、標準Content側で重複して配布へ追加しない。上記25件でこの読込も成功した。

この追加修正と次の欠落報告の修正を含む最終配布物はSDK 0.1.42・VSIX 0.1.13。旧0.1.40の成功を最終候補の証拠へ読み替えず、最終候補の梱包・配布実行を別に確認した。

Releaseを全体へ広げると、正しいモデル一覧があり指定モデルだけが欠けた場合に、内部のKeyNotFoundExceptionが描画の欠落報告へ届かない2件を再現した（`c12fcfae505d4036a42d149231001eff`、148件中146合格・2失敗）。ModelContentIndexの検索契約は維持し、ModelAssetBundleの読込境界でこの欠落だけをFileNotFoundExceptionへ変換した。欠落を一度報告して描画しない既存の挙動へ接続する。破損や曖昧な一覧は従来どおり拒否する。

修正後は`40a2804c331a46b6ad9420652c550269`でモデル準備10件、元の欠落報告2件、モデル・描画・衝突・実行中編集の全148件がRelease成功した。同じタスクで梱包整合17件とSDK 0.1.42の梱包も成功。直前の候補では`c12fcfae505d4036a42d149231001eff`の先行部分でNumerics全80件がDebug/Release、生成器全410件とVSIX全1565件がRelease成功した。最終候補のVSIXは0.1.13。Windows Releaseのメモリ取消はその後の成功した148件で解消した。

SDK0.1.42の外部4テンプレートビルドと三人称Doubleの自己完結配布作成は`90573f3a2f6641d6a3abb7b8e2933e7e`で成功。最終配布物の実行は`8d333c2f11364430a2102d34420cc2f9`で成功し、型検索無効で旧30モデルの読込とFloatからの別プロセスDouble復元、WARP110フレームの移動15.700m・上昇/下降1.228m・音要求15回・D3D12エラー0を確認した。配布境界の監査と画像は既述のproduction-evidence以下に更新した。最終配布物には元モデルとソースSDK/Roslynの実行依存がなく、開発環境のPATHを使わずに起動する。

## 最終テンプレートの実行回帰

`9691cc4942ac413cbf89b46b40e2caa3`で、最終SDK0.1.42の4テンプレートがすべて成功した。いずれもホスト入力を無効にした非表示WARPのスクリプト入力で、D3D12デバッグレイヤー有効・エラー0。各報告・画像はproduction-evidence以下のruntime-<Template>へ保存した。

| テンプレート | 観測した結果 |
|---|---|
| ThirdPerson Float/Identity Debug | 110フレーム、移動15.700m、上昇/下降1.228m、音要求15回 |
| FirstPerson Debug | 移動16.000m、上昇/下降0.863m、最低カメラ高1.654m、音要求15回 |
| TopDown Debug | クリック移動と停止、経路探索あり/なしの比較、障害物迂回、長押し/解放を検証。目的地誤差0.01m、経路15.44mに対し直線9.64m、最終ずれ0 |
| Vehicle Debug | 通常入力で移動62.614m・旋回25.5°。半分のアクセル入力で23.107m、半分の操舵入力で12.8°。速度/ギア表示も検証 |

配布・設定伝達・保存境界・共通モデルのバックグラウンド受け入れは完了。ユーザーが誤解だったと確認したcontextの追加要求は対象外とする。M0128全体は、以下の実画面操作が未検証であるためPartiallyImplementedを維持する。

## 元モデルを含めない配布の画面比較

`a036d12644424872899d22f500e205f0`で、旧SDK 0.1.9999.3の実行用出力を比較専用のコピーへ移し、元GLB/glTF 29ファイルをコピー対象から外して修正前の動作を再現した。元の出力は変更していない。修正後は最終SDK 0.1.42の自己完結Double配布物を使用し、両方とも元モデル0ファイル、Double/Identity、WARP、1280×720、入力なし、12フレームでの保存を指定した。

旧版は`glTF file not found`で描画開始前に終了し、修正前の画像は存在しない。修正版は12フレームを完了し、キャラクター・床・壁・スロープを表示した。デバッグレイヤー有効、D3D12エラー0。エラー、条件、修正後画像と描画レポートは`D:/CodexTemp/CgeVisualComparison-20261102/{before|after}/`に保存した。比較不能な修正前画像を合成せず、取得できなかった理由と修正後の実画像をユーザーへ提示した。

## モデル生成基盤の先行検証
切替前のモデル生成基盤では、次の検証を行った。組み込みglTF取り込み器を追加し、専用ワーカーで共通IRへ読み込む。Manny本体・歩行クリップ・テクスチャ付きモデルで形状・UV・逆行列・骨名の対応・回転の向きと補間を既存読込と比較した。材質・画像は既存IAssetCookerで別ワーカー内に生成し、親は識別子・ハッシュ・正規形式・依存関係を検証する。テンプレートの床にあるグリッド情報も材質生成物へ保存する。材質9件は `6a64b4d14de84ca19dc8309743c7dd09`、床を含むモデル生成15件は `e2afe3a2caba480fb71ba8f86235cd96` で成功。これらは実行・プレビューの切替前の検証である。

前回成功したモデルが資産一覧に残ってもコンパイル結果の参照一覧から抜ける問題を `b6d7f58a686e4594bb08d73eca806d32` で再現した。失敗した資産の正規エンベロープと依存閉包を検証して結果へ残し、全件失敗・資産なしも扱う。拡張した既存試験7件は `1c56257786724625976a274680b17bf5` で成功し、不要データ整理後も保存済み生成物を読み戻せる。プロジェクト一括CLIの2資産生成→片方失敗・外部参照変更→復旧→削除→資産なしの試験は `929410a4ef9349a78b59f2e757f4b5c5` で成功した。取り込み本体を変更しない外部参照の変更も、保存済みCASの実データまで確認した。

## 実画面とVisual Studio用ビルドの確認

専用CgeStatic42、日本語・暗色で新規UiFeedbackを作成し、開き直さず最初の設定ページが開いた（21）。AnyCPU/x64を復元前から宣言する4テンプレートの修正を実画面でも確認した。数値型5種類と固定幅・小数位置、深度方式の選択・保存は22〜27。InspectorでSM_CubeのPosition.Rを12から13.25へ変更し、文書を保存してJSONに同じ値が入ることを確認した（28/29）。実画像と詳しい操作記録は`.artifacts/StaticProduction/ui-evidence-20261103/`に保存し、`comparison.html`で修正前後を並べた。

配置済みモデルのCook一式の発行漏れを修正し、同じモデルのプレビューに成功した（16→17）。登録したID `7daec64e-0a20-4e51-8d18-1d72a89e2e45`は、UiRenamedCubeからUiFinalCubeへの改名後も保持された。再配置後に消えたテンプレート一覧と新規作成は、通常終了後の`/UpdateConfiguration`で回復した（`9388093525844b1cab10a0acfafbf6f6`）。再配置スクリプトは、配置が変わった場合だけ構成更新→テンプレート更新を行う。

IDEの最初のDouble/Identityビルドは、補助DLLの生成先で261文字のパスになり失敗した（30）。生成先を中間ルートの構成・数値設定以下へ短縮した。続いてVisual Studio用MSBuildでモデル・距離場の新しいPath APIが使えない不一致を観測し、共通のMSBuild関数へ変更した。SDK0.1.44の実プロジェクトはDebugビルド・自己完結Release発行・元モデル0件のWARP12フレームを完了し、D3D12エラー0（`8c8d32461e3b43b78ca3810a2ab997fd`）。関連の出力テスト3件は両構成、梱包・テンプレート・Authoringなど52件も成功（`e8168ae33b604f38bcae073982368d86`）。IMP001・IMP703などの既存警告を非表示にしていない。

SDK0.1.44の編集用参照はDoubleでも既定DLLを使っていた（`9e765198ffbe4727b63ce76539dbadcd`）。SDK0.1.45では生成と参照解決を分け、編集用評価も生成済みの同じ設定のNumerics/Worldを使う。未生成ならビルドを要求する。既存の数値出力試験へ参照選択を統合し、修正前の失敗（`81a379f95b2f470699c0fd7912d64faa`）と修正後の成功（`d1466143e4f64c9f9c00e9f0d827b020`）を確認した。通常ビルドと編集用評価の実プロジェクトでの一致、最終梱包とIDE起動・再開は継続中。

## 残る受け入れ条件

1. 最終候補の各数値設定で、IDEからの編集・保存・再読込とゲーム起動を含む制作一巡を確認する。
2. 最終結果を台帳へ反映して完了判定する。生成Worldの保存APIと別プロセス復元を、ゲーム全体の保存UIの実装証拠にしない。

M0128はこの受け入れ条件が残るためPartiallyImplemented。製品仕様の新たな未決事項はない。contextの追加要求はユーザーが勘違いだったと確認済みで対象外。

### 2026-11-04: ThirdPerson シーン表示の壁位置（F-L8-036 / M0128）

今回の壁修正は Implemented / TestVerified・RuntimeVerified（非表示描画）。Scene Designer とシーン・モデルのサムネイルに残っていた旧単一 glTF 読込を、AssetAuthoring worker の共通モデル成果物へ接続した。成果物ハッシュを照合し、階層変換・サブメッシュ材質・選択用形状を保持する。シーン単位でモデルをまとめて準備する。片側原点の壁 SM_Cube2 は配置値を変えず、誤った F=20..60 から F=-20..20 へ戻る。実 ThirdPerson・ソース削除後の読込・ハッシュ不一致を含むワーカー15件、固定値と float の四分の一回転・部品 ID/選択/材質、VSIX Release 全1596件が成功した。旧読込を再実行した修正前と修正後を同一カメラ・900×900・WARPで直接描画し、両方54描画オブジェクト、D3D12エラー0件を確認した（.artifacts/StaticProduction/wall-preview-20261104）。VS の画面写真ではなく、実ステージのエンジン描画の比較。FBXと生成プリミティブの既存経路、プレビューのテクスチャ制約は維持する。F-L8-036/M0128全体の PartiallyImplemented は維持し、残る制作一巡の実画面受け入れを別途確認する。設計・実装・検証の対応は 20261104-thirdperson-wall-preview.md に記録する。

### SDK46 / VSIX16 と実画面の残件
SDK46の発行・実行・数値参照照合・配布専用境界は上記ジョブで成功した。VSIX16の配置とロード済みDLLのSHA256は `3566A2E0DC1DE6B742D22D829AE7EC8DA72173BB3D86BB9CDF553A66C124215D` で一致する。

実VSはステージの読み込み後に「Sceneの表示に失敗」と表示した。画像 `.artifacts/StaticProduction/ui-evidence-20261103/32-sdk46-scene-preview-failed.jpg` と同じ時点のAX記録を保存した。通常GPU環境とWARPの差、runtime viewとシーンプレビューの差が原因候補だが、未確定。固定値の座標を動かす回避や待ち時間延長、エラー非表示は追加していない。

診断時には未保存データの可能性が指摘されたため、保存状態を確認し、ユーザーの許可を得て確認用VSを再起動した。その後のDouble角度とアセット選択の実画面確認は下記を参照する。各数値設定とゲーム起動までの制作一巡は未完了。
### 実行用プレビュー停止の修正候補（VSIX17）
別プロセスの再現で、シーン表示は通常GPU/WARPともOpen・Update・Render・Close成功。実行用プレビューだけ最初のRenderが停止した。取得したスタックでは、素材の非同期読込みを待つRenderと、同じGateで待機する多数の15Hz周期更新を確認した。TickEditorWorldをロック取得待ちにせず、処理中の回を次の周期へ送る最小修正で、同じSDK46・同じ配布物の両GPUでOpen・Update・Render・Closeが成功した（d4ab6a673dcf4fa780d9ef9886ff6243）。次の更新は既存のMeasureFrameDeltaを通る。座標やGPU選択は変更しない。

VSIX17を梱包済み。PackagedRuntimeViewTestsの共通起動・終了を整理し、既存ゲームのモデル付きFirstPersonStageSceneの初回描画を追加した。梱包関連23件とRelease全1597件が成功（a5b2b0ebfbc1429f8d69487b6919ffee）。値編集・ドラッグ・定期更新・破棄を含む既存Windowsプレビュー9件も成功（cecda80016634c30a83f6bfd834badf7）。SDK46の配布物は通常GPUでも12フレーム・D3D12エラー0件だった（c367662ec9b547c797bb6e9cf945b461）。

SDK 0.1.48 / VSIX 0.1.23 では、Doubleの角度45度を入力・保存し、再起動後も保持されること、モデルのアセット選択・保存、シーン表示を確認した。同じ出力先に対するサムネイルとシーンの同時生成はプロセス間で排他する。関連するContentCompiler 34件、VSIX 1,611件が成功した。全数値設定での実画面操作とゲーム起動を含む制作一巡は未確認のため、M0128はPartiallyImplementedを維持する。

ThirdPersonはUE 5.0 `ThirdPersonMap` の配置・開始位置と灰色／青の資産を使用する。格子はワールド基準で近似し、壁面テキストは未移植。モデル・壁・開始位置に関する15件のテストと、VSのシーン表示を確認した。同一カメラ条件の差し替え前後と実VS画面は `.artifacts/UE50-reference/render/comparison.html` を参照する。

SDK 0.1.49 / VSIX 0.1.24 のソースでは、UE 5.0の壁文字を静的メッシュにし、格子の原点を物体ごとに設定した。キャラクターの初期位置をステージのPlayerStartに合わせ、キャラクター単体と起動シーン群にそれぞれ適した表示範囲を作る。静的メッシュ選択ではglTFのスキン付きモデルとメッシュを持たないファイルを除外し、単一アセットのInspectorでは512pxの派生画像をペインに合わせて表示する。VSIXと描画ホストのコンパイル、VSIX関連92件、サムネイル契約5件を確認した。WARPの実描画で外壁と格子の比較、および128pxと512pxの出力を保存した（`.artifacts/UE50-reference/renderer-check/comparison.html`）。修正前のゲームはWARPで約0.82 FPSかつ床下への落下を再現した。梱包後の実画面、修正後の通常GPUでのフレーム時間とゲーム操作は検証待ち。

## 経過の記録（Roadmap.md・Architecture.md から移動、2026-11-15）

ADR-0098 の段階 A の作業として、Roadmap.md と Architecture.md に積んでいた M0128 の経過報告を書かれたままここへ移した。版の番号や「未確認」は書かれた時点のもので、現在の状態は上の各節と残件が正本である。v1 の読み取りの記述は 2026-11-15 に削除済み（ADR-0098 §5 規則 2）。

- Architecture.md: 生成後に元ファイルを削除して共通読込できることを確認済み。
- Architecture.md: 切替後のSource/SDKビルドと関連CPU/WARPはDebugで検証済み。
- Architecture.md: 実行時の型検索を無効にしたContent全71件が合格した。修正版の自己完結Double配布物で旧30モデルの読込、Floatからの保存再開、WARP110フレームの移動・ジャンプ・描画を確認した。最終SDK0.1.42の自己完結Double実行と4テンプレート実行、関連148件のRelease回帰が成功した。
- Architecture.md: 実画面では新規作成、素材追加、モデル描画、登録後のIDを保持した改名、Inspector編集と保存を確認した。最終候補のIDE実行・再開はM0128で追跡する。
- Architecture.md: 実設定画面で全数値型と固定幅・小数位置の保存を確認した。
- Architecture.md: 各設定の画面なしの物理保存・再生と、別設定でのSnapshot読み込みを検証済みだが、制作画面の操作・描画・配布物の受け入れは別途確認する。
- Architecture.md: 設定の評価・XAML読込とCPU/GPUのビュー契約は検証済み。数値型・固定幅・小数位置・深度の実画面からの保存は確認済み。SDK0.1.44はVisual Studio用MSBuildでDebugビルド・自己完結Release発行・元モデルなしの実行に成功した。最終候補のIDE起動・再開はM0128で確認する。
- Architecture.md: 2026-11-04: ThirdPerson シーン表示の壁位置（F-L8-036 / M0128） 今回の壁修正は Implemented / TestVerified・RuntimeVerified（非表示描画）。Scene Designer とシーン・モデルのサムネイルに残っていた旧単一 glTF 読込を、AssetAuthoring worker の共通モデル成果物へ接続した。成果物ハッシュを照合し、階層変換・サブメッシュ材質・選択用形状を保持する。シーン単位でモデルをまとめて準備する。片側原点の壁 SM_Cube2 は配置値を変えず、誤った F=20..60 から F=-20..20 へ戻る。実 ThirdPerson・ソース削除後の読込・ハッシュ不一致を含むワーカー15件、固定値と float の四分の一回転・部品 ID/選択/材質、VSIX Release 全1596件が成功した。旧読込を再実行した修正前と修正後を同一カメラ・900×900・WARPで直接描画し、両方54描画オブジェクト、D3D12エラー0件を確認した（.artifacts/StaticProduction/wall-preview-20261104）。VS の画面写真ではなく、実ステージのエンジン描画の比較。FBXと生成プリミティブの既存経路、プレビューのテクスチャ制約は維持する。F-L8-036/M0128全体の PartiallyImplemented は維持し、残る制作一巡の実画面受け入れを別途確認する。設計・実装・検証の対応は 20261104-thirdperson-wall-preview.md に記録する。  2026-11-04 実画面追記: SDK46/VSIX16の梱包と実プロジェクトの発行・WARP起動・制作専用DLL除外は成功した。一方、専用VSでのシーン表示はSceneRenderFailedを実際に観測した（32-sdk46-scene-preview-failed.jpg）。非表示の比較描画の成功を実VSの成功へ読み替えない。この統合表示はInvestigating / Mismatch、F-L8-036・M0128全体はPartiallyImplementedのまま。通常終了の自動承認審査と操作ツールのタイムアウトがあり、保存確認済みのVSの終了・診断付き再起動を明示確認待ち。詳細と次の検証はM0128へ記録した。  追記: 実VS16の失敗は実行用プレビューでの素材読み込みと周期更新の待機を独立再現した。VSIX17は周期更新を積み残さず、同じSDK46で通常GPU/WARPの描画が成功。梱包関連23件、Release全1597件、既存Windowsプレビュー9件が成功。17の配置と実画面確認は保存済みVSの終了・再起動の明示確認待ち。Current integration remains PartiallyImplemented / Partial; see M0128 and 20261104-runtime-preview-ticks.md.
- Roadmap.md: The accepted M0128 design adds configurable fixed 64/128/192-bit storage or actual float/double computation, independently selectable Logarithmic/Identity projection, static persistence and an authoring-to-distribution path. F-L1-012 remains partial pending final-candidate acceptance. Numeric-library modes, Source/SDK dependency and cooking-tool propagation, isolated outputs, profile mismatch diagnostics and mathematical-range handling are implemented. Actual Japanese project pages save all numeric modes, the three fixed widths/fraction positions and independent Identity depth. SDK0.1.42 passes all four scripted template runtimes, source-free self-contained Double execution and separate-process Float-to-Double restoration. SDK0.1.44 also passes Visual Studio MSBuild Debug build, self-contained Release publish and source-free WARP execution. SDK0.1.45 corrects the editor's numeric references; its final package, actual editor/runtime DLL agreement and minimal IDE run/reopen acceptance remain unverified. F-L1-011 provides common persisted values; WorldCell v2 and F-L6-007's generated Snapshot v2 include explicit legacy reading. [M0128](Milestones/M0128-Static-Production-Flow.md) and [Numeric Configuration](Specifications/Numeric-Configuration.md) record the exact verification scopes. Earlier fixed-Q64.64/log-only plans below are extended by these accepted numeric/projection choices.
