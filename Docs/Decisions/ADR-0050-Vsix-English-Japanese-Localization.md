# ADR-0050 — VSIXの英語neutral・日本語satelliteローカライズ

- Status: Accepted
- Date: 2026-08-24

## Context

VSIXのメニュー、WPF UI、ステータス、ダイアログ、インストーラー、Sceneテンプレートには英語と日本語の直書きが混在していた。Visual Studioの表示言語と異なる文言、Culture 1033へ登録された日本語テンプレート、Named Pipeのwire tokenをそのまま表示する経路もあり、翻訳漏れと通信契約の混同を静的に検出できなかった。

## Decision

英語`en-US`をneutralかつ最終フォールバック、日本語`ja-JP`をsatellite resourceとして配布する。Package初期化時にVisual Studio UIスレッドの`CurrentUICulture`をVSIX専用状態へ固定し、devenv全体のThread cultureは変更しない。

WPFとC#の表示文言は`VsixStrings`、`VsixText`、`LocExtension`へ集約する。全文formatを正本とし、英日でplaceholderを一致させる。日本語resourceまたはkeyが無い場合は英語、英語keyも無い場合は`[Resource.Key]`を表示してActivity Logへ一度だけ記録する。ファイルパス、C#シンボル、HRESULT、Stable ID、wire token、Build UTC identityは翻訳しない。

メニューは英語neutral CTOと日本語CTO、インストーラーは英語manifestと`ja-JP/Extension.vsixlangpack`、Scene item templateは同一ソースをCulture 1033/1041へ登録する。Spatial/Physics debug通信は理由enumと不変wire tokenを保持し、ユーザー表示は受信側でlocale別の全文へ変換する。

## Alternatives considered

- 日本語をneutralとして英語を追加: 未対応言語で日本語になるため不採用。
- VSIX独自の言語設定: Visual Studioの表示言語と二重管理になり、実行中切替にはWPF再構築も必要なため不採用。
- Thread cultureをdevenv全体で変更: 他拡張やVisual Studio本体へ影響するため不採用。
- wire上へ翻訳済みStatusを送る: protocolとUI cultureが結合するため不採用。

## Consequences

Visual Studio再起動後に選択言語へ自動追従し、未対応言語では英語になる。resource key、VSCT構造、template bytes、VSIX zip内容を継続的に試験する必要がある。専門用語はAPIとの対応を優先し、日本語UIでも`Scene View`、`Transform`、`Collider`、`RigidBody`、`AABB`、`Logical256`等を維持する。

## Verification

英日resource parity、placeholder、XAML key、UI sink、VSCT構造、debug理由format、satellite DLL、language pack、Culture 1033/1041 templateを自動試験する。`/LCID 1033`と`/LCID 1041`の実機確認が両方完了するまではF-L8-015を`Implemented / TestVerified`として扱う。

## Related features

- F-L8-015
- M0056
