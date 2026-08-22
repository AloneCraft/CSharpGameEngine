# C# Game Engine 開発ルール

作業開始前に `.github/AGENTS.md`、`.github/copilot-instructions.md`、および同ファイルが指定するドキュメントを読むこと。

## Visual Studio ソリューション登録

- `CSharpGameEngine.slnx` はプロジェクト表示の正本である。
- リポジトリ直下と全サブフォルダにある `bin`、`obj`、`.git`、`.vs`、`.gitignore` で除外されたローカル `VSIXProject1` 以外の全 `.csproj` と `.shproj` を、同じ変更内に `CSharpGameEngine.slnx` へ登録すること。
- 全 `.projitems` も同じ変更内にソリューション項目として登録すること。
- `.shproj` は対応する `.projitems` を `Label="Shared"` で import し、共有ファイル一覧はワイルドカードではなく明示的に管理すること。
- プロジェクトまたは共有プロジェクトファイルを追加・削除・移動・改名した場合、登録漏れ検証テストを実行してから完了とすること。
