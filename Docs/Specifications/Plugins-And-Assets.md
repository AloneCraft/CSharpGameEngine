# プラグインとアセット

ADR-0079 / M0103。ゲームに機能とデータを足す方法。UE のプラグインとアセットに対応する。

## 1. どちらを作るか
| やりたいこと | 作るもの |
|---|---|
| 新しい種類の物(車、ナビメッシュ、破壊できる物)と、その振る舞いを足す | プラグイン |
| 既にある種類の物の値(メッシュ、音、車の重さ)を足す・変える | アセット |
| 新しい種類のデータ(車のエンジンの設定一式)を定義する | 型に `[DataAsset]` を付ける(コード)。値は `.cgeasset`(アセット) |
| このゲームだけの遊び | ゲームのコード(プラグインにしなくてよい) |

- プラグインはコードを持つ。アセットはデータだけで、処理を持たない。
- 毎フレーム動くもの、振る舞いを決めるものはコード。人が調整する値はアセット。
- プラグインはアセットとシーンを同梱できる。アセットはプラグインを含まない。

## 2. 置き場所と有効にする方法
| 置き場所 | UE で言うと | 有効にする方法 |
|---|---|---|
| ゲームの `Plugins/<名前>/` | Project プラグイン | 置けば有効 |
| エンジンの `plugins/<フォルダ>/`(SDK パッケージの `plugins/`) | Engine プラグイン | ゲームの csproj に `<CgePlugins>Vehicles;…</CgePlugins>` |
| NuGet パッケージ | Fab のプラグイン | `PackageReference` で有効(パッケージの `build/<Id>.targets` が自分を取り込む) |

- 取り込みは `build/CSharpGameEngine.Game.targets` が行う。プロジェクトファイルの後に読まれるので、`CgePlugins` が見える(リポジトリでは `Directory.Build.targets`、パッケージでは `CSharpGameEngine.Sdk.targets` から読む)。
- エンジンのプラグインは Vehicles(`plugins/CSharpGameEngine.Vehicles`。UE の ChaosVehicles に当たる Chaos 相当の車。M0109。Vehicle テンプレートが有効にする)と Navigation(`plugins/CSharpGameEngine.Navigation`。TopDown テンプレートが有効にする。M0106)。UserEcsProject は両方を有効にしている。VS の新規作成は、テンプレートの csproj の `CgePlugins` を生成する csproj へ写す(`GameProjectGenerator.PluginsOf`)。

## 3. プラグインの形
```
<フォルダ>/
  <名前>.cgeplugin          マニフェスト
  <名前>.plugin.props       有効かを決める
  <名前>.plugin.items       有効なときだけ読まれる、このプラグインの項目
  Runtime/<名前>.Runtime.projitems(と .shproj)
                            Runtime モジュール: Component、System、サービス、イベント。ゲームと一緒にコンパイルする
  Assets/                   アセット。実行時は Plugins/<名前>/Assets/… にある。カタログは Assets.Plugins.<名前>.*
  Scenes/                   シーン(ゲームのシーンと同じ扱い。startupOrder があれば起動時に読まれる)
```
- Library モジュール(任意): DLL。`[assembly: PluginLibrary]` を付ける。公開 struct はパレットとスキーマに出る。`World`・`State`・`Entity`・`ISystem` を名指ししない。NuGet のプラグインはパッケージの `lib/` に入れる。
- Cook モジュール(任意): `IAssetCooker`(`CSharpGameEngine.Cooking`)を実装した DLL。§6。Navigation は `Cook/CSharpGameEngine.Navigation.Cook.csproj`(ソースはエンジンのリポジトリでビルドされ、SDK パッケージでは `Cook/lib/` に出力が入る)。
- 見本: `samples/Plugins/Weather`(プロジェクトのプラグイン)、`samples/Plugins/CSharpGameEngine.Samples.Lightning`(NuGet のプラグイン。Library の DLL と Runtime のソース)。

### マニフェスト `<名前>.cgeplugin`
```json
{
  "Name": "Vehicles",
  "Version": "0.1.0",
  "EngineVersion": ">=0.1.16",
  "Description": "…",
  "Dependencies": []
}
```
| 項目 | 規則 | 診断 |
|---|---|---|
| `Name` | C# の識別子。カタログの名前とアセットの置き場所になる | PLUGIN001(不正)、PLUGIN002(有効なプラグインに同じ名前が 2 つ) |
| `Version` | 1〜4 個の数(`1`、`1.2.3`) | PLUGIN001 |
| `EngineVersion` | 空白で区切った条件がすべて成り立つ(`>=`、`>`、`<=`、`<`、`=`、数だけなら一致)。エンジンの版は `build/CSharpGameEngine.Version.props` | PLUGIN001(書式)、PLUGIN005(合わない) |
| `Dependencies` | 有効なプラグインの名前の配列 | PLUGIN003(有効でない)、PLUGIN004(循環) |

