# Copilot Instructions

このリポジトリのC#構文・書式ルールは次のとおりです。

- 対象: `.cs` ファイル
- 空行は使わない
- インデント以外の不要なスペースは入れない
- 開き波括弧 `{` は行末に置く
- パラメーター名は PascalCase
- フィールド名は PascalCase
- ローカル変数名・インスタンスメンバー名には PascalCase を使用する
- ローカル変数宣言は、型推論できるすべての箇所で `var` を使用する（`const`、引数、フィールド、戻り値など `var` を使用できない宣言は除く）
- インスタンスメンバーアクセスには常に `this.` を付ける

このルールは `src/Engine/Core/StableId.cs` の現在の書式に合わせる。

## 識別子の言語（ADR-0099）

- 日本語の識別子を使ってよいもの:
  - コメント
  - テストのプロジェクト（`tests/` の下）の中のすべて。プロジェクト名、クラス、メソッド、プロパティ、引数、ローカル変数を含む。
  - ランタイムの内部（利用者が使わない、public でない要素）
- 英語（ASCII の英数字と `_`）が必須のもの:
  - 利用者が使うライブラリの public・protected の要素すべて。型、メソッド、引数、プロパティ、フィールド、イベント、列挙値、型引数を含む。
  - ソースのまま利用者のゲームへ入るファイル（`src/Runtime`、`src/Entities`、`plugins/*/Runtime`）では、利用者のコードから見える `internal` の要素も英語にする。日本語を使えるのは private とローカル変数だけ。
- 守り方: 診断 NAME001（`src/CodeAnalysis/EnglishApiIdentifierAnalyzer.cs`）が、エンジン自身のプロジェクトと、このリポジトリの中のゲームで、英語でない公開名をビルドエラーにする。利用者のゲームの名前の付け方は縛らない。

## プロジェクトの名前と置き場所（ADR-0099）

- フォルダ名・プロジェクト名・DLL 名には `CSharpGameEngine.` を付けない（例: `src/Engine/Engine.csproj` が `Engine.dll` を作る）。名前空間、NuGet の PackageId、VSIX の ID、テンプレートの ID は `CSharpGameEngine` のまま。
- プロジェクトは、分ける理由（対象の環境、別のプロセスで動く必要、ソースのまま取り込む、配布物の単位など）があるものだけを分ける。理由が無いものはまとめる。
- テストかどうかは名前ではなく、`tests/` の下にあるかで決まる（`CgeTestProject`）。エンジンのプロジェクトかどうかも、置き場所で決まる（`CgeEngineAssembly`）。

## Visual Studio ソリューション登録

- `CSharpGameEngine.slnx` はプロジェクト表示の正本である。
- リポジトリ直下と全サブフォルダにある `bin`、`obj`、`.git`、`.vs`、`.gitignore` で除外されたローカル `VSIXProject1` 以外の全 `.csproj` と `.shproj` を、同じ変更内に `CSharpGameEngine.slnx` へ登録すること。
- 全 `.projitems` も同じ変更内にソリューション項目として登録すること。
- `.shproj` は対応する `.projitems` を `Label="Shared"` で import し、共有ファイル一覧はワイルドカードではなく明示的に管理すること。
- プロジェクトまたは共有プロジェクトファイルを追加・削除・移動・改名した場合、登録整合性テストを実行してから完了とすること。
