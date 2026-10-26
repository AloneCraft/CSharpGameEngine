# ADR-0081 — Solution Explorer と連動するアセット表示

- Status: Accepted
- Date: 2026-10-25
- Authorization: ユーザーが設計を承認し、実装を指示。
- 更新: 2026-10-27、ダブルクリックの動作のうち、モデル・テクスチャ・音声・フォント・データアセットの行は [ADR-0089](ADR-0089-Asset-Editor-Windows.md)（既定フローティングの専用エディター）で置き換えた。それ以外の決定は有効。

## Context

Solution Explorer がフォルダー階層を持つ。別のブラウザーで階層を複製しない。旧 AssetMetaWatcher は GUID 行形式を生成するが、Content の正規メタデータは JSON である。M0114 のインポート統合は未完了。

## Decision

Solution Explorer をナビゲーションの入口とし、下部の Assets ウィンドウはサムネイル、検索、整理操作と参照関係を表示する。Inspector は選択内容、中央の既存エディターはドキュメント編集を担当する。初回だけ Output とタブ結合し、以後の配置は VS に任せる。

ソースとライブバッファを正本とする。索引とサムネイルは派生物。未登録資産に閲覧目的で ID を作らない。メタデータの書き手を Content に限定し、旧形式は明示的な一度限りの変換で GUID を保持する。通常処理に旧形式リーダーを残さない。

改名・移動は参照更新を含む事前計画を検証してから適用する。C# は意味解析、JSON とグラフは形式に従った解析を使い、生成ファイルを書き換えない。競合、未解決参照、読み取り専用対象は変更前に拒否する。削除には参照解消か型の合う置換先が必要。VS のネイティブ Undo を使用する。

SDK・エンジン・NuGet の資産は読み取り専用。プロジェクトへの複製は新しい ID を持つ。取り込み実行は正規 Content/L7 サービスを使用し、未接続機能を旧経路へのフォールバックで補わない。

## Alternatives considered

- Solution Explorer のみ: 視覚比較に不足。
- 独立ブラウザー: フォルダー操作が重複。
- Solution Explorer と下部表示: 採用。標準のプロジェクト操作と視覚比較を両立。

## Verification

仕様は [Asset Workspace](../Specifications/Asset-Workspace.md)。自動検証と実際の Experimental Instance による表示・ドッキング・選択・整理操作の検証を区別する。部分実装は完了扱いにしない。

## Related features

F-L8-019、F-L8-026、F-L8-035。M0114 の未完了範囲は本変更で完了したとみなさない。