### `<名前>.plugin.props` と `<名前>.plugin.items`
```xml
<!-- Weather.plugin.props(Weather の 2 か所だけをプラグインの名前に変える) -->
<Project>
  <PropertyGroup>
    <CgePluginEnabled>false</CgePluginEnabled>
    <CgePluginEnabled Condition="'$(_CgePluginLocation)' != 'Engine' or $([System.String]::Copy(';$(CgePlugins);').Contains(';Weather;'))">true</CgePluginEnabled>
    <CgePluginRoots Condition="'$(CgePluginEnabled)' == 'true'">$(CgePluginRoots)|Weather=$(MSBuildThisFileDirectory)</CgePluginRoots>
  </PropertyGroup>
  <Import Project="$(MSBuildThisFileDirectory)Weather.plugin.items" Condition="'$(CgePluginEnabled)' == 'true'" />
</Project>
```
- `.plugin.items` には、Runtime の `.projitems` の取り込み、マニフェストとシーンとアセットの `AdditionalFiles`、アセットのコピー(`Link="Plugins\<名前>\Assets\…"`)を `$(MSBuildThisFileDirectory)` で書く。
- 2 つに分ける理由: MSBuild は項目をすべてのプロパティの後で評価する。プラグインごとに上書きする `CgePluginEnabled` で項目に条件を付けると、最後のプラグインの値しか効かない。`Import` の条件は順に評価されるので正しい。
- 判定を共通のファイルに置かない理由: MSBuild は 1 つのプロジェクトで同じファイルを 1 回しか取り込まない(2 回目は警告 MSB4011 を出して無視する)。共通のファイルは最初のプラグインにしか効かず、M0103 の検証で 2 つ目のプラグインが有効にならなかった。
- 有効なプラグインのフォルダは `CgePluginRoots`(`|名前=フォルダ|…`)に足され、生成器がそのシーンとアセットを読む。区切りが `|` なのは、プロパティが .editorconfig を通って生成器に渡り、その読み取りが `;` と `#` から後ろを注釈として捨てるため(`;` で区切った最初の実装では値が空になり、パッケージのプラグインのシーンが作られなかった)。
- 同じ理由で、生成器へ渡すフォルダ(プロジェクトとプラグイン)は、`#` を `?h`、`;` を `?s` に書き換えて渡す(`CgeProjectDirectory`、`CgePluginRootsEncoded`。`Game.targets` の `CgeEncodeGeneratorPaths` が .editorconfig を書く直前に作る)。`?` は Windows のパスに使えない。書き換える前は、`C:\C#\…` のようなフォルダのゲームは ProjectDir が `C:\C` で切れ、シーンもアセットのカタログも無しにビルドされて失敗した。

## 4. 足し方
| 足したいもの | 書き方 |
|---|---|
| Component | Runtime の `public struct`。パレットに出すには `[SceneComponentOrder]` か `[SceneComponentIcon]` を付ける(`CSharpGameEngine.*` の名前空間のとき) |
| System | Runtime の `struct X : ISystem`。ゲームの System と同じ書き方(ADR-0077) |
| サービス | `[EngineService] public sealed class X`。System の引数に `X x` と書けば渡る。§5 |
| データの型と値 | `[DataAsset]` の struct か class。値は `Assets/**/*.cgeasset`。§7 |
| 重い計算、ネイティブ、外部ライブラリ | Library の DLL。System から呼ぶ |
| 取り込みとベイク | Cook の DLL。§6 |

### 作れないもの(v1)
- Roslyn の生成器(生成ルール)。エンジンの生成器 1 つだけが `World` と Archetype を作る。生成器どうしは互いの出力を見られないため。
- DLL だけの System。System はソースで配る。本体を隠したいときは Library の DLL に置く。
- 描画のパスと計算シェーダー。描画契約はエンジンが持つ。
- 独自のエディタの画面(WPF)。エディタへの拡張は属性による宣言だけ。

