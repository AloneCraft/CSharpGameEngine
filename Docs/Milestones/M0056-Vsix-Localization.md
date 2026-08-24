# M0056: VSIX英語・日本語ローカライズ

- Status: Implemented
- Date: 2026-08-24

## Goal

VSIXが所有するインストーラー、メニュー、ツールウィンドウ、Scene View、Hierarchy、Inspector、Component Palette、Options、ダイアログ、ステータス、Scene item templateを英語・日本語で提供し、Visual Studio表示言語へ自動追従させる。

## Scope

- 英語neutral `VsixStrings`と日本語`ja-JP` satellite。
- `VsixLocalization`、`VsixText`、WPF `LocExtension`。
- 英日VSCT、VSPackage resource、VSIX language pack。
- Culture 1033/1041の同一Scene template。
- Spatial/Physics debug理由と表示文言の分離。
- VSIX version 0.1.7。

## Invariants

- Visual Studio全体のThread cultureを変更しない。
- Build UTC identity、パス、識別子、wire token、C#値をlocale変換しない。
- Runtime、Source Generator、ECS API、Named Pipe wire formatを変更しない。
- 技術用語は日本語UIでも英語表記を維持する。

## Verification state

Debug/ReleaseのSolution buildと各1,564テスト（VSIX各958件）、current-build VSIX byte一致、satellite/BAML/template成果物検査は完了したため`Implemented / TestVerified`とする。`/LCID 1041`は`ja-JP`選択とinstalled/build hash一致を実機確認済み。現在のVisual Studioには1033 resourceがなく`/LCID 1033`が`ja-JP`へfallbackするため、英語language pack導入後の1033確認と両言語の目視layout確認までは`RuntimeVerified`へ昇格しない。

## Feature ID

- F-L8-015
