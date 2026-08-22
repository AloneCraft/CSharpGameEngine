# C# Game Engine — Codex向けリポジトリ指示

作業を計画・実装・レビュー・文書化する前に、必ず次を読んでください。

1. `.github/copilot-instructions.md`
2. `.github/skills/CSharpGameEngine-Documentation/SKILL.md`
3. `Docs/Architecture.md`
4. `Docs/Implementation-Status.md`
5. タスクに関係するLayer、Specification、ADR、Milestone文書

`.github/copilot-instructions.md`はCopilot専用ファイルとして扱わず、このリポジトリの共通ルールとして読み、その指示に従ってください。

ドキュメントSkillで規定された更新は、すべての実装タスクの完了条件に含まれます。

コード変更後は、元の依頼文ではなく最終コード・テスト・実行結果から実際の仕様を再調査し、Intended Specificationとの適合を記録してください。

## Visual Studio ソリューション登録

- `CSharpGameEngine.slnx` はプロジェクト表示の正本である。
- リポジトリ直下と全サブフォルダにある `bin`、`obj`、`.git`、`.vs`、`.gitignore` で除外されたローカル `VSIXProject1` 以外の全 `.csproj` と `.shproj` を、同じ変更内に `CSharpGameEngine.slnx` へ登録すること。
- 全 `.projitems` も同じ変更内にソリューション項目として登録すること。
- `.shproj` は対応する `.projitems` を `Label="Shared"` で import し、共有ファイル一覧はワイルドカードではなく明示的に管理すること。
- プロジェクトまたは共有プロジェクトファイルを追加・削除・移動・改名した場合、`dotnet test CSharpGameEngine.slnx -c Debug` を実行し、登録整合性テストを通すこと。