## 5. サービス `[EngineService]`
- 参照型のクラスに付ける。コンストラクターは、引数なし、または `EngineHost` を 1 つ取る public のもの(ゲームのアセンブリの中なら internal でもよい)。使えなければ SYSGEN043。
- 生成器は型ごとに `GeneratedSystemSet` のフィールドを 1 つ置き、最初に使う System の呼び出しで作る(`Systems.Service0 ??= new X()`)。同じゲームの全 System が同じ 1 つを受ける。
- `IDisposable` なら、`Shutdown`(と `ShutdownEditor`)の最後に、型名の逆順で破棄する。破棄したフィールドは null に戻る。
- 引数なしで作るサービスだけを使う System は、`EngineHost` を要らない。
- `EngineHost` のプロパティのサービス(`Audio`、`Hud` など)は今のまま。
- `sealed` で `IDisposable` でないクラスも使える(後始末は `object` を介して調べる)。エンジン自身の `[EngineService]` に、Scene を実行中に作る `Spawner`(M0105。`Spawner.Spawn<TScene>(位置)`。根に `Transform` を持つ Scene だけが渡せる。`SpawnerFlush` が各フレームの終わりに作る)がある。

## 6. Cook モジュール(取り込みとベイク)
- `CSharpGameEngine.Cooking` の `IAssetCooker`(`Name`、`InputExtensions`、`Cook(CookRequest)`)を、public で引数なしのコンストラクターを持つクラスで実装する。
- `CSharpGameEngine.Cook.Cli`(パッケージでは `$(CgeCookCli)`)が、別のプロセスで DLL を読んで動かす。
  - `list <module.dll>`: 処理の名前と拡張子。
  - `cook <module.dll> <入力> <出力フォルダ> [--project <フォルダ>] [--cooker <名前>] [--verify]`: 書いたファイルを出力フォルダからの相対パスで出す。
  - 処理が成功のとき返したメッセージ(警告や統計)は標準エラーへ、失敗のときは処理のメッセージを 1 行ずつ標準エラーへ出す。MSBuild の `Exec` が読めるように、処理は `ファイル: error コード: 内容` の形で返す。
- 守ること(違えば失敗):
  - 出力は出力フォルダの中だけ。
  - 書くと言ったファイルは在る。
  - `--verify` なら 2 回の実行でファイルとバイト列が一致する(決定的)。
- 読み込みは専用の AssemblyLoadContext。契約の DLL だけをホストと共有する。モジュールの依存は、モジュールの隣の `.deps.json`(プロジェクトに `EnableDynamicLoading` と `CopyLocalLockFileAssemblies` を付ける)で解く。
- 終了コード: 0 成功、1 失敗、2 使い方の誤り。

### 実例: ナビメッシュ(Navigation プラグイン、M0106)
- 入力はシーンの JSON。シーンの `NavMeshBounds`(UE の NavMeshBoundsVolume)が領域と歩く者の大きさを決め、起動シーンの静的な衝突形状から `Assets/Navigation/<Id>.cgenav` を書く([M0106](../Milestones/M0106-Navmesh-Bake-And-Path-Following.md))。
- ビルド中の自動実行: プラグインの `Navigation.plugin.items` が、`Scenes/**/*.json` のうち `CSharpGameEngine.Navigation.NavMeshBounds` を含むシーンごとに、Cook を別のプロセスで動かす(ターゲット `CgeCookNavigation`。`AssignTargetPaths` の前)。
  - 出力は `obj/…/cge-navigation/Assets/Navigation/<Id>.cgenav` で、ビルドが `Assets/Navigation/` へ複製する(出力に入る)。
  - シーン、`Assets/**` のファイル、Cook CLI、プラグインの Cook モジュールのどれかが、前回より新しいときだけ作り直す(`Inputs`/`Outputs` とスタンプ)。リポジトリのビルド(SourceMode)は Cook CLI とモジュールをビルドしてから使い、パッケージのビルド(PackageMode)は `tools/Cook/` と `plugins/…/Cook/lib/` を使う。
  - 失敗は処理の `error NAVnnn` 行がそのままビルドのエラーになる。
- VSIX: Scene Designer の「ナビメッシュ」の節のボタンが、選択したシーンのゲームのプロジェクトで同じターゲットを `dotnet msbuild -t:CgeCookNavigation` で動かし、出力と診断を表示する(`NavigationCookService`)。物理のベイクのように、エンジンのソースの場所には頼らない。
- まだ無いもの: 既存の `Physics.Cli bake` の Cook への載せ直し(ベイクの形が違う: ゲームのアセンブリを読んで世界を回す)。

