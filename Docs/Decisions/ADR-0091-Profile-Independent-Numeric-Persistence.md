# ADR-0091 — 数値設定と保存形式の分離

- Status: Accepted
- Date: 2026-10-31

## Context
Q64.64固定の内部配置を保存契約にすると、数値設定変更時に保存形式が変わる。ユーザーは文字列だけでなく整数部と小数部を分けたバイナリでも汎用化する方針を指定した。

## Decision
数値の保存契約を内部の型・ビット幅から独立させる。[数値保存仕様](../Specifications/Numeric-Persistence.md)の符号・整数部・2進小数部を共通形式とし、読み込み先で最近接・偶数丸めする。範囲外は拒否し、復元の失敗時は状態を一部だけ反映しない。テキストは正確な有限小数で表す。

## Alternatives considered
内部の生バイト保存は設定に依存するため汎用の契約にはしない。文字列だけへの限定も不要。float/doubleの短い表示文字列を共通の正確値とする方式は、保存元と読み込み先で値の意味が変わるため採用しない。

## Consequences
保存境界の変換は必要だが、数値設定ごとの保存データ移行は不要にできる。丸めで変更した値は元のバイト列に戻るとは限らないため、破損検査と復元後の再保存一致を分離する必要がある。WorldCellはADR-0094、SnapshotはADR-0095で版を分けて接続済み。旧形式の対応範囲は各契約で明示する。

## Verification
`NumericValueCodecTests` の独立した整数オラクルとIEEE変換比較、LogicalScalar境界往復、切り詰め・非正規表現拒否で確認する。

## Related features
F-L1-011。全体設計は [静的構成と制作フローの統合](../refactoring-designs/20261031-static-production-flow.md)。
