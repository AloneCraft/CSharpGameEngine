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

このルールは `src/CSharpGameEngine.Core/StableId.cs` の現在の書式に合わせる。

## Visual Studio ソリューション登録

- `CSharpGameEngine.slnx` はプロジェクト表示の正本である。
- リポジトリ直下と全サブフォルダにある `bin`、`obj`、`.git`、`.vs`、`.gitignore` で除外されたローカル `VSIXProject1` 以外の全 `.csproj` と `.shproj` を、同じ変更内に `CSharpGameEngine.slnx` へ登録すること。
- 全 `.projitems` も同じ変更内にソリューション項目として登録すること。
- `.shproj` は対応する `.projitems` を `Label="Shared"` で import し、共有ファイル一覧はワイルドカードではなく明示的に管理すること。
- プロジェクトまたは共有プロジェクトファイルを追加・削除・移動・改名した場合、登録整合性テストを実行してから完了とすること。