## 7. データアセット `[DataAsset]` と `.cgeasset`
```json
{ "schemaVersion": 3, "type": "Weather.StormSetup", "members": { "RateMultiplier": 2, "Gusts": 7 } }
```
- 書き方はシーンの Component 1 つと同じで、値の規則も同じ(列挙は名前、数は型の接尾辞つきの文字どおり、`[Angle]` は回転数)。キーは `$schema`、`schemaVersion`(3)、`type`、`members` だけ。
- 生成器が C# の値にして、カタログの静的メンバーにする: `Assets/Cars/Sport.cgeasset` → `Assets.Cars.Sport`、プラグインの `Assets/Storm.cgeasset` → `Assets.Plugins.Weather.Storm`。型はデータの型そのもので、`Assets.Cars.Sport.Mass` のように読む。実行時の読み込みもリフレクションも無い。
- 不正な文書はシーンと同じ診断(SCENE201〜205)を `.cgeasset` の行と列に出し、カタログから外す。
- まだ無いもの: Designer での編集と、シーンの値からの参照(コードから参照する)。

### 実例: 車の設定 `VehicleSetup`(Vehicles プラグイン、M0109)
- 型はエンジンのプラグインの `[DataAsset] public sealed class VehicleSetup`(`plugins/CSharpGameEngine.Vehicles/Runtime/VehicleSetup.cs`)。UE の `ChaosWheeledVehicleMovementComponent` と `ChaosVehicleWheel` の値: 質量、空気抵抗、`Engine`(トルク曲線の点の配列、最大トルク、回転数)、`Transmission`(ギア比の配列、変速の回転数)、`Steering`(アッカーマン、速さの曲線)、差動、`FrontWheel`・`RearWheel`(車輪とサスペンション)、入力の速さ、眠り。入れ子の struct と配列もシーンと同じ書き方で書ける。
- 値はゲームのアセット `Templates/CSharpGameEngine.Templates.Vehicle/Assets/Vehicles/SportsCar/SportsCarSetup.cgeasset`(UE の `BP_VehicleAdvSportsCar` の値を SI 単位に直したもの)。カタログでは `Assets.Vehicles.SportsCar.SportsCarSetup`(ファイル名をフォルダ名と同じにすると、クラスと同じ名前のメンバーになりコンパイルできないので、`…Setup` とした)。
- シーンからは参照できないので、ゲームのコードが車の Component へ入れる: `internal struct UseSportsCarSetup:ISystem { public void Update(ref Vehicle Car){ Car.Setup??=Assets.Vehicles.SportsCar.SportsCarSetup; } }`。設定の無い車は力を加えない(物理だけで落ちる)。
- 角度の値(`MaxSteerAngleDegrees`)は、シーンと同じく JSON では回転数(`"MaxSteerAngle": 0.1111111111111111` = 40°)。

## 8. アセットの置き場所とカタログ
| ファイル | 実行時のパス | カタログ |
|---|---|---|
| ゲームの `Assets/Sounds/Jump.wav` | `Assets/Sounds/Jump.wav` | `Assets.Sounds.Jump` |
| プロジェクトのプラグインの `Plugins/Weather/Assets/Rain.wav` | `Plugins/Weather/Assets/Rain.wav` | `Assets.Plugins.Weather.Rain` |
| エンジンとパッケージのプラグインの `<フォルダ>/Assets/Car.glb` | `Plugins/<名前>/Assets/Car.glb`(ビルドがコピーする) | `Assets.Plugins.<名前>.Car` |
- シーン JSON が `"Plugins/<名前>/Assets/…"` と書いたパスも、ゲームのアセットと同じく存在を調べる(SCENE208)。

## 9. 共有する枠
- Component の種類は、ゲームとすべてのプラグインで合わせて 512 まで(SYSGEN026)。マスクは 64 種類ごとに 1 語。128 種類以下の生成コードは、上限を広げる前と同じ。
- 同じ単純名の Component(例: Weather と Lightning の `Intensity`)は、生成する名前を名前空間つき(`Weather_Intensity`、`Lightning_Intensity`)にする。それでも重なるときだけ SYSGEN020。
- プラグインの Runtime のソースは、SDK の版に結び付く(生成器がその版の形でコードを出す)。マニフェストの `EngineVersion` で範囲を書く。
