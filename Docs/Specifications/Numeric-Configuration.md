# ビルド時の数値設定

- Status: PartiallyImplemented
- Feature: F-L1-012
- Design: [静的構成と制作フロー](../refactoring-designs/20261031-static-production-flow.md)
- Decision: [ADR-0093](../Decisions/ADR-0093-Configurable-Numeric-Builds.md)

## 目標の契約

プロジェクトは固定小数点64/128/192bitと小数位置、float、doubleから位置・シミュレーションの計算型を選ぶ。既定は128bit・小数64bit。小数位置の既定は幅の半分で、固定小数点の有効範囲は0から総ビット数−2とする。符号と数値1を表す領域を残す。色・音声・GPU入出力の既存型はこの設定で置換しない。

Float/Doubleの実体はSystem.Single/System.Doubleとし、演算を固定小数点へ変換しない。依存DLL、生成コード、キャッシュ、SDK配布物も同じ設定で構築する。対数／Identity描画は独立した設定とする。保存する数値は [Numeric-Persistence](Numeric-Persistence.md) の共通表現とし、設定間の読込で最近接・偶数丸めと範囲検査を行う。異なる設定間のシミュレーションのビット一致は要求しない。

## 現在の実装

数値ライブラリ、Sourceモードのゲーム、パッケージSDKをMSBuildへ接続した。ゲームのプロパティを数値型を使う依存プロジェクトと作成ツールへ渡し、生成コードと共有ランタイムにも同じ別名を適用する。標準の設定ページと、シーンJSON・Inspectorの選択幅に応じた入力範囲検査を接続した。実際の日本語設定ページでFixed64/32・128/64・192/96・Float・Doubleの選択とcsprojへの保存を確認した。最終候補の実行・再開はM0128で追跡する。

| プロパティ | 現在の値・既定 |
|---|---|
| CgeNumericMode | Fixed / Float / Double。既定Fixed |
| CgeFixedBits | 64 / 128 / 192。既定128 |
| CgeFractionalBits | 0〜幅−2。省略時は32 / 64 / 96 |
| CgeNumericProfileId | fixed-幅-小数位置 / float32 / float64 |

```powershell
dotnet build src/Numerics/Numerics.csproj -c Debug -p:CgeFixedBits=192 -p:CgeFractionalBits=96
dotnet build src/Numerics/Numerics.csproj -c Release -p:CgeNumericMode=Float
dotnet build UserEcsProject/UserEcsProject.csproj -c Debug -p:CgeNumericMode=Float
dotnet build UserEcsProject/UserEcsProject.csproj -c Debug -p:CgeFixedBits=192 -p:CgeFractionalBits=96
```

既定Q64.64は既存の最適化済み実装を使う。他のFixed設定は `ProfileLogicalScalar` をコンパイルする。格納する値はlong、Int128、3桁のInt192で、実体サイズは8/16/24バイト。幅と小数位置は生成される定数で、値のインスタンスに設定を保持しない。非既定の出力は `bin/<Configuration>/fixed-<bits>-<fraction>/<TFM>`、中間出力も設定ごとに分離する。既定の出力先は維持する。

`FixedArithmetic` は最大8個の64bit桁で加減乗除、剰余、平方根、幅・小数位置の変更、Single/Doubleとの相互変換を処理する。公開する固定幅は64/128/192bit、数学関数の一時型は256bit。乗除・縮小・平方根は最近接・偶数丸め。演算の作業領域は固定長のstackallocで、管理ヒープへ割り当てない。出力は範囲検査後にだけ更新し、入力と出力が同じ領域でも動作する。10進文字列と共通保存形式の変換は既存の保存境界を再利用する。

数学関数はRawのInt128前提を外し、剰余・整数倍を数値型の操作へ集約した。係数の1/nは整数nを固定小数点へ拡大してから割る方式をやめ、分数を直接丸める。これにより64bitで大きな階乗の分母を扱える。軸角の1/2も小数64bitの生ビット定数から分数へ変更した。

小数位置が32bit未満、または整数部が4bit未満の固定設定では、数学関数だけが256bitの一時型を使う。作業精度は選択した小数位置と32の大きい方で、保存型は変更しない。結果を選択した型へ丸めて戻せない場合、Try処理はfalseと既定値を返す。π・π/2・2πは個別のプロパティで取得し、その定数を表せない場合だけOverflowExceptionとする。型初期化で数学関数全体を使用不能にしない。小数精度が64bitを超える設定ではsin/cosの級数とatanの項数を精度に合わせ、既定Q64.64の多項式経路は維持する。

Float/Doubleはビルド時の別名でLogicalScalarをSystem.Single/System.Doubleへ解決する。拡張メンバーは生成・検査・保存用のAPIを補い、演算と三角関数はそのCLR型とMathF/Mathを直接使う。LogicalVector3の実体は12/24バイト、LogicalQuaternionは16/32バイト。最小非ゼロ値と負のゼロを格納時に維持する。保存形式は共通境界で正規化する。NUM001はDouble選択時にdoubleを許可し、decimalとBigIntegerの状態への流入は引き続き拒否する。

## ゲーム・依存先・離散座標

`CgeNumericProfileProject` または `CgeGameProject` のプロジェクトは、プロジェクト本文の設定を読み、Microsoft.CommonがOutDir・TargetPath・参照用DLLのパスを確定する前に設定を適用する。参照プロジェクトのAdditionalPropertiesと作成ツールのMSBuild呼出へ設定を明示する。Analyzer自体は別の数値型へ切り替えない。非既定のゲーム・World・Entities・作成ツールも出力先と生成ソースを分離する。独自のCustomBeforeMicrosoftCommonTargetsを指定する場合は、同ファイルからNumericProfile.BeforeCommon.targetsを取り込む。遅すぎる設定はビルドエラーとし、完成DLLを共有出力へ上書きしない。

設定を持つDLLにはAssemblyMetadataのCgeNumericProfileを記録する。NUM002は参照DLLとの不一致をビルドエラーにし、Numerics・World・Entitiesの識別情報欠落も拒否する。数値設定を持たない独立したDLLは許可する。既定DLLへの暗黙の置換は行わない。

`LogicalNumeric` は設定の識別、整数格子、比率による拡縮、精度に応じた許容誤差を共有する。格子の商は負の値も負の無限大へ丸める。Fixedは一時256bit桁を使い、Float/DoubleはIEEEの符号・仮数・指数から整数商を求める。浮動小数点除算が整数境界を越えて丸まる場合も、誤ったセルを選ばない。浮動小数点のシミュレーション値を固定小数点へ変換する処理ではない。比率拡縮はFixedの一時的な積の幅を確保し、最終結果の範囲を検査する。

物理の調理データ、物理キャッシュの識別情報、生成プレビューは選択した数値設定を識別する。既定Q64.64の既存キャッシュ名は維持し、他の設定は別の識別名を使う。調理データは設定が異なれば再作成する。WorldCellの値の移行とは契約が異なる。

## パッケージSDK

既定設定はパッケージの事前ビルドDLLを使う。他の設定では、不変のパッケージ内ソースからNumerics・Worldと有効なNavigation/DestructionのCookモジュールを、利用側のobj以下に生成した補助プロジェクトでビルドする。DLL名とモジュール境界を保ち、別設定のDLLへ読み替えない。補助プロジェクトは親のDirectory.Build.props/targetsを取り込まず、必要な設定と参照を明示する。パッケージ自体は書き換えない。

コンパイル参照と実行先へのコピーを選択したDLLへ置き換える。NUM002の検査はSDK利用側にも適用する。必要なDLLが生成されない場合は拒否する。リポジトリへの参照やインストールを利用側へ要求しない。

リポジトリ外に実際の三人称テンプレートを生成し、既定、Float、Double、64/32、192/96のビルドとFloat/Double Releaseを検証した。Floatの座標実体はSingle、ベクトルは12バイトで、補助プロジェクトのDLLとコピー先のハッシュが一致する。TopDownはFloatのNavigation/Destructionモジュールを作り、Navigationの生成物まで出力した。三人称Floatは画面を作らない180tickの物理保存・再生が一致した。これらは設定画面、描画、入力操作、開発環境なしでの配布物起動の証拠とは分ける。

## 検証範囲

数値全体Debug/Release各80件。整数オラクルで公開幅と内部256bitの境界・ランダム値、小数位置0/1/半分/上限付近、失敗時の不変を確認する。Single/Double変換と設定間変換は独立した保存境界と比較する。ウォーム後の乗算・除算・平方根は管理ヒープ割当0。

別々のコンパイルで12通りのFixed設定とFloat/Doubleを実行する。整数のみ、最大小数位置、最小非ゼロ値、負のゼロ、格納サイズ、演算、数学関数、ベクトル・回転、最大tickの位相、保存を確認する。高精度のsin(1/2)とatan2(1/2,1/2)は、ガード128bitの独立整数級数と比較し32刻み以内を確認する。この比較は指定入力の精度検証で、全引数に対する誤差保証ではない。実MSBuildでは通常の固定設定に加え64/0、192/190、Float/Doubleを両構成でビルドする。最終コマンドとタスクIDは [M0128](../Milestones/M0128-Static-Production-Flow.md) に記録する。

## 残る制約

Source・SDKと数値型を持つ依存DLL・作成ツールへの伝達を接続した。WorldCellとSnapshotは共通数値表現を使うv2へ移行した。WorldCellはv1のQ64.64データを読み、Snapshotは旧構造を確定できる範囲で静的に移行する。Snapshotの設定間変換は、実際の別設定のWorldで検証済み。設定の実操作・保存は確認済み。SDK0.1.44はVisual Studio用MSBuildでDebugビルド・自己完結Release発行・元モデルなしの実行に成功した。最終候補の編集用参照・IDE実行・開き直しはM0128で別に確認する。指定シーンの保存APIと、実行中ゲームの保存UIは区別する。選択した数学関数の検証を全入力の誤差保証や全設定の性能保証へ広げない。

## プロジェクトプロパティ

Source・SDKのゲームはVisual Studio標準のC# Game Engineページを登録する。英語と日本語（VSLang=1041）を同梱し、数値型、固定幅、小数位置、[深度方式](Render-Projection.md)をcsprojに保存する。Fixed以外では固定専用項目を表示しない。小数位置はUIで0〜190、ビルドでは選択幅−2までを検査する。標準型でのXAML読込とMSBuildでの登録・言語選択は検証済み。実画面での保存操作の受け入れはM0128で別に記録する。

登録は [.NET Project Systemのプロパティ定義](https://github.com/dotnet/project-system/blob/main/docs/repo/property-pages/property-specification.md) に従い、Context=Project、PageTemplate=generic、保存先ProjectFileとする。

SDKの補助ライブラリは、中間出力ルートの`CgeProfiles/<構成>/<数値設定>/`で生成する。ゲームのTFM・RID・深度の下へ重ねず、数値DLLに無関係な深度設定を混ぜない。パス計算は両MSBuildで利用できるNormalizePath/MakeRelativeを使う。

数値設定を変更したら一度ビルドする。編集用の参照も、そこで生成したNumerics/Worldの同じ設定のDLLを使う。未生成の設定を既定の固定小数点DLLへ戻さず、ビルドを求める診断にする。編集用の評価では補助ライブラリの復元・ビルドを繰り返さない。

## シーン入力の精度と範囲

シーンJSONの固定値は、参照先LogicalScalarのTotalBits/FractionalBits定数で設定を確定し、10進係数と指数を整数として読む。選択した小数位置へ最近接・偶数丸めした結果が範囲内か検査し、floatを経由せず明示的なParseInvariant式を生成する。負の下限は含み、丸めで正の上限を越す値はJSONの元の位置でSCENE203になる。入力と展開後の10進表記は最大4096文字。幅・小数位置を確定できない型を既定Q64.64と推測しない。

生成器・Visual Studioは同じ処理を使う。閉じたScene DSLは、LogicalScalar自身の静的ParseInvariantと定数文字列についてだけ、この数値リテラルの経路を認める。一般のメソッド呼び出しを許可しない。JSONへの戻し、Inspectorの入力、保存前の予測モデルも同じ範囲検査を使い、失敗時にfloatへ切り替えない。プレビュー表示は型の値から必要なfloat座標へ投影する。

Double設定のメンバーは、その宣言側の数値プロファイルがfloat64であるときに編集できる。Double入力は直接のdoubleリテラルを保ち、floatを経由しない。NUM001はエンジン名の有無によらず、数値設定を持つSDKゲームにも適用する。固定値の角度は既存の単位変換を経て、JSONへ戻すときもその単位契約を保つ。

実行中のシーン編集では、生成されたゲームの`ParseRuntimeLogicalScalar`を型付きdelegateとして接続し、ゲームが選択した`LogicalNumeric.ParseInvariant`を直接呼ぶ。固定値をfloat経由で構築しない。読み取り処理がない古いビルドは再ビルドを要求し、戻り値のCLR型がゲームの型と違う場合も拒否する。Float/Doubleはそれぞれのネイティブ型を直接読む。任意のコンポーネントの項目を構築する開発ツールの既存メタデータ境界と、ゲーム自身の数値解析を分ける。
